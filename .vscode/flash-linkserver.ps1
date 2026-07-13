$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$elf = Join-Path $workspace "debug\audio_stream_demo_app_cm33_core0.elf"

if (!(Test-Path $elf)) {
    throw "ELF not found: $elf. Build first."
}

# Release stale debug sessions which can keep the core halted after flash.
$staleDebugProcs = Get-Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ProcessName -in @("LinkServer", "gdbserver") -or
        $_.ProcessName -like "crt_emu*"
    }

foreach ($proc in $staleDebugProcs) {
    try {
        Stop-Process -Id $proc.Id -Force -ErrorAction Stop
        Write-Host "Stopped stale debug process: $($proc.ProcessName) (PID $($proc.Id))"
    }
    catch {
        Write-Host "Could not stop process $($proc.ProcessName) (PID $($proc.Id)): $($_.Exception.Message)"
    }
}

$linkserverCandidates = @(
    "C:\nxp\LinkServer_25.6.131\LinkServer.exe",
    "C:\nxp\LinkServer_25.6.131\dist\LinkServer.exe"
)

$discoveredCandidates = Get-ChildItem "C:\nxp" -Directory -Filter "LinkServer_*" -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "LinkServer.exe" }

$linkserver = ($linkserverCandidates + $discoveredCandidates) |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1

if (-not $linkserver) {
    throw "LinkServer.exe not found. Install NXP LinkServer or update .vscode/flash-linkserver.ps1."
}

Write-Host "Using LinkServer: $linkserver"
Write-Host "Flashing: $elf"
& $linkserver flash --probe "#1" --bootconfig xxxx "MCXN947:FRDM-MCXN947" load -e -R $elf

if ($LASTEXITCODE -ne 0) {
    throw "LinkServer flash failed with exit code $LASTEXITCODE."
}

# Ensure ISP/boot pins are released and force a clean hardware reset after flash.
& $linkserver probe "#1" wirebootconfig xxxx
if ($LASTEXITCODE -ne 0) {
    throw "LinkServer wirebootconfig failed with exit code $LASTEXITCODE."
}

$resetPulseMs = 200
$postResetDelayMs = 1500
& $linkserver probe "#1" wiretimedreset $resetPulseMs
if ($LASTEXITCODE -ne 0) {
    throw "LinkServer timed reset failed with exit code $LASTEXITCODE."
}

Start-Sleep -Milliseconds $postResetDelayMs
Write-Host "Flash completed. Hardware reset applied; target should now run standalone."
