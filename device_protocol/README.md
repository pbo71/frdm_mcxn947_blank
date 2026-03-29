# Device Protocol

Binary protocol on USB bulk command endpoints.

Transport split:

- Command/Event control plane uses `CMD OUT 0x01` and `CMD IN 0x81` only.
- Audio stream data plane uses `AUDIO OUT 0x02` and `AUDIO IN 0x82` only.
- Command/event protocol frames must never be sent on the audio endpoints.
- Large audio payloads belong on the audio bulk endpoints, not in command payloads.
- Audio stream data uses its own dedicated audio header, not the command/event protocol header.

Audio stream header layout:

```text
u16 magic          = 0x4155
u8  version        = 1
u8  headerSize     = 24
u16 flags
u16 reserved0
u32 sequenceNumber
u32 timestamp
u16 payloadBytes
u8  channelCount
u8  bitsPerSample
u32 sampleRateHz
u8  payload[payloadBytes]
```

Audio stream flags:

- `1 << 0` `START_OF_STREAM`
- `1 << 1` `END_OF_STREAM`
- `1 << 2` `DISCONTINUITY`
- `1 << 3` `FORMAT_CHANGE`

Audio data-plane rules:

- Audio packets must start with `audio_stream_header_t`.
- `payloadBytes + headerSize` must match the USB packet length exactly.
- `sampleRateHz`, `channelCount`, and `bitsPerSample` must be non-zero.
- The audio service marks outbound packets with `DISCONTINUITY` if sequence numbers jump.
- `END_OF_STREAM` stops the current stream and emits a control-plane `StreamStopped` event.

Layering:

- `usb_vendor_bulk.c` is transport only.
- `services/control_plane_service.*` owns command/event handling for the control plane.
- `services/audio_stream_service.*` owns the audio data plane.
- `device_protocol/*` contains frame format, parsing and command dispatch internals.

Control-plane state machine:

- `Idle`: transport not configured.
- `Configured`: command/event channel is ready.
- `Streaming`: audio data plane is active.

Frame layout:

```text
u16 magic         = 0x4153
u8  version       = 1
u8  type          = 1 command, 2 response, 3 event
u8  opcode
u8  status
u16 sequence
u16 payloadLength
u8  payload[payloadLength]
```

Flow for a valid command:

1. PC sends a `Command` frame.
2. Device returns a `Response` frame with the same `opcode` and `sequence`.

That `Response` is the acknowledgment.

Packet-size rule:

- A control-plane frame must fit within the current command packet size negotiated by USB.
- Frames larger than the current command packet size are rejected with `InvalidLength`.
- Response frames are also capped to the current command packet size.
- Event frames larger than the current command packet size are not queued for transmission.
- Dropped oversized outbound frames are counted in `oversizedOutboundDropCount` for debug visibility.
- In practice this means a maximum total frame size of `64` bytes on FS and `512` bytes on HS.
- Since the frame header is `10` bytes, the maximum command payload is `54` bytes on FS and `502` bytes on HS.

Commands:

- `1` `GetInfo`
- `2` `SetLed`
- `3` `GetUsbDebugState`
- `4` `Ping`
- `5` `StartStream`
- `6` `StopStream`
- `7` `SetGeneratorConfig`
- `8` `I2cWriteRegister`
- `9` `I2cReadRegister`

StartStream sources:

- `0` `HostRxLoopback`
- `1` `DeviceGeneratedSine`
- `2` `DeviceGeneratedChirp`
- `3` `DeviceGeneratedNoise`

Status codes:

| Value | Name | Meaning |
| --- | --- | --- |
| `0` | `Ok` | Command handled successfully. |
| `1` | `InvalidMagic` | Frame magic was not `0x4153`. |
| `2` | `InvalidVersion` | Unsupported protocol version. |
| `3` | `InvalidType` | Frame type was not `Command` where required. |
| `4` | `InvalidLength` | Payload size or frame length was invalid. |
| `5` | `UnsupportedCommand` | Unknown or unsupported command opcode. |
| `6` | `InvalidArgument` | Command payload values were syntactically valid but semantically invalid. |

Response payloads:

