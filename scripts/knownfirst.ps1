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
    nothing is installed on a device automatically; package creation ("Build Android test APK"
    / "Create Google Play AAB") calls the existing, already-reviewed
    publish-android-test-packages.ps1 and publish-google-play-bundle.ps1 scripts instead of
    duplicating their signing logic.

    Successful results can be reused across runs (see -Force). A result under
    artifacts\launcher-state\ is only ever reused when the worktree is clean, HEAD has not
    moved, the .NET SDK version matches, the action/configuration/parameters match, the
    operation previously succeeded, every expected output file still exists and is non-empty,
    the state record itself is well-formed, and the relevant script files are unchanged.
    Anything else - including a merely dirty worktree or a corrupt state file - is treated as
    "not reusable", never as a launcher error.

.PARAMETER Action
    Test | GuiTest | WindowsBuild | AndroidTestPackage | GooglePlayBundle | ValidateAll
    When omitted, the interactive menu is shown instead.

.PARAMETER Configuration
    Debug | Release | Diagnostic | DebugRelease | All
    Applies to WindowsBuild (Debug, Release, or All; Diagnostic is not a Windows configuration)
    and AndroidTestPackage (Debug, Release, Diagnostic, DebugRelease, or All). For GuiTest,
    only supported configurations (e.g., Debug) are valid. Omitted or blank means All for
    WindowsBuild and AndroidTestPackage. DebugRelease is an internal configuration for the
    interactive menu; it creates Debug and Release APKs only.

.PARAMETER GuiScenario
    Scenario id for -Action GuiTest. Supported: StartupSmoke (default), or future scenarios.
    Invalid or NotImplemented scenarios are rejected before execution.

.PARAMETER WhatIf
    Prints what each operation would do and its expected output path without running it.
    Useful to validate an -Action name or check the launcher itself without building,
    testing, or creating any package.

.PARAMETER Force
    Ignores any stored reusable result and always executes the requested operation.

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
    .\scripts\knownfirst.ps1 -Action WindowsBuild -Configuration Debug

.EXAMPLE
    .\scripts\knownfirst.ps1 -Action AndroidTestPackage -Configuration Diagnostic

.EXAMPLE
    .\scripts\knownfirst.ps1 -Action GooglePlayBundle -WhatIf
    Prints what would happen without creating an AAB.

.EXAMPLE
    .\scripts\knownfirst.ps1 -Action WindowsBuild -Configuration Debug -Force
    Rebuilds even if a valid reusable result already exists.
#>
[CmdletBinding()]
param(
    [ValidateSet('Test', 'GuiTest', 'WindowsBuild', 'AndroidTestPackage', 'GooglePlayBundle', 'ValidateAll')]
    [string]$Action,

    [ValidateSet('Debug', 'Release', 'Diagnostic', 'DebugRelease', 'All')]
    [string]$Configuration,

    [ValidateSet('StartupSmoke')]
    [string]$GuiScenario = 'StartupSmoke',

    [switch]$WhatIf,

    [switch]$Force,

    [string]$KeystorePath,

    [string]$PasswordFilePath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $projectRoot 'KnownFirst.csproj'
$testProjectPath = Join-Path $projectRoot 'KnownFirst.Tests\KnownFirst.Tests.csproj'
$logRoot = Join-Path $projectRoot 'artifacts\launcher-logs'
$stateRoot = Join-Path $projectRoot 'artifacts\launcher-state'
$windowsTargetFramework = 'net10.0-windows10.0.19041.0'
$androidTargetFramework = 'net10.0-android'

# --- Environment probes (cached: cheap, but no need to ask git/dotnet more than once) ------

$script:cachedWorktreeClean = $null
function Test-GitWorktreeClean {
    if ($null -eq $script:cachedWorktreeClean) {
        try {
            $status = & git -C $projectRoot status --short 2>$null
            $script:cachedWorktreeClean = [string]::IsNullOrEmpty(($status -join ''))
        }
        catch {
            $script:cachedWorktreeClean = $false
        }
    }
    return $script:cachedWorktreeClean
}

$script:cachedHeadSha = $null
function Get-CurrentHeadSha {
    if ($null -eq $script:cachedHeadSha) {
        try {
            $script:cachedHeadSha = (& git -C $projectRoot rev-parse HEAD 2>$null | Select-Object -First 1).Trim()
        }
        catch {
            $script:cachedHeadSha = ''
        }
        if (-not $script:cachedHeadSha) {
            $script:cachedHeadSha = ''
        }
    }
    return $script:cachedHeadSha
}

$script:cachedDotnetVersion = $null
function Get-CurrentDotnetVersion {
    if ($null -eq $script:cachedDotnetVersion) {
        try {
            $script:cachedDotnetVersion = (& dotnet --version 2>$null | Select-Object -First 1).Trim()
        }
        catch {
            $script:cachedDotnetVersion = ''
        }
        if (-not $script:cachedDotnetVersion) {
            $script:cachedDotnetVersion = ''
        }
    }
    return $script:cachedDotnetVersion
}

$script:cachedVersionInfo = $null
function Get-KnownFirstVersionInfo {
    if ($null -ne $script:cachedVersionInfo) {
        return $script:cachedVersionInfo
    }

    $output = & dotnet msbuild $projectPath -nologo `
        -getProperty:KnownFirstProductVersion `
        -getProperty:KnownFirstBuildNumber
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read KnownFirstProductVersion/KnownFirstBuildNumber from $projectPath."
    }

    $parsed = ($output | Out-String).Trim() | ConvertFrom-Json
    $script:cachedVersionInfo = [pscustomobject]@{
        ProductVersion = $parsed.Properties.KnownFirstProductVersion
        BuildNumber    = $parsed.Properties.KnownFirstBuildNumber
    }
    return $script:cachedVersionInfo
}

# --- Reusable-result state --------------------------------------------------------------
#
# One JSON record per operation+configuration under artifacts\launcher-state\. A record is
# only ever treated as reusable when every one of these holds:
#   1. the worktree is clean;
#   2. the recorded full HEAD SHA equals the current full HEAD SHA;
#   3. the recorded `dotnet --version` equals the current one;
#   4. action, configuration, target framework, and parameters match;
#   5. the recorded operation succeeded;
#   6. every expected output file still exists and is non-empty;
#   7. the record itself is well-formed JSON with the required fields;
#   8. the relevant script file hashes are unchanged.
# Timestamps are stored for information only and are never used as proof of freshness.
# Any failure of any check - including a corrupt/incomplete record - simply means "not
# reusable"; it is never a fatal launcher error.

