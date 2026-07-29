[CmdletBinding()]
param(
    [string]$KeystorePath,
    [string]$PasswordFilePath
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot "KnownFirst.csproj"
$secretsRoot = Join-Path ([Environment]::GetFolderPath("UserProfile")) "KnownFirst-Secrets"
if ([string]::IsNullOrWhiteSpace($KeystorePath)) {
    $KeystorePath = Join-Path $secretsRoot "knownfirst-beta.keystore"
}
if ([string]::IsNullOrWhiteSpace($PasswordFilePath)) {
    $PasswordFilePath = Join-Path $secretsRoot "knownfirst-beta-signing-password.txt"
}

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

$versionInfo = Get-KnownFirstVersionInfo -ProjectPath $projectPath

$artifactRoot = Join-Path $projectRoot "artifacts\android-google-play"
$bundleName = "KnownFirst-$($versionInfo.ProductVersion)-code$($versionInfo.BuildNumber).aab"
$bundlePath = Join-Path $artifactRoot $bundleName
$checksumPath = "$bundlePath.sha256.txt"

if ((Test-Path -LiteralPath $bundlePath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw "Final artifact paths already exist. Collision protection failed closed."
}

if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
    throw "The Android beta keystore is missing at $KeystorePath. Restore the existing signing identity before publishing."
}

$signingPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
if ([string]::IsNullOrWhiteSpace($signingPassword)) {
    if (-not (Test-Path -LiteralPath $PasswordFilePath -PathType Leaf)) {
        throw "The Android beta signing password is unavailable. Set KNOWNFIRST_ANDROID_SIGNING_PASSWORD or restore $PasswordFilePath."
    }

    $signingPassword = (Get-Content -LiteralPath $PasswordFilePath -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($signingPassword)) {
    throw "The Android beta signing password is empty."
}

$javaHomes = @($env:JAVA_HOME)
$androidOpenJdkRoot = Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Android\openjdk"
if (Test-Path -LiteralPath $androidOpenJdkRoot -PathType Container) {
    $javaHomes += Get-ChildItem -LiteralPath $androidOpenJdkRoot -Directory |
        Sort-Object Name -Descending |
        Select-Object -ExpandProperty FullName
}
$javaHome = $javaHomes |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath (Join-Path $_ "bin\jarsigner.exe") -PathType Leaf) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($javaHome)) {
    throw "An Android-compatible Java installation with jarsigner was not found."
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$stagingDir = Join-Path $artifactRoot ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$stagingBundlePath = Join-Path $stagingDir $bundleName
$stagingChecksumPath = "$stagingBundlePath.sha256.txt"

$previousPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
$previousJavaHome = $env:JAVA_HOME
$env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $signingPassword
$env:JAVA_HOME = $javaHome
$finalFilesCreated = @()
$failureMessage = $null

try {
    & dotnet clean $projectPath `
        -f net10.0-android `
        -c Release `
        --nologo `
        --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Android Release clean failed with exit code $LASTEXITCODE."
    }

    $publishStartedUtc = [DateTime]::UtcNow
    $publishArguments = @(
        "publish",
        $projectPath,
        "-f", "net10.0-android",
        "-c", "Release",
        "-m:1",
        "-p:TreatWarningsAsErrors=true",
        "-p:AndroidPackageFormats=aab",
        "-p:AndroidKeyStore=true",
        "-p:AndroidSigningKeyStore=$KeystorePath",
        "-p:AndroidSigningKeyAlias=knownfirst-beta",
        "-p:AndroidSigningKeyPass=env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD",
        "-p:AndroidSigningStorePass=env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD"
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Android Release AAB publish failed with exit code $LASTEXITCODE."
    }

    $releaseRoot = Join-Path $projectRoot "bin\Release\net10.0-android"
    $candidates = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Filter "*-Signed.aab" |
        Where-Object { $_.LastWriteTimeUtc -ge $publishStartedUtc.AddSeconds(-2) })
    if ($candidates.Count -eq 0) {
        throw "Android Release publish completed, but no newly signed AAB was found under $releaseRoot."
    }
    if ($candidates.Count -gt 1) {
        throw "Android Release publish completed, but candidate selection is ambiguous."
    }
    $signedBundle = $candidates[0]

    Copy-Item -LiteralPath $signedBundle.FullName -Destination $stagingBundlePath
    if (-not (Test-Path -LiteralPath $stagingBundlePath -PathType Leaf)) {
        throw "The signed AAB was not copied to $stagingBundlePath."
    }

    $jarSigner = Join-Path $javaHome "bin\jarsigner.exe"
    $jarsignerOutput = & $jarSigner -verify -strict $stagingBundlePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "AAB signature verification failed with exit code $LASTEXITCODE."
    }

    $stagedSha256 = (Get-FileHash -LiteralPath $stagingBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $stagingChecksumPath -Encoding utf8 -Value "$stagedSha256  $bundleName"

    $stagedRecomputedSha256 = (Get-FileHash -LiteralPath $stagingBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedSidecarContent = (Get-Content -LiteralPath $stagingChecksumPath -Raw).Trim()
    if ($stagedSidecarContent -notmatch "^([a-f0-9]{64})\s+(.+)$") {
        throw "Staged sidecar content is malformed."
    }
    $sidecarHash = $matches[1]
    $sidecarFilename = $matches[2]

    if ($sidecarFilename -ne $bundleName) {
        throw "Staged sidecar filename does not match final AAB filename."
    }
    if ($stagedRecomputedSha256 -ne $stagedSha256) {
        throw "Recomputed staged hash does not equal expected staged hash."
    }
    if ($sidecarHash -ne $stagedSha256) {
        throw "Sidecar hash does not equal staged hash."
    }

    if ((Test-Path -LiteralPath $bundlePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Final artifact paths exist prior to finalization step."
    }

    Move-Item -LiteralPath $stagingBundlePath -Destination $bundlePath
    $finalFilesCreated += $bundlePath
    Move-Item -LiteralPath $stagingChecksumPath -Destination $checksumPath
    $finalFilesCreated += $checksumPath

    if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf) -or -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw "Final files do not exist after move."
    }

    $finalSha256 = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $finalSidecarContent = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($finalSidecarContent -notmatch "^([a-f0-9]{64})\s+(.+)$") {
        throw "Final sidecar content is malformed."
    }
    $finalSidecarHash = $matches[1]
    $finalSidecarFilename = $matches[2]

    if ($finalSidecarFilename -ne $bundleName) {
        throw "Final sidecar filename does not match final AAB filename."
    }
    if ($finalSidecarHash -ne $finalSha256) {
        throw "Final sidecar hash does not equal final AAB hash."
    }
    if ($finalSha256 -ne $stagedSha256) {
        throw "Final AAB hash does not equal staged expected hash."
    }

    Write-Output "AAB: $bundlePath"
    Write-Output "SHA-256: $finalSha256"
    Write-Output "Checksum: $checksumPath"
    Write-Output "Signature: strict verification passed"
}
catch {
    $failureMessage = $_.Exception.Message
    if ($null -ne $finalFilesCreated) {
        foreach ($f in $finalFilesCreated) {
            if (Test-Path -LiteralPath $f -PathType Leaf) {
                Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
finally {
    if ($null -eq $previousPassword) {
        Remove-Item Env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $previousPassword
    }
    if ($null -eq $previousJavaHome) {
        Remove-Item Env:JAVA_HOME -ErrorAction SilentlyContinue
    }
    else {
        $env:JAVA_HOME = $previousJavaHome
    }

    $signingPassword = $null

    if ($null -ne $stagingDir -and (Test-Path -LiteralPath $stagingDir -PathType Container)) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($null -ne $failureMessage) {
    Write-Error "Release AAB publishing failed: $failureMessage" -ErrorAction Continue
    exit 1
}
