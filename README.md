# Audio Stream Demo App

Embedded USB audio/control demo for FRDM-MCXN947.

The project uses the NXP MCUXpresso SDK through CMake and is set up so the SDK can live outside the Git repository. That means the repository only needs to contain the application code and project configuration, not the full SDK tree.

## Repository Contents

Tracked in Git:

- application sources like `main.cpp`, `usb_vendor_bulk.c`, `usb_hw_init.c`
- protocol and service code in `device_protocol/` and `services/`
- board/project configuration in `CMakeLists.txt`, `CMakePresets.json`, `prj.conf`, `mcux_include.json`, `frdmmcxn947_cm33_core0/`
- host-side reference tool in `tools/winusb-endpoint-probe/`
- documentation like `CSharpToolIntegration.md`

Not tracked in Git:

- local SDK mirror in `__repo__/`
- build output in `debug/`
- generated binaries and logs
- local IDE state

## Requirements

- FRDM-MCXN947 board
- MCUXpresso SDK installed locally
- CMake and Ninja
- ARM GCC toolchain used by the MCUX SDK
- VS Code with CMake Tools, or equivalent command-line tools

## SDK Setup

The CMake presets expect an environment variable named `SdkRootDirPath`.

This project then resolves the toolchain from:

`%SdkRootDirPath%/mcuxsdk/cmake/toolchain/armgcc.cmake`

Example on Windows PowerShell:

```powershell
$env:SdkRootDirPath = "C:\Users\perbo\source\repos\AudioStreamDemoApp\sdks\frdm_mcxn947_blank_sdk"
```

If that folder contains `mcuxsdk/`, the project presets should work as-is.

## Build

With VS Code tasks:

- `CMake: configure`
- `CMake: build`

Or with presets from a terminal:

```powershell
cmake --preset debug
cmake --build --preset debug
```

## Flash

The workspace includes a flash task using LinkServer:

- `Flash (LinkServer)`
- `Build + Flash (LinkServer)`

## Git Workflow

The repository is configured to ignore SDK and generated files. A reasonable first push flow is:

```powershell
git add .
git status
git commit -m "Initial audio stream demo app"
git push -u origin main
```

If you want to be more selective, stage only the project code and config files first.

## Notes

- `SetGeneratorConfig` in the current protocol includes `noiseSeed` and uses a 22-byte payload.
- The host reference tool is useful for validating control-plane and generated-audio behavior over WinUSB.