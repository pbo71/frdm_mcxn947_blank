$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$elf = Join-Path $workspace "debug\audio_stream_demo_app_cm33_core0.elf"

if (!(Test-Path $elf)) {
    throw "ELF not found: $elf. Build first."
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
& $linkserver flash --probe "#1" "MCXN947:FRDM-MCXN947" load -e $elf
