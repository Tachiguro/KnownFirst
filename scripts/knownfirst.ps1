<#
.SYNOPSIS
    Single entry point for the everyday KnownFirst local build/test/package operations.

.DESCRIPTION
    Running this script without arguments shows a numbered interactive menu. It can also be
    driven non-interactively with -Action for scripting or CI-style usage. Every operation
    prints a plain-language explanation before it runs and the resulting output path when it
    finishes. Nothing is ever uploaded to Google Play and nothing is installed on a device
    automatically; package creation ("Create Android test package" / "Create Google Play
    bundle") calls the existing, already-reviewed publish-android-test-packages.ps1 and
    publish-google-play-bundle.ps1 scripts instead of duplicating their signing logic.

.PARAMETER Action
    Test | WindowsBuild | AndroidTestPackage | GooglePlayBundle | ValidateAll
    When omitted, the interactive menu is shown instead.

.PARAMETER WhatIf
    Prints what each operation would do and its expected output path without running it.
    Useful to validate an -Action name or check the launcher itself without building,
    testing, or creating any package.

.PARAMETER KeystorePath
    Forwarded to the Android publish scripts. See their own defaults if omitted.

.PARAMETER PasswordFilePath
    Forwarded to the Android publish scripts. See their own defaults if omitted.

.EXAMPLE
    .\scripts\knownfirst.ps1
    Shows the interactive menu.

.EXAMPLE
    .\scripts\knownfirst.ps1 -Action Test

.EXAMPLE
    .\scripts\knownfirst.ps1 -Action GooglePlayBundle -WhatIf
    Prints what would happen without creating an AAB.
#>
[CmdletBinding()]
param(
    [ValidateSet('Test', 'WindowsBuild', 'AndroidTestPackage', 'GooglePlayBundle', 'ValidateAll')]
    [string]$Action,

    [switch]$WhatIf,

    [string]$KeystorePath,

    [string]$PasswordFilePath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $projectRoot 'KnownFirst.csproj'
$testProjectPath = Join-Path $projectRoot 'KnownFirst.Tests\KnownFirst.Tests.csproj'

function Invoke-DotNetOrFail {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (dotnet exit code $LASTEXITCODE)."
    }
}

function Invoke-RunTests {
    Write-Host 'Restoring and running the complete KnownFirst.Tests suite (every unit test). This only compiles and executes tests; nothing is installed or packaged.'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: dotnet restore/test on $testProjectPath"
        return
    }

    Invoke-DotNetOrFail -Arguments @('restore', $testProjectPath) -FailureMessage 'Test project restore failed'
    Invoke-DotNetOrFail -Arguments @(
        'test', $testProjectPath, '-c', 'Debug', '--no-restore', '--logger', 'console;verbosity=minimal'
    ) -FailureMessage 'The test suite failed'
    Write-Host 'Result: all tests passed.'
}

function Invoke-WindowsBuildAction {
    Write-Host 'Building the Windows desktop app in Debug and Release. This only compiles the app for local use; it does not package, sign, or publish anything for distribution.'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: dotnet build $projectPath -f net10.0-windows10.0.19041.0 (Debug, Release)"
        return
    }

    Invoke-DotNetOrFail -Arguments @('restore', $projectPath) -FailureMessage 'App restore failed'
    foreach ($configuration in @('Debug', 'Release')) {
        Invoke-DotNetOrFail -Arguments @(
            'build', $projectPath, '-c', $configuration, '-f', 'net10.0-windows10.0.19041.0', '--no-restore'
        ) -FailureMessage "Windows $configuration build failed"
        $outputPath = Join-Path $projectRoot "bin\$configuration\net10.0-windows10.0.19041.0\win-x64\KnownFirst.dll"
        Write-Host "Output ($configuration): $outputPath"
    }
}

function Invoke-AndroidTestPackageAction {
    Write-Host 'Creating signed Android APKs for manual sideload testing (release, diagnostic, and debug builds). This is NOT the Google Play bundle, is never uploaded anywhere, and nothing is installed automatically on any device.'
    $scriptPath = Join-Path $scriptRoot 'publish-android-test-packages.ps1'
    $outputRoot = Join-Path $projectRoot 'artifacts\android-beta'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: $scriptPath -> $outputRoot"
        return
    }

    $forwardedArguments = @{}
    if ($KeystorePath) { $forwardedArguments['KeystorePath'] = $KeystorePath }
    if ($PasswordFilePath) { $forwardedArguments['PasswordFilePath'] = $PasswordFilePath }
    & $scriptPath @forwardedArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Android test package creation failed (exit code $LASTEXITCODE)."
    }

    Write-Host "Output: $outputRoot"
}

