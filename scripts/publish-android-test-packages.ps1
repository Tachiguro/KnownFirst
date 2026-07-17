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
    throw "Android SDK apksigner was not found; the APKs cannot be accepted without signature verification."
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

$artifactRoot = Join-Path $projectRoot "artifacts\android-beta"
$stagingRoot = Join-Path $artifactRoot ".staging"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$packages = @(
    [pscustomobject]@{
        Configuration = "Release"
        Kind = "release"
        BaseName = "KnownFirst-0.1.0-beta.3-android-release"
        Title = "KnownFirst"
        PackageId = "com.tachiguro.knownfirst"
        Version = "0.1.0-beta.3"
    },
    [pscustomobject]@{
        Configuration = "BetaDiagnostic"
        Kind = "diagnostic"
        BaseName = "KnownFirst-0.1.0-beta.3-android-diagnostic"
        Title = "KnownFirst Diagnostic"
        PackageId = "com.tachiguro.knownfirst.diagnostic"
        Version = "0.1.0-beta.3-diagnostic"
    },
    [pscustomobject]@{
        Configuration = "Debug"
        Kind = "debug"
        BaseName = "KnownFirst-0.1.0-beta.3-android-debug"
        Title = "KnownFirst Debug"
        PackageId = "com.tachiguro.knownfirst.debug"
        Version = "0.1.0-beta.3-debug"
    }
)

$previousPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
$previousJavaHome = $env:JAVA_HOME
$env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $signingPassword
$env:JAVA_HOME = $javaHome
try {
    foreach ($package in $packages) {
        $apkName = "$($package.BaseName).apk"
        $apkPath = Join-Path $artifactRoot $apkName
        $checksumPath = "$apkPath.sha256.txt"
        $zipPath = Join-Path $artifactRoot "$($package.BaseName).zip"
        $stagingPath = Join-Path $stagingRoot $package.Kind
        foreach ($path in @($apkPath, $checksumPath, $zipPath, $stagingPath)) {
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Recurse -Force
            }
        }

        & dotnet clean $projectPath `
            -f net10.0-android `
            -c $package.Configuration `
            --nologo `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "$($package.Configuration) Android clean failed with exit code $LASTEXITCODE."
        }

        $publishStartedUtc = [DateTime]::UtcNow
        $publishArguments = @(
            "publish",
            $projectPath,
            "-f", "net10.0-android",
            "-c", $package.Configuration,
            "-p:AndroidPackageFormats=apk",
            "-p:AndroidKeyStore=true",
            "-p:AndroidSigningKeyStore=$KeystorePath",
            "-p:AndroidSigningKeyAlias=knownfirst-beta",
            "-p:AndroidSigningKeyPass=env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD",
            "-p:AndroidSigningStorePass=env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD"
        )
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$($package.Configuration) Android publish failed with exit code $LASTEXITCODE."
        }

        $configurationRoot = Join-Path $projectRoot "bin\$($package.Configuration)\net10.0-android"
        $signedApk = Get-ChildItem -LiteralPath $configurationRoot -Recurse -File -Filter "*-Signed.apk" |
            Where-Object { $_.LastWriteTimeUtc -ge $publishStartedUtc.AddSeconds(-2) } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $signedApk) {
            throw "$($package.Configuration) publish completed, but no newly signed APK was found under $configurationRoot."
        }

        Copy-Item -LiteralPath $signedApk.FullName -Destination $apkPath
        & $apkSigner verify --verbose --print-certs $apkPath
        if ($LASTEXITCODE -ne 0) {
            throw "$($package.Configuration) APK signature verification failed with exit code $LASTEXITCODE."
        }

        $sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath $checksumPath -Encoding utf8 -Value "$sha256  $apkName"

        New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
        $stagingApk = Join-Path $stagingPath $apkName
        $installationPath = Join-Path $stagingPath "INSTALLATION.txt"
        Copy-Item -LiteralPath $apkPath -Destination $stagingApk
        $installation = @(
            "$($package.Title) $($package.Version) for Android",
            "",
            "Package ID: $($package.PackageId)",
            "Application version: 3",
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
            "This package is signed with the existing KnownFirst beta identity. Future updates must reuse the same keystore."
        )
        Set-Content -LiteralPath $installationPath -Encoding utf8 -Value $installation
        Compress-Archive -LiteralPath @($stagingApk, $installationPath) -DestinationPath $zipPath -CompressionLevel Optimal

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $entryNames = @($archive.Entries | Select-Object -ExpandProperty FullName | Sort-Object)
            $expectedEntryNames = @("INSTALLATION.txt", $apkName) | Sort-Object
            if ($entryNames.Count -ne 2 -or (Compare-Object $entryNames $expectedEntryNames)) {
                throw "$($package.Configuration) ZIP contains unexpected entries: $($entryNames -join ', ')."
            }
        }
        finally {
            $archive.Dispose()
        }

        Write-Output "$($package.Kind): APK=$apkPath"
        Write-Output "$($package.Kind): ZIP=$zipPath"
        Write-Output "$($package.Kind): SHA-256=$sha256"
        Write-Output "$($package.Kind): Signature=verified"
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
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
