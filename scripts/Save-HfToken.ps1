<#
.SYNOPSIS
    Stores the Hugging Face access token for speaker diarization (pyannote.audio),
    encrypted.

.DESCRIPTION
    Saves it under %LOCALAPPDATA%\voice-local-cli\hf-token.dpapi -- DPAPI encryption,
    bound to the Windows account and this machine.

.PARAMETER FromFile
    Path to a file containing only the token. Deleted after encryption.

.EXAMPLE
    .\Save-HfToken.ps1
    Prompts for the token with hidden input (not echoed to the screen).
#>
[CmdletBinding()]
param(
    [string] $FromFile,
    [switch] $KeepSourceFile
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_Common.ps1"
$store = Join-Path $env:LOCALAPPDATA 'voice-local-cli'
$file  = Join-Path $store 'hf-token.dpapi'

if ($FromFile) {
    if (-not (Test-Path $FromFile)) { throw "File not found: $FromFile" }
    $plain = (Get-Content $FromFile -Raw).Trim()
    if (-not $plain) { throw "File is empty: $FromFile" }
    $sec = ConvertTo-SecureString $plain -AsPlainText -Force
} else {
    $sec = Read-Host "Hugging Face access token (hf_...)" -AsSecureString
    if ($sec.Length -eq 0) { throw "No token entered." }
}

New-Item -ItemType Directory -Force -Path $store | Out-Null
ConvertFrom-SecureString $sec | Set-Content $file -NoNewline

$acl = Get-Acl $file
$acl.SetAccessRuleProtection($true, $false)
$acl.SetAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
    "$env:USERDOMAIN\$env:USERNAME", 'FullControl', 'Allow')))
Set-Acl $file $acl

Write-Ok "Token stored: $file"

if ($FromFile -and -not $KeepSourceFile) {
    Remove-Item $FromFile -Force
    Write-Ok "Deleted the plain-text source file: $FromFile"
}

$check = & (Join-Path $PSScriptRoot 'Get-HfToken.ps1')
if ($check) { Write-Ok "Read-back check passed ($($check.Length) characters)" }
else        { Write-Err "Read-back check failed" }
