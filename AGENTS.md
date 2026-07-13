# AGENTS.md

Embedded USB audio/control firmware for FRDM-MCXN947 (NXP MCUXpresso SDK, FreeRTOS, CMake/Ninja, ARM GCC). See [README.md](README.md) for full setup/build/flash instructions.

## Build and Flash

- Requires env var `SdkRootDirPath` pointing at a local MCUXpresso SDK checkout (see [README.md](README.md#sdk-setup)). Nothing under `__repo__/` or `debug/` is tracked in Git — don't hand-edit generated files there.
- Use the VS Code tasks, not raw `ninja`/`cmake` invocations: `CMake: configure`, `CMake: build`, `Flash (LinkServer)`, or the composite `Build + Flash (LinkServer)` / `Clean + Build + Flash (LinkServer)`.
- Host reference tool lives in `tools/winusb-endpoint-probe` (.NET 8, `dotnet build`/`dotnet run`); use it to exercise control-plane and audio-plane behavior against real hardware.
- After renaming the CMake project/target, stale old-named `.elf`/`.bin` may remain in `debug/`; do a clean build if artifacts look wrong.
- After flashing with LinkServer, the device sometimes doesn't enumerate over WinUSB cleanly. A manual debug-session start followed by stop (via `launch.json`) makes it boot correctly and enumerate as `Vendor Bulk Audio` (VID:PID `1FC9:00B0`) — don't assume a flash failure if enumeration looks wrong right after flashing.

## Architecture

Layering is strict and split by transport/plane — follow it when adding functionality:

- `usb_vendor_bulk.c` / `usb_hw_init.c`: USB transport only (EHCI/USBHS, not KHCI/USBFS on this board). No protocol logic here.
- `device_protocol/`: binary frame format, parsing, and command dispatch (`protocol_core`, `protocol_dispatch`, `protocol_*_commands`). See [device_protocol/README.md](device_protocol/README.md) for the full wire format, opcodes, and status codes.
- `services/control_plane_service.*`: owns command/event handling (`CMD OUT 0x01` / `CMD IN 0x81`).
- `services/audio_stream_service.*`, `audio_playback_buffer.*`, `audio_codec.*`: own the audio data plane (`AUDIO OUT 0x02` / `AUDIO IN 0x82`) and SGTL5000 codec control.
- Control-plane and audio-plane frames must never cross endpoints — see [UsbControlPlaneVsDataPlaneExecutiveNote.md](UsbControlPlaneVsDataPlaneExecutiveNote.md) for the rationale.
- `main.cpp` wires everything together in a fixed init order (`AudioPlaybackBuffer_Init` → `AudioStreamService_Init` → `ControlPlaneService_Init` → USB init → start FreeRTOS tasks). Follow the existing `XxxService_Init()` / `XxxService_Start()` naming convention for new services, and fail boot via the existing `BootFailureResetLoop()` pattern (blink + `NVIC_SystemReset`) rather than hanging.
- C is used for SDK/HAL/USB glue (wrapped in `extern "C"` from `main.cpp`); C++ is used for services and protocol logic.

## Hardware Gotchas

- MCXN947 SysTick reload is 24-bit; `SysTick_Config(150000000UL)` silently fails. Use a smaller period (e.g. `SystemCoreClock / 1000U`) and divide inside `SysTick_Handler`.
- `BOARD_Codec_I2C_*` helpers in `frdmmcxn947_cm33_core0/frdmmcxn947/board.c` may not be linked in this build variant (guarded by SDK I2C component config). For codec I2C access, use the direct `LPI2C_MasterInit`/`LPI2C_MasterTransferBlocking` pattern from `device_protocol/protocol_system_commands.cpp` instead.
- SGTL5000 analog power bring-up is order-sensitive: writing `CHIP_ANA_POWER` combinations that enable `VAG + DAC` together can destabilize the I2C bus (`kStatus_LPI2C_ArbitrationLost`/`Nak`) after ~250 ms. Keep new codec init sequences close to the verified baseline registers in `services/audio_codec.cpp` and test on hardware before changing analog power bits.
- `frdmmcxn947_cm33_core0/cm33_core0/hardware_init.c` must call `BOARD_InitBootPeripherals()` for SAI-based audio bring-up; keep non-generated overrides (e.g. SAI1 MCLK) applied *after* peripheral init so MCUXpresso Config Tools regeneration doesn't silently drop them.

## Documentation Index

Root-level `*.md` files cover specific topics in depth — read them instead of re-deriving this information:

- [device_protocol/README.md](device_protocol/README.md): full binary protocol (frame layout, opcodes, status codes, audio header).
- [CSharpToolIntegration.md](CSharpToolIntegration.md): how an external host tool talks to the device over WinUSB.
- [AudioTestToolMvp.md](AudioTestToolMvp.md) / [AudioTestToolImplementationNotes.md](AudioTestToolImplementationNotes.md): host-side test tool scope and implementation notes.
- [DigitalLoopbackDocsOverview.md](DigitalLoopbackDocsOverview.md): index of the digital-loopback design/porting/measurement/regression docs (start here before opening any `DigitalLoopback*`/`LPC54628DigitalLoopback*` doc).
- [UsbControlPlaneVsDataPlaneExecutiveNote.md](UsbControlPlaneVsDataPlaneExecutiveNote.md) / [UsbControlPlaneVsDataPlanePosition.md](UsbControlPlaneVsDataPlanePosition.md): rationale for keeping control and data planes separate.
