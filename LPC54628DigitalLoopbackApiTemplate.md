# LPC54628 Digital Loopback API Template

This document proposes a small internal API for carrying the digital loopback design into an LPC54628-based code base.

The goal is not to reproduce the exact file structure from this repository.
The goal is to define a clean set of internal interfaces that preserve the useful behavior.

This API template assumes:

- the LPC54628 project keeps its existing command and event protocol
- digital loopback is implemented as an internal service
- USB transport is adapted separately from the core stream logic
- later microphone and codec-input modes may be added without redesigning the digital reference path

Related documents:

- [DigitalLoopbackPortingGuideLPC54628.md](DigitalLoopbackPortingGuideLPC54628.md)
- [LPC54628DigitalLoopbackImplementationChecklist.md](LPC54628DigitalLoopbackImplementationChecklist.md)

## 1. Design Goals

The internal API should make these responsibilities explicit:

1. stream lifecycle
2. packet validation
3. loopback packet generation
4. playback-side buffering and metrics
5. transport notifications
6. daily-verification-friendly metrics access

The API should not depend directly on:

- LPC registers
- USB endpoint numbers
- command opcode values
- board-specific headers

Those belong in adapters.

## 2. Recommended Module Split

Recommended internal modules:

- `audio_stream_format.h`
- `audio_loopback_types.h`
- `audio_playback_buffer.h`
- `audio_loopback_service.h`
- `audio_loopback_metrics.h`
- `audio_loopback_controller.h`
- `usb_audio_transport_adapter.h`

Recommended LPC-specific glue:

- `usb_audio_transport_adapter_lpc54628.c`
- `command_audio_test_glue.c`
- `audio_clock_or_task_adapter.c`

## 3. Suggested Core Types

### Stream Format

```c
typedef struct audio_stream_config
{
    uint32_t sampleRateHz;
    uint8_t channelCount;
    uint8_t bitsPerSample;
    uint8_t mode;
    uint8_t reserved;
} audio_stream_config_t;
```

Suggested mode values:

```c
enum
{
    AUDIO_TEST_MODE_DIGITAL_LOOPBACK = 0U,
    AUDIO_TEST_MODE_GENERATED_REFERENCE = 1U,
    AUDIO_TEST_MODE_MIC_CAPTURE = 2U,
    AUDIO_TEST_MODE_CODEC_CAPTURE = 3U,
};
```

### Stop Reason

```c
enum
{
    AUDIO_STREAM_STOP_REASON_TRANSPORT_RESET = 1U,
    AUDIO_STREAM_STOP_REASON_END_OF_STREAM = 2U,
    AUDIO_STREAM_STOP_REASON_COMMAND = 3U,
    AUDIO_STREAM_STOP_REASON_INTERNAL_FAULT = 4U,
};
```

### Metrics

```c
typedef struct audio_loopback_metrics
{
    uint32_t streamActive;
    uint32_t packetsReceived;
    uint32_t packetsTransmitted;
    uint32_t discontinuityCount;
    uint32_t invalidHeaderCount;
    uint32_t invalidLengthCount;
    uint32_t formatMismatchCount;
    uint32_t playbackFillLevelBytes;
    uint32_t playbackMinFillLevelBytes;
    uint32_t playbackMaxFillLevelBytes;
    uint32_t playbackUnderrunCount;
    uint32_t playbackOverrunCount;
    uint32_t playbackDroppedBytes;
} audio_loopback_metrics_t;
```

This is intentionally operational rather than protocol-specific.

## 4. Suggested Playback Buffer API

This is the ring buffer used for the USB RX side of digital loopback or any playback-like consumption path.

