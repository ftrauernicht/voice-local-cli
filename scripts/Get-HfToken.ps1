<#
.SYNOPSIS
    Returns the stored Hugging Face access token.

.DESCRIPTION
    Reads %LOCALAPPDATA%\voice-local-cli\hf-token.dpapi and decrypts it via DPAPI.
    Stored by Save-HfToken.ps1.

        $token = .\Get-HfToken.ps1

.PARAMETER Quiet
    Report nothing if the token is missing, just return nothing.
#>
[CmdletBinding()]
param([switch] $Quiet)

$file = Join-Path $env:LOCALAPPDATA 'voice-local-cli\hf-token.dpapi'

if (-not (Test-Path $file)) {
    if (-not $Quiet) {
        Write-Warning "No HF token stored. See README.md -- create one at https://huggingface.co/settings/tokens, then run Save-HfToken.ps1."
    }
    return $null
}

try {
    $sec = Get-Content $file -Raw | ConvertTo-SecureString
    [Net.NetworkCredential]::new('', $sec).Password
} catch {
    if (-not $Quiet) {
        Write-Warning "Could not decrypt the token. DPAPI is bound to the user account and this machine."
    }
    return $null
}