| Command | Response `opcode` | Response payload |
| --- | --- | --- |
| `GetInfo` | `GetInfo` | `GetInfoResponsePayload { u16 protocolVersion, u16 maxCommandPacketSize, u32 capabilities }` |
| `SetLed` | `SetLed` | `SetLedResponsePayload { u8 appliedAction, u8 reserved0, u16 reserved1 }` |
| `GetUsbDebugState` | `GetUsbDebugState` | `GetUsbDebugStateResponsePayload { stage, lastEvent, lastStatus, usbSpeed, oversizedOutboundDropCount, setupBmRequestType, setupBRequest, setupWValue, setupWIndex, setupWLength }` |
| `Ping` | `Ping` | Same bytes as command payload, echoed unchanged |
| `StartStream` | `StartStream` | `StartStreamResponsePayload { u32 sampleRateHz, u8 channelCount, u8 bitsPerSample, u8 source, u8 reserved }` |
| `StopStream` | `StopStream` | `StopStreamResponsePayload { u32 stopReason }` |
| `SetGeneratorConfig` | `SetGeneratorConfig` | `SetGeneratorConfigResponsePayload { u32 primaryFrequencyHz, u32 secondaryFrequencyHz, u32 modulationPeriodMs, u16 amplitude, u8 source, u8 noiseType, u8 amplitudeEnvelope, u8 reserved, u32 noiseSeed }` |
| `I2cWriteRegister` | `I2cWriteRegister` | `I2cTransferStatusPayload { u32 driverStatus }` |
| `I2cReadRegister` | `I2cReadRegister` | `I2cReadRegisterResponseHeader { u32 driverStatus, u8 readLength, u8 reserved0, u16 reserved1 }` followed by `readLength` data bytes |

Codec register access:

- `I2cWriteRegister` command payload begins with `I2cWriteRegisterCommandHeader { u8 deviceAddress, u8 registerAddressSize, u8 writeLength, u8 reserved, u32 registerAddress }` followed by `writeLength` data bytes.
- `I2cReadRegister` command payload is `I2cReadRegisterCommandPayload { u8 deviceAddress, u8 registerAddressSize, u8 readLength, u8 reserved, u32 registerAddress }`.
- `deviceAddress` is a 7-bit I2C slave address. For SGTL5000 use `0x0A`.
- `registerAddressSize` is the number of register-address bytes placed on the bus before the read or write data. SGTL5000 uses `2`.
- `writeLength` and `readLength` must be in the range `1..32` so frames fit inside the control-plane packet budget even on full-speed USB.
- The commands use the existing codec I2C bus selected by `BOARD_Codec_I2C_*`.

Stream control:

- Streaming must be started explicitly with `StartStream` on the control plane before audio packets are accepted.
- `StartStream` configures the expected `sampleRateHz`, `channelCount`, `bitsPerSample`, and `source` for the audio data plane.
- Audio packets with a format that does not match the active stream configuration are rejected.
- `StopStream` stops the active stream on the control plane.
- `END_OF_STREAM` in the audio header may still stop the active stream, but control-plane start/stop is the primary lifecycle mechanism.
- `DeviceGeneratedSine` streams a generated 1 kHz sine wave from the device on `AUDIO IN` and does not require audio packets on `AUDIO OUT`.
- `SetGeneratorConfig` updates generated-source parameters before starting a generated stream.
- `DeviceGeneratedChirp` uses `primaryFrequencyHz` and `secondaryFrequencyHz` as the sweep start/end frequencies, and `modulationPeriodMs` as sweep duration.
- `DeviceGeneratedNoise` uses `noiseType` to select `White`, `SampleHold`, or `Binary` noise.
- `noiseSeed` controls the initial pseudo-random state for generated noise so repeated runs can be deterministic.
- `amplitudeEnvelope` selects `Constant`, `FadeIn`, `FadeOut`, or `Triangle` amplitude shaping.
- A `modulationPeriodMs` value of `0` falls back to the default `1000 ms` modulation period.

Events payloads:

| Event | Payload |
| --- | --- |
| `DeviceReady` | `DeviceReadyEventPayload` |
| `StreamStarted` | `StreamStartedEventPayload` |
| `StreamStopped` | `StreamStoppedEventPayload` |
| `Fault` | `FaultEventPayload` |

Events:

- `1` `DeviceReady`
- `2` `StreamStarted`
- `3` `StreamStopped`
- `4` `Fault`

Current host reference implementation is in `tools/winusb-endpoint-probe`.