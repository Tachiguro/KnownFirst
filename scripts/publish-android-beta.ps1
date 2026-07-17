[CmdletBinding()]
param(
    [string]$KeystorePath,
    [string]$PasswordFilePath
)

$scriptPath = Join-Path $PSScriptRoot "publish-android-test-packages.ps1"
& $scriptPath @PSBoundParameters
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
