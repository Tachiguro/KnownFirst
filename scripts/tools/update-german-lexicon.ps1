<#
.SYNOPSIS
    Deterministically regenerates the offline German lexicon runtime asset from an explicitly
    pinned DuyguA/german-morph-dictionaries commit.

.DESCRIPTION
    Wraps `scripts/tools/GermanLexiconGenerator` (a KnownFirst Apache-2.0 .NET console tool).
    Always requires an explicit -Commit; there is no "latest"/branch-HEAD mode. Fetches
    morf_dict.zip and LICENSE from that exact commit, verifies the upstream LICENSE still
    designates CC BY-SA 4.0, parses the dictionary, and publishes a single compact runtime
    bundle file (`german-lexicon.v2.kfgl`) to -OutDir only after the generator's own staging and
    validation succeed. The bundle carries its own machine-readable provenance (upstream
    project/repository/commit, source path, source SHA-256, license identifier, and generation
    counts) embedded alongside the lexical data — there is no separate manifest file. The static
    `NOTICE.md` and `UPSTREAM_LICENSE.txt` files in the output directory are human-maintained and
    are not written, regenerated, or otherwise touched by this update.

.PARAMETER Commit
    The exact upstream commit SHA to fetch and generate from. Mandatory. Never a branch name.

.PARAMETER OutDir
    Destination directory for the generated `german-lexicon.v2.kfgl` bundle. Defaults to the
    committed production asset location.

.EXAMPLE
    ./scripts/tools/update-german-lexicon.ps1 -Commit 1780890c0fd25a989201c96000af323cd201fa5c
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$Commit,

    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\KnownFirst.Core\Text\German\Assets')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$toolProject = Join-Path $repoRoot 'scripts\tools\GermanLexiconGenerator\GermanLexiconGenerator.csproj'

if (-not (Test-Path $toolProject)) {
    throw "German lexicon generator project not found at '$toolProject'."
}

Write-Host "Updating German lexicon runtime asset from pinned commit $Commit" -ForegroundColor Cyan
Write-Host "Output directory: $OutDir"

& dotnet run --project $toolProject -c Release -- update --commit $Commit --out $OutDir
if ($LASTEXITCODE -ne 0) {
    throw "German lexicon generation failed with exit code $LASTEXITCODE."
}

Write-Host "German lexicon runtime asset updated successfully." -ForegroundColor Green
