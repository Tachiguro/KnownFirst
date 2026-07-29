[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2100000000)]
    [int]$VersionCode,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DisplayVersion,

    [string]$KeystorePath,
    [string]$PasswordFilePath
)

throw "This script is deprecated and unsupported. Use the canonical entry point instead: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\knownfirst.ps1 -Action GooglePlayBundle"
