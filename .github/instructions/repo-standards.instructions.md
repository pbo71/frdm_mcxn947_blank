---
applyTo: "**/*"
---
# Repository standards

Follow these rules for changes in this workspace:

- Read [AGENTS.md](../../AGENTS.md) before modifying firmware behavior.
- Keep USB transport, protocol logic, services, and main boot wiring in their own layers.
- Do not edit generated files under [debug/](../../debug/) or [__repo__/](../../__repo__/).
- Preserve documented protocol opcodes and frame layouts.
- Use the existing service naming conventions and boot-failure pattern.
- Verify changes with the relevant build or test flow before finishing.
