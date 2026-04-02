# C# Tool Integration Guide

This document describes how a separate C# tool can connect to the device over WinUSB and use the existing control-plane and audio-plane protocols.

## Overview

The device is a vendor-specific USB bulk device. It is not exposed as a standard USB Audio Class device.

The transport is split into two planes:

- Control plane:
  - `CMD OUT = 0x01`
  - `CMD IN = 0x81`
- Audio plane:
  - `AUDIO OUT = 0x02`
  - `AUDIO IN = 0x82`

The control plane is used for commands, responses, and events.

The audio plane is used only for audio packets with the dedicated audio header.

Do not send command frames on the audio endpoints.
Do not send audio packets on the command endpoints.

## USB Identification

Device identifiers:

- `VID = 0x1FC9`
- `PID = 0x00B0`

Known default interface GUID used by the reference host tool:

- `D5959801-45C1-49DB-9053-89B5365C2800`

In practice, the Windows-registered WinUSB interface GUID may differ. The recommended approach is:

1. Try the expected interface GUID.
2. If no device is found, inspect the Windows registry entry under `SYSTEM\CurrentControlSet\Enum\USB\VID_1FC9&PID_00B0...\Device Parameters`.
3. Read `DeviceInterfaceGUIDs` and use that GUID.

The reference implementation already does this in [tools/winusb-endpoint-probe/Program.cs](tools/winusb-endpoint-probe/Program.cs).

## Recommended Connection Flow

1. Discover the device interface path using SetupAPI and the interface GUID.
2. Open the interface path with `CreateFile(...)`.
3. Initialize WinUSB with `WinUsb_Initialize(...)`.
4. Query interface settings with `WinUsb_QueryInterfaceSettings(...)`.
5. Query pipes with `WinUsb_QueryPipe(...)`.
6. Store the four bulk endpoints:
   - `0x01`, `0x81`, `0x02`, `0x82`
7. Apply a pipe timeout using `WinUsb_SetPipePolicy(...)`.
8. Optionally read one initial control-plane frame from `0x81`.
9. Send `GetInfo` and/or `GetUsbDebugState` to verify connectivity.
10. Send `StartStream` before sending or receiving any stream audio.

## Control-Plane Protocol

Control-plane frames use this binary layout:

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

Rules:

- PC sends `type = 1` command frames on `CMD OUT`.
- Device returns `type = 2` response frames on `CMD IN`.
- Device may also send `type = 3` event frames on `CMD IN`.
- Response frames reuse the same `opcode` and `sequence` as the command.
- The response is the acknowledgment.

### Commands

- `1 = GetInfo`
- `2 = SetLed`
- `3 = GetUsbDebugState`
- `4 = Ping`
- `5 = StartStream`
- `6 = StopStream`
- `7 = SetGeneratorConfig`

`StartStream` source values:

- `0 = HostRxLoopback`
- `1 = DeviceGeneratedSine`
- `2 = DeviceGeneratedChirp`
- `3 = DeviceGeneratedNoise`

`SetGeneratorConfig` payload:

- `u32 primaryFrequencyHz`
- `u32 secondaryFrequencyHz`
- `u32 modulationPeriodMs`
- `u16 amplitude`
- `u8 source`
- `u8 noiseType`
- `u8 amplitudeEnvelope`
- `u8 reserved`
- `u32 noiseSeed`

### Quick Note: Implementing `SetGeneratorConfig`

If you only need the minimum needed to support the new generated modes in your C# tool, implement `SetGeneratorConfig` like this:

- `opcode = 7`
- `type = Command`
- `sequence = your normal command sequence`
- `payloadLength = 22`

Payload layout, little-endian:

```text
offset  size  field
0       4     primaryFrequencyHz
4       4     secondaryFrequencyHz
8       4     modulationPeriodMs
12      2     amplitude
14      1     source
15      1     noiseType
16      1     amplitudeEnvelope
17      1     reserved = 0
18      4     noiseSeed
```

Source values:

- `0 = HostRxLoopback`:
  - not relevant for `SetGeneratorConfig`
- `1 = DeviceGeneratedSine`
- `2 = DeviceGeneratedChirp`
- `3 = DeviceGeneratedNoise`

