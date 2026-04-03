# LPC54628 Digital Loopback Implementation Checklist

This checklist turns the digital loopback porting note into a concrete implementation plan for an LPC54628-based code base.

Use it as a working checklist when lifting the reference design into the target project.

Related documents:

- [DigitalLoopbackPortingGuideLPC54628.md](DigitalLoopbackPortingGuideLPC54628.md)
- [DigitalLoopbackMeasurementSpec.md](DigitalLoopbackMeasurementSpec.md)
- [DigitalLoopbackRegressionPack.md](DigitalLoopbackRegressionPack.md)

## 1. Define The Target Scope

Before writing code, lock the first LPC54628 target scope.

Minimum recommended first scope:

- digital loopback only
- existing command/event protocol unchanged
- USB transport on LPC54628
- same audio header format as the reference implementation
- host-driven performance measurements for latency and jitter

Do not mix in microphone capture, codec capture, or analog verification in the first porting step.

## 2. Freeze The Internal Contract

Define the internal audio loopback contract that the LPC code base will support.

Recommended contract:

- stream must be started before audio packets are accepted
- audio packets must carry a fixed header
- active stream format must be tracked
- sequence numbers must be validated
- discontinuities must be surfaced
- health counters must be queryable

Recommended core fields:

- sample rate
- channel count
- bits per sample
- source or mode
- packet sequence number
- packet timestamp
- payload size

## 3. Create Platform-Neutral Modules

Create these reusable modules inside the LPC54628 project.

### Required

- `audio_stream_format.h`
- `audio_stream_service.h`
- `audio_stream_service.c` or `.cpp`
- `audio_playback_buffer.h`
- `audio_playback_buffer.c` or `.cpp`

### Optional But Recommended

- `audio_test_metrics.h`
- `audio_test_metrics.c` or `.cpp`
- `audio_test_controller.h`
- `audio_test_controller.c` or `.cpp`

These files should not include LPC register headers directly.

## 4. Move The Audio Header Format First

Port the audio header definition from the reference implementation first.

Expected checks:

- packed header size remains `24` bytes
- magic and version constants are preserved
- flag layout is preserved
- sample extraction rules are documented in the LPC project

This is the smallest and least risky first step.

## 5. Port The Playback Buffer

Port the ring buffer behavior from the reference playback buffer.

Required behavior:

- whole-frame writes only
- whole-frame reads only
- buffer reset
- current fill-level reporting
- min fill-level reporting
- max fill-level reporting
- underrun count
- overrun count
- dropped-byte count
- start-threshold reporting

Recommended API shape:

- `Init`
- `Reset`
- `Configure`
- `Write`
- `Read`
- `GetFillLevel`
- `GetMinFillLevel`
- `GetMaxFillLevel`
- `GetUnderrunCount`
- `GetOverrunCount`
- `GetDroppedBytes`

## 6. Port The Stream Service

Port the stream-state logic next.

Required behavior:

- stream start
- stream stop
- transport reset handling
- transport ready handling
- packet header validation
- format validation
- format mismatch handling
- sequence validation
- discontinuity reporting
- end-of-stream handling

For the first LPC54628 port, generated audio support can be deferred if needed.

## 7. Define The LPC Transport Adapter

Create an LPC-specific adapter that connects USB packet I/O to the platform-neutral stream service.

Recommended responsibilities:

- receive audio packet from LPC USB stack
- pass received packet into `AudioStreamService_HandleRxPacket(...)`
- send the returned packet back through the LPC USB IN endpoint
- notify the stream service when transport is ready or reset

Keep this adapter thin.

Do not place stream rules inside the USB callback layer.

## 8. Keep The Existing Command Protocol

Do not port the MCX command protocol.

Instead, map existing LPC commands to equivalent actions.

Minimum actions to expose:

- start digital loopback
- stop digital loopback
- query stream status
- query buffer metrics
- query transport metrics

Optional actions:

- reset metrics
- configure generated reference mode later

## 9. Add A Periodic Drain Task

