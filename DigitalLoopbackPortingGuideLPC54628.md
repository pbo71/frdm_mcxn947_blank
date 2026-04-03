# Digital Loopback Porting Guide for LPC54628

This note describes what the implemented digital loopback test is, why it is useful, and how to lift the design into a different firmware code base built on LPC54628.

It is intentionally focused on architecture and reuse boundaries, not on MCXN947 board details.

## Purpose

The digital loopback path in this project should be treated as a reference transport and verification path.

Its main value is that it gives a known digital baseline before microphone, codec input, analog gain stages, or external audio equipment are introduced into the measurement chain.

That makes it useful for:

- USB protocol and transport testing
- latency and jitter characterization
- packet integrity and sequence validation
- firmware and host-tool interoperability checks
- daily verification of the digital path
- baseline comparison before analog-path verification is added

It should not be treated as evidence of analog audio performance.

Related reference documents in this repository:

- [DigitalLoopbackMeasurementSpec.md](DigitalLoopbackMeasurementSpec.md)
- [DigitalLoopbackRegressionPack.md](DigitalLoopbackRegressionPack.md)

## What The Current Implementation Does

At a high level, the current implementation provides a USB vendor bulk transport split into two planes:

- control plane for commands, responses, and events
- audio plane for audio packets only

In loopback mode, the host sends audio packets to the device, the device validates them, updates stream state, writes payload into a playback-style buffer, and returns a matching audio packet back to the host.

The implementation also tracks stream health and error conditions such as:

- invalid audio headers
- format mismatches
- sequence discontinuities
- underrun and overrun conditions
- dropped bytes

The core implementation is spread across these files:

- [services/audio_stream_format.h](services/audio_stream_format.h)
- [services/audio_stream_service.h](services/audio_stream_service.h)
- [services/audio_stream_service.cpp](services/audio_stream_service.cpp)
- [services/audio_playback_buffer.h](services/audio_playback_buffer.h)
- [services/audio_playback_buffer.cpp](services/audio_playback_buffer.cpp)
- [audio_tx_service.cpp](audio_tx_service.cpp)

The USB transport binding that connects the services to the MCXN947 USB device stack is in:

- [usb_vendor_bulk.c](usb_vendor_bulk.c)
- [usb_hw_init.c](usb_hw_init.c)

## Why This Is Worth Reusing

The most important thing to reuse is not the MCXN947 transport code itself, but the test model.

That model gives you a clean digital reference path with explicit behavior:

1. Streaming must be started before audio is accepted.
2. Each audio packet is self-describing through its header.
3. Packet sequence is monitored.
4. Format changes and discontinuities are visible.
5. Buffer health is measurable.
6. The host tool can verify exact payload round-trip behavior.

For an LPC54628-based system, this is valuable because it lets you answer basic confidence questions before analog testing begins:

- Does USB transport still work correctly?
- Is latency stable?
- Is jitter inside expected limits?
- Are packets dropped or reordered?
- Are stream settings negotiated and enforced correctly?

## What To Reuse Directly

These parts are the best candidates for direct or near-direct reuse in an LPC54628 code base.

### 1. Audio Packet Format

Reuse the audio packet header contract from [services/audio_stream_format.h](services/audio_stream_format.h).

That includes:

- magic
- version
- header size
- flags
- sequence number
- timestamp
- payload byte count
- channel count
- bits per sample
- sample rate

This is small, self-contained, and easy to carry into another transport layer.

### 2. Stream State Machine

Reuse the stream behavior from [services/audio_stream_service.cpp](services/audio_stream_service.cpp).

The useful behavior is:

- reject audio before stream start
- validate incoming audio packets
- enforce active stream format
- detect discontinuities
- mark first packet and end-of-stream conditions
- maintain generated-source support if needed later

Even if your LPC code base keeps its own protocol, the internal stream rules are still useful.

### 3. Playback Buffer Pattern

Reuse the ring-buffer pattern from [services/audio_playback_buffer.cpp](services/audio_playback_buffer.cpp).

The useful features are:

- format-aware buffer sizing
- whole-frame trimming
- fill-level tracking
- min and max fill tracking
- underrun count
- overrun count
- dropped-byte count
- start-threshold logic

This pattern is useful both for USB receive loopback testing and for real playback-side smoothing.

### 4. Periodic Drain Model

Reuse the idea in [audio_tx_service.cpp](audio_tx_service.cpp), even if the implementation changes.

The important part is not the exact task, but the concept:

- one producer fills the buffer
- one time-based consumer drains it at the configured audio rate
- metrics become meaningful because data is consumed at a realistic pace

That is a good test harness even before a true codec playback path is wired in.

## What Not To Reuse Directly

The following parts should not be copied directly into an LPC54628 code base.

### 1. MCXN947 Board Support

Do not carry over:

- [frdmmcxn947_cm33_core0/frdmmcxn947/board.h](frdmmcxn947_cm33_core0/frdmmcxn947/board.h)
- [frdmmcxn947_cm33_core0/frdmmcxn947/board.c](frdmmcxn947_cm33_core0/frdmmcxn947/board.c)
- [frdmmcxn947_cm33_core0/frdmmcxn947/clock_config.c](frdmmcxn947_cm33_core0/frdmmcxn947/clock_config.c)
- [frdmmcxn947_cm33_core0/audio_stream_demo_app/pin_mux.c](frdmmcxn947_cm33_core0/audio_stream_demo_app/pin_mux.c)

Those files are board- and SoC-specific.

### 2. MCXN947 USB Initialization

Do not copy [usb_hw_init.c](usb_hw_init.c) directly.

That file contains MCXN947-specific USB PHY, clock, and interrupt setup.

### 3. The Existing Command Protocol Binding

Do not adopt the control-plane design if the LPC code base already has its own command and event protocol.

Instead, keep the existing LPC protocol and let it call into the loopback services internally.

## Recommended LPC54628 Integration Strategy

If the target code base already has its own command and event protocol, the cleanest approach is to port the digital loopback as an internal service layer.

### Suggested Layering

1. Existing LPC command and event protocol
2. Audio test controller
3. Audio stream and buffer services
4. LPC54628 USB transport and hardware drivers

This means:

- your wire protocol stays unchanged
- your host integration does not need a protocol rewrite
- only the internal implementation behind relevant commands changes

### Suggested Internal Commands or Actions

Your existing protocol should trigger operations equivalent to:

- start digital loopback
- stop digital loopback
- get stream health metrics
- get latency and jitter metrics if measured on the host side
- optionally configure generated reference signal mode

You do not need the same names as this repository. You only need the same functional behavior.

## Suggested LPC54628 Module Split

One practical way to carry this over is to create a small group of platform-neutral audio test modules and platform-specific adapters.

### Platform-Neutral Modules

- `audio_stream_format.h`
- `audio_stream_service.c/.cpp`
- `audio_playback_buffer.c/.cpp`
- optional `audio_capture_buffer.c/.cpp`
- optional `audio_test_controller.c/.cpp`

### LPC54628-Specific Modules

- `usb_audio_transport_lpc54628.c/.cpp`
- `codec_capture_adapter_lpc54628.c/.cpp`
- `playback_adapter_lpc54628.c/.cpp`
- command handler glue for the existing LPC protocol

The boundary should be simple:

- platform-neutral modules know nothing about LPC registers or SDK calls
- LPC modules know how to move data to and from USB, DMA, I2S, codec, and tasking primitives

## Daily Verification Role

For the LPC54628 system, digital loopback should be kept as one part of a daily verification flow.

It is a good fit for checking:

- USB session start and stop health
- transport stability
- repeatable round-trip packet behavior
- stream counters staying within limits
- expected latency and jitter band

It should be treated as a digital confidence check, not as a full analog-path verification.

If the device will also verify microphones or codec inputs, then digital loopback should be combined with separate analog-path checks.

## Why A Capture Buffer Is Also Likely Needed

This project currently contains a playback-side ring buffer. In an LPC54628 system that must test microphones or codec inputs, a capture-side buffer is usually also needed.

Reason:

- microphone or codec input produces samples continuously
- USB transmit consumes samples in packets and under host timing
- those two timing domains are not identical

So the LPC target will likely need:

- a playback buffer for USB RX to device-side consumption
- a capture buffer for device-side audio capture to USB TX

The digital loopback path remains useful as the reference mode, but analog capture modes normally require their own producer-to-USB buffer.

## Suggested Porting Phases

### Phase 1: Lift The Digital Contract

Port only:

- audio stream header
- stream validation rules
- sequence handling
- playback buffer
- loopback response path

Goal:

- exact digital round-trip behavior on LPC54628

### Phase 2: Reconnect To Existing LPC Protocol

Add command glue in the LPC code base so existing commands can:

- start loopback
- stop loopback
- query counters and state

Goal:

- no protocol redesign

### Phase 3: Add Measurement Hooks

Add stable metrics for:

- latency
- jitter
- underrun count
- overrun count
- dropped bytes
- discontinuity count

Goal:

- use the loopback path for USB and firmware performance verification

### Phase 4: Add Analog Verification Paths

After digital reference mode is stable, add:

- microphone capture mode
- codec input mode
- optional generated reference signal mode

Goal:

- daily verification becomes broader than transport-only testing

## Minimum Success Criteria After Porting

The LPC54628 port of the digital loopback should be considered successful only when all of the following are true:

1. Stream start succeeds reliably.
2. Stream stop succeeds reliably.
3. Packet payload round-trip matches exactly in reference mode.
4. No unexpected discontinuities are reported.
5. Underrun, overrun, and dropped-byte counts remain zero in the reference case.
6. Measured latency and jitter are repeatable across runs.
7. The host-side regression flow can be rerun without changing the LPC command protocol.

## Final Recommendation

Treat the current digital loopback implementation as a reusable reference design for digital audio transport verification.

For LPC54628:

- port the stream rules and metrics
- keep the existing command and event protocol
- rewrite only the platform adapters
- preserve digital loopback as a permanent reference mode
- build daily verification on top of it, not instead of it

That approach gives the best balance of reuse, low risk, and long-term test value.