Expected use per source:

- `DeviceGeneratedSine`:
  - `primaryFrequencyHz` is used
  - `secondaryFrequencyHz` can be the same as primary
  - `modulationPeriodMs` controls the envelope period when envelope is not `Constant`
  - `amplitude` is used
- `DeviceGeneratedChirp`:
  - `primaryFrequencyHz` = sweep start
  - `secondaryFrequencyHz` = sweep end
  - `modulationPeriodMs` = sweep duration in milliseconds
  - `amplitude` is used
- `DeviceGeneratedNoise`:
  - frequency fields are ignored
  - `modulationPeriodMs` controls sample-hold cadence and envelope period
  - `amplitude` is used
  - `noiseSeed` makes the pseudo-random sequence repeatable across runs

Noise types:

- `0 = White`
- `1 = SampleHold`
- `2 = Binary`

Amplitude envelope types:

- `0 = Constant`
- `1 = FadeIn`
- `2 = FadeOut`
- `3 = Triangle`

Response:

- `opcode = 7`
- same `sequence` as the command
- `status = Ok` on success
- response payload is also 22 bytes and mirrors the accepted config

Recommended call order:

1. Send `SetGeneratorConfig`.
2. Wait for `Response SetGeneratorConfig` with `status = Ok`.
3. Send `StartStream(..., source = DeviceGeneratedSine|Chirp|Noise)`.
4. Read audio packets from `AUDIO IN`.

Minimal C# payload example:

```csharp
static byte[] BuildSetGeneratorConfigPayload(
    uint primaryFrequencyHz,
    uint secondaryFrequencyHz,
    uint modulationPeriodMs,
    ushort amplitude,
    byte source,
    byte noiseType,
  byte amplitudeEnvelope,
  uint noiseSeed)
{
  var payload = new byte[22];
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), primaryFrequencyHz);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), secondaryFrequencyHz);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), modulationPeriodMs);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), amplitude);
    payload[14] = source;
    payload[15] = noiseType;
    payload[16] = amplitudeEnvelope;
    payload[17] = 0;
  BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(18, 4), noiseSeed);
    return payload;
}
```

Examples:

- sine at `1200 Hz`, amplitude `9000`:

```text
primaryFrequencyHz = 1200
secondaryFrequencyHz = 1200
modulationPeriodMs = 750
amplitude = 9000
source = 1
noiseType = 0
amplitudeEnvelope = 3
noiseSeed = 0x13579BDF
```

- chirp from `500 Hz` to `3500 Hz`, amplitude `10000`:

```text
primaryFrequencyHz = 500
secondaryFrequencyHz = 3500
modulationPeriodMs = 1500
amplitude = 10000
source = 2
noiseType = 0
amplitudeEnvelope = 1
noiseSeed = 0x13579BDF
```

- noise with amplitude `8000`:

```text
primaryFrequencyHz = 0
secondaryFrequencyHz = 0
modulationPeriodMs = 32
amplitude = 8000
source = 3
noiseType = 2
amplitudeEnvelope = 3
noiseSeed = 0x2468ACE1
```

### Quick Note: Implementing `StartStream`

`StartStream` still uses `opcode = 5`, but the payload now includes a `source` field.

- `opcode = 5`
- `type = Command`
- `payloadLength = 8`

Payload layout, little-endian:

```text
offset  size  field
0       4     sampleRateHz
4       1     channelCount
5       1     bitsPerSample
6       1     source
7       1     reserved = 0
```

Source values:

- `0 = HostRxLoopback`
- `1 = DeviceGeneratedSine`
- `2 = DeviceGeneratedChirp`
- `3 = DeviceGeneratedNoise`

Expected usage:

- for normal host-to-device loopback:
  - use `source = 0`
  - then write audio packets to `AUDIO OUT`
- for generated-device streaming:
  - use `source = 1`, `2`, or `3`
  - then read audio packets from `AUDIO IN`

Response:

- `opcode = 5`
- same `sequence` as the command
- `status = Ok` on success
- 8-byte response payload:

