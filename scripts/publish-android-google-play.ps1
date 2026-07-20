[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2100000000)]
    [int]$VersionCode,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DisplayVersion,

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

$previousPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
$previousJavaHome = $env:JAVA_HOME
$signingPassword = $null
$failureMessage = $null

try {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "KnownFirst.csproj was not found at $projectPath."
    }
    if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
        throw "The Android beta keystore is missing. Restore the existing signing identity before publishing."
    }

    $signingPassword = $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD
    if ([string]::IsNullOrWhiteSpace($signingPassword)) {
        if (-not (Test-Path -LiteralPath $PasswordFilePath -PathType Leaf)) {
            throw "The Android beta signing password is unavailable. Set KNOWNFIRST_ANDROID_SIGNING_PASSWORD or restore the external password file."
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
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            (Test-Path -LiteralPath (Join-Path $_ "bin\jarsigner.exe") -PathType Leaf)
        } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($javaHome)) {
        throw "An Android-compatible Java installation with jarsigner was not found."
    }

    $safeDisplayVersion = $DisplayVersion.Trim() -replace '[^0-9A-Za-z._-]', '-'
    if ([string]::IsNullOrWhiteSpace($safeDisplayVersion)) {
        throw "DisplayVersion must contain at least one filename-safe character."
    }

    $artifactRoot = Join-Path $projectRoot "artifacts\android\google-play-internal"
    $bundleName = "KnownFirst-$safeDisplayVersion-code$VersionCode-google-play-internal.aab"
    $bundlePath = Join-Path $artifactRoot $bundleName
    $checksumPath = "$bundlePath.sha256.txt"
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    foreach ($path in @($bundlePath, $checksumPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    $env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD = $signingPassword
    $env:JAVA_HOME = $javaHome

    & dotnet restore $projectPath `
        -p:Configuration=Release `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Android Release restore failed with exit code $LASTEXITCODE."
    }

    $publishStartedUtc = [DateTime]::UtcNow
    $publishArguments = @(
        "publish",
        $projectPath,
        "-f", "net10.0-android",
        "-c", "Release",
        "--no-restore",
        "-p:ApplicationVersion=$VersionCode",
        "-p:ApplicationDisplayVersion=$($DisplayVersion.Trim())",
        "-p:ApplicationId=com.tachiguro.knownfirst",
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

    $configurationRoot = Join-Path $projectRoot "bin\Release\net10.0-android"
    $signedBundle = Get-ChildItem -LiteralPath $configurationRoot -Recurse -File -Filter "*-Signed.aab" |
        Where-Object { $_.LastWriteTimeUtc -ge $publishStartedUtc.AddSeconds(-2) } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $signedBundle) {
        throw "Android Release publish completed, but no newly signed AAB was found."
    }

    Copy-Item -LiteralPath $signedBundle.FullName -Destination $bundlePath
    if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
        throw "The signed AAB was not copied to the Google Play artifact directory."
    }

    $jarSigner = Join-Path $javaHome "bin\jarsigner.exe"
    & $jarSigner -verify $bundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "AAB signature verification failed with exit code $LASTEXITCODE."
    }

    $sha256 = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $checksumPath -Encoding utf8 -Value "$sha256  $bundleName"
    Write-Output "AAB: $bundlePath"
    Write-Output "SHA-256: $sha256"
    Write-Output "Checksum: $checksumPath"
    Write-Output "Signature: verified"
}
catch {
    $failureMessage = $_.Exception.Message
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

if ($null -ne $failureMessage) {
    Write-Error "Release AAB publishing failed: $failureMessage" -ErrorAction Continue
    exit 1
}
