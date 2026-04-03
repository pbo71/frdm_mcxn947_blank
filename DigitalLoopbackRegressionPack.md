# Digital Loopback Regression Pack v1.0

This document defines the minimum regression test pack for the digital USB loopback path.

It is based on `DigitalLoopbackMeasurementSpec.md` and is intended to be used by the host audio tool as a repeatable release gate after firmware or tool changes.

## Purpose

The pack verifies three things:

1. USB audio transport correctness
2. Sample-format correctness for 24-bit audio in 32-bit slots
3. FFT and THD analysis stability against the baseline v1.0 method

This is still a digital-only gate. It must not be used as evidence of analog codec performance.

## Global Pass Conditions

Every test in this pack must satisfy all of the following:

1. `StartStream` returns `OK`
2. `StopStream` returns `OK`
3. transmitted byte count equals received byte count
4. payload comparison passes exactly
5. `playbackUnderrunCount = 0`
6. `playbackOverrunCount = 0`
7. `playbackDroppedBytes = 0`

If any one of these fails, the test result is `FAIL` regardless of FFT or THD values.

## Recommended Transport Settings

Use these settings unless the pack is intentionally revised:

- `sampleRateHz = 48000`
- `channelCount = 2`
- `bitsPerSample = 32`
- `source = HostRxLoopback`
- packet payload = `256` bytes
- frames per packet = `32`
- warm-up discard = `1024` samples
- FFT block length = `4096` samples

## Test Group A: Bin-aligned Matrix

### Inputs

Frequencies:

- `503.90625 Hz`
- `996.09375 Hz`
- `2003.90625 Hz`

Levels:

- `-12 dBFS`
- `-6 dBFS`
- `-3 dBFS`

Window:

- none

This produces 9 measurements.

### PASS Criteria

For each of the 9 measurements:

1. payload match must be `True`
2. peak bin must equal the expected bin exactly
3. peak level must be within `+/- 0.20 dB` of the requested level
4. fundamental level must be within `+/- 0.20 dB` of the requested level
5. THD must be `<= 0.000020 %`
6. THD must be `<= -134 dB`

### Matrix PASS Rule

The full bin-aligned matrix passes only if all 9 measurements pass.

## Test Group B: Non-bin-aligned Hann Matrix

### Inputs

Frequency:

- `1000 Hz`

Levels:

- `-12 dBFS`
- `-6 dBFS`
- `-3 dBFS`

Window:

- Hann

This produces 3 measurements.

### PASS Criteria

For each of the 3 measurements:

1. payload match must be `True`
2. peak frequency must fall in the expected neighborhood of the `1000 Hz` tone
3. the reported peak bin must remain stable across runs
4. THD must be `<= 0.000150 %`
5. THD must be `<= -116 dB`

### Fundamental Level Rule

For the Hann matrix, fundamental level is treated as a stability metric, not an absolute amplitude gate in v1.0.

That means:

1. the three results should remain internally consistent
2. the same tool version should produce nearly the same fundamental estimate between repeated runs

Do not fail v1.0 solely on the absolute fundamental level if the method still carries a known, stable Hann-related offset.

### Matrix PASS Rule

The non-bin-aligned Hann matrix passes only if all 3 measurements pass.

## Full Pack PASS Rule

The regression pack passes only if:

1. Test Group A passes
2. Test Group B passes

## Recommended Output Format

For each measurement, log:

1. frequency
2. requested level
3. transmitted bytes
4. received bytes
5. payload match
6. peak bin or peak frequency
7. peak level
8. fundamental level
9. `H2` through `H5`
10. `THD %`
11. `THD dB`
12. result

At the end of the run, log:

1. bin-aligned matrix result
2. non-bin-aligned Hann matrix result
3. full pack result

## Failure Classification

When a test fails, classify it in one of these buckets:

1. `TransportFailure`
   - byte count mismatch
   - payload mismatch
   - stream start/stop failure
   - underrun/overrun/drop counters non-zero
2. `FrequencyFailure`
   - incorrect peak bin
   - unexpected peak location
3. `AmplitudeFailure`
   - peak or fundamental level outside allowed tolerance in the bin-aligned matrix
4. `DistortionFailure`
   - THD above threshold
5. `MethodStabilityFailure`
   - non-bin-aligned Hann test results unstable between repeated runs

## Revision Rule

If the host-tool analysis method changes in a way that materially changes expected THD or amplitude numbers, do not silently update thresholds.

Create a new revision of the regression pack instead, for example `v1.1` or `v2.0`, and record why the limits changed.