```text
offset  size  field
0       4     sampleRateHz
4       1     channelCount
5       1     bitsPerSample
6       1     source
7       1     reserved
```

Minimal C# payload example:

```csharp
static byte[] BuildStartStreamPayload(
    uint sampleRateHz,
    byte channelCount,
    byte bitsPerSample,
    byte source)
{
    var payload = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sampleRateHz);
    payload[4] = channelCount;
    payload[5] = bitsPerSample;
    payload[6] = source;
    payload[7] = 0;
    return payload;
}
```

Examples:

- loopback stream:

```text
sampleRateHz = 48000
channelCount = 2
bitsPerSample = 32
source = 0
```

Loopback payload convention:

```text
Each sample is little-endian 32-bit.
byte 0 = bits 7:0
byte 1 = bits 15:8
byte 2 = bits 23:16
byte 3 = sign-extension of bit 23
```

- generated sine stream:

```text
sampleRateHz = 48000
channelCount = 2
bitsPerSample = 32
source = 1
```

- generated chirp stream:

```text
sampleRateHz = 48000
channelCount = 2
bitsPerSample = 32
source = 2
```

- generated noise stream:

```text
sampleRateHz = 48000
channelCount = 2
bitsPerSample = 32
source = 3
```

The generated sources use the same 32-bit little-endian container as loopback. The generator control-plane `amplitude` field remains 16-bit and is scaled into the 24-bit sample range inside the device.

Recommended order when using generated sources:

1. Send `SetGeneratorConfig`.
2. Wait for `Response SetGeneratorConfig`.
3. Send `StartStream` with the matching generated `source`.
4. Read audio packets from `AUDIO IN`.
5. Send `StopStream` when done.

### Status Codes

- `0 = Ok`
- `1 = InvalidMagic`
- `2 = InvalidVersion`
- `3 = InvalidType`
- `4 = InvalidLength`
- `5 = UnsupportedCommand`
- `6 = InvalidArgument`

### Important Response/Event Handling Rule

Your C# tool must not assume that the next frame on `CMD IN` is always the response you want.

You must:

1. Read frames from `CMD IN` in a loop.
2. If a frame is an event, handle or log it and continue reading.
3. If a frame is a response, match it by `sequence`.
4. Ignore unrelated responses only if your design allows multiple in-flight commands.

There can also be a trailing event left over from a previous run, for example `StreamStopped`.

## Audio-Plane Protocol

Audio packets use their own header and are sent on `AUDIO OUT` and `AUDIO IN`.

Binary layout:

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

Current loopback payload format is 24-bit PCM carried in a 32-bit little-endian container. The first three bytes of each 32-bit sample contain the valid PCM bits, and the fourth byte is sign-extension.

The same 32-bit container format is used by the device-generated sine, chirp, and noise sources.

Flags:

- `1 << 0 = START_OF_STREAM`
- `1 << 1 = END_OF_STREAM`
- `1 << 2 = DISCONTINUITY`
- `1 << 3 = FORMAT_CHANGE`

Rules:

- `headerSize` must be `24`.
- `magic` must be `0x4155`.
- `version` must be `1`.
- `payloadBytes + headerSize` must equal the total USB packet length.
- `sampleRateHz`, `channelCount`, and `bitsPerSample` must be non-zero.
- The audio format in each packet must match the active stream configuration from `StartStream` when the source is `HostRxLoopback`.

## Stream Lifecycle

The intended stream lifecycle is:

1. Send `StartStream(sampleRateHz, channelCount, bitsPerSample, source)` on the control plane.
2. Wait for `Response StartStream` with `status = Ok`.
3. If `source = HostRxLoopback`, send audio packets on `AUDIO OUT`.
4. If `source = DeviceGeneratedSine`, read generated audio packets from `AUDIO IN`.
5. Stop with either:
   - `StopStream` on the control plane, or
   - `END_OF_STREAM` in the final audio packet.

Important behavior already verified in this project:

- Audio packets are rejected until `StartStream` succeeds.
- Sequence jumps are reported back with `DISCONTINUITY`.
- `END_OF_STREAM` stops the active stream and emits a `StreamStopped` event.
- Format mismatch packets are dropped without an audio echo.

