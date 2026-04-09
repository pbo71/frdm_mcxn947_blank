# Position Note: Separate USB Control Plane and Data Plane

## Purpose

This note explains why the device should keep the USB control plane and audio data plane on separate endpoints.

It is written as a practical engineering position, not as a theoretical preference.

The goal is to support a robust firmware architecture for:

- streaming audio transport
- digital loopback reference testing
- future codec and microphone paths
- host-side FFT and analysis workflows
- maintainable long-term verification and diagnostics

## Executive Summary

The control plane and data plane should remain separated on different USB endpoints.

This is the most appropriate design because the two planes have different:

- traffic patterns
- timing sensitivity
- error handling needs
- observability requirements
- scaling behavior

Combining them on a single endpoint may appear to reduce protocol count on the PC side, but in practice it pushes unnecessary complexity into both the firmware and the host application.

The result is not a simpler system. It is a more fragile one.

## What The Two Planes Are For

### Control Plane

The control plane is responsible for:

- start and stop actions
- configuration
- status queries
- device events
- fault reporting
- debug and verification metrics

This traffic is:

- low bandwidth
- bursty
- semantically important
- order-sensitive

### Data Plane

The data plane is responsible for:

- audio packets only
- continuous streaming
- timing-sensitive transport
- predictable packet flow

This traffic is:

- higher bandwidth
- repetitive
- latency-sensitive
- sensitive to jitter and blocking

These are fundamentally different behaviors and should not be forced through the same endpoint.

## Why Separate Endpoints Are Better

There is also a broader engineering principle behind this recommendation:

When USB bulk transport is used for more than one kind of traffic, it is generally best practice to separate distinct data types onto distinct endpoints whenever the device has the endpoint budget to do so.

In this case, command and event traffic is one data type, while streaming audio is another.

That separation improves:

- transport clarity
- parser simplicity
- fault isolation
- timing predictability
- long-term maintainability

So the recommendation in this document is not only project-specific. It is also aligned with a sound general USB bulk design practice.

### 1. It Reduces Head-of-Line Blocking

If control and audio share a single endpoint, small but important control messages can be delayed behind audio packets.

Likewise, audio packets can be delayed by control traffic.

That creates avoidable timing uncertainty in the most time-sensitive path in the system.

With separate endpoints:

- control messages stay responsive
- audio traffic stays continuous
- transport behavior is easier to reason about

### 2. It Keeps Firmware Simpler Where It Matters Most

Firmware is the critical real-time component in this device.

It is responsible for:

- transport handling
- stream state
- buffering
- timing behavior
- codec integration later
- fault containment

If control and data are merged onto one endpoint, firmware must perform more transport-level multiplexing and demultiplexing in the same path that handles streaming behavior.

That increases:

- state complexity
- parser complexity
- corner cases
- debugging cost
- risk of unintended interactions between commands and stream traffic

That is exactly the wrong place to add complexity.

### 3. It Improves Observability And Fault Isolation

When the planes are separated, it is much easier to answer questions such as:

- Is the problem in command handling or in audio transport?
- Did the stream fail to start, or did packets get corrupted later?
- Is a timing issue caused by audio load, or by control-plane traffic?

With a mixed endpoint, those questions become harder to answer because all traffic shares the same transport channel and parser path.

### 4. It Matches The Intended Architecture Of The Device

The device already naturally separates concerns:

- commands and events belong to control logic
- audio packets belong to stream logic

The USB transport should reflect that architecture instead of obscuring it.

Keeping separate endpoints is the transport equivalent of keeping separate modules in software.

### 5. It Scales Better For Future Modes

The current reference implementation already shows why the split is useful.

Future modes are likely to include:

- digital loopback reference mode
- generated reference signal mode
- microphone capture mode
- codec input mode
- daily verification sequences

As more modes are added, a mixed endpoint design becomes harder to extend cleanly because every new mode shares the same framing, queueing, and arbitration path.

Separate endpoints make those expansions much easier to manage.

## Response To The "One More Protocol" Argument

The main argument against separate endpoints is that the PC core team does not want to maintain "another protocol."

That argument sounds reasonable at first, but it does not hold up well technically.

### There Is Not Necessarily Another Protocol

Separate endpoints do not require inventing another control protocol.

They require only a transport split.

The framing can remain very simple:

- control endpoint carries command, response, and event frames
- audio endpoint carries audio frames only

That is not two unrelated protocols.
It is one device architecture with two traffic classes.

### A Single Endpoint Does Not Remove Complexity

A single shared endpoint does not eliminate protocol complexity.

It only relocates it.

Instead of having:

- one clean control path
- one clean audio path

you now need:

- multiplexing rules
- mixed-frame parsing
- interleaving handling
- queue arbitration
- tighter sequencing assumptions
- additional recovery logic when mixed traffic arrives in awkward order

That is not less protocol to maintain.
It is more coupling to maintain.

### The Host Application Is Not The Critical Constraint

In this project, the host application performs analysis such as FFT and verification logic.

That application is important, but it is not the most critical runtime component in the system.

The firmware is the more critical point because it owns:

- the real-time behavior
- the stream boundary conditions
- the transport reliability under load
- future codec and capture integration

If a design choice makes firmware more fragile in order to make the host transport model slightly more uniform, that is a poor trade.

The right trade is to keep firmware robust and explicit, then let the host application adapt to that cleaner design.

## Why This Matters Even More With Codec And Capture Paths

In a more complete audio system based on LPC54607 or LPC54628, the firmware will already need to manage:

- DMA
- I2S or audio interface timing
- codec configuration
- capture and playback buffers
- stream error handling

That makes it even less desirable to blur control and audio transport together.

As soon as real audio hardware paths are introduced, the data plane becomes more timing-sensitive and more structurally different from the control plane.

That strengthens the case for keeping them separate.

## Practical Benefits For Verification

Separate endpoints directly support better verification.

For example:

- digital loopback can be measured without control traffic polluting the data path
- debug and health queries can be issued without disturbing audio packet flow
- device-side counters remain meaningful
- latency and jitter measurements are easier to interpret

This is especially important when the digital loopback path is being used as a reference baseline before analog-path work is added.

## Recommendation

The USB design should keep:

- a dedicated control endpoint for commands, responses, events, and metrics
- a dedicated audio endpoint for audio packets only

This is the most appropriate architecture because it:

- preserves real-time clarity in firmware
- keeps audio transport behavior predictable
- improves diagnosability
- supports cleaner verification flows
- scales better toward codec and capture integration

## Final Position

The proposal to merge control plane and data plane onto a single USB endpoint should be rejected.

The apparent benefit of "one less protocol" is outweighed by the real costs:

- more coupling
- more parser complexity
- weaker isolation
- poorer timing behavior
- more fragile firmware

The firmware is the critical part of this device.
It should not be burdened with avoidable transport coupling merely to simplify the host-side view.

Keeping control and data on separate endpoints is the more robust, more maintainable, and more technically appropriate design.