function Get-LauncherStatePath {
    param([Parameter(Mandatory = $true)][string]$StateKey)
    return Join-Path $stateRoot "$StateKey.json"
}

function Get-RelativeScriptPath {
    param([Parameter(Mandatory = $true)][string]$FullPath)
    $resolved = $FullPath
    if ($resolved.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $resolved = $resolved.Substring($projectRoot.Length)
    }
    return $resolved.TrimStart('\', '/')
}

function Get-ScriptHashMap {
    param([Parameter(Mandatory = $true)][string[]]$ScriptPaths)

    $map = [ordered]@{}
    foreach ($path in $ScriptPaths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $key = Get-RelativeScriptPath -FullPath $path
            $map[$key] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    return $map
}

function Get-ParameterSignature {
    param($Parameters)

    if ($null -eq $Parameters) {
        return ''
    }

    if ($Parameters -is [System.Collections.IDictionary]) {
        $keys = @($Parameters.Keys) | Sort-Object
        $parts = foreach ($key in $keys) { "$key=$($Parameters[$key])" }
    }
    else {
        $keys = @($Parameters.PSObject.Properties.Name) | Sort-Object
        $parts = foreach ($key in $keys) { "$key=$($Parameters.$key)" }
    }
    return ($parts -join '|')
}

function Save-LauncherState {
    param(
        [Parameter(Mandatory = $true)][string]$StateKey,
        [Parameter(Mandatory = $true)][string]$Action,
        [string]$Configuration = '',
        [string]$TargetFramework = '',
        [hashtable]$Parameters = @{},
        [Parameter(Mandatory = $true)][bool]$Succeeded,
        [string]$Summary = '',
        [Parameter(Mandatory = $true)][string[]]$OutputFiles,
        [Parameter(Mandatory = $true)][string[]]$RelevantScriptPaths
    )

    if (-not $Succeeded) {
        return
    }
    if (-not (Test-GitWorktreeClean)) {
        # A dirty-worktree result must never be stored as reusable for a future clean commit.
        return
    }

    New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    $record = [ordered]@{
        Action          = $Action
        Configuration   = $Configuration
        TargetFramework = $TargetFramework
        ParameterSignature = Get-ParameterSignature -Parameters $Parameters
        HeadSha         = Get-CurrentHeadSha
        DotnetVersion   = Get-CurrentDotnetVersion
        Succeeded       = $true
        Summary         = $Summary
        OutputFiles     = @($OutputFiles)
        ScriptHashes    = Get-ScriptHashMap -ScriptPaths $RelevantScriptPaths
        CreatedAtUtc    = (Get-Date).ToUniversalTime().ToString('o')
    }

    $statePath = Get-LauncherStatePath -StateKey $StateKey
    try {
        ($record | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $statePath -Encoding UTF8
    }
    catch {
        # Being unable to persist a reusable-result record must never fail the operation that
        # already succeeded; the next run will simply execute again instead of reusing.
        Write-Host "Note: the reusable-result record for $StateKey could not be saved ($($_.Exception.Message))." -ForegroundColor Yellow
    }
}

function Test-LauncherStateReusable {
    param(
        [Parameter(Mandatory = $true)][string]$StateKey,
        [Parameter(Mandatory = $true)][string]$Action,
        [string]$Configuration = '',
        [string]$TargetFramework = '',
        [hashtable]$Parameters = @{},
        [Parameter(Mandatory = $true)][string[]]$OutputFiles,
        [Parameter(Mandatory = $true)][string[]]$RelevantScriptPaths
    )

    if ($Force) {
        return $null
    }

    $statePath = Get-LauncherStatePath -StateKey $StateKey
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        return $null
    }

    $record = $null
    try {
        $raw = Get-Content -LiteralPath $statePath -Raw -ErrorAction Stop
        $record = $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return $null
    }

    if ($null -eq $record) {
        return $null
    }

    $requiredFields = @('Action', 'HeadSha', 'DotnetVersion', 'Succeeded', 'OutputFiles', 'ScriptHashes')
    foreach ($field in $requiredFields) {
        if (-not ($record.PSObject.Properties.Name -contains $field)) {
            return $null
        }
    }

    if ($record.Succeeded -ne $true) { return $null }
    if (-not (Test-GitWorktreeClean)) { return $null }
    if ([string]$record.HeadSha -ne (Get-CurrentHeadSha) -or [string]::IsNullOrEmpty([string]$record.HeadSha)) { return $null }
    if ([string]$record.DotnetVersion -ne (Get-CurrentDotnetVersion) -or [string]::IsNullOrEmpty([string]$record.DotnetVersion)) { return $null }
    if ([string]$record.Action -ne $Action) { return $null }
    if ([string]$record.Configuration -ne $Configuration) { return $null }
    if ([string]$record.TargetFramework -ne $TargetFramework) { return $null }

    $expectedSignature = Get-ParameterSignature -Parameters $Parameters
    $recordedSignature = if ($record.PSObject.Properties.Name -contains 'ParameterSignature') { [string]$record.ParameterSignature } else { '' }
    if ($recordedSignature -ne $expectedSignature) { return $null }

    foreach ($outputFile in $OutputFiles) {
        if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) { return $null }
        if ((Get-Item -LiteralPath $outputFile).Length -le 0) { return $null }
    }

    foreach ($scriptPath in $RelevantScriptPaths) {
        if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) { return $null }
        $relativePath = Get-RelativeScriptPath -FullPath $scriptPath
        $recordedHash = $null
        if ($record.ScriptHashes.PSObject.Properties.Name -contains $relativePath) {
            $recordedHash = $record.ScriptHashes.$relativePath
        }
        if (-not $recordedHash) { return $null }
        $currentHash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
        if ($currentHash -ne $recordedHash) { return $null }
    }

    if ($Action -eq 'Test') {
        # Extra proof for tests specifically: the retained log must still show the same
        # successful totals, not just be present and non-empty.
        $currentSummary = Get-TestSummaryFromLog -LogPath $OutputFiles[0]
        if (-not $currentSummary) { return $null }
        if ($currentSummary -ne [string]$record.Summary) { return $null }
        if ($currentSummary -notmatch 'Failed:\s*0\b') { return $null }
    }

    return $record
}