## Device-Generated Audio Using PowerQuad

Yes, that is feasible, but with one important distinction:

PowerQuad does not by itself stream USB audio packets.

What PowerQuad can do is accelerate DSP work on the MCU, for example:

- FFT
- inverse FFT
- filtering
- vector math
- support for generated test signals such as tones, chirps, noise, or synthesized block data

The CPU and the USB transport still have to:

1. Prepare audio blocks in memory.
2. Add the audio stream header.
3. Send the packet on `AUDIO IN`.

### Current Project Behavior

The firmware now supports two sources:

1. `HostRxLoopback`
2. `DeviceGeneratedSine`

The loopback path is still available:

1. The PC starts streaming with `StartStream`.
2. In loopback mode, the PC sends audio packets on `AUDIO OUT`.
3. The device validates the packet.
4. The device copies that packet back to `AUDIO IN`.

The generated path is also available:

1. The PC starts streaming with `StartStream(..., source = DeviceGeneratedSine)`.
2. The device generates a 1 kHz sine wave internally.
3. The device streams those packets on `AUDIO IN`.

Additional generated sources are also available:

- `DeviceGeneratedSine`
- `DeviceGeneratedChirp`
- `DeviceGeneratedNoise`

Use `SetGeneratorConfig` before `StartStream` to control the generated source parameters.

That means the device can now act as either a validated echo path or a simple independent audio source.

### What Would Need To Change

To support a generated FFT/test stream from the device to your C# tool, the firmware needs a transmit-side producer mode.

Implemented first step:

1. `StartStream` now has a `source` field in its payload.
2. `source = DeviceGeneratedSine`, `DeviceGeneratedChirp`, or `DeviceGeneratedNoise` starts a generated stream.
3. `SetGeneratorConfig` configures frequency and amplitude parameters for generated sources.
4. A background task pushes generated packets to `AUDIO IN`.
5. The same audio header is reused.

Recommended future design:

1. Either keep using the `StartStream.source` field, or add dedicated generator commands later.
2. Let the command carry parameters such as:
   - signal type
   - sample rate
   - channel count
   - bits per sample
   - block size
   - tone frequency or sweep range
3. Add a device-side generator service that fills audio buffers periodically.
4. Optionally use PowerQuad to generate or transform the data.
5. Push those generated buffers out on `AUDIO IN` without requiring packets from `AUDIO OUT`.

### Good Generator Modes For Your Tool

If your goal is to test FFT visualization or analysis in the PC tool, these are the most useful generated sources:

- single sine tone
- dual tone
- logarithmic chirp
- white noise
- impulse
- precomputed spectral pattern converted back to time-domain blocks

Those sources are usually more useful than trying to have PowerQuad "invent audio" directly.

### Recommended Architecture

The cleanest architecture is to keep the current transport split and add one more audio source mode:

- `Loopback` mode:
  - current behavior
  - host sends audio on `AUDIO OUT`
  - device echoes it back on `AUDIO IN`
- `Generated` mode:
  - host sends only control commands
  - device generates audio blocks locally
  - device streams them on `AUDIO IN`

That lets your C# tool connect in two ways:

- analysis of host-provided audio
- analysis of device-generated test patterns

### Suggested Control-Plane Extension

One practical option is to extend the control plane with commands like:

- `StartGeneratedStream`
- `StopGeneratedStream`
- `SetGeneratorConfig`

Example generator configuration fields:

- `sourceType`:
  - sine
  - dualTone
  - chirp
  - noise
  - impulse
- `sampleRateHz`
- `channelCount`
- `bitsPerSample`
- `frameSamples`
- `toneFrequencyHz`
- `amplitude`

Another option is to extend `StartStream` with a `source` field:

- `source = HostRxLoopback`
- `source = DeviceGenerated`

That keeps the protocol smaller, but only if you expect a small number of modes.

### How PowerQuad Fits In

PowerQuad is most useful here as an accelerator for DSP processing inside the generation pipeline, not as a transport engine.

Examples:

