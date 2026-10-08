<#
.SYNOPSIS
    Picks the two audio devices used for recording and stores them in devices.local.json.

.DESCRIPTION
    Two separate devices, for two separate purposes:

    - "call": the bus carrying the mixed call audio (remote party + own microphone), for
      Call/Live mode. Normally a virtual Voicemeeter bus (e.g. "Voicemeeter Out B1") --
      see README.md for how to set that up. Optional: only needed for Call/Live.
    - "mic": the user's own microphone directly, for Dictate mode. Needs NO Voicemeeter
      -- any normal microphone works.

    Device selection goes through ffmpeg/dshow, the same mechanism the recording itself
    uses -- whatever is selectable here as number X is exactly what actually gets
    recorded later.

.EXAMPLE
    .\Set-AudioDevices.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_Common.ps1"

Write-Step "Available audio devices (ffmpeg/dshow)"
$devices = Get-DshowAudioDevices

if ($devices.Count -eq 0) {
    Write-Err "No audio devices found. Is ffmpeg installed correctly? See .\Setup.ps1."
    exit 1
}

for ($i = 0; $i -lt $devices.Count; $i++) {
    Write-Host ("  [{0}] {1}" -f ($i + 1), $devices[$i])
}

function Read-DeviceChoice([string]$Prompt) {
    while ($true) {
        $choice = Read-Host $Prompt
        $idx = 0
        if ([int]::TryParse($choice, [ref]$idx) -and $idx -ge 1 -and $idx -le $devices.Count) {
            return $devices[$idx - 1]
        }
        Write-Warn "Invalid number, try again (1-$($devices.Count))."
    }
}

Write-Step "Call device (for Call/Live mode -- optional, only needed for those)"
Write-Host "The bus carrying the mixed call audio -- with a Voicemeeter setup, normally"
Write-Host "'Voicemeeter Out B1' or similar. See README.md if this isn't set up yet."
$callDevice = Read-DeviceChoice "Number"

Write-Step "Microphone device (for Dictate mode)"
Write-Host "The user's own microphone directly, no Voicemeeter needed -- for dictation."
$micDevice = Read-DeviceChoice "Number"

$config = [ordered]@{
    callDevice = $callDevice
    micDevice  = $micDevice
}
$config | ConvertTo-Json | Set-Content $script:DevicesConfigPath -Encoding UTF8

Write-Step "Saved"
Write-Ok "call: $callDevice"
Write-Ok "mic:  $micDevice"
Write-Host "`nStored in devices.local.json (machine-specific, not part of the repo)." -ForegroundColor Cyan
