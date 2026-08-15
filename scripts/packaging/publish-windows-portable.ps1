<#
.SYNOPSIS
    Creates one transportable, self-contained, unpackaged Windows x64 Release payload of
    KnownFirst and archives the COMPLETE payload as a ZIP with a SHA-256 sidecar.

.DESCRIPTION
    This is the canonical portable Windows distribution artifact. It is a manual replacement
    channel: the ZIP carries no updater, no version check, and no self-update mechanism.
    Replacing an installed copy means extracting a newer ZIP by hand. The Microsoft Store MSIX
    channel (scripts/packaging/publish-windows-msix.ps1) is the canonical production install/update path.

    Nothing is installed, launched, signed, uploaded, or published. The produced ZIP is written
    to artifacts\windows-portable\ and is never committed (artifacts\ is git-ignored).

    Safety model, mirroring the existing canonical packaging scripts in scripts/:
      - a clean worktree and a resolvable HEAD are required, because the artifact filename
        carries a commit identity that must not misdescribe modified content;
      - a FileShare.None lock serialises concurrent packaging runs;
      - existing final artifact paths fail closed instead of being overwritten;
      - everything is built in a GUID staging directory and moved into place only after the
        archive, its hash, and its sidecar have all been verified;
      - a failed run rolls back anything it already finalised and preserves the original error.

    Build isolation: the packaging publish uses its own output and intermediate trees under
    artifacts\build\windows-portable\ and artifacts\obj\windows-portable\, so it can never
    rewrite bin\Release\..., whose files back the WindowsBuild and ValidateAll reusable-result
    records in artifacts\launcher-state\.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $projectRoot "KnownFirst.csproj"
$targetFramework = "net10.0-windows10.0.19041.0"
$runtimeIdentifier = "win-x64"

# Artifact naming is shared with the launcher so both derive the identical final path.
. (Join-Path $PSScriptRoot 'windows-distribution-common.ps1')

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "KnownFirst.csproj was not found at $projectPath."
}

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

