<#
Shared helper functions for every script in this folder: colored console output and the
device configuration. Dot-sourced, not run directly:

    . "$PSScriptRoot\_Common.ps1"
#>

# Without both of these, umlauts/special characters render garbled as soon as output is
# redirected or captured by an outer process (depends on the system codepage) -- a known
# class of bug on Windows PowerShell 5.1. [Console]::OutputEncoding alone isn't enough once
# stdout is redirected instead of going straight to the console -- $OutputEncoding needs
# setting too.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# A freshly started process often doesn't see a PATH change winget just made (e.g. via
# Setup.ps1) -- Windows only refreshes it for newly started processes whose parent
# process (usually Explorer) has already refreshed its own environment. So reload it from
# the registry here instead of forcing the user to restart the shell manually.
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

function Write-Info($text)  { Write-Host $text -ForegroundColor Cyan }
function Write-Ok($text)    { Write-Host "  [ok] $text" -ForegroundColor Green }
function Write-Warn($text)  { Write-Host "  [!] $text" -ForegroundColor Yellow }
function Write-Err($text)   { Write-Host "  [error] $text" -ForegroundColor Red }
function Write-Todo($text)  { Write-Host "  [ ] $text" -ForegroundColor Yellow }
function Write-Step($text)  { Write-Host "`n==> $text" -ForegroundColor Cyan }

$script:DevicesConfigPath = Join-Path $PSScriptRoot 'devices.local.json'

function Get-ConfiguredDevice {
    <#
    .SYNOPSIS
        Reads a device name from devices.local.json.

    .PARAMETER Purpose
        "call" (the mixed call-audio device, for Call/Live mode) or "mic" (the user's own
        microphone, for Dictate mode).
    #>
    param(
        [ValidateSet("call", "mic")]
        [Parameter(Mandatory)]
        [string]$Purpose
    )

    if (-not (Test-Path $script:DevicesConfigPath)) {
        Write-Err "No devices configured."
        Write-Host "  Run once: .\Set-AudioDevices.ps1" -ForegroundColor Yellow
        exit 1
    }

    $config = Get-Content $script:DevicesConfigPath -Raw | ConvertFrom-Json
    $value = if ($Purpose -eq "call") { $config.callDevice } else { $config.micDevice }

    if (-not $value) {
        Write-Err "Device for '$Purpose' is not set in devices.local.json."
        Write-Host "  Set it up again: .\Set-AudioDevices.ps1" -ForegroundColor Yellow
        exit 1
    }

    return $value
}

function Get-DshowAudioDevices {
    <#
    .SYNOPSIS
        Lists the audio devices visible via ffmpeg/dshow (names only).
    #>
    if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
        Write-Err "ffmpeg is not installed or not on PATH."
        Write-Host "  Set it up: .\Setup.ps1" -ForegroundColor Yellow
        exit 1
    }

    # ffmpeg routinely writes its device list to stderr -- that's normal, not a real
    # error. With $ErrorActionPreference = 'Stop' (set in every bootstrap script),
    # PowerShell would otherwise abort on the first stderr line, before the device list
    # is even assembled.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $raw = & ffmpeg -hide_banner -list_devices true -f dshow -i dummy 2>&1
    $ErrorActionPreference = $prevEap

    $devices = @()
    foreach ($line in $raw) {
        if ($line -match '"([^"]+)"\s*\(audio\)') {
            $devices += $matches[1]
        }
    }
    return $devices
}