```c
void AudioPlaybackBuffer_Init(void);
void AudioPlaybackBuffer_Reset(void);

bool AudioPlaybackBuffer_Configure(uint32_t sampleRateHz,
                                   uint8_t channelCount,
                                   uint8_t bitsPerSample);

bool AudioPlaybackBuffer_IsConfigured(void);

uint32_t AudioPlaybackBuffer_Write(const uint8_t *data,
                                   uint32_t lengthBytes);

uint32_t AudioPlaybackBuffer_Read(uint8_t *data,
                                  uint32_t capacityBytes);

uint32_t AudioPlaybackBuffer_GetFillLevelBytes(void);
uint32_t AudioPlaybackBuffer_GetMinFillLevelBytes(void);
uint32_t AudioPlaybackBuffer_GetMaxFillLevelBytes(void);
uint32_t AudioPlaybackBuffer_GetUnderrunCount(void);
uint32_t AudioPlaybackBuffer_GetOverrunCount(void);
uint32_t AudioPlaybackBuffer_GetDroppedBytes(void);
uint32_t AudioPlaybackBuffer_GetBytesPerFrame(void);
uint32_t AudioPlaybackBuffer_GetSampleRateHz(void);
bool AudioPlaybackBuffer_StartThresholdReached(void);
```

This module should remain platform-neutral.

## 5. Suggested Loopback Service API

This is the central service that owns stream state and packet validation.

```c
void AudioLoopbackService_Init(void);

void AudioLoopbackService_OnTransportReady(uint16_t maxAudioPacketSize);
void AudioLoopbackService_OnTransportReset(void);

bool AudioLoopbackService_Start(const audio_stream_config_t *config);
bool AudioLoopbackService_Stop(uint32_t stopReason);

bool AudioLoopbackService_IsActive(void);
bool AudioLoopbackService_GetActiveConfig(audio_stream_config_t *config);

bool AudioLoopbackService_HandleRxPacket(const uint8_t *rxPacket,
                                         uint32_t rxLength,
                                         uint8_t *txPacket,
                                         uint32_t txCapacity,
                                         uint32_t *txLength);

void AudioLoopbackService_GetMetrics(audio_loopback_metrics_t *metrics);
void AudioLoopbackService_ResetMetrics(void);
```

Expected behavior:

- `Start` validates stream configuration
- `HandleRxPacket` validates packet structure and active format
- `HandleRxPacket` returns a ready-to-send loopback packet when valid
- `Stop` terminates cleanly and preserves final metrics

## 6. Suggested Optional Generator API

If generated reference mode is kept, isolate it behind an optional API.

```c
typedef struct audio_generator_config
{
    uint32_t primaryFrequencyHz;
    uint32_t secondaryFrequencyHz;
    uint32_t modulationPeriodMs;
    uint16_t amplitude;
    uint8_t source;
    uint8_t noiseType;
    uint8_t amplitudeEnvelope;
    uint8_t reserved;
    uint32_t noiseSeed;
} audio_generator_config_t;

bool AudioLoopbackService_SetGeneratorConfig(const audio_generator_config_t *config);

bool AudioLoopbackService_TryBuildGeneratedPacket(uint8_t *txPacket,
                                                  uint32_t txCapacity,
                                                  uint32_t *txLength);
```

This should stay optional for the first LPC54628 digital loopback port.

## 7. Suggested Controller API

This is the adapter-facing API that your existing command protocol can call.

```c
void AudioTestController_Init(void);

bool AudioTestController_StartDigitalLoopback(uint32_t sampleRateHz,
                                              uint8_t channelCount,
                                              uint8_t bitsPerSample);

bool AudioTestController_Stop(uint32_t stopReason);

bool AudioTestController_GetMetrics(audio_loopback_metrics_t *metrics);
bool AudioTestController_ResetMetrics(void);

bool AudioTestController_GetStreamConfig(audio_stream_config_t *config);
bool AudioTestController_IsActive(void);
```

This controller can later grow to support:

- start generated reference mode
- start microphone capture mode
- start codec capture mode

The controller should be the stable entry point for command handlers.

## 8. Suggested USB Adapter API

The LPC USB transport should live behind a thin adapter.

```c
void UsbAudioTransportAdapter_Init(void);
void UsbAudioTransportAdapter_OnConfigured(uint16_t maxPacketSize);
void UsbAudioTransportAdapter_OnReset(void);

void UsbAudioTransportAdapter_OnAudioOutPacket(const uint8_t *packet,
                                               uint32_t length);

bool UsbAudioTransportAdapter_TrySendPendingAudioIn(void);
```