- generate a test tone, then run PowerQuad FFT on it to verify the expected spectral bins on-device
- generate white noise, then shape it with filtering
- generate blocks in the frequency domain, then transform them to time domain before sending
- compute windowing or vector operations efficiently before packetizing

If your only goal is to feed an FFT display on the PC side, PowerQuad is optional. A simple CPU-based sine or chirp generator is enough to get value quickly.

If your goal is to benchmark or demonstrate the MCU DSP path, then PowerQuad becomes a strong fit.

### USB Behavior For A Generated Stream

For a generated stream, your C# tool would:

1. Connect with WinUSB.
2. Send `StartGeneratedStream` or equivalent.
3. Read audio packets continuously from `AUDIO IN`.
4. Parse the standard audio header exactly as today.
5. Stop the stream with a control-plane command.

In that model, `AUDIO OUT` is not needed during the generated stream session.

### Recommendation

Yes, this platform can absolutely be used to generate synthetic test audio for your C# FFT tool.

The most pragmatic rollout is:

1. Configurable generated sine, chirp, and noise modes without PowerQuad are now implemented.
2. Reuse the existing audio header and USB audio endpoint.
3. Add one new control-plane command to start/stop generated streaming.
4. After that works, optionally move parts of the generation or DSP preprocessing onto PowerQuad.

That gets you a fast proof of concept first, and it keeps the protocol stable for the PC tool.

## Packet Size Rules

Do not hardcode the packet size.

Read `MaximumPacketSize` from WinUSB for each pipe.

Observed values:

- High Speed: `512`
- Full Speed: `64`

Control-plane implication:

- Total control frame size must fit inside the current command pipe packet size.
- The control-frame header is `10` bytes.
- That gives a maximum command payload of:
  - `54` bytes on FS
  - `502` bytes on HS

Audio-plane implication:

- Each audio packet must fit within the current audio pipe packet size.

## Minimal Integration Model

For an existing C# audio tool, the cleanest approach is to wrap the protocol in three layers:

1. `WinUsbTransport`
2. `ControlPlaneClient`
3. `AudioStreamClient`

Suggested responsibilities:

### WinUsbTransport

- device discovery
- `CreateFile`
- `WinUsb_Initialize`
- pipe enumeration
- `WritePipe`
- `ReadPipe`
- timeouts

### ControlPlaneClient

- encode command frames
- decode response/event frames
- sequence tracking
- `GetInfo`
- `GetUsbDebugState`
- `StartStream`
- `StopStream`

### AudioStreamClient

- build audio headers
- track `sequenceNumber`
- track timestamps
- send audio packets
- read returned audio packets if needed

## Practical C# Pseudocode

```csharp
await transport.ConnectAsync();

var info = await controlPlane.GetInfoAsync();

var start = await controlPlane.StartStreamAsync(
    sampleRateHz: 48000,
    channelCount: 2,
  bitsPerSample: 32);

if (start.Status != 0)
{
    throw new InvalidOperationException("StartStream failed");
}

await audio.SendPacketAsync(new AudioPacket
{
    Flags = AudioFlags.StartOfStream,
    SequenceNumber = 0,
    Timestamp = 0,
    ChannelCount = 2,
    BitsPerSample = 32,
    SampleRateHz = 48000,
    Payload = pcmBytes,
});

await controlPlane.StopStreamAsync();
```

## Reference Files In This Repository

Use these files as the authoritative reference when implementing the integration:

- [tools/winusb-endpoint-probe/Program.cs](tools/winusb-endpoint-probe/Program.cs)
- [device_protocol/README.md](device_protocol/README.md)
- [usb_vendor_bulk_descriptor.h](usb_vendor_bulk_descriptor.h)

## Recommended First Bring-Up Sequence

When you connect your other C# tool for the first time, use this order:

1. Open WinUSB successfully.
2. Print discovered pipes and packet sizes.
3. Read one optional initial frame from `CMD IN`.
4. Send `GetInfo`.
5. Send `StartStream(48000, 2, 16)`.
6. Send one audio packet with `START_OF_STREAM`.
7. Verify the device accepts it.
8. Send `StopStream`.
9. Drain any trailing `StreamStopped` event.

That gives you the fastest path to proving your tool is wired correctly.