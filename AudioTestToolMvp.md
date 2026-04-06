# Audio Test Tool MVP

This note defines the minimum useful first version of the PC-side audio test tool.

The MVP is only meant to support the digital loopback reference path.

It should not try to solve generator mode, microphone verification, codec verification, FFT analysis, or full reporting yet.

## MVP Goal

The first version of the tool should answer one simple question reliably:

Can the device run a clean digital loopback session with correct payload round-trip and healthy device-side counters?

## MVP Features

The tool should implement only these core features.

### 1. Connect To Device

The tool must be able to:

- find the device
- open the connection
- discover or confirm the usable endpoints or channels

If this is not stable, nothing else matters.

### 2. Start And Stop Stream

The tool must be able to:

- send stream start
- verify start response
- send stream stop
- verify stop response

This is the minimum session control.

### 3. Build And Send Audio Packets

The tool must be able to:

- build the audio header correctly
- packetize a known payload
- increment sequence numbers correctly
- send a controlled packet burst

Use fixed known settings in the first version.

Recommended initial settings:

- `48000 Hz`
- `2 channels`
- `32-bit container`
- `32 frames per packet`
- `256 bytes payload per packet`

### 4. Receive Returned Audio Packets

The tool must be able to:

- receive looped-back packets
- parse the audio header
- match returned packets to the sent sequence flow

### 5. Check Exact Payload Match

The tool must compare the returned payload against the sent payload.

This is the most important pass or fail condition in MVP scope.

If payloads do not match exactly, the run fails.

### 6. Read Device Metrics

The tool must fetch and print device-side metrics before and after the run.

Minimum metrics to show:

- playback fill level
- playback max fill level
- playback underrun count
- playback overrun count
- playback dropped bytes
- discontinuity count

### 7. Print A Clear Result

The tool must end each run with a simple result:

- `PASS`
- `FAIL`

And on failure it should say why, for example:

- stream start failed
- stop failed
- payload mismatch
- byte count mismatch
- device counters non-zero

## Recommended MVP Run Flow

The MVP should do exactly this:

1. Connect.
2. Read debug state before.
3. Start stream in digital loopback mode.
4. Send a fixed packet burst.
5. Receive returned packets.
6. Compare payloads.
7. Read debug state during or after.
8. Stop stream.
9. Read debug state after.
10. Print `PASS` or `FAIL`.

## What The MVP Does Not Need

The MVP does not need:

- GUI
- plotting
- FFT
- THD
- generator-mode support
- microphone-mode support
- codec-mode support
- advanced report export
- batch regression packs

Those can all wait.

## MVP Success Criteria

The first version is good enough when it can repeatedly:

1. start a loopback session
2. send known packets
3. receive the same packets back
4. show that payloads match exactly
5. show device counters are healthy
6. print a reliable pass or fail summary

Once that works, the tool is already useful.

## Next Step After MVP

The first expansion after MVP should be:

1. latency measurement
2. jitter measurement
3. scripted repeated runs

That is the natural step from “it works” to “it is a real digital reference tool.”