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

.PARAMETER EngineDir
    Path to the engine source (the folder containing pyproject.toml). Defaults to this
    checkout's own src/engine -- only overridden by the installed orchestrator, which
    bootstraps against its own bundled copy of the engine instead of a git checkout.

.PARAMETER VenvDir
    Where to create the Python venv. Defaults to $EngineDir\.venv (this script's own
    behavior for as long as it's existed). The installed orchestrator overrides this to a
    stable, per-machine location outside its own update-managed install folder -- see
    RepositoryLayout.EngineVenvRoot's doc comment in the .NET project for why.

.EXAMPLE
    .\Setup.ps1

.EXAMPLE
    .\Setup.ps1 -EngineDir 'C:\path\to\bundled\engine' -VenvDir 'C:\path\to\persistent\venv'
#>
[CmdletBinding()]
param(
    [string] $EngineDir,
    [string] $VenvDir
)

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

# --- 2. Python 3.14 -----------------------------------------------------
# Specifically 3.14, not "any Python" -- step 3 below hardcodes `py -3.14` so every
# machine builds the venv against the same interpreter version. Checking only "is some
# py/python command on PATH" (this step's original check) passes on a machine with an
# older Python already installed for something else, then step 3's `py -3.14` fails --
# found for real on a second machine that only had Python 3.10.
Write-Step "Python 3.14"
$py314Version = $null
try {
    $py314Version = & py -3.14 --version 2>&1
    if ($LASTEXITCODE -ne 0) { $py314Version = $null }
} catch {
    $py314Version = $null
}

if ($py314Version) {
    Write-Ok "already present ($py314Version)"
} else {
    Write-Host "  Python 3.14 not found via the 'py' launcher -- installing via winget ..."
    Install-WingetPackage -Id "Python.Python.3.14" -FriendlyName "Python 3.14"
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")
    Write-Ok "installed"
}

# --- 3. venv + Python packages ----------------------------------------------
$engineDir = if ($EngineDir) { (Resolve-Path $EngineDir).Path } else { (Resolve-Path (Join-Path $PSScriptRoot "..\src\engine")).Path }
$venvDir = if ($VenvDir) { $VenvDir } else { Join-Path $engineDir ".venv" }

Write-Step "Python venv (installed from $engineDir\pyproject.toml)"
if (-not (Test-Path "$venvDir\Scripts\python.exe")) {
    New-Item -ItemType Directory -Force -Path (Split-Path $venvDir -Parent) | Out-Null
    py -3.14 -m venv $venvDir
    # `py -3.14 -m venv` can fail (e.g. "Requested Python version (3.14) not installed")
    # without PowerShell treating it as a terminating error -- check for real instead of
    # trusting the exit code alone, since this is exactly the step that silently
    # "succeeded" on a machine where step 2 should have caught the missing 3.14 first.
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$venvDir\Scripts\python.exe")) {
        Write-Err "venv creation failed (exit code $LASTEXITCODE) -- check the error above."
        exit 1
    }
    Write-Ok "venv created at $venvDir"
} else {
    Write-Ok "venv already exists at $venvDir"
}
& "$venvDir\Scripts\python.exe" -m pip install --upgrade pip --quiet
# Dependencies live in $engineDir\pyproject.toml, not duplicated here -- see that file for
# why each one is needed (forced alignment, VAD, etc.).
& "$venvDir\Scripts\python.exe" -m pip install -e "$engineDir" --quiet
if ($LASTEXITCODE -ne 0) {
    Write-Err "pip install failed (exit code $LASTEXITCODE) -- check the error above."
    exit 1
}
Write-Ok "packages installed/up to date"

# --- 3b. Optional GPU acceleration -----------------------------------------
# Intel GPUs only, via OpenVINO. Deliberately not fatal if this fails -- faster-whisper
# on CPU remains the baseline on every machine, GPU is a bonus, not a requirement.
Write-Step "Optional GPU acceleration (OpenVINO, Intel GPUs only)"
& "$venvDir\Scripts\python.exe" -m pip install -e "$engineDir[gpu]" --quiet 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Ok "OpenVINO packages installed -- used automatically if a matching Intel GPU is present"
} else {
    Write-Warn "OpenVINO installation skipped/failed -- no problem, transcription just runs on CPU"
}

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
