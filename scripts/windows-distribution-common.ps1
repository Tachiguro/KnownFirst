<#
.SYNOPSIS
    Shared, side-effect-free helpers for the two Windows distribution channels.

.DESCRIPTION
    This file is dot-sourced by scripts/knownfirst.ps1, scripts/publish-windows-portable.ps1, and
    scripts/publish-windows-msix.ps1 so that artifact naming and identity classification have a
    single source of truth.

    The launcher must be able to predict the exact final artifact path before running anything, in
    order to evaluate its reusable-result records under artifacts\launcher-state\. Previously the
    launcher and each publishing script derived those names independently; if the two derivations
    ever drifted, the launcher would record an expected output that the publisher never produced,
    and every subsequent run would fail on the publisher's own collision guard. Centralizing the
    derivations here removes that failure mode structurally instead of relying on a comment.

    Contract for everything in this file:
      - pure and non-mutating: no build, publish, package, install, network, or device operation;
      - creates no files or directories (the ZIP and MSIX inspectors open existing paths read-only);
      - contains no signing secret and never reads a certificate thumbprint, password, or store;
      - safe to dot-source from the launcher and from a standalone publishing script.

    Windows PowerShell 5.1 compatible: no ternary operators and no parenthesized inline `if` used
    as a command argument.
#>

# --- BEGIN SHORT COMMIT CONTRACT ---
# Both the launcher and the publishing scripts embed the same commit prefix in every artifact
# filename. `git rev-parse --short` is deliberately NOT used: its abbreviation length widens to
# disambiguate, so it cannot be relied on to produce the same string in two separate processes.
function Get-KnownFirstShortCommitPrefix {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$HeadSha
    )

    if ($HeadSha.Length -lt 7) {
        return ''
    }
    return $HeadSha.Substring(0, 7)
}
# --- END SHORT COMMIT CONTRACT ---

# --- BEGIN PORTABLE ARTIFACT NAME CONTRACT ---
function Get-KnownFirstPortableArchiveName {
    param(
        [Parameter(Mandatory = $true)][string]$ProductVersion,
        [Parameter(Mandatory = $true)][string]$BuildNumber,
        [Parameter(Mandatory = $true)][string]$ShortCommit
    )

    return "KnownFirst-$ProductVersion-build$BuildNumber-win-x64-$ShortCommit.zip"
}
# --- END PORTABLE ARTIFACT NAME CONTRACT ---

# --- BEGIN MSIX SIGNING MARKER CONTRACT ---
# The marker describes what the artifact actually is. An externally signed package must never be
# labelled "unsigned": that file is the only one that can be sideloaded, so mislabelling it would
# be actively misleading.
function Get-KnownFirstMsixSigningMarker {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('None', 'External')]
        [string]$MsixSigning
    )

    if ($MsixSigning -eq 'External') {
        return 'signed'
    }
    return 'unsigned'
}
# --- END MSIX SIGNING MARKER CONTRACT ---

# --- BEGIN STORE IDENTITY PLACEHOLDER CONTRACT ---
# MAUI's GeneratePackageAppxManifest task substitutes the package Identity Name (from
# $(ApplicationId)), the package Version, the display name, and the logos. It never substitutes
# Publisher or PublisherDisplayName. A Microsoft Store submission additionally requires the
# Identity Name to match a name reserved in Partner Center, which $(ApplicationId) is not.
#
# Every required Partner Center identity component is therefore inspected, and the artifact is
# classified as a development-identity artifact when ANY of them is still a known MAUI/development
# placeholder, is empty, or is missing. Only a manifest whose three identity fields are all
# explicitly non-placeholder yields 'storeidentity'. This fails safe: an unknown or partially
# configured manifest is never presented as Store-submission ready.
function Test-KnownFirstIdentityPlaceholderValue {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $normalized = $Value.Trim()
    $placeholders = @(
        'maui-package-name-placeholder',
        'CN=User Name',
        'User Name',
        '$placeholder$'
    )
    if ($placeholders -contains $normalized) {
        return $true
    }
    return $false
}

function Get-KnownFirstMsixIdentityMarker {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "The Windows package manifest was not found at $ManifestPath."
    }

    try {
        [xml]$manifestXml = Get-Content -LiteralPath $ManifestPath -Raw
    }
    catch {
        throw "The Windows package manifest at $ManifestPath could not be parsed as XML."
    }

    $identityName = [string]$manifestXml.Package.Identity.Name
    $publisher = [string]$manifestXml.Package.Identity.Publisher
    $publisherDisplayName = [string]$manifestXml.Package.Properties.PublisherDisplayName

    $hasPlaceholder = $false
    foreach ($identityValue in @($identityName, $publisher, $publisherDisplayName)) {
        if (Test-KnownFirstIdentityPlaceholderValue -Value $identityValue) {
            $hasPlaceholder = $true
        }
    }

    if ($hasPlaceholder) {
        return 'devidentity'
    }
    return 'storeidentity'
}
# --- END STORE IDENTITY PLACEHOLDER CONTRACT ---

# --- BEGIN MSIX ARTIFACT NAME CONTRACT ---
function Get-KnownFirstMsixPackageName {
    param(
        [Parameter(Mandatory = $true)][string]$ProductVersion,
        [Parameter(Mandatory = $true)][string]$BuildNumber,
        [Parameter(Mandatory = $true)][string]$ShortCommit,
        [Parameter(Mandatory = $true)][string]$SigningMarker,
        [Parameter(Mandatory = $true)][string]$IdentityMarker
    )

    return "KnownFirst-$ProductVersion-build$BuildNumber-x64-$ShortCommit-$SigningMarker-$IdentityMarker.msix"
}
# --- END MSIX ARTIFACT NAME CONTRACT ---

# --- BEGIN MSIX CANDIDATE SELECTION CONTRACT ---
# Fail closed on zero and on more than one produced package: an ambiguous selection must never be
# resolved by guessing, because the wrong file would then be hashed and published as the artifact.
function Select-KnownFirstMsixCandidate {
    param([Parameter(Mandatory = $true)][string]$AppxPackageDir)

    if (-not (Test-Path -LiteralPath $AppxPackageDir -PathType Container)) {
        throw "Windows MSIX publish completed, but no package directory was created at $AppxPackageDir."
    }

    $candidates = @(Get-ChildItem -LiteralPath $AppxPackageDir -Recurse -File -Filter "*.msix")
    if ($candidates.Count -eq 0) {
        throw "Windows MSIX publish completed, but no .msix package was found under $AppxPackageDir."
    }
    if ($candidates.Count -gt 1) {
        throw "Windows MSIX publish completed, but candidate selection is ambiguous: $($candidates.Count) .msix packages were found under $AppxPackageDir."
    }

    return $candidates[0].FullName
}
# --- END MSIX CANDIDATE SELECTION CONTRACT ---

# --- BEGIN ZIP FILE ENTRY COUNT CONTRACT ---
# ZipFile.CreateFromDirectory emits an entry for every EMPTY directory in addition to one entry
# per file. Such directory entries have an empty Name, and counting them would make an otherwise
# valid archive look like it had gained entries. Only file entries are counted here so that a
# publish payload containing an empty directory cannot cause a false packaging failure.
function Get-KnownFirstZipFileEntryCount {
    param([Parameter(Mandatory = $true)][string]$ArchivePath)

    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
        throw "The archive was not found at $ArchivePath."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        return @($archive.Entries | Where-Object { $_.Name -ne '' }).Count
    }
    finally {
        $archive.Dispose()
    }
}
# --- END ZIP FILE ENTRY COUNT CONTRACT ---
