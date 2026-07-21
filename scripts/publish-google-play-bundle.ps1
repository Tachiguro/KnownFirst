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

$artifactRoot = Join-Path $projectRoot "artifacts\android-google-play"
$bundleName = "KnownFirst-1.0.0-beta.5-code5.aab"
$bundlePath = Join-Path $artifactRoot $bundleName
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
if (Test-Path -LiteralPath $bundlePath) {
    Remove-Item -LiteralPath $bundlePath -Force
}

$previousPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
$previousJavaHome = $env:JAVA_HOME
$env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $signingPassword
$env:JAVA_HOME = $javaHome
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
    $signedBundle = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Filter "*-Signed.aab" |
        Where-Object { $_.LastWriteTimeUtc -ge $publishStartedUtc.AddSeconds(-2) } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $signedBundle) {
        throw "Android Release publish completed, but no newly signed AAB was found under $releaseRoot."
    }

    Copy-Item -LiteralPath $signedBundle.FullName -Destination $bundlePath
    if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
        throw "The signed AAB was not copied to $bundlePath."
    }

    $jarSigner = Join-Path $javaHome "bin\jarsigner.exe"
    & $jarSigner -verify $bundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "AAB signature verification failed with exit code $LASTEXITCODE."
    }

    $sha256 = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "AAB: $bundlePath"
    Write-Output "SHA-256: $sha256"
    Write-Output "Signature: verified"
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
}