function Write-ReuseMessage {
    param([Parameter(Mandatory = $true)][string]$DisplayName)

    $sha = Get-CurrentHeadSha
    $shortSha = if ($sha.Length -ge 7) { $sha.Substring(0, 7) } else { $sha }
    Write-Host "$DisplayName is already current for commit $shortSha. Skipped." -ForegroundColor DarkGray
}

# --- Reliable native-command runner --------------------------------------------------------
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
    # $CommandArguments accepts EITHER a [string[]] (for native commands like dotnet.exe,
    # where splatting is purely positional/order-preserving argv - exactly right for a CLI
    # that reads flags like -c/-f itself) OR an [System.Collections.IDictionary]/hashtable
    # (for invoking another .ps1 script that needs real PowerShell named-parameter binding).
    # This distinction matters: splatting a STRING ARRAY via @CommandArguments always binds
    # positionally in PowerShell, even when the strings look like "-Name value" pairs, so a
    # target script's own param() block will NOT recognize them as named parameters. Only
    # splatting a hashtable produces genuine -Name value binding.
    param(
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$FilePath,
        $CommandArguments = @(),
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $isNamedArguments = $CommandArguments -is [System.Collections.IDictionary]
    $argumentCount = if ($isNamedArguments) { $CommandArguments.Count } else { @($CommandArguments).Count }

    $commandText = if ($argumentCount -eq 0) {
        $FilePath
    }
    elseif ($isNamedArguments) {
        $pairs = foreach ($key in $CommandArguments.Keys) { "-$key $($CommandArguments[$key])" }
        "$FilePath $($pairs -join ' ')"
    }
    else {
        "$FilePath $($CommandArguments -join ' ')"
    }

    Write-Host ''
    Write-Host "[$StepName] $commandText" -ForegroundColor Cyan

    $exitCode = 1
    $errorMessage = $null
    try {
        if ($argumentCount -gt 0) {
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
        [string]$Summary,
        [bool]$Reused = $false,
        [string]$RunDirectory = '',
        [string]$SummaryPath = '',
        [string]$ReportZipPath = ''
    )

    # Precomputed into a plain variable (not an inline parenthesized "-Prop (if ...)"
    # argument) because Windows PowerShell 5.1 cannot parse an `if` statement inside a
    # group-expression used as a command/property argument.
    $status = if ($Succeeded) { 'PASSED' } else { 'FAILED' }

    return [pscustomobject]@{
        ActionName     = $ActionName
        Succeeded      = $Succeeded
        Status         = $status
        FailedStepName = $FailedStepName
        FailedCommand  = $FailedCommand
        ExitCode       = $ExitCode
        LogPath        = $LogPath
        Summary        = $Summary
        Reused         = $Reused
        RunDirectory   = $RunDirectory
        SummaryPath    = $SummaryPath
        ReportZipPath  = $ReportZipPath
    }
}

function Write-ActionResult {
    param([Parameter(Mandatory = $true)]$Result)

    Write-Host ''
    if ($Result.Succeeded) {
        $tag = if ($Result.Reused) { 'SUCCESS (REUSED)' } else { 'SUCCESS' }
        Write-Host "${tag}: $($Result.ActionName) completed." -ForegroundColor Green
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
    Write-Host 'Restores and executes the complete automated test suite only.'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: dotnet restore/test on $testProjectPath"
        return New-ActionResult -ActionName 'Test' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'Test'
    $outputFiles = @($logPath)
    $relevantScripts = @((Join-Path $scriptRoot 'knownfirst.ps1'))

    $reusable = Test-LauncherStateReusable -StateKey 'Test' -Action 'Test' `
        -OutputFiles $outputFiles -RelevantScriptPaths $relevantScripts
    if ($reusable) {
        Write-ReuseMessage -DisplayName 'Tests'
        return New-ActionResult -ActionName 'Test' -Succeeded $true -LogPath $outputFiles[0] -Summary $reusable.Summary -Reused $true
    }

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

    Save-LauncherState -StateKey 'Test' -Action 'Test' -Succeeded $true -Summary $summary `
        -OutputFiles $outputFiles -RelevantScriptPaths $relevantScripts

    return New-ActionResult -ActionName 'Test' -Succeeded $true -LogPath $logPath -Summary $summary
}

function Get-EffectiveWindowsConfigurations {
    param([string]$RequestedConfiguration)

    $effective = if ([string]::IsNullOrEmpty($RequestedConfiguration)) { 'All' } else { $RequestedConfiguration }
    if ($effective -notin @('Debug', 'Release', 'All')) {
        throw "WindowsBuild does not support Configuration '$effective'. Use Debug, Release, or All."
    }
    if ($effective -eq 'All') { return @('Debug', 'Release') }
    return @($effective)
}

function Invoke-WindowsBuildAction {
    Write-Host 'Compiles the selected Windows configuration and does not create an installer.'
    $configurations = Get-EffectiveWindowsConfigurations -RequestedConfiguration $Configuration

    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: dotnet build $projectPath -f $windowsTargetFramework ($($configurations -join ', '))"
        return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $relevantScripts = @((Join-Path $scriptRoot 'knownfirst.ps1'))
    $pending = @()
    foreach ($configuration in $configurations) {
        $outputPath = Join-Path $projectRoot "bin\$configuration\$windowsTargetFramework\win-x64\KnownFirst.dll"
        $reusable = Test-LauncherStateReusable -StateKey "WindowsBuild-$configuration" -Action 'WindowsBuild' `
            -Configuration $configuration -TargetFramework $windowsTargetFramework `
            -OutputFiles @($outputPath) -RelevantScriptPaths $relevantScripts
        if ($reusable) {
            Write-ReuseMessage -DisplayName "Windows $configuration"
        }
        else {
            $pending += $configuration
        }
    }

    if ($pending.Count -eq 0) {
        return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $true -Summary 'All requested configurations reused.' -Reused $true
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

    foreach ($configuration in $pending) {
        $buildResult = Invoke-KnownFirstCommand -StepName "Windows $configuration build" -FilePath 'dotnet' -CommandArguments @(
            'build', $projectPath, '-c', $configuration, '-f', $windowsTargetFramework, '--no-restore'
        ) -LogPath $logPath
        if (-not $buildResult.Succeeded) {
            return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $false `
                -FailedStepName $buildResult.StepName -FailedCommand $buildResult.Command `
                -ExitCode $buildResult.ExitCode -LogPath $logPath
        }

        $outputPath = Join-Path $projectRoot "bin\$configuration\$windowsTargetFramework\win-x64\KnownFirst.dll"
        Write-Host "Output ($configuration): $outputPath"
        Save-LauncherState -StateKey "WindowsBuild-$configuration" -Action 'WindowsBuild' `
            -Configuration $configuration -TargetFramework $windowsTargetFramework -Succeeded $true `
            -OutputFiles @($outputPath) -RelevantScriptPaths $relevantScripts
    }

    return New-ActionResult -ActionName 'WindowsBuild' -Succeeded $true -LogPath $logPath
}

function Get-EffectiveAndroidPackageConfigurations {
    param([string]$RequestedConfiguration)

    $effective = if ([string]::IsNullOrEmpty($RequestedConfiguration)) { 'All' } else { $RequestedConfiguration }
    if ($effective -notin @('Debug', 'Diagnostic', 'Release', 'DebugRelease', 'All')) {
        throw "AndroidTestPackage does not support Configuration '$effective'. Use Debug, Diagnostic, Release, DebugRelease, or All."
    }
    if ($effective -eq 'All') { return @('Debug', 'Diagnostic', 'Release') }
    if ($effective -eq 'DebugRelease') { return @('Debug', 'Release') }
    return @($effective)
}

function Invoke-AndroidTestPackageAction {
    Write-Host 'Creates selected signed APKs for manual sideload testing; nothing is installed or uploaded.'
    $effectiveConfiguration = if ([string]::IsNullOrEmpty($Configuration)) { 'All' } else { $Configuration }
    $requestedKinds = Get-EffectiveAndroidPackageConfigurations -RequestedConfiguration $Configuration
    $scriptPath = Join-Path $scriptRoot 'publish-android-test-packages.ps1'
    $outputRoot = Join-Path $projectRoot 'artifacts\android-beta'

    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: $scriptPath -Configuration $effectiveConfiguration -> $outputRoot"
        return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $versionInfo = Get-KnownFirstVersionInfo
    $relevantScripts = @(
        (Join-Path $scriptRoot 'knownfirst.ps1'),
        $scriptPath
    )
    $parameters = @{ KeystorePath = $KeystorePath; PasswordFilePath = $PasswordFilePath }

    function Get-AndroidTestPackageOutputFiles {
        param([Parameter(Mandatory = $true)][string]$Kind)
        $kindLower = $Kind.ToLowerInvariant()
        $baseName = "KnownFirst-$($versionInfo.ProductVersion)-android-$kindLower"
        return @(
            (Join-Path $outputRoot "$baseName.apk"),
            (Join-Path $outputRoot "$baseName.zip")
        )
    }

    # "All" is treated as reusable only when every one of the three individual packages is
    # still valid; if any one is stale, the whole requested selection is rebuilt in a single
    # publish-android-test-packages.ps1 invocation rather than partially re-running (simpler,
    # never does less work than required, and keeps the packaging script the single owner of
    # its own build loop).
    $allReusable = $true
    foreach ($kind in $requestedKinds) {
        $reusable = Test-LauncherStateReusable -StateKey "AndroidTestPackage-$kind" -Action 'AndroidTestPackage' `
            -Configuration $kind -Parameters $parameters `
            -OutputFiles (Get-AndroidTestPackageOutputFiles -Kind $kind) -RelevantScriptPaths $relevantScripts
        if (-not $reusable) {
            $allReusable = $false
        }
    }

    if ($allReusable) {
        foreach ($kind in $requestedKinds) {
            Write-ReuseMessage -DisplayName "Android $kind test APK"
        }
        return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $true -Summary 'All requested APKs reused.' -Reused $true
    }

    $logPath = New-LauncherLogPath -ActionName 'AndroidTestPackage'
    Write-Host "Log: $logPath"

    $scriptArguments = [ordered]@{ Configuration = $effectiveConfiguration }
    if ($KeystorePath) { $scriptArguments['KeystorePath'] = $KeystorePath }
    if ($PasswordFilePath) { $scriptArguments['PasswordFilePath'] = $PasswordFilePath }

    $result = Invoke-KnownFirstCommand -StepName 'Create Android test packages' -FilePath $scriptPath `
        -CommandArguments $scriptArguments -LogPath $logPath
    if (-not $result.Succeeded) {
        return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $false `
            -FailedStepName $result.StepName -FailedCommand $result.Command `
            -ExitCode $result.ExitCode -LogPath $logPath
    }

    foreach ($kind in $requestedKinds) {
        Save-LauncherState -StateKey "AndroidTestPackage-$kind" -Action 'AndroidTestPackage' `
            -Configuration $kind -Parameters $parameters -Succeeded $true `
            -OutputFiles (Get-AndroidTestPackageOutputFiles -Kind $kind) -RelevantScriptPaths $relevantScripts
    }

    Write-Host "Output: $outputRoot"
    return New-ActionResult -ActionName 'AndroidTestPackage' -Succeeded $true -LogPath $logPath
}

function Invoke-GooglePlayBundleAction {
    Write-Host 'Creates one signed local App Bundle; nothing is uploaded.'
    $scriptPath = Join-Path $scriptRoot 'publish-google-play-bundle.ps1'
    $outputRoot = Join-Path $projectRoot 'artifacts\android-google-play'
    if ($WhatIf) {
        Write-Host "[WhatIf] Would run: $scriptPath -> $outputRoot"
        return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $versionInfo = Get-KnownFirstVersionInfo
    $aabPath = Join-Path $outputRoot "KnownFirst-$($versionInfo.ProductVersion)-code$($versionInfo.BuildNumber).aab"
    $relevantScripts = @(
        (Join-Path $scriptRoot 'knownfirst.ps1'),
        $scriptPath
    )
    $parameters = @{ KeystorePath = $KeystorePath; PasswordFilePath = $PasswordFilePath }

    $reusable = Test-LauncherStateReusable -StateKey 'GooglePlayBundle' -Action 'GooglePlayBundle' `
        -Parameters $parameters -OutputFiles @($aabPath) -RelevantScriptPaths $relevantScripts
    if ($reusable) {
        Write-ReuseMessage -DisplayName 'Google Play AAB'
        return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $true -Reused $true
    }

    $logPath = New-LauncherLogPath -ActionName 'GooglePlayBundle'
    Write-Host "Log: $logPath"

    $scriptArguments = [ordered]@{}
    if ($KeystorePath) { $scriptArguments['KeystorePath'] = $KeystorePath }
    if ($PasswordFilePath) { $scriptArguments['PasswordFilePath'] = $PasswordFilePath }

    $result = Invoke-KnownFirstCommand -StepName 'Create Google Play bundle' -FilePath $scriptPath `
        -CommandArguments $scriptArguments -LogPath $logPath
    if (-not $result.Succeeded) {
        return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $false `
            -FailedStepName $result.StepName -FailedCommand $result.Command `
            -ExitCode $result.ExitCode -LogPath $logPath
    }

    Save-LauncherState -StateKey 'GooglePlayBundle' -Action 'GooglePlayBundle' -Parameters $parameters `
        -Succeeded $true -OutputFiles @($aabPath) -RelevantScriptPaths $relevantScripts

    Write-Host "Output: $outputRoot"
    return New-ActionResult -ActionName 'GooglePlayBundle' -Succeeded $true -LogPath $logPath
}

function Invoke-ValidateAllStep {
    param(
        [Parameter(Mandatory = $true)][string]$DisplayName,
        [Parameter(Mandatory = $true)][string]$StateKey,
        [Parameter(Mandatory = $true)][string]$Action,
        [string]$Configuration = '',
        [string]$TargetFramework = '',
        [Parameter(Mandatory = $true)][string[]]$OutputFiles,
        [Parameter(Mandatory = $true)][string[]]$RelevantScriptPaths,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$CommandArguments
    )

    $reusable = Test-LauncherStateReusable -StateKey $StateKey -Action $Action -Configuration $Configuration `
        -TargetFramework $TargetFramework -OutputFiles $OutputFiles -RelevantScriptPaths $RelevantScriptPaths
    if ($reusable) {
        Write-Host "$DisplayName : REUSED"
        Write-ReuseMessage -DisplayName $DisplayName
        Write-Host "$DisplayName : PASSED" -ForegroundColor Green
        return [pscustomobject]@{ Succeeded = $true; Summary = [string]$reusable.Summary; StepResult = $null }
    }

    Write-Host "$DisplayName : EXECUTED"
    $stepResult = Invoke-KnownFirstCommand -StepName $StepName -FilePath $FilePath `
        -CommandArguments $CommandArguments -LogPath $LogPath

    if (-not $stepResult.Succeeded) {
        Write-Host "$DisplayName : FAILED" -ForegroundColor Red
        return [pscustomobject]@{ Succeeded = $false; Summary = $null; StepResult = $stepResult }
    }

    $summary = if ($Action -eq 'Test') { Get-TestSummaryFromLog -LogPath $LogPath } else { '' }
    Save-LauncherState -StateKey $StateKey -Action $Action -Configuration $Configuration `
        -TargetFramework $TargetFramework -Succeeded $true -Summary $summary `
        -OutputFiles $OutputFiles -RelevantScriptPaths $RelevantScriptPaths
    Write-Host "$DisplayName : PASSED" -ForegroundColor Green
    return [pscustomobject]@{ Succeeded = $true; Summary = $summary; StepResult = $stepResult }
}

function Invoke-ValidateAllAction {
    Write-Host 'Verifies tests and the required Windows and Android builds; it creates no APK or AAB package.'
    Write-Host 'This runs: automated tests, Windows Debug, Windows Release, Android Debug, and Android Release builds.'
    if ($WhatIf) {
        Write-Host '[WhatIf] Would evaluate (reuse or run): tests, then Windows Debug/Release and Android Debug/Release builds.'
        return New-ActionResult -ActionName 'ValidateAll' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $logPath = New-LauncherLogPath -ActionName 'ValidateAll'
    Write-Host "Log: $logPath"
    $launcherScript = @((Join-Path $scriptRoot 'knownfirst.ps1'))

    # App/test-project restore is required whenever at least one build/test step below is
    # going to actually execute (not be reused). Restoring is cheap and idempotent, so it is
    # simplest and safest to always restore both up front rather than trying to predict which
    # individual steps will need it.
    $restoreResult = Invoke-KnownFirstCommand -StepName 'App restore' -FilePath 'dotnet' `
        -CommandArguments @('restore', $projectPath) -LogPath $logPath
    if (-not $restoreResult.Succeeded) {
        return New-ActionResult -ActionName 'ValidateAll' -Succeeded $false `
            -FailedStepName $restoreResult.StepName -FailedCommand $restoreResult.Command `
            -ExitCode $restoreResult.ExitCode -LogPath $logPath
    }
    $testRestoreResult = Invoke-KnownFirstCommand -StepName 'Test project restore' -FilePath 'dotnet' `
        -CommandArguments @('restore', $testProjectPath) -LogPath $logPath
    if (-not $testRestoreResult.Succeeded) {
        return New-ActionResult -ActionName 'ValidateAll' -Succeeded $false `
            -FailedStepName $testRestoreResult.StepName -FailedCommand $testRestoreResult.Command `
            -ExitCode $testRestoreResult.ExitCode -LogPath $logPath
    }

    $steps = @(
        @{
            DisplayName = 'Tests'; StateKey = 'Test'; Action = 'Test'
            OutputFiles = @($logPath); RelevantScriptPaths = $launcherScript
            StepName = 'Run tests'; FilePath = 'dotnet'
            CommandArguments = @('test', $testProjectPath, '-c', 'Debug', '--no-restore', '--logger', 'console;verbosity=normal')
        }
        @{
            DisplayName = 'Windows Debug'; StateKey = 'WindowsBuild-Debug'; Action = 'WindowsBuild'
            Configuration = 'Debug'; TargetFramework = $windowsTargetFramework
            OutputFiles = @((Join-Path $projectRoot "bin\Debug\$windowsTargetFramework\win-x64\KnownFirst.dll"))
            RelevantScriptPaths = $launcherScript
            StepName = 'Windows Debug build'; FilePath = 'dotnet'
            CommandArguments = @('build', $projectPath, '-c', 'Debug', '-f', $windowsTargetFramework, '--no-restore')
        }
        @{
            DisplayName = 'Windows Release'; StateKey = 'WindowsBuild-Release'; Action = 'WindowsBuild'
            Configuration = 'Release'; TargetFramework = $windowsTargetFramework
            OutputFiles = @((Join-Path $projectRoot "bin\Release\$windowsTargetFramework\win-x64\KnownFirst.dll"))
            RelevantScriptPaths = $launcherScript
            StepName = 'Windows Release build'; FilePath = 'dotnet'
            CommandArguments = @('build', $projectPath, '-c', 'Release', '-f', $windowsTargetFramework, '--no-restore')
        }
        @{
            DisplayName = 'Android Debug build'; StateKey = 'AndroidBuildValidation-Debug'; Action = 'AndroidBuildValidation'
            Configuration = 'Debug'; TargetFramework = $androidTargetFramework
            OutputFiles = @((Join-Path $projectRoot "bin\Debug\$androidTargetFramework\KnownFirst.dll"))
            RelevantScriptPaths = $launcherScript
            StepName = 'Android Debug build'; FilePath = 'dotnet'
            CommandArguments = @('build', $projectPath, '-c', 'Debug', '-f', $androidTargetFramework, '-m:1', '--no-restore')
        }
        @{
            DisplayName = 'Android Release build'; StateKey = 'AndroidBuildValidation-Release'; Action = 'AndroidBuildValidation'
            Configuration = 'Release'; TargetFramework = $androidTargetFramework
            OutputFiles = @((Join-Path $projectRoot "bin\Release\$androidTargetFramework\KnownFirst.dll"))
            RelevantScriptPaths = $launcherScript
            StepName = 'Android Release build'; FilePath = 'dotnet'
            CommandArguments = @('build', $projectPath, '-c', 'Release', '-f', $androidTargetFramework, '-m:1', '--no-restore')
        }
    )

    $overallSummary = $null
    foreach ($step in $steps) {
        $stepConfiguration = if ($step.ContainsKey('Configuration')) { $step.Configuration } else { '' }
        $stepTargetFramework = if ($step.ContainsKey('TargetFramework')) { $step.TargetFramework } else { '' }
        $outcome = Invoke-ValidateAllStep -DisplayName $step.DisplayName -StateKey $step.StateKey -Action $step.Action `
            -Configuration $stepConfiguration -TargetFramework $stepTargetFramework `
            -OutputFiles $step.OutputFiles -RelevantScriptPaths $step.RelevantScriptPaths `
            -LogPath $logPath -StepName $step.StepName -FilePath $step.FilePath -CommandArguments $step.CommandArguments

        if ($step.StateKey -eq 'Test' -and $outcome.Summary) {
            $overallSummary = $outcome.Summary
        }

        if (-not $outcome.Succeeded) {
            $failedStepName = if ($outcome.StepResult) { $outcome.StepResult.StepName } else { $step.StepName }
            $failedCommand = if ($outcome.StepResult) { $outcome.StepResult.Command } else { '' }
            $failedExitCode = if ($outcome.StepResult) { $outcome.StepResult.ExitCode } else { 1 }
            return New-ActionResult -ActionName 'ValidateAll' -Succeeded $false `
                -FailedStepName $failedStepName -FailedCommand $failedCommand `
                -ExitCode $failedExitCode -LogPath $logPath -Summary $overallSummary
        }
    }

    return New-ActionResult -ActionName 'ValidateAll' -Succeeded $true -LogPath $logPath -Summary $overallSummary
}

function Invoke-GuiTestAction {
    param([Parameter(Mandatory = $true)][string]$Scenario)

    Write-Host "Runs automated Windows GUI tests using the $Scenario scenario."

    # GuiTest only ever supports Debug (see scenarios.json). Never forward a null, empty, or
    # whitespace Configuration to the scenario runner - its own ValidateSet('Debug') would
    # reject an explicit blank value before falling back to its own default.
    $effectiveConfiguration = if ([string]::IsNullOrWhiteSpace($Configuration)) { 'Debug' } else { $Configuration }

    if ($WhatIf) {
        $scenarioName = switch ($Scenario) {
            'StartupSmoke' { 'Startup smoke test' }
            default { $Scenario }
        }
        Write-Host "[WhatIf] Would run: GUI test scenario '$scenarioName' in $effectiveConfiguration configuration"
        Write-Host "[WhatIf] No directories, profiles, builds, tests, or processes created."
        return New-ActionResult -ActionName 'GuiTest' -Succeeded $true -Summary '[WhatIf] no commands executed.'
    }

    $scriptPath = Join-Path $scriptRoot 'gui-tests\windows\run-scenario-startup-smoke.ps1'
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        return New-ActionResult -ActionName 'GuiTest' -Succeeded $false -ExitCode 1 `
            -FailedStepName 'Setup' -FailedCommand 'Launcher' `
            -Summary "GUI test runner script not found: $scriptPath"
    }

    $logPath = New-LauncherLogPath -ActionName "GuiTest-$Scenario"
    Write-Host "Log: $logPath"

    $guiRunsRoot = Join-Path $projectRoot 'artifacts\gui-tests\windows\runs'
    $priorRunDirNames = @()
    if (Test-Path -LiteralPath $guiRunsRoot -PathType Container) {
        $priorRunDirNames = @(Get-ChildItem -LiteralPath $guiRunsRoot -Directory | Select-Object -ExpandProperty Name)
    }

    $exitCode = 1
    $errorMessage = $null
    try {
        & $scriptPath -Configuration $effectiveConfiguration -WorkingDirectory $projectRoot | Tee-Object -Variable 'guiTestOutput'
        $exitCode = $LASTEXITCODE
    }
    catch {
        $errorMessage = $_.Exception.Message
    }

    if ($guiTestOutput) {
        Add-Content -LiteralPath $logPath -Value $guiTestOutput -Encoding UTF8
    }
    if ($errorMessage) {
        Add-Content -LiteralPath $logPath -Value "ERROR: $errorMessage" -Encoding UTF8
        Write-Host "ERROR: $errorMessage" -ForegroundColor Red
    }

    # Discover the actual GUI run directory the scenario runner created (its RunId is
    # timestamp-generated and not known ahead of time). artifacts\launcher-logs is never the
    # GUI run directory - it only holds this launcher's own wrapper log.
    $newRunDir = $null
    if (Test-Path -LiteralPath $guiRunsRoot -PathType Container) {
        $newRunDir = Get-ChildItem -LiteralPath $guiRunsRoot -Directory |
            Where-Object { $priorRunDirNames -notcontains $_.Name } |
            Sort-Object -Property Name -Descending |
            Select-Object -First 1
    }

    if (-not $newRunDir) {
        # The runner never got past its own preconditions, so no GUI run was ever created.
        # This is a launcher/precondition-level failure, never a smoke-test result - do not
        # fabricate a run directory, summary.json, or report.zip path for it.
        $summary = if ($errorMessage) { "No GUI run directory was created. $errorMessage" } else { 'No GUI run directory was created.' }
        return New-ActionResult -ActionName 'GuiTest' -Succeeded $false `
            -FailedStepName 'GuiTestRunner' -ExitCode $exitCode -LogPath $logPath -Summary $summary
    }

    $runDirectory = $newRunDir.FullName
    $summaryPath = Join-Path $runDirectory 'summary.json'
    $reportZipPath = Join-Path $runDirectory 'report.zip'
    if (-not (Test-Path -LiteralPath $reportZipPath -PathType Leaf)) {
        $reportZipPath = ''
    }

    if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
        $summary = if ($errorMessage) { "GUI run directory was created but summary.json was not found. $errorMessage" } else { 'GUI run directory was created but summary.json was not found.' }
        return New-ActionResult -ActionName 'GuiTest' -Succeeded $false `
            -FailedStepName 'GuiTestRunner' -ExitCode $exitCode -LogPath $logPath -Summary $summary `
            -RunDirectory $runDirectory -ReportZipPath $reportZipPath
    }

    # summary.json is the authoritative record of whether the process/window/startup-event
    # assertions passed. It is written before report packaging and before any later console
    # formatting runs, so an unrelated error occurring after it (e.g. a packaging failure or a
    # PowerShell expression error) must never overwrite this genuine outcome.
    try {
        $runSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    }
    catch {
        return New-ActionResult -ActionName 'GuiTest' -Succeeded $false `
            -FailedStepName 'GuiTestRunner' -ExitCode $exitCode -LogPath $logPath `
            -Summary "summary.json could not be read: $($_.Exception.Message)" `
            -RunDirectory $runDirectory -SummaryPath $summaryPath -ReportZipPath $reportZipPath
    }

    $succeeded = ($runSummary.result -eq 'Passed')
    if ($succeeded) {
        $summaryText = if ($errorMessage) { "Smoke checks passed; a secondary error occurred after results were recorded: $errorMessage" } else { '' }
        return New-ActionResult -ActionName 'GuiTest' -Succeeded $true -LogPath $logPath -Summary $summaryText `
            -RunDirectory $runDirectory -SummaryPath $summaryPath -ReportZipPath $reportZipPath
    }

    $failedStepName = if ($runSummary.failedStep) { [string]$runSummary.failedStep } else { 'SmokeTest' }
    $summaryText = if ($runSummary.errorMessage) { [string]$runSummary.errorMessage } else { $errorMessage }
    return New-ActionResult -ActionName 'GuiTest' -Succeeded $false `
        -FailedStepName $failedStepName -ExitCode $exitCode -LogPath $logPath -Summary $summaryText `
        -RunDirectory $runDirectory -SummaryPath $summaryPath -ReportZipPath $reportZipPath
}

function Invoke-KnownFirstAction {
    param([Parameter(Mandatory = $true)][string]$SelectedAction)

    try {
        switch ($SelectedAction) {
            'Test' { return Invoke-RunTests }
            'GuiTest' { return Invoke-GuiTestAction -Scenario $GuiScenario }
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
        # truly unexpected still escapes (e.g. a Configuration validation failure), report it
        # the same structured way instead of letting it crash the launcher or the parent host.
        return New-ActionResult -ActionName $SelectedAction -Succeeded $false -ExitCode 1 `
            -FailedStepName 'Launcher' -FailedCommand $SelectedAction `
            -Summary $_.Exception.Message
    }
}

# --- Menu / entry point ----------------------------------------------------------------

function Show-WindowsBuildMenu {
    Write-Host ''
    Write-Host 'Build Windows app' -ForegroundColor Green
    Write-Host '1. Debug'
    Write-Host '2. Release'
    Write-Host '3. Debug and Release'
    Write-Host '4. Back'
    Write-Host ''
    $choice = Read-Host 'Choose an option (1-4)'

    $selectedConfiguration = switch ($choice) {
        '1' { 'Debug' }
        '2' { 'Release' }
        '3' { 'All' }
        default { $null }
    }

    if ($choice -eq '4' -or -not $selectedConfiguration) {
        if ($choice -ne '4') {
            Write-Host "Unrecognized option: $choice" -ForegroundColor Yellow
        }
        return
    }

    $script:Configuration = $selectedConfiguration
    $result = Invoke-KnownFirstAction -SelectedAction 'WindowsBuild'
    Write-ActionResult -Result $result
    if (-not $result.Succeeded) {
        Read-Host 'Press Enter to return to the menu'
    }
}

function Show-AndroidTestPackageMenu {
    Write-Host ''
    Write-Host 'Build Android APK' -ForegroundColor Green
    Write-Host '1. Debug'
    Write-Host '2. Release'
    Write-Host '3. Debug and Release'
    Write-Host '4. Diagnostic (advanced)'
    Write-Host '5. Back'
    Write-Host ''
    Write-Host 'Diagnostic is for investigating release-only, AOT, or trimming problems.' -ForegroundColor DarkGray
    Write-Host ''
    $choice = Read-Host 'Choose an option (1-5)'

    $selectedConfiguration = switch ($choice) {
        '1' { 'Debug' }
        '2' { 'Release' }
        '3' { 'DebugRelease' }
        '4' { 'Diagnostic' }
        default { $null }
    }

    if ($choice -eq '5' -or -not $selectedConfiguration) {
        if ($choice -ne '5') {
            Write-Host "Unrecognized option: $choice" -ForegroundColor Yellow
        }
        return
    }

    $script:Configuration = $selectedConfiguration
    $result = Invoke-KnownFirstAction -SelectedAction 'AndroidTestPackage'
    Write-ActionResult -Result $result
    if (-not $result.Succeeded) {
        Read-Host 'Press Enter to return to the menu'
    }
}

function Show-GuiTestMenu {
    Write-Host ''
    Write-Host 'Windows GUI tests' -ForegroundColor Green
    Write-Host '1. Startup smoke test'
    Write-Host '2. Run all available GUI tests'
    Write-Host '3. Show last GUI-test result'
    Write-Host '4. Back'
    Write-Host ''
    $choice = Read-Host 'Choose an option (1-4)'

    switch ($choice) {
        '1' {
            $script:GuiScenario = 'StartupSmoke'
            $script:Configuration = 'Debug'
            $result = Invoke-KnownFirstAction -SelectedAction 'GuiTest'
            Write-ActionResult -Result $result
            Write-Host ''
            Write-Host "Result: $($result.ActionName) $($result.Status)"
            if ($result.RunDirectory) {
                Write-Host "Run directory: $($result.RunDirectory)"
            }
            else {
                Write-Host 'No GUI run directory was created.' -ForegroundColor Yellow
            }
            if ($result.SummaryPath) { Write-Host "Summary: $($result.SummaryPath)" }
            if ($result.ReportZipPath) { Write-Host "Report package: $($result.ReportZipPath)" }
            Read-Host 'Press Enter to return to the menu'
            return $true
        }
        '2' {
            Write-Host 'Running all available GUI tests (currently only StartupSmoke)...'
            $script:GuiScenario = 'StartupSmoke'
            $script:Configuration = 'Debug'
            $result = Invoke-KnownFirstAction -SelectedAction 'GuiTest'
            Write-ActionResult -Result $result
            Write-Host ''
            Write-Host "Result: $($result.ActionName) $($result.Status)"
            if ($result.RunDirectory) {
                Write-Host "Run directory: $($result.RunDirectory)"
            }
            else {
                Write-Host 'No GUI run directory was created.' -ForegroundColor Yellow
            }
            if ($result.SummaryPath) { Write-Host "Summary: $($result.SummaryPath)" }
            if ($result.ReportZipPath) { Write-Host "Report package: $($result.ReportZipPath)" }
            Read-Host 'Press Enter to return to the menu'
            return $true
        }
        '3' {
            $runsRoot = Join-Path $projectRoot 'artifacts\gui-tests\windows\runs'
            if (-not (Test-Path -LiteralPath $runsRoot -PathType Container)) {
                Write-Host 'No GUI test runs found.' -ForegroundColor Yellow
                Read-Host 'Press Enter to return to the menu'
                return $true
            }

            $runDirs = @(Get-ChildItem -LiteralPath $runsRoot -Directory | Sort-Object -Property Name -Descending)
            if ($runDirs.Count -eq 0) {
                Write-Host 'No GUI test runs found.' -ForegroundColor Yellow
                Read-Host 'Press Enter to return to the menu'
                return $true
            }

            $lastRunDir = $runDirs[0]
            $summaryPath = Join-Path $lastRunDir.FullName 'summary.json'
            $reportZipPath = Join-Path $lastRunDir.FullName 'report.zip'

            if (Test-Path -LiteralPath $summaryPath -PathType Leaf) {
                $summary = Get-Content -LiteralPath $summaryPath | ConvertFrom-Json
                Write-Host ''
                Write-Host "Last GUI test run: $($lastRunDir.Name)"
                $resultColor = if ($summary.result -eq 'Passed') { 'Green' } else { 'Red' }
                Write-Host "Result: $($summary.result)" -ForegroundColor $resultColor
                Write-Host "Scenario: $($summary.displayName)"
                Write-Host "Completed: $($summary.completedAtUtc)"
                Write-Host "Run directory: $($lastRunDir.FullName)"
                Write-Host "Summary: $summaryPath"
                if (Test-Path -LiteralPath $reportZipPath) {
                    Write-Host "Report package: $reportZipPath"
                }
                if ($summary.failedStep) {
                    Write-Host "Failed step: $($summary.failedStep)"
                }
            }
            else {
                Write-Host 'Last run summary not found.' -ForegroundColor Yellow
            }
            Read-Host 'Press Enter to return to the menu'
            return $true
        }
        '4' {
            return $true
        }
        default {
            Write-Host "Unrecognized option: $choice" -ForegroundColor Yellow
            return $true
        }
    }
}

function Show-KnownFirstMenu {
    Write-Host ''
    Write-Host 'KnownFirst build launcher' -ForegroundColor Green
    Write-Host '1. Automated tests (unit, integration and contract)'
    Write-Host '2. Windows GUI tests'
    Write-Host '3. Build Windows app'
    Write-Host '4. Build Android APK'
    Write-Host '5. Create Google Play AAB'
    Write-Host '6. Full validation (automated tests + Windows/Android builds)'
    Write-Host '7. Exit'
    Write-Host ''
    $choice = Read-Host 'Choose an option (1-7)'

    switch ($choice) {
        '1' {
            $result = Invoke-KnownFirstAction -SelectedAction 'Test'
            Write-ActionResult -Result $result
            if (-not $result.Succeeded) { Read-Host 'Press Enter to return to the menu' }
            return $true
        }
        '2' {
            Show-GuiTestMenu
            return $true
        }
        '3' {
            Show-WindowsBuildMenu
            return $true
        }
        '4' {
            Show-AndroidTestPackageMenu
            return $true
        }
        '5' {
            $result = Invoke-KnownFirstAction -SelectedAction 'GooglePlayBundle'
            Write-ActionResult -Result $result
            if (-not $result.Succeeded) { Read-Host 'Press Enter to return to the menu' }
            return $true
        }
        '6' {
            $result = Invoke-KnownFirstAction -SelectedAction 'ValidateAll'
            Write-ActionResult -Result $result
            if (-not $result.Succeeded) { Read-Host 'Press Enter to return to the menu' }
            return $true
        }
        '7' {
            Write-Host 'Exiting.'
            return $false
        }
        default {
            Write-Host "Unrecognized option: $choice" -ForegroundColor Yellow
            return $true
        }
    }
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