function Invoke-GooglePlayBundleAction {
    Write-Host 'Creating the signed Android App Bundle (.aab) for Google Play. This produces a local file only; this script never uploads it to Google Play.'
    $scriptPath = Join-Path $scriptRoot 'publish-google-play-bundle.ps1'
    $outputRoot = Join-Path $projectRoot 'artifacts\android-google-play'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: $scriptPath -> $outputRoot"
        return
    }

    $forwardedArguments = @{}
    if ($KeystorePath) { $forwardedArguments['KeystorePath'] = $KeystorePath }
    if ($PasswordFilePath) { $forwardedArguments['PasswordFilePath'] = $PasswordFilePath }
    & $scriptPath @forwardedArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Google Play bundle creation failed (exit code $LASTEXITCODE)."
    }

    Write-Host "Output: $outputRoot"
}

function Invoke-ValidateAllAction {
    Write-Host 'Running the full local validation matrix: restore, the complete test suite, then Windows and Android builds in both Debug and Release. This mirrors what must pass before any release and creates no APK, AAB, or other package.'
    if ($WhatIf) {
        Write-Host '[WhatIf] Would run: restore, dotnet test, and four dotnet build passes (Windows Debug/Release, Android Debug/Release).'
        return
    }

    Invoke-DotNetOrFail -Arguments @('restore', $projectPath) -FailureMessage 'App restore failed'
    Invoke-DotNetOrFail -Arguments @('restore', $testProjectPath) -FailureMessage 'Test project restore failed'
    Invoke-DotNetOrFail -Arguments @(
        'test', $testProjectPath, '-c', 'Debug', '--no-restore', '--logger', 'console;verbosity=minimal'
    ) -FailureMessage 'The complete test suite failed'

    foreach ($configuration in @('Debug', 'Release')) {
        Invoke-DotNetOrFail -Arguments @(
            'build', $projectPath, '-c', $configuration, '-f', 'net10.0-windows10.0.19041.0', '--no-restore'
        ) -FailureMessage "Windows $configuration build failed"
    }
    foreach ($configuration in @('Debug', 'Release')) {
        Invoke-DotNetOrFail -Arguments @(
            'build', $projectPath, '-c', $configuration, '-f', 'net10.0-android', '-m:1', '--no-restore'
        ) -FailureMessage "Android $configuration build failed"
    }

    Write-Host 'Result: tests passed and all four builds (Windows Debug/Release, Android Debug/Release) succeeded.'
}

function Invoke-KnownFirstAction {
    param([Parameter(Mandatory = $true)][string]$SelectedAction)

    switch ($SelectedAction) {
        'Test' { Invoke-RunTests }
        'WindowsBuild' { Invoke-WindowsBuildAction }
        'AndroidTestPackage' { Invoke-AndroidTestPackageAction }
        'GooglePlayBundle' { Invoke-GooglePlayBundleAction }
        'ValidateAll' { Invoke-ValidateAllAction }
        default { throw "Unknown action: $SelectedAction" }
    }
}

function Show-KnownFirstMenu {
    Write-Host ''
    Write-Host 'KnownFirst build launcher' -ForegroundColor Green
    Write-Host '1. Run tests'
    Write-Host '2. Build Windows'
    Write-Host '3. Create Android test package'
    Write-Host '4. Create Google Play bundle'
    Write-Host '5. Validate everything'
    Write-Host '6. Exit'
    Write-Host ''
    $choice = Read-Host 'Choose an option (1-6)'

    switch ($choice) {
        '1' { Invoke-KnownFirstAction 'Test'; return $true }
        '2' { Invoke-KnownFirstAction 'WindowsBuild'; return $true }
        '3' { Invoke-KnownFirstAction 'AndroidTestPackage'; return $true }
        '4' { Invoke-KnownFirstAction 'GooglePlayBundle'; return $true }
        '5' { Invoke-KnownFirstAction 'ValidateAll'; return $true }
        '6' { Write-Host 'Exiting.'; return $false }
        default { Write-Host "Unrecognized option: $choice" -ForegroundColor Yellow; return $true }
    }
}

if ($Action) {
    Invoke-KnownFirstAction -SelectedAction $Action
}
else {
    do {
        $continueMenu = Show-KnownFirstMenu
    } while ($continueMenu)
}
