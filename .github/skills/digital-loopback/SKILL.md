---
name: digital-loopback
description: 'Use when porting the digital audio loopback reference path to a new board (e.g. LPC54628), or when running the digital loopback FFT/THD measurement and regression validation on this firmware. Trigger words: digital loopback, LPC54628 port, FFT, THD, regression pack, audio bring-up reference path.'
---

# Digital Loopback

This repo's audio loopback path (`device_protocol` control-plane + `services/audio_stream_service` data-plane) doubles as a portable reference design and as a validation baseline. Detailed docs already exist at the repo root — read them instead of re-deriving this information.

## When to Use

- Porting/lifting the digital loopback architecture to a new board (e.g. LPC54628).
- Validating audio bring-up on hardware using the FFT/THD digital measurement baseline.
- Running or updating the release-gate regression pack.

## Start Here

Always read [DigitalLoopbackDocsOverview.md](../../../DigitalLoopbackDocsOverview.md) first — it indexes the documents below and tells you which one to read for a given task.

## Porting to LPC54628

Read in this order:

1. [LPC54628DigitalLoopbackDesignMemo.md](../../../LPC54628DigitalLoopbackDesignMemo.md) — short handoff: architecture direction and reuse boundaries.
2. [LPC54628DigitalLoopbackImplementationChecklist.md](../../../LPC54628DigitalLoopbackImplementationChecklist.md) — concrete bring-up sequence, negative test list, exit criteria.
3. [LPC54628DigitalLoopbackApiTemplate.md](../../../LPC54628DigitalLoopbackApiTemplate.md) — suggested module structure (controller/service/buffer/USB adapter split); use it when shaping new code.
4. [DigitalLoopbackPortingGuideLPC54628.md](../../../DigitalLoopbackPortingGuideLPC54628.md) — full rationale for what should and shouldn't be ported.

## Measurement and Regression

1. [DigitalLoopbackMeasurementSpec.md](../../../DigitalLoopbackMeasurementSpec.md) — FFT/THD baseline method, packetization, and pass criteria.
2. [DigitalLoopbackRegressionPack.md](../../../DigitalLoopbackRegressionPack.md) — repeatable matrix-based regression/release gate.

## Related Context

- [device_protocol/README.md](../../../device_protocol/README.md): wire protocol used by the loopback path (`StartStream` source `HostRxLoopback`, audio header, flags).
- [AGENTS.md](../../../AGENTS.md): overall firmware architecture and build/flash workflow for this repo.
- Host-side validation uses `tools/winusb-endpoint-probe` — see [CSharpToolIntegration.md](../../../CSharpToolIntegration.md) for WinUSB connection details.

## Practical Rule

If time is short: read the design memo first, use the checklist while implementing, use the API template only when shaping code structure, and use the measurement/regression docs only when validating behavior.
