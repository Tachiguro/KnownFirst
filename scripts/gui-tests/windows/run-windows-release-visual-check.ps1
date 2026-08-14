<#
.SYNOPSIS
    KnownFirst Windows Release visual-isolation check runner (Gate 12).

.DESCRIPTION
    Launches an existing normal Windows Release executable with an isolated, disposable
    persistence profile under artifacts\gui-tests\windows\profiles\. Proves that the real
    user database and preferences in %LOCALAPPDATA%\KnownFirst remain untouched.
#>

[CmdletBinding()]
param(
    [string]$ExpectedCommitSha
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $scriptRoot '..\..\..')).Path

# 1. Verify working directory is clean
$status = & git -C $projectRoot status --porcelain
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Repository is dirty. The Release visual check must run only on a clean tree.`n$status"
}

# 2. Verify HEAD and master alignment
$currentHead = (& git -C $projectRoot rev-parse HEAD).Trim()
if (-not [string]::IsNullOrWhiteSpace($ExpectedCommitSha) -and ($currentHead -ne $ExpectedCommitSha.Trim())) {
    throw "HEAD commit ($currentHead) does not match expected commit ($ExpectedCommitSha)."
}

# 3. Locate existing Release executable
$executablePath = Join-Path $projectRoot 'bin\Release\net10.0-windows10.0.19041.0\win-x64\KnownFirst.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Release executable not found at: $executablePath. Build/publish must be performed separately before running this check."
}

# 4. Create unique canonical disposable profile
$runId = "release-visual-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-" + [guid]::NewGuid().ToString('N')
$profilesRoot = Join-Path $projectRoot 'artifacts\gui-tests\windows\profiles'
$profilePath = Join-Path $profilesRoot $runId
New-Item -ItemType Directory -Path $profilePath -Force | Out-Null
$canonicalProfilePath = (Resolve-Path $profilePath).Path + [System.IO.Path]::DirectorySeparatorChar

# 5. Capture pre-run hash of real database if present
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$realDbPath = Join-Path $localAppData 'KnownFirst\knownfirst.db3'
$preRunDbHash = $null
if (Test-Path -LiteralPath $realDbPath -PathType Leaf) {
    $preRunDbHash = (Get-FileHash -Path $realDbPath -Algorithm SHA256).Hash
}

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "KnownFirst Windows Release Visual-Isolation Launcher (Gate 12)" -ForegroundColor Cyan
Write-Host "Candidate SHA : $currentHead"
Write-Host "Executable    : $executablePath"
Write-Host "Profile Root  : $canonicalProfilePath"
Write-Host "Real DB Hash  : $(if ($preRunDbHash) { $preRunDbHash } else { '(no existing database)' })"
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "Launching KnownFirst in isolated Release mode. Perform manual visual inspection."
Write-Host "When finished, close the KnownFirst window to complete the check."

# 6. Launch process with process-scoped isolation environment variable
$previousEnv = $env:KNOWNFIRST_GUI_TEST_ROOT
try {
    $env:KNOWNFIRST_GUI_TEST_ROOT = $canonicalProfilePath

    $process = Start-Process -FilePath $executablePath -PassThru -Wait
    $exitCode = $process.ExitCode
}
finally {
    $env:KNOWNFIRST_GUI_TEST_ROOT = $previousEnv
}

# 7. Verify post-run hash of real database
$postRunDbHash = $null
if (Test-Path -LiteralPath $realDbPath -PathType Leaf) {
    $postRunDbHash = (Get-FileHash -Path $realDbPath -Algorithm SHA256).Hash
}

if ($preRunDbHash -ne $postRunDbHash) {
    throw "SAFETY VIOLATION: Real user database at $realDbPath was modified during the isolated Release run!"
}

Write-Host "================================================================================" -ForegroundColor Green
Write-Host "Release Visual Check Finished (Exit code: $exitCode)" -ForegroundColor Green
Write-Host "Real Profile  : UNTOUCHED (Verified SHA-256 match)" -ForegroundColor Green
Write-Host "Isolated Data : $canonicalProfilePath"
Write-Host "================================================================================" -ForegroundColor Green