Expected behavior:

- receive packet from LPC USB stack
- call `AudioLoopbackService_HandleRxPacket(...)`
- if a TX packet is produced, queue or send it on the USB IN endpoint
- notify the service when transport resets

This adapter owns endpoint-level details.
The loopback service must not.

## 9. Suggested Task Or Timer Hook

If the design needs playback-rate drain behavior, keep it outside the loopback service.

```c
void AudioPlaybackDrainTask_RunPeriodically(uint32_t elapsedMs);
```

Expected role:

- read configured sample rate
- compute bytes to consume for the elapsed period
- drain the playback buffer
- make underrun and overrun behavior measurable

This keeps pacing logic separate from packet validation.

## 10. Suggested Command-Glue Mapping

Your existing LPC command handlers can map to the controller like this.

### Example Actions

- `CmdStartUsbLoopback` -> `AudioTestController_StartDigitalLoopback(...)`
- `CmdStopUsbLoopback` -> `AudioTestController_Stop(...)`
- `CmdGetAudioTestMetrics` -> `AudioTestController_GetMetrics(...)`
- `CmdResetAudioTestMetrics` -> `AudioTestController_ResetMetrics()`

If the current protocol already has start/stop and status commands, reuse them.

## 11. Suggested File Ownership Rules

To avoid future architecture drift, keep these rules explicit.

### Platform-Neutral Files May

- know audio formats
- know stream state
- know metrics
- know buffer math

### Platform-Neutral Files May Not

- include LPC SDK headers
- call USB endpoint APIs directly
- inspect command opcodes
- read hardware registers

### LPC Adapter Files May

- include LPC USB and SDK headers
- manage endpoint state
- call RTOS or scheduler hooks
- translate command actions into controller calls

## 12. Suggested Minimum Bring-Up API Flow

Recommended runtime flow:

1. `AudioTestController_Init()`
2. `UsbAudioTransportAdapter_Init()`
3. USB becomes configured
4. `UsbAudioTransportAdapter_OnConfigured(maxPacketSize)`
5. command handler calls `AudioTestController_StartDigitalLoopback(...)`
6. host sends audio packet
7. USB adapter receives packet and calls `AudioLoopbackService_HandleRxPacket(...)`
8. returned packet is sent on USB IN
9. command handler queries metrics through `AudioTestController_GetMetrics(...)`
10. command handler calls `AudioTestController_Stop(...)`

## 13. Suggested Future Extension Point For Capture

If microphone or codec capture is added later, add a separate capture buffer and separate service entry points rather than overloading the loopback API.

Suggested future API:

```c
void AudioCaptureBuffer_Init(void);
bool AudioCaptureBuffer_Configure(uint32_t sampleRateHz,
                                  uint8_t channelCount,
                                  uint8_t bitsPerSample);

uint32_t AudioCaptureBuffer_WriteFromIsr(const uint8_t *data,
                                         uint32_t lengthBytes);

uint32_t AudioCaptureBuffer_ReadForUsbTx(uint8_t *data,
                                         uint32_t capacityBytes);
```

That keeps the digital reference loopback path conceptually separate from analog-source capture.

## 14. Recommended First Implementation Cut

For the first LPC54628 implementation, build only this subset:

- `audio_stream_format.h`
- `audio_playback_buffer.*`
- `audio_loopback_service.*`
- `audio_loopback_metrics.h`
- `audio_test_controller.*`
- `usb_audio_transport_adapter_lpc54628.*`

Skip for now:

- generator mode
- microphone capture
- codec capture
- complex eventing beyond simple status reporting

That gives the shortest path to a useful digital reference mode.

## 15. Final Recommendation

Use this API template as an internal architectural boundary, not as a public SDK.

If the LPC54628 code base follows this shape, you should be able to:

- keep the existing command and event protocol
- preserve the reference digital loopback behavior
- measure USB transport quality and stability
- extend later toward daily verification and analog-path checks

without pulling MCXN947-specific structure into the target project.