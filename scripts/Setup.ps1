<#
.SYNOPSIS
    Sets up voice-local-cli on a (new) Windows machine.

.DESCRIPTION
    Installs/creates everything that can be automated: ffmpeg, a Python venv with the
    speech engine's dependencies, and optionally Intel-GPU acceleration (OpenVINO; not
    fatal if this fails -- see README.md, "Model choice and GPU acceleration"). Idempotent
    -- running it more than once is harmless, anything already present is skipped.

    Voicemeeter is installed only if you plan to use Call/Live mode (full-call recording
    with the remote party's audio mixed in) -- Dictate mode, the primary use case, only
    ever needs a plain microphone and never touches Voicemeeter.

    What is NOT automated, because it needs GUI interaction, a reboot, or a separate
    account, is listed at the end as a checklist.

.EXAMPLE
    .\Setup.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_Common.ps1"

function Install-WingetPackage([string]$Id, [string]$FriendlyName) {
    winget install --id $Id --accept-package-agreements --accept-source-agreements -e
    if ($LASTEXITCODE -ne 0) {
        Write-Err "$FriendlyName installation via winget failed (exit code $LASTEXITCODE)."
        Write-Host "  Install it manually, or check the winget error above." -ForegroundColor Yellow
        exit 1
    }
}

# --- 1. ffmpeg -----------------------------------------------------------
Write-Step "ffmpeg"
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")
if (Get-Command ffmpeg -ErrorAction SilentlyContinue) {
    Write-Ok "already present ($(& ffmpeg -version 2>&1 | Select-Object -First 1))"
} else {
    Install-WingetPackage -Id "Gyan.FFmpeg" -FriendlyName "ffmpeg"
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")
    Write-Ok "installed"
}

# --- 2. Python --------------------------------------------------------
Write-Step "Python"
$pyCmd = Get-Command py -ErrorAction SilentlyContinue
if (-not $pyCmd) { $pyCmd = Get-Command python -ErrorAction SilentlyContinue }
if ($pyCmd) {
    Write-Ok "already present ($(& $pyCmd.Source --version 2>&1))"
} else {
    Write-Host "  No Python installation found -- installing Python 3.14 via winget ..."
    Install-WingetPackage -Id "Python.Python.3.14" -FriendlyName "Python"
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")
    Write-Ok "installed"
}

# --- 3. venv + Python packages ----------------------------------------------
$engineDir = Join-Path $PSScriptRoot "..\src\engine" | Resolve-Path
Write-Step "Python venv (installed from src/engine/pyproject.toml)"
Set-Location $engineDir
if (-not (Test-Path "$engineDir\.venv\Scripts\python.exe")) {
    py -3.14 -m venv .venv
    Write-Ok "venv created"
} else {
    Write-Ok "venv already exists"
}
& "$engineDir\.venv\Scripts\python.exe" -m pip install --upgrade pip --quiet
# Dependencies live in src/engine/pyproject.toml, not duplicated here -- see that file for
# why each one is needed (forced alignment, VAD, etc.).
& "$engineDir\.venv\Scripts\python.exe" -m pip install -e "$engineDir" --quiet
if ($LASTEXITCODE -ne 0) {
    Write-Err "pip install failed (exit code $LASTEXITCODE) -- check the error above."
    exit 1
}
Write-Ok "packages installed/up to date"

# --- 3b. Optional GPU acceleration -----------------------------------------
# Intel GPUs only, via OpenVINO. Deliberately not fatal if this fails -- faster-whisper
# on CPU remains the baseline on every machine, GPU is a bonus, not a requirement.
Write-Step "Optional GPU acceleration (OpenVINO, Intel GPUs only)"
& "$engineDir\.venv\Scripts\python.exe" -m pip install -e "$engineDir[gpu]" --quiet 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Ok "OpenVINO packages installed -- used automatically if a matching Intel GPU is present"
} else {
    Write-Warn "OpenVINO installation skipped/failed -- no problem, transcription just runs on CPU"
}

Set-Location $PSScriptRoot

# --- Checklist: what only works by hand -------------------------------------
Write-Step "Still to do by hand (details in README.md)"
Write-Todo "Pick a microphone: .\Set-AudioDevices.ps1 (choose the 'mic' device -- this is all Dictate mode needs)"
Write-Todo "Dictate mode is now ready to try"
Write-Host ""
Write-Host "Only if you also want Call/Live mode (recording a call with the remote party's" -ForegroundColor DarkGray
Write-Host "audio mixed in -- NOT needed for Dictate mode):" -ForegroundColor DarkGray
Write-Todo "Install Voicemeeter: winget install VB-Audio.Voicemeeter (or from vb-audio.com)"
Write-Todo "Reboot -- Voicemeeter's audio driver needs it, otherwise the devices are missing"
Write-Todo "Voicemeeter: route the Virtual Input 1 strip to a bus (e.g. B1) AND A1 (A1 = audible, B-bus = recordable)"
Write-Todo "Voicemeeter: set Hardware Out A1 to your own headset/speakers"
Write-Todo "If other apps (browser etc.) go silent afterward: switch A1's driver mode to MME (click the device at HARDWARE OUT A1)"
Write-Todo "Windows sound settings: set your call app's output to the chosen Voicemeeter input (that app only, not system-wide)"
Write-Todo "Voicemeeter: put a stereo input strip on your own microphone, enable ONLY the B-bus there (not A1, or you'll hear an echo)"
Write-Todo "Pick the 'call' device too: .\Set-AudioDevices.ps1 (only makes sense after the Voicemeeter steps above)"
Write-Todo "Hugging Face token (for speaker diarization): create an account, accept the terms on pyannote's gated model pages, generate a token, then .\Save-HfToken.ps1"
