# Audio Test Tool Implementation Notes

This note describes what the PC-side audio test tool should implement to work well with the digital loopback reference path and later audio verification paths.

The focus is on practical host-side functionality, not on a specific programming language.

## Purpose

The tool should support three main jobs:

1. drive the device through controlled audio test sessions
2. measure digital transport behavior such as payload integrity, latency, and jitter
3. provide a foundation for later daily verification and analog-path checks

For now, the most important reference mode is digital loopback.

## Minimum Functional Scope

The first useful version of the tool should support:

- connect to the device
- start and stop an audio test session
- send audio packets to the device
- receive returned audio packets from the device
- compare sent and received payloads
- read debug or metrics state from the device
- log a clear pass or fail result

This is enough to support the digital reference path.

## Recommended Internal Layers

The tool should ideally be split into three layers.

### 1. Transport Layer

Responsibilities:

- open the device connection
- enumerate endpoints or channels
- send bytes reliably
- receive bytes reliably
- apply timeouts
- report packet-size limits

For the current reference implementation this maps well to the split between control traffic and audio traffic.

### 2. Session Control Layer

Responsibilities:

- start a test session
- stop a test session
- read device metrics
- reset or drain stale state if needed
- manage sequence numbers for commands if the protocol uses them

This layer should hide protocol details from the measurement logic.

### 3. Measurement Layer

Responsibilities:

- generate test audio blocks
- packetize them
- transmit them in a controlled burst or stream
- receive looped-back packets
- compare payloads
- compute latency and jitter
- classify pass or fail

## Minimum Session Flow

For digital loopback, the host tool should support this session flow.

1. Connect to the device.
2. Read device info and packet-size limits.
3. Read one initial debug state if useful.
4. Start stream in digital loopback mode.
5. Send a defined packet burst.
6. Receive returned packets.
7. Compare packet count and payload content.
8. Read debug state during or after the run.
9. Stop stream.
10. Read final debug state.
11. Report pass or fail.

The tool should be able to run this flow repeatably without manual intervention.

## Audio Packet Support

The tool must be able to:

- build the audio packet header
- serialize sample payloads correctly
- track packet sequence numbers
- track per-packet timestamps
- parse returned packet headers
- detect flags such as start-of-stream, end-of-stream, discontinuity, and format change

For digital loopback, payload comparison should normally be exact.

## Required Measurements

The tool should provide at least these measurements in digital loopback mode.

### Required

- packet count sent
- packet count received
- total bytes sent
- total bytes received
- payload match result
- stream start result
- stream stop result

### Strongly Recommended

- round-trip latency
- latency min
- latency max
- average latency
- jitter estimate
- discontinuity count observed on returned packets

These measurements make the loopback useful as a digital reference.

## Required Device Metrics To Read

The host tool should fetch and display device-side metrics such as:

- playback fill level
- playback max fill level
- playback underrun count
- playback overrun count
- playback dropped bytes
- discontinuity count

If more firmware-side reject counters are added later, the tool should surface those too.

The host tool should treat device metrics as part of the pass or fail logic, not just as debug text.

## Suggested Result Classification

Each run should end in one of a few clear result classes.

- `Pass`
- `TransportFailure`
- `PayloadMismatch`
- `LatencyFailure`
- `JitterFailure`
- `DeviceHealthFailure`
- `ProtocolFailure`

This makes logs and regression runs much easier to interpret.

## Minimum Output Per Test Run

For each run, the tool should log:

- selected mode
- sample rate
- channel count
- bits per sample
- packet payload bytes
- packet count planned
- packet count received
- payload match result
- total bytes sent
- total bytes received
- latency summary
- jitter summary
- device metrics before the run
- device metrics during or after the run
- final result

This should be human-readable first. Export formats can come later.

## Recommended Modes

The tool should eventually support explicit modes rather than one generic test action.

### Phase 1

- `DigitalLoopbackReference`

### Phase 2

- `GeneratedReferenceSignal`
- `LoopbackStress`

### Phase 3

- `MicCaptureVerification`
- `CodecInputVerification`

Separate modes keep digital reference work from being mixed with analog verification.

## Digital Loopback Reference Mode

This should be the first complete mode.

It should:

- send controlled packets to the device
- receive the same packets back
- verify exact payload match
- compute transport metrics
- read device-side counters

This mode is the baseline that later microphone and codec tests should be compared against.

## Generated Reference Mode

If the firmware supports generated audio, the host tool should also support a receive-only reference mode.

It should:

- configure the generated source
- start stream in generated mode
- receive audio packets from the device
- analyze frequency, amplitude, and continuity
- stop stream cleanly

This is useful when no host-to-device payload generation is needed.

## Daily Verification Role

Later, the host tool should support a compact daily verification sequence.

In that context the tool should:

- run the digital loopback reference mode
- verify device-side counters are healthy
- verify latency and jitter stay inside expected bounds
- record a timestamped result

If analog paths are added later, the tool can extend the sequence with microphone or codec-specific checks.

## Recommended First Implementation Priority

The best first implementation order is:

1. connection and transport setup
2. stream start and stop handling
3. audio packet build and parse support
4. exact payload match reporting
5. device metrics fetch and display
6. latency and jitter measurement
7. scripted repeatable test runs

That gives useful value quickly without overbuilding the tool.

## What The Tool Does Not Need First

The first version does not need:

- full GUI complexity
- advanced plotting
- full FFT or THD support
- analog-path calibration features
- report export to many formats

Those can come later.

The first goal is a solid digital reference and performance tool.

## Final Recommendation

Build the audio test tool first as a deterministic digital loopback driver and observer.

If it can:

- start a session
- send known payloads
- receive exact loopback payloads
- read device health metrics
- measure latency and jitter
- emit a clear pass or fail result

then it is already delivering most of the value needed for the current reference path.

Everything else can be layered on top of that foundation.