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

$artifactRoot = Join-Path $projectRoot "artifacts\android-beta"
$apkName = "KnownFirst-0.1.0-beta.2-android.apk"
$apkPath = Join-Path $artifactRoot $apkName
$checksumPath = "$apkPath.sha256.txt"
$installationPath = Join-Path $artifactRoot "INSTALLATION.txt"
$zipPath = Join-Path $artifactRoot "KnownFirst-0.1.0-beta.2-android.zip"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

foreach ($path in @($apkPath, $checksumPath, $installationPath, $zipPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$previousPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
$env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $signingPassword
try {
    & dotnet publish $projectPath `
        -f net10.0-android `
        -c Release `
        -p:AndroidPackageFormats=apk `
        -p:AndroidKeyStore=true `
        "-p:AndroidSigningKeyStore=$KeystorePath" `
        -p:AndroidSigningKeyAlias=knownfirst-beta `
        -p:AndroidSigningKeyPass=env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD `
        -p:AndroidSigningStorePass=env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
    if ($LASTEXITCODE -ne 0) {
        throw "Android beta publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $previousPassword) {
        Remove-Item Env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $previousPassword
    }

    $signingPassword = $null
}

$releaseRoot = Join-Path $projectRoot "bin\Release\net10.0-android"
$signedApk = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Filter "*-Signed.apk" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $signedApk) {
    throw "The Android publish completed, but no signed APK was found under $releaseRoot."
}

Copy-Item -LiteralPath $signedApk.FullName -Destination $apkPath

$sdkCandidates = @(
    $env:ANDROID_SDK_ROOT,
    $env:ANDROID_HOME,
    $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA "Android\Sdk" }),
    $(if ([Environment]::GetFolderPath("ProgramFilesX86")) { Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Android\android-sdk" }),
    $(if ([Environment]::GetFolderPath("ProgramFiles")) { Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Android\android-sdk" })
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) }
$apkSigner = $sdkCandidates |
    ForEach-Object { Get-ChildItem -LiteralPath (Join-Path $_ "build-tools") -Directory -ErrorAction SilentlyContinue } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName "apksigner.bat" } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($apkSigner)) {
    throw "Android SDK apksigner was not found; the APK cannot be accepted without signature verification."
}

$javaHomes = @($env:JAVA_HOME)
$androidOpenJdkRoot = Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Android\openjdk"
if (Test-Path -LiteralPath $androidOpenJdkRoot -PathType Container) {
    $javaHomes += Get-ChildItem -LiteralPath $androidOpenJdkRoot -Directory |
        Sort-Object Name -Descending |
        Select-Object -ExpandProperty FullName
}
$javaHome = $javaHomes |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath (Join-Path $_ "bin\java.exe") -PathType Leaf) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($javaHome)) {
    throw "An Android-compatible Java installation was not found; apksigner cannot run."
}

$previousJavaHome = $env:JAVA_HOME
$env:JAVA_HOME = $javaHome
try {
    & $apkSigner verify --verbose --print-certs $apkPath
    if ($LASTEXITCODE -ne 0) {
        throw "APK signature verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $previousJavaHome) {
        Remove-Item Env:JAVA_HOME -ErrorAction SilentlyContinue
    }
    else {
        $env:JAVA_HOME = $previousJavaHome
    }
}

$sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Encoding utf8 -Value "$sha256  $apkName"
$installation = @(
    "KnownFirst 0.1.0-beta.2 for Android",
    "",
    "Package ID: com.tachiguro.knownfirst",
    "Minimum Android version: Android 7.0 (API 24)",
    "SHA-256: $sha256",
    "",
    "Installation:",
    "1. Copy $apkName to the Android device.",
    "2. Allow installation from the chosen file source when Android asks.",
    "3. Open the APK and confirm installation.",
    "",
    "Authorized adb alternative:",
    "adb install -r `"$apkName`"",
    "",
    "This beta is signed with the KnownFirst beta identity. Future updates must reuse the same keystore."
)
Set-Content -LiteralPath $installationPath -Encoding utf8 -Value $installation
Compress-Archive -LiteralPath @($apkPath, $installationPath) -DestinationPath $zipPath -CompressionLevel Optimal

Write-Output "APK: $apkPath"
Write-Output "ZIP: $zipPath"
Write-Output "SHA-256: $sha256"
Write-Output "Signature: verified"
