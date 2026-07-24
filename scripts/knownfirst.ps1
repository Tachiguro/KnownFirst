<#
.SYNOPSIS
    Single entry point for the everyday KnownFirst local build/test/package operations.

.DESCRIPTION
    Running this script without arguments shows a numbered interactive menu. It can also be
    driven non-interactively with -Action for scripting or CI-style usage. Every operation
    prints the exact command it is about to run, streams that command's real output live to
    the console, and (for real, non-WhatIf runs) also writes the same output to a timestamped
    UTF-8 log under artifacts\launcher-logs\. A failure never crashes the launcher or the
    parent PowerShell window: it is reported as the failed command, its exit code, and the log
    path, and the interactive menu keeps running. Nothing is ever uploaded to Google Play and
    nothing is installed on a device automatically; package creation ("Create Android test
    package" / "Create Google Play bundle") calls the existing, already-reviewed
    publish-android-test-packages.ps1 and publish-google-play-bundle.ps1 scripts instead of
    duplicating their signing logic.

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
$logRoot = Join-Path $projectRoot 'artifacts\launcher-logs'

# --- Reliable command runner --------------------------------------------------------------
#
# Used consistently by every action below. It:
#   - prints the exact executable and arguments before running anything;
#   - runs the command completely unredirected so stdout/stderr stream live with normal
#     dotnet formatting (colors, progress, everything) exactly as if run directly;
#   - ALSO captures stdout (only) into a variable via Tee-Object -Variable, purely so it can
#     be appended to a UTF-8 log file afterward. Tee-Object has no -Encoding parameter in
#     Windows PowerShell 5.1, so the capture goes through Add-Content -Encoding UTF8 instead
#     of letting Tee-Object touch the file directly. Native stderr is intentionally left out
#     of the log (it still displays live): redirecting stderr on a native command in
#     Windows PowerShell 5.1 (2>&1) wraps each line as a NativeCommandError object, which is
#     exactly the kind of pipeline arrangement that can lose or misreport the real exit code
#     and replace useful native text with PowerShell stack-trace noise.
#   - captures $LASTEXITCODE as the very next statement after the native call, before any
#     other command can run and overwrite it;
#   - never lets an exception (either a non-zero exit code, or a genuine PowerShell
#     terminating error such as a `throw` inside a called .ps1 script) escape uncaught -
#     every result comes back as a structured object instead.

function New-LauncherLogPath {
    param([Parameter(Mandatory = $true)][string]$ActionName)

    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $logPath = Join-Path $logRoot "$ActionName-$timestamp.log"
    New-Item -ItemType File -Path $logPath -Force | Out-Null
    return $logPath
}

function Invoke-KnownFirstCommand {
    param(
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$CommandArguments = @(),
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $commandText = if ($CommandArguments.Count -gt 0) {
        "$FilePath $($CommandArguments -join ' ')"
    }
    else {
        $FilePath
    }

    Write-Host ''
    Write-Host "[$StepName] $commandText" -ForegroundColor Cyan

    $exitCode = 1
    $errorMessage = $null
    try {
        if ($CommandArguments.Count -gt 0) {
            & $FilePath @CommandArguments | Tee-Object -Variable 'knownFirstStepOutput'
        }
        else {
            & $FilePath | Tee-Object -Variable 'knownFirstStepOutput'
        }
        $exitCode = $LASTEXITCODE
    }
    catch {
        $errorMessage = $_.Exception.Message
    }

    if ($knownFirstStepOutput) {
        Add-Content -LiteralPath $LogPath -Value $knownFirstStepOutput -Encoding UTF8
    }
    if ($errorMessage) {
        Add-Content -LiteralPath $LogPath -Value "ERROR: $errorMessage" -Encoding UTF8
        Write-Host "ERROR: $errorMessage" -ForegroundColor Red
    }

    return [pscustomobject]@{
        StepName     = $StepName
        Command      = $commandText
        ExitCode     = $exitCode
        Succeeded    = ($exitCode -eq 0 -and -not $errorMessage)
        LogPath      = $LogPath
        ErrorMessage = $errorMessage
    }
}

function Get-TestSummaryFromLog {
    param([Parameter(Mandatory = $true)][string]$LogPath)

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return $null
    }

    $logLines = Get-Content -LiteralPath $LogPath
    $totalsLine = $logLines | Where-Object { $_ -match '^\s*Total tests:\s*\d+' } | Select-Object -Last 1
    if (-not $totalsLine) {
        return $null
    }

    $passedLine = $logLines | Where-Object { $_ -match '^\s*Passed:\s*\d+' } | Select-Object -Last 1
    $failedLine = $logLines | Where-Object { $_ -match '^\s*Failed:\s*\d+' } | Select-Object -Last 1
    $skippedLine = $logLines | Where-Object { $_ -match '^\s*Skipped:\s*\d+' } | Select-Object -Last 1

    $parts = @($totalsLine.Trim())
    foreach ($line in @($passedLine, $failedLine, $skippedLine)) {
        if ($line) { $parts += $line.Trim() }
    }
    return ($parts -join ' | ')
}

function New-ActionResult {
    param(
        [Parameter(Mandatory = $true)][string]$ActionName,
        [Parameter(Mandatory = $true)][bool]$Succeeded,
        [string]$FailedStepName,
        [string]$FailedCommand,
        [int]$ExitCode,
        [string]$LogPath,
        [string]$Summary
    )

    return [pscustomobject]@{
        ActionName     = $ActionName
        Succeeded      = $Succeeded
        FailedStepName = $FailedStepName
        FailedCommand  = $FailedCommand
        ExitCode       = $ExitCode
        LogPath        = $LogPath
        Summary        = $Summary
    }
}

function Write-ActionResult {
    param([Parameter(Mandatory = $true)]$Result)

    Write-Host ''
    if ($Result.Succeeded) {
        Write-Host "SUCCESS: $($Result.ActionName) completed." -ForegroundColor Green
        if ($Result.Summary) { Write-Host "Result: $($Result.Summary)" }
        if ($Result.LogPath) { Write-Host "Log: $($Result.LogPath)" }
    }
    else {
        Write-Host "FAILED: $($Result.ActionName)" -ForegroundColor Red
        if ($Result.FailedStepName) { Write-Host "Failed step: $($Result.FailedStepName)" }
        if ($Result.FailedCommand) { Write-Host "Failed command: $($Result.FailedCommand)" }
        Write-Host "Exit code: $($Result.ExitCode)"
        if ($Result.Summary) { Write-Host "Result: $($Result.Summary)" }
        if ($Result.LogPath) { Write-Host "Log: $($Result.LogPath)" }
        Write-Host 'The real command output above (and in the log) already shows the actual diagnostics for this failure.'
    }
}

# --- Actions -------------------------------------------------------------------------------

function Invoke-RunTests {
    Write-Host 'Restoring and running the complete KnownFirst.Tests suite (every unit test). This only compiles and executes tests; nothing is installed or packaged.'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: dotnet restore/test on $testProjectPath"
        return New-ActionResult -ActionName 'Test' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'Test'
    Write-Host "Log: $logPath"

    Write-Host 'Stage 1/2: restoring the test project.'
    $restoreResult = Invoke-KnownFirstCommand -StepName 'Test project restore' -FilePath 'dotnet' `
        -CommandArguments @('restore', $testProjectPath) -LogPath $logPath
    if (-not $restoreResult.Succeeded) {
        return New-ActionResult -ActionName 'Test' -Succeeded $false `
            -FailedStepName $restoreResult.StepName -FailedCommand $restoreResult.Command `
            -ExitCode $restoreResult.ExitCode -LogPath $logPath
    }

    Write-Host 'Stage 2/2: running the complete test suite.'
    $testResult = Invoke-KnownFirstCommand -StepName 'Run tests' -FilePath 'dotnet' -CommandArguments @(
        'test', $testProjectPath, '-c', 'Debug', '--no-restore', '--logger', 'console;verbosity=normal'
    ) -LogPath $logPath
    $summary = Get-TestSummaryFromLog -LogPath $logPath

    if (-not $testResult.Succeeded) {
        return New-ActionResult -ActionName 'Test' -Succeeded $false `
            -FailedStepName $testResult.StepName -FailedCommand $testResult.Command `
            -ExitCode $testResult.ExitCode -LogPath $logPath -Summary $summary
    }

    return New-ActionResult -ActionName 'Test' -Succeeded $true -LogPath $logPath -Summary $summary
}

function Invoke-WindowsBuildAction {
    Write-Host 'Building the Windows desktop app in Debug and Release. This only compiles the app for local use; it does not package, sign, or publish anything for distribution.'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: dotnet build $projectPath -f net10.0-windows10.0.19041.0 (Debug, Release)"
        return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'WindowsBuild'
    Write-Host "Log: $logPath"

    $restoreResult = Invoke-KnownFirstCommand -StepName 'App restore' -FilePath 'dotnet' `
        -CommandArguments @('restore', $projectPath) -LogPath $logPath
    if (-not $restoreResult.Succeeded) {
        return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $false `
            -FailedStepName $restoreResult.StepName -FailedCommand $restoreResult.Command `
            -ExitCode $restoreResult.ExitCode -LogPath $logPath
    }

    foreach ($configuration in @('Debug', 'Release')) {
        $buildResult = Invoke-KnownFirstCommand -StepName "Windows $configuration build" -FilePath 'dotnet' -CommandArguments @(
            'build', $projectPath, '-c', $configuration, '-f', 'net10.0-windows10.0.19041.0', '--no-restore'
        ) -LogPath $logPath
        if (-not $buildResult.Succeeded) {
            return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $false `
                -FailedStepName $buildResult.StepName -FailedCommand $buildResult.Command `
                -ExitCode $buildResult.ExitCode -LogPath $logPath
        }

        $outputPath = Join-Path $projectRoot "bin\$configuration\net10.0-windows10.0.19041.0\win-x64\KnownFirst.dll"
        Write-Host "Output ($configuration): $outputPath"
    }

    return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $true -LogPath $logPath
}

function Invoke-AndroidTestPackageAction {
    Write-Host 'Creating signed Android APKs for manual sideload testing (release, diagnostic, and debug builds). This is NOT the Google Play bundle, is never uploaded anywhere, and nothing is installed automatically on any device.'
    $scriptPath = Join-Path $scriptRoot 'publish-android-test-packages.ps1'
    $outputRoot = Join-Path $projectRoot 'artifacts\android-beta'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: $scriptPath -> $outputRoot"
        return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'AndroidTestPackage'
    Write-Host "Log: $logPath"

    $scriptArguments = @()
    if ($KeystorePath) { $scriptArguments += @('-KeystorePath', $KeystorePath) }
    if ($PasswordFilePath) { $scriptArguments += @('-PasswordFilePath', $PasswordFilePath) }

    $result = Invoke-KnownFirstCommand -StepName 'Create Android test packages' -FilePath $scriptPath `
        -CommandArguments $scriptArguments -LogPath $logPath
    if (-not $result.Succeeded) {
        return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $false `
            -FailedStepName $result.StepName -FailedCommand $result.Command `
            -ExitCode $result.ExitCode -LogPath $logPath
    }

    Write-Host "Output: $outputRoot"
    return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $true -LogPath $logPath
}

function Invoke-GooglePlayBundleAction {
    Write-Host 'Creating the signed Android App Bundle (.aab) for Google Play. This produces a local file only; this script never uploads it to Google Play.'
    $scriptPath = Join-Path $scriptRoot 'publish-google-play-bundle.ps1'
    $outputRoot = Join-Path $projectRoot 'artifacts\android-google-play'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: $scriptPath -> $outputRoot"
        return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'GooglePlayBundle'
    Write-Host "Log: $logPath"

    $scriptArguments = @()
    if ($KeystorePath) { $scriptArguments += @('-KeystorePath', $KeystorePath) }
    if ($PasswordFilePath) { $scriptArguments += @('-PasswordFilePath', $PasswordFilePath) }

    $result = Invoke-KnownFirstCommand -StepName 'Create Google Play bundle' -FilePath $scriptPath `
        -CommandArguments $scriptArguments -LogPath $logPath
    if (-not $result.Succeeded) {
        return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $false `
            -FailedStepName $result.StepName -FailedCommand $result.Command `
            -ExitCode $result.ExitCode -LogPath $logPath
    }

    Write-Host "Output: $outputRoot"
    return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $true -LogPath $logPath
}

function Invoke-ValidateAllAction {
    Write-Host 'Running the full local validation matrix: restore, the complete test suite, then Windows and Android builds in both Debug and Release. This mirrors what must pass before any release and creates no APK, AAB, or other package.'
    if ($WhatIf) {
        Write-Host '[WhatIf] Would run: restore, dotnet test, and four dotnet build passes (Windows Debug/Release, Android Debug/Release).'
        return New-ActionResult -ActionName 'ValidateAll' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'ValidateAll'
    Write-Host "Log: $logPath"

    $stages = @(
        @{ Number = 1; Name = 'App restore'; FilePath = 'dotnet'; Arguments = @('restore', $projectPath) }
        @{ Number = 2; Name = 'Test project restore'; FilePath = 'dotnet'; Arguments = @('restore', $testProjectPath) }
        @{ Number = 3; Name = 'Complete test suite'; FilePath = 'dotnet'; Arguments = @(
                'test', $testProjectPath, '-c', 'Debug', '--no-restore', '--logger', 'console;verbosity=normal'
            )
        }
        @{ Number = 4; Name = 'Windows Debug build'; FilePath = 'dotnet'; Arguments = @(
                'build', $projectPath, '-c', 'Debug', '-f', 'net10.0-windows10.0.19041.0', '--no-restore'
            )
        }
        @{ Number = 5; Name = 'Windows Release build'; FilePath = 'dotnet'; Arguments = @(
                'build', $projectPath, '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0', '--no-restore'
            )
        }
        @{ Number = 6; Name = 'Android Debug build'; FilePath = 'dotnet'; Arguments = @(
                'build', $projectPath, '-c', 'Debug', '-f', 'net10.0-android', '-m:1', '--no-restore'
            )
        }
        @{ Number = 7; Name = 'Android Release build'; FilePath = 'dotnet'; Arguments = @(
                'build', $projectPath, '-c', 'Release', '-f', 'net10.0-android', '-m:1', '--no-restore'
            )
        }
    )

    $summary = $null
    foreach ($stage in $stages) {
        Write-Host "Stage $($stage.Number)/7: $($stage.Name)."
        $stepResult = Invoke-KnownFirstCommand -StepName $stage.Name -FilePath $stage.FilePath `
            -CommandArguments $stage.Arguments -LogPath $logPath

        if ($stage.Number -eq 3) {
            $summary = Get-TestSummaryFromLog -LogPath $logPath
        }

        if (-not $stepResult.Succeeded) {
            return New-ActionResult -ActionName 'ValidateAll' -Succeeded $false `
                -FailedStepName $stepResult.StepName -FailedCommand $stepResult.Command `
                -ExitCode $stepResult.ExitCode -LogPath $logPath -Summary $summary
        }
    }

    return New-ActionResult -ActionName 'ValidateAll' -Succeeded $true -LogPath $logPath -Summary $summary
}

function Invoke-KnownFirstAction {
    param([Parameter(Mandatory = $true)][string]$SelectedAction)

    try {
        switch ($SelectedAction) {
            'Test' { return Invoke-RunTests }
            'WindowsBuild' { return Invoke-WindowsBuildAction }
            'AndroidTestPackage' { return Invoke-AndroidTestPackageAction }
            'GooglePlayBundle' { return Invoke-GooglePlayBundleAction }
            'ValidateAll' { return Invoke-ValidateAllAction }
            default { throw "Unknown action: $SelectedAction" }
        }
    }
    catch {
        # Defense in depth: nothing above is expected to throw uncaught (every native/script
        # step goes through Invoke-KnownFirstCommand, which never rethrows), but if something
        # truly unexpected still escapes, report it the same structured way instead of letting
        # it crash the launcher or the parent PowerShell host.
        return New-ActionResult -ActionName $SelectedAction -Succeeded $false -ExitCode 1 `
            -FailedStepName 'Launcher' -FailedCommand $SelectedAction `
            -Summary $_.Exception.Message
    }
}

# --- Menu / entry point ----------------------------------------------------------------

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

    $selectedAction = switch ($choice) {
        '1' { 'Test' }
        '2' { 'WindowsBuild' }
        '3' { 'AndroidTestPackage' }
        '4' { 'GooglePlayBundle' }
        '5' { 'ValidateAll' }
        '6' { $null }
        default { $null }
    }

    if ($choice -eq '6') {
        Write-Host 'Exiting.'
        return $false
    }
    if (-not $selectedAction) {
        Write-Host "Unrecognized option: $choice" -ForegroundColor Yellow
        return $true
    }

    $result = Invoke-KnownFirstAction -SelectedAction $selectedAction
    Write-ActionResult -Result $result
    if (-not $result.Succeeded) {
        Read-Host 'Press Enter to return to the menu'
    }

    return $true
}

if ($Action) {
    $result = Invoke-KnownFirstAction -SelectedAction $Action
    Write-ActionResult -Result $result
    # $host.SetShouldExit records the process exit code without forcibly terminating anything:
    # a dedicated "powershell -File knownfirst.ps1 -Action X" process still exits with this code
    # when it naturally finishes (so automation can see success/failure), but if this script is
    # instead invoked from inside an already-running interactive PowerShell window, that window
    # is left open and usable afterward instead of being abruptly closed.
    if ($result.Succeeded) {
        $host.SetShouldExit(0)
    }
    else {
        $host.SetShouldExit($result.ExitCode)
    }
}
else {
    do {
        $continueMenu = Show-KnownFirstMenu
    } while ($continueMenu)
}