If the LPC loopback design uses a buffer, add a periodic task or equivalent scheduler callback that drains the playback-side buffer at the configured audio rate.

Purpose:

- model device-side consumption
- make underrun and overrun counters meaningful
- support latency and jitter measurements under realistic pacing

Minimum task inputs:

- sample rate
- bytes per frame
- elapsed period

Minimum task outputs:

- drained byte count
- updated fill levels

## 10. Add Metric Collection

Expose the following minimum metrics in the LPC target project.

### Required Device Metrics

- stream active or inactive
- configured format
- playback fill level
- playback min fill
- playback max fill
- playback underrun count
- playback overrun count
- playback dropped bytes
- discontinuity count

### Recommended Host-Side Metrics

- round-trip latency
- latency min and max
- latency mean
- jitter estimate
- payload match result
- byte count match result

The device should own stream-health counters.
The host should own round-trip timing analysis unless the target has a clear on-device timing reference for it.

## 11. Define Reference Test Modes

Create explicit mode names in the LPC code base.

Recommended first mode:

- `DigitalLoopbackReference`

Recommended later modes:

- `GeneratedReferenceSignal`
- `MicCaptureToUsb`
- `CodecInToUsb`

This keeps the reference path isolated from later analog verification paths.

## 12. Build A Minimal Bring-Up Sequence

The LPC54628 implementation should be brought up in this order.

1. Enumerate USB successfully.
2. Start a stream successfully using the existing LPC command protocol.
3. Send one well-formed audio packet.
4. Verify one matching audio packet is returned.
5. Repeat with a packet burst.
6. Verify sequence continuity.
7. Verify counters remain zero in the healthy reference case.
8. Stop the stream cleanly.

Do not start with FFT, THD, or analog verification.

## 13. Run Negative Tests Early

Before calling the port complete, run deliberate failure cases.

Minimum negative cases:

- send audio before stream start
- send packet with bad magic
- send packet with bad version
- send packet with bad payload length
- send packet with wrong format
- send sequence numbers with a gap
- stop stream twice

Verify that the LPC implementation reacts deterministically.

## 14. Match The Existing Measurement Spec

Once reference loopback is stable on LPC54628, rerun the digital measurement method from:

- [DigitalLoopbackMeasurementSpec.md](DigitalLoopbackMeasurementSpec.md)
- [DigitalLoopbackRegressionPack.md](DigitalLoopbackRegressionPack.md)

Goal:

- reuse the same host-side logic and reporting style
- compare LPC results against the same digital baseline method

Do not silently change the analysis method while porting the firmware.

## 15. Prepare For Daily Verification

After the LPC digital loopback path is stable, define how it will participate in daily verification.

Recommended daily-verification role:

- confirm USB and stream startup health
- confirm packet round-trip integrity
- confirm no discontinuities or buffer faults
- confirm latency and jitter remain inside expected bounds

This should be recorded as a digital verification step, not as full analog-path verification.

## 16. Plan The Second Buffer Early

If the LPC54628 system will later test microphone or codec input, plan for a capture-side buffer even if it is not implemented in phase 1.

Expected later module:

- `audio_capture_buffer.h`
- `audio_capture_buffer.c` or `.cpp`

Expected responsibilities:

- accept DMA or ISR-produced capture blocks
- present packet-sized reads to USB TX
- track fill levels and faults

## 17. Define Exit Criteria

The digital loopback port should be called done only when all of these are true.

1. USB stream start and stop are stable.
2. Payload round-trip matches exactly.
3. Reference packet sequence handling is correct.
4. Healthy-case counters remain zero.
5. Negative tests fail in the expected way.
6. Host-side latency and jitter measurements are repeatable.
7. The path can be rerun as part of a daily verification workflow.

## 18. Final Engineering Rule

When porting to LPC54628, keep this separation strict:

- reuse logic and measurement intent
- rewrite platform adapters
- preserve the existing LPC command protocol

That is the shortest path to carrying over the value of the implemented digital loopback without dragging MCXN947-specific design decisions into the target code base.