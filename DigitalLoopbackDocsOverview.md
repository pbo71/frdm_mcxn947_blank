# Digital Loopback Documentation Overview

This file is a quick guide to the digital loopback documents in this repository.

Use it to decide what to read first depending on the task.

## Read This First

If you only want one document to understand how the implemented digital loopback should be lifted into an LPC54628-based project, start with:

- [LPC54628DigitalLoopbackDesignMemo.md](LPC54628DigitalLoopbackDesignMemo.md)

That document is the shortest complete handoff note.

## Document Roles

### Short Design Handoff

- [LPC54628DigitalLoopbackDesignMemo.md](LPC54628DigitalLoopbackDesignMemo.md)

Read this when you want:

- the short version
- architecture direction
- reuse boundaries
- LPC54628-oriented integration guidance

### Detailed Porting Rationale

- [DigitalLoopbackPortingGuideLPC54628.md](DigitalLoopbackPortingGuideLPC54628.md)

Read this when you want:

- more explanation of why the digital loopback is worth reusing
- what should and should not be ported
- how the reference path fits into daily verification

### Implementation Checklist

- [LPC54628DigitalLoopbackImplementationChecklist.md](LPC54628DigitalLoopbackImplementationChecklist.md)

Read this when you want:

- a concrete work sequence
- bring-up order
- negative test list
- exit criteria for the LPC54628 implementation

### API Shape

- [LPC54628DigitalLoopbackApiTemplate.md](LPC54628DigitalLoopbackApiTemplate.md)

Read this when you want:

- a suggested internal module structure
- candidate function names
- separation between controller, service, buffer, and USB adapter

### Measurement Method

- [DigitalLoopbackMeasurementSpec.md](DigitalLoopbackMeasurementSpec.md)

Read this when you want:

- the digital measurement baseline
- FFT and THD reference method
- packetization and pass criteria for the digital reference path

### Regression Gate

- [DigitalLoopbackRegressionPack.md](DigitalLoopbackRegressionPack.md)

Read this when you want:

- the repeatable regression pack
- matrix-based pass and fail rules
- release-gate style transport and FFT checks

## Recommended Reading Order

For architecture transfer to LPC54628:

1. [LPC54628DigitalLoopbackDesignMemo.md](LPC54628DigitalLoopbackDesignMemo.md)
2. [LPC54628DigitalLoopbackImplementationChecklist.md](LPC54628DigitalLoopbackImplementationChecklist.md)
3. [LPC54628DigitalLoopbackApiTemplate.md](LPC54628DigitalLoopbackApiTemplate.md)
4. [DigitalLoopbackPortingGuideLPC54628.md](DigitalLoopbackPortingGuideLPC54628.md)

For measurement and regression work:

1. [DigitalLoopbackMeasurementSpec.md](DigitalLoopbackMeasurementSpec.md)
2. [DigitalLoopbackRegressionPack.md](DigitalLoopbackRegressionPack.md)

## Practical Rule

If time is short:

- read the design memo first
- use the checklist while implementing
- use the API template only when shaping code structure
- use the measurement and regression documents only when validating behavior