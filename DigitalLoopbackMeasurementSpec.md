# Digital Loopback Measurement Spec v1.0

This document defines the current reference method for validating the digital USB audio path before SGTL5000 or any analog playback path is added.

The goal is repeatable measurements, not maximum theoretical DSP rigor. The same method should be reused later so digital and analog results can be compared against a known-good baseline.

## Scope

Use this spec for:

- host-generated tone -> `HostRxLoopback` -> echoed capture
- digital FFT verification
- digital THD baseline measurements

Do not use this spec to claim analog codec performance. It is only a transport and analysis baseline.

## Stream Setup

Use these stream settings unless a test explicitly says otherwise:

- `sampleRateHz = 48000`
- `channelCount = 2`
- `bitsPerSample = 32`
- `source = HostRxLoopback`

Payload samples are 24-bit signed PCM carried in 32-bit little-endian slots, MSB-justified in each 32-bit word.

Sample extraction rule:

1. Read each sample as signed `int32` little-endian.
2. Convert to signed 24-bit sample domain using arithmetic shift right by 8.

## Capture Block

Standard capture settings:

- warm-up discard: `1024` samples
- analysis block: `4096` samples
- analysis channel: left channel only for the first-pass reference measurements

The block must be contiguous.

## Packetization

For host transmission tests, audio must be sent as audio packets, not one monolithic byte block.

Reference packetization used in the baseline:

- `32` frames per packet
- stereo 32-bit container format -> `8` bytes per frame
- payload per packet -> `256` bytes

Each audio packet must carry its own audio header, `sequenceNumber`, and `timestamp`.

## FFT Modes

Two FFT modes are part of the reference method.

### Mode A: Bin-aligned reference

- no window
- tone frequency aligned to an FFT bin

Recommended aligned tones for `N = 4096`, `fs = 48000`:

- `503.90625 Hz` (`k = 43`)
- `996.09375 Hz` (`k = 85`)
- `2003.90625 Hz` (`k = 171`)
- `3996.09375 Hz` (`k = 341`)

### Mode B: Non-bin-aligned reference

- Hann window
- tone frequency not aligned to a single FFT bin

Reference tone:

- `1000 Hz`

## Amplitude Set

Reference amplitudes:

- `-12 dBFS`
- `-6 dBFS`
- `-3 dBFS`

These three levels form the minimum baseline matrix.

## Fundamental Estimation

Use a fixed three-bin power sum around the detected peak.

Procedure:

1. Find the peak bin near the expected fundamental.
2. Define the fundamental band as `k-1`, `k`, `k+1`.
3. Sum power, not amplitudes, over those three bins.

Formula:

$$
P_1 = P_{k-1} + P_k + P_{k+1}
$$

Use the same rule for every measurement.

## Harmonic Estimation

For harmonics `H2` through `H5`:

1. Compute the expected harmonic frequency.
2. Find the nearest bin for that harmonic region.
3. Sum power in `k_n-1`, `k_n`, `k_n+1`.

Formula:

$$
P_n = P_{k_n-1} + P_{k_n} + P_{k_n+1}
$$

## THD Calculation

Use the first five harmonics for the reference THD value.

Formula:

$$
THD = \sqrt{\frac{P_2 + P_3 + P_4 + P_5}{P_1}}
$$

Report both percent and dB:

$$
THD_{\%} = 100 \cdot THD
$$

$$
THD_{dB} = 20 \log_{10}(THD)
$$

## Windowing Rule

For the Hann-window mode:

1. Use Hann on the full `4096`-sample block.
2. Keep the same window implementation across all measurements.
3. Keep the same amplitude correction rule across all measurements.

If a fixed amplitude offset remains but is stable, document it and keep the method unchanged until a deliberate v2.0 update is made.

## PASS Criteria

For a digital loopback measurement to pass:

1. `StartStream` returns `OK`.
2. Total transmitted bytes equal total received bytes.
3. Packet payload comparison passes exactly.
4. Peak frequency lands in the expected region.
5. Fundamental level is stable across repeated runs.
6. THD is low and stable across repeated runs.
7. `playbackUnderrunCount = 0`.
8. `playbackOverrunCount = 0`.
9. `playbackDroppedBytes = 0`.

## Reference Matrices

The current baseline is defined by these two matrix types:

1. Bin-aligned matrix:
   - frequencies: `503.90625 Hz`, `996.09375 Hz`, `2003.90625 Hz`
   - amplitudes: `-12 dBFS`, `-6 dBFS`, `-3 dBFS`
2. Non-bin-aligned matrix:
   - frequency: `1000 Hz`
   - amplitudes: `-12 dBFS`, `-6 dBFS`, `-3 dBFS`
   - Hann window

## Interpretation Notes

- The bin-aligned matrix is the best-case digital reference.
- The non-bin-aligned Hann matrix is the realistic analysis reference.
- Do not compare absolute THD numbers between the two modes without noting the different analysis method.
- Later analog SGTL5000 measurements should be compared against this digital baseline, not against an assumed ideal codec number.

## Versioning

If the measurement method changes materially, create `v2.0` rather than silently changing the meaning of `v1.0` results.

The matching release-gate style regression pack for this method is documented in `DigitalLoopbackRegressionPack.md`.