# A commit identity is embedded in the artifact filename, so a dirty or unresolvable worktree must
# fail closed: the name would otherwise misdescribe modified content. The seven-character prefix
# itself is derived by the shared helper, which the launcher uses too.
function Get-KnownFirstShortCommit {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $status = & git -C $ProjectRoot status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine the Git worktree state for $ProjectRoot."
    }
    if (-not [string]::IsNullOrEmpty(($status -join ''))) {
        throw "The worktree is not clean. A portable package filename carries a commit identity and must never describe modified content. Commit or revert the pending changes first."
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

$versionInfo = Get-KnownFirstVersionInfo -ProjectPath $projectPath
$shortCommit = Get-KnownFirstShortCommit -ProjectRoot $projectRoot

$artifactRoot = Join-Path $projectRoot "artifacts\windows-portable"
$archiveName = Get-KnownFirstPortableArchiveName `
    -ProductVersion $versionInfo.ProductVersion `
    -BuildNumber $versionInfo.BuildNumber `
    -ShortCommit $shortCommit
$archivePath = Join-Path $artifactRoot $archiveName
$checksumPath = "$archivePath.sha256.txt"

$publishRoot = Join-Path $projectRoot "artifacts\build\windows-portable\Release\$targetFramework\$runtimeIdentifier\publish"

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
        throw "Another Windows portable packaging process is already running."
    }

    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Final artifact paths already exist. Collision protection failed closed."
    }

    $stagingDir = Join-Path $artifactRoot ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    $stagingArchivePath = Join-Path $stagingDir $archiveName
    $stagingChecksumPath = "$stagingArchivePath.sha256.txt"

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
        "-p:KnownFirstWindowsPortablePackaging=true",
        "-p:RuntimeIdentifierOverride=win-x64",
        "-p:WindowsPackageType=None",
        "-p:SelfContained=true",
        "-p:WindowsAppSDKSelfContained=true"
    )
    & dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windows portable restore failed with exit code $LASTEXITCODE."
    }

    if (Test-Path -LiteralPath $publishRoot -PathType Container) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }

    # WindowsAppSDKSelfContained already defaults to true for an unpackaged MAUI app, but
    # SelfContained does not: without it the payload is framework-dependent and the target PC
    # needs the .NET Desktop Runtime installed, which defeats the purpose of a portable package.
    # Both are therefore stated explicitly so the contract cannot drift with a future SDK default.
    $publishArguments = @(
        "publish",
        $projectPath,
        "-f", "net10.0-windows10.0.19041.0",
        "-c", "Release",
        "--no-restore",
        "-p:KnownFirstWindowsPortablePackaging=true",
        "-p:RuntimeIdentifierOverride=win-x64",
        "-p:WindowsPackageType=None",
        "-p:WindowsAppSDKSelfContained=true",
        "-p:SelfContained=true",
        "-p:PublishAppXPackage=false"
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windows portable publish failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
        throw "The publish directory was not created at $publishRoot."
    }

    # A transportable payload must be complete. hostfxr.dll proves the .NET host really was
    # included (SelfContained applied) and the bootstrapper proves the Windows App SDK really was
    # included; without these the ZIP would silently require a runtime install on the target PC.
    $requiredPayloadFiles = @(
        "KnownFirst.exe",
        "KnownFirst.dll",
        "hostfxr.dll",
        "Microsoft.WindowsAppRuntime.Bootstrap.dll"
    )
    foreach ($requiredFile in $requiredPayloadFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile) -PathType Leaf)) {
            throw "The publish payload is incomplete: $requiredFile is missing from $publishRoot. The package would not be self-contained."
        }
    }

    $payloadFileCount = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File).Count

    # The COMPLETE publish directory is archived, never a hand-picked file list: the executable
    # alone is not runnable.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $publishRoot,
        $stagingArchivePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    if (-not (Test-Path -LiteralPath $stagingArchivePath -PathType Leaf)) {
        throw "The portable archive was not created at $stagingArchivePath."
    }

    # Only file entries are compared. CreateFromDirectory also emits an entry for every empty
    # directory, so counting raw entries would reject a valid payload that happens to contain one.
    $stagedFileEntryCount = Get-KnownFirstZipFileEntryCount -ArchivePath $stagingArchivePath
    if ($stagedFileEntryCount -ne $payloadFileCount) {
        throw "The portable archive contains $stagedFileEntryCount file entries but the publish payload has $payloadFileCount files."
    }

    $stagedSha256 = (Get-FileHash -LiteralPath $stagingArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($stagingChecksumPath, "$stagedSha256  $archiveName`n", (New-Object System.Text.UTF8Encoding($false)))

    $stagedRecomputedSha256 = (Get-FileHash -LiteralPath $stagingArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedSidecarContent = (Get-Content -LiteralPath $stagingChecksumPath -Raw).Trim()
    if ($stagedSidecarContent -notmatch "\A([a-f0-9]{64})[ \t]+([^\r\n]+)\r?\n?\z") {
        throw "Staged sidecar content is malformed."
    }
    $sidecarHash = $matches[1]
    $sidecarFilename = $matches[2]

    if ($sidecarFilename -ne $archiveName) {
        throw "Staged sidecar filename does not match final archive filename."
    }
    if ($stagedRecomputedSha256 -ne $stagedSha256) {
        throw "Recomputed staged hash does not equal expected staged hash."
    }
    if ($sidecarHash -ne $stagedSha256) {
        throw "Sidecar hash does not equal staged hash."
    }

    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Final artifact paths exist prior to finalization step."
    }

    Move-Item -LiteralPath $stagingArchivePath -Destination $archivePath
    $finalFilesCreated += $archivePath
    Move-Item -LiteralPath $stagingChecksumPath -Destination $checksumPath
    $finalFilesCreated += $checksumPath

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw "Final files do not exist after move."
    }

    $finalSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $finalSidecarContent = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($finalSidecarContent -notmatch "\A([a-f0-9]{64})[ \t]+([^\r\n]+)\r?\n?\z") {
        throw "Final sidecar content is malformed."
    }
    $finalSidecarHash = $matches[1]
    $finalSidecarFilename = $matches[2]

    if ($finalSidecarFilename -ne $archiveName) {
        throw "Final sidecar filename does not match final archive filename."
    }
    if ($finalSidecarHash -ne $finalSha256) {
        throw "Final sidecar hash does not equal final archive hash."
    }
    if ($finalSha256 -ne $stagedSha256) {
        throw "Final archive hash does not equal staged expected hash."
    }

    Write-Output "Portable package: $archivePath"
    Write-Output "SHA-256: $finalSha256"
    Write-Output "Checksum: $checksumPath"
    Write-Output "Payload files: $payloadFileCount"
    Write-Output "Update channel: manual replacement only. This package contains no updater."
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
    if ($null -ne $stagingDir -and (Test-Path -LiteralPath $stagingDir -PathType Container)) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($null -ne $failureRecord) {
    throw $failureRecord
}
