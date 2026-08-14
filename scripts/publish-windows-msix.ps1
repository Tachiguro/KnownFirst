<#
.SYNOPSIS
    Creates one x64 Release MSIX package of KnownFirst with a SHA-256 sidecar. Unsigned by
    default; optional signing uses an already installed certificate identified only by thumbprint.

.DESCRIPTION
    MSIX is the canonical production Windows install/update channel: an app installed from the
    Microsoft Store receives its updates through Microsoft Store infrastructure. This script only
    CREATES the package. It never installs it, never trusts or creates a certificate, never
    contacts Partner Center, and never uploads anything. Store submission is a separate,
    separately authorized operation.

    Signing modes:
      None     (default) - AppxPackageSigningEnabled=false. The Microsoft Store re-signs MSIX
                           packages with a Microsoft certificate during publishing, so a Store
                           submission candidate does not need to be signed here. An unsigned
                           package cannot be sideloaded without separately trusting a signature.
      External           - signs with a certificate that already exists in the current user's
                           certificate store, identified only by its SHA-1 thumbprint read from
                           KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT. No PFX file, no password, and
                           no certificate creation or trust installation is ever performed by this
                           repository, and no signing material is written to any log or artifact.

    Store identity: Platforms\Windows\Package.appxmanifest still carries the MAUI template
    placeholder Publisher / PublisherDisplayName values. MAUI substitutes the package Identity
    Name and Version but never the publisher fields, so until real Microsoft Partner Center values
    exist, every package produced here is a technical/development-identity artifact and is NOT a
    Store submission candidate. That fact is detected, reported, and encoded in the filename
    rather than being silently presented as a production Store package.

    Package version: the MSIX Identity Version is remapped by the KnownFirstWindowsMsixPackaging
    variant in KnownFirst.csproj to 1.0.<KnownFirstBuildNumber>.0, because the Microsoft Store
    requires the fourth section to be 0. See WindowsPackageVersionMappingTests.

    Safety model, build isolation, locking, staging, collision protection, and rollback behavior
    match scripts/publish-windows-portable.ps1 and the other canonical packaging scripts in
    scripts/.
#>
[CmdletBinding()]
param(
    [ValidateSet('None', 'External')]
    [string]$MsixSigning = 'None'
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot "KnownFirst.csproj"
$targetFramework = "net10.0-windows10.0.19041.0"
$runtimeIdentifier = "win-x64"

# Artifact naming, identity classification, and candidate selection are shared with the launcher
# so both derive the identical final path. The helper never sees any signing input.
. (Join-Path $PSScriptRoot 'windows-distribution-common.ps1')

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "KnownFirst.csproj was not found at $projectPath."
}

# --- BEGIN EXTERNAL SIGNING INPUT CONTRACT ---
# Fail closed on missing or malformed external signing input BEFORE anything is restored, built,
# published, or written. The thumbprint identifies a certificate that must already be installed in
# the current user's certificate store; this repository never creates, imports, or trusts one.
$signingThumbprint = $null
if ($MsixSigning -eq 'External') {
    $signingThumbprint = $env:KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT
    if ([string]::IsNullOrWhiteSpace($signingThumbprint)) {
        throw "The external MSIX signing thumbprint is invalid or missing. Set KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT to the 40-character SHA-1 thumbprint of a certificate that is already installed in your certificate store."
    }
    $signingThumbprint = $signingThumbprint.Trim()
    if ($signingThumbprint -notmatch '\A[0-9A-Fa-f]{40}\z') {
        throw "The external MSIX signing thumbprint is invalid or missing. It must be exactly 40 hexadecimal characters."
    }
}
# --- END EXTERNAL SIGNING INPUT CONTRACT ---

function Get-KnownFirstVersionInfo {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $output = & dotnet msbuild $ProjectPath -nologo `
        -getProperty:KnownFirstProductVersion `
        -getProperty:KnownFirstBuildNumber
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read KnownFirstProductVersion/KnownFirstBuildNumber from $ProjectPath.`n$($output | Out-String)"
    }

    $parsed = ($output | Out-String).Trim() | ConvertFrom-Json
    $productVersion = $parsed.Properties.KnownFirstProductVersion
    $buildNumber = $parsed.Properties.KnownFirstBuildNumber
    if ([string]::IsNullOrWhiteSpace($productVersion) -or [string]::IsNullOrWhiteSpace($buildNumber)) {
        throw "KnownFirstProductVersion or KnownFirstBuildNumber was empty when evaluated from $ProjectPath."
    }

    return [pscustomobject]@{
        ProductVersion = $productVersion
        BuildNumber = $buildNumber
    }
}

# The launcher must be able to predict this exact filename for its reusable-result check, so both
# sides derive the short commit identically as a fixed seven-character prefix of the full SHA.
function Get-KnownFirstShortCommit {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $status = & git -C $ProjectRoot status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine the Git worktree state for $ProjectRoot."
    }
    if (-not [string]::IsNullOrEmpty(($status -join ''))) {
        throw "The worktree is not clean. An MSIX package filename carries a commit identity and must never describe modified content. Commit or revert the pending changes first."
    }

    $headSha = (& git -C $ProjectRoot rev-parse HEAD | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($headSha)) {
        throw "Could not resolve HEAD for $ProjectRoot."
    }
    $headSha = $headSha.Trim()
    if ($headSha -notmatch '\A[0-9a-f]{40}\z') {
        throw "HEAD did not resolve to a full commit SHA."
    }

    return Get-KnownFirstShortCommitPrefix -HeadSha $headSha
}

$manifestPath = Join-Path $projectRoot "Platforms\Windows\Package.appxmanifest"
$identityMarker = Get-KnownFirstMsixIdentityMarker -ManifestPath $manifestPath
if ($identityMarker -eq 'devidentity') {
    Write-Output "STORE_IDENTITY_PLACEHOLDER: Package.appxmanifest still carries at least one placeholder Partner Center identity value (Identity Name, Publisher, or PublisherDisplayName). The produced MSIX is a technical development-identity artifact and is NOT a Microsoft Store submission candidate. Real Partner Center identity values are required first."
}

$versionInfo = Get-KnownFirstVersionInfo -ProjectPath $projectPath
$shortCommit = Get-KnownFirstShortCommit -ProjectRoot $projectRoot
$signingMarker = Get-KnownFirstMsixSigningMarker -MsixSigning $MsixSigning

$artifactRoot = Join-Path $projectRoot "artifacts\windows-msix"
$packageName = Get-KnownFirstMsixPackageName `
    -ProductVersion $versionInfo.ProductVersion `
    -BuildNumber $versionInfo.BuildNumber `
    -ShortCommit $shortCommit `
    -SigningMarker $signingMarker `
    -IdentityMarker $identityMarker
$packagePath = Join-Path $artifactRoot $packageName
$checksumPath = "$packagePath.sha256.txt"

$appxPackageDir = Join-Path $projectRoot "artifacts\build\windows-msix\AppPackages\"

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$lockFilePath = Join-Path $artifactRoot "publish.lock"
$lockFileStream = $null
$stagingDir = $null
$finalFilesCreated = @()
$failureRecord = $null

try {
    try {
        $lockFileStream = [System.IO.File]::Open($lockFilePath, 'OpenOrCreate', 'ReadWrite', 'None')
    }
    catch [System.IO.IOException] {
        throw "Another Windows MSIX packaging process is already running."
    }

    if ((Test-Path -LiteralPath $packagePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Final artifact paths already exist. Collision protection failed closed."
    }

    $stagingDir = Join-Path $artifactRoot ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    $stagingPackagePath = Join-Path $stagingDir $packageName
    $stagingChecksumPath = "$stagingPackagePath.sha256.txt"

    # The isolated intermediate tree has its own project.assets.json, so this variant must be
    # restored on its own before a --no-restore publish can consume it.
    #
    # Every restore-graph-affecting property the publish below relies on MUST be supplied here as
    # well. SelfContained and WindowsAppSDKSelfContained are what put the .NET runtime pack and
    # the Windows App SDK runtime content into the restore graph; restoring without them and then
    # publishing with them produces NETSDK1112 ("the runtime pack ... was not downloaded"), or -
    # worse - succeeds only by accident on a machine where an unrelated restore already populated
    # those packs. WindowsDistributionPackagingContractTests pins this parity.
    $restoreArguments = @(
        "restore",
        $projectPath,
        "-p:Configuration=Release",
        "-p:KnownFirstWindowsMsixPackaging=true",
        "-p:RuntimeIdentifierOverride=win-x64",
        "-p:WindowsPackageType=MSIX",
        "-p:SelfContained=true",
        "-p:WindowsAppSDKSelfContained=true"
    )
    & dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windows MSIX restore failed with exit code $LASTEXITCODE."
    }

    # Stale output would make the single-candidate selection below untrustworthy.
    if (Test-Path -LiteralPath $appxPackageDir -PathType Container) {
        Remove-Item -LiteralPath $appxPackageDir -Recurse -Force
    }

    # WindowsPackageType=MSIX is supplied here rather than in the project file on purpose: the
    # project default stays None so that ordinary WindowsBuild and ValidateAll operations remain
    # unpackaged. Supplying it as a global property flips PublishAppXPackage to true for this one
    # invocation only.
    $publishArguments = @(
        "publish",
        $projectPath,
        "-f", "net10.0-windows10.0.19041.0",
        "-c", "Release",
        "--no-restore",
        "-p:KnownFirstWindowsMsixPackaging=true",
        "-p:RuntimeIdentifierOverride=win-x64",
        "-p:WindowsPackageType=MSIX",
        "-p:AppxBundle=Never",
        "-p:UapAppxPackageBuildMode=SideloadOnly",
        "-p:AppxPackageDir=$appxPackageDir",
        "-p:SelfContained=true",
        "-p:WindowsAppSDKSelfContained=true"
    )
    if ($MsixSigning -eq 'External') {
        # The thumbprint names an already installed certificate. It is appended to the argument
        # array and never written to output, so it cannot reach a launcher log or state record.
        $publishArguments += "-p:AppxPackageSigningEnabled=true"
        $publishArguments += "-p:PackageCertificateThumbprint=$signingThumbprint"
    }
    else {
        $publishArguments += "-p:AppxPackageSigningEnabled=false"
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windows MSIX publish failed with exit code $LASTEXITCODE."
    }

    $producedPackagePath = Select-KnownFirstMsixCandidate -AppxPackageDir $appxPackageDir

    Copy-Item -LiteralPath $producedPackagePath -Destination $stagingPackagePath
    if (-not (Test-Path -LiteralPath $stagingPackagePath -PathType Leaf)) {
        throw "The produced MSIX was not copied to $stagingPackagePath."
    }

    $stagedSha256 = (Get-FileHash -LiteralPath $stagingPackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($stagingChecksumPath, "$stagedSha256  $packageName`n", (New-Object System.Text.UTF8Encoding($false)))

    $stagedRecomputedSha256 = (Get-FileHash -LiteralPath $stagingPackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedSidecarContent = (Get-Content -LiteralPath $stagingChecksumPath -Raw).Trim()
    if ($stagedSidecarContent -notmatch "\A([a-f0-9]{64})[ \t]+([^\r\n]+)\r?\n?\z") {
        throw "Staged sidecar content is malformed."
    }
    $sidecarHash = $matches[1]
    $sidecarFilename = $matches[2]

    if ($sidecarFilename -ne $packageName) {
        throw "Staged sidecar filename does not match final package filename."
    }
    if ($stagedRecomputedSha256 -ne $stagedSha256) {
        throw "Recomputed staged hash does not equal expected staged hash."
    }
    if ($sidecarHash -ne $stagedSha256) {
        throw "Sidecar hash does not equal staged hash."
    }

    if ((Test-Path -LiteralPath $packagePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Final artifact paths exist prior to finalization step."
    }

    Move-Item -LiteralPath $stagingPackagePath -Destination $packagePath
    $finalFilesCreated += $packagePath
    Move-Item -LiteralPath $stagingChecksumPath -Destination $checksumPath
    $finalFilesCreated += $checksumPath

    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf) -or -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw "Final files do not exist after move."
    }

    $finalSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $finalSidecarContent = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($finalSidecarContent -notmatch "\A([a-f0-9]{64})[ \t]+([^\r\n]+)\r?\n?\z") {
        throw "Final sidecar content is malformed."
    }
    $finalSidecarHash = $matches[1]
    $finalSidecarFilename = $matches[2]

    if ($finalSidecarFilename -ne $packageName) {
        throw "Final sidecar filename does not match final package filename."
    }
    if ($finalSidecarHash -ne $finalSha256) {
        throw "Final sidecar hash does not equal final package hash."
    }
    if ($finalSha256 -ne $stagedSha256) {
        throw "Final package hash does not equal staged expected hash."
    }

    Write-Output "MSIX package: $packagePath"
    Write-Output "SHA-256: $finalSha256"
    Write-Output "Checksum: $checksumPath"
    Write-Output "Signing mode: $MsixSigning ($signingMarker)"
    Write-Output "Store identity: $identityMarker"
    Write-Output "Creating this package does not authorize installation, sideloading, or Microsoft Store submission."
}
catch {
    $failureRecord = $_
    try {
        if ($null -ne $finalFilesCreated) {
            foreach ($f in $finalFilesCreated) {
                if (Test-Path -LiteralPath $f -PathType Leaf) {
                    Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
    catch {
        # Suppress cleanup errors to avoid masking root failure
    }
}
finally {
    if ($null -ne $lockFileStream) {
        $lockFileStream.Dispose()
    }
    $signingThumbprint = $null
    if ($null -ne $stagingDir -and (Test-Path -LiteralPath $stagingDir -PathType Container)) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($null -ne $failureRecord) {
    throw $failureRecord
}
