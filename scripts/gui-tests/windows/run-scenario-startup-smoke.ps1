<#
.SYNOPSIS
    Invokes the StartupSmoke GUI test scenario with structured reporting.

.DESCRIPTION
    Wraps scripts/smoke-test-windows.ps1 and generates structured test artifacts.
    Creates a run directory under artifacts/gui-tests/windows/runs/ with:
    - summary.json: test metadata and result
    - steps.jsonl: newline-delimited JSON step log
    - runner.log: combined stdout/stderr capture
    - environment.json: build/system environment state
    - report.zip: compressed artifact package (no databases, secrets, or unrelated logs)

.PARAMETER Configuration
    Build configuration: Debug only (supported for StartupSmoke).

.PARAMETER RunId
    Optional run identifier (timestamp-scenarioid). If omitted, generated automatically.

.PARAMETER WorkingDirectory
    Project root. Defaults to parent of parent of this script.

.EXAMPLE
    & ".\scripts\gui-tests\windows\run-scenario-startup-smoke.ps1" -Configuration Debug
#>

param(
    [ValidateSet('Debug')]
    [string]$Configuration = 'Debug',

    [string]$RunId,

    [string]$WorkingDirectory
)

$ErrorActionPreference = 'Stop'
$WarningPreference = 'SilentlyContinue'

if (-not $WorkingDirectory) {
    $WorkingDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

if (-not $RunId) {
    $timestamp = Get-Date -Format 'yyyyMMddTHHmmssZ'
    $RunId = "$timestamp-StartupSmoke"
}

$runsRoot = Join-Path $WorkingDirectory 'artifacts\gui-tests\windows\runs'
$runDir = Join-Path $runsRoot $RunId
$summaryPath = Join-Path $runDir 'summary.json'
$stepsPath = Join-Path $runDir 'steps.jsonl'
$runnerLogPath = Join-Path $runDir 'runner.log'
$envPath = Join-Path $runDir 'environment.json'
$reportZipPath = Join-Path $runDir 'report.zip'
$profilesRoot = Join-Path $WorkingDirectory 'artifacts\gui-tests\windows\profiles'
$liveProfileDir = Join-Path $profilesRoot $RunId

# --- Initialize result state -------
$startedAtUtc = [DateTime]::UtcNow
$completedAtUtc = $null
$result = 'NotRunnable'
$failedStep = $null
$errorMessage = $null
$exitCode = 1
$steps = New-Object System.Collections.Generic.List[object]
$appLogsSnapshot = @{}
$previousEnvValue = $null

function Log-Step {
    param(
        [string]$Name,
        [string]$Status = 'Started',
        [string]$Message = '',
        [int]$DurationMs = 0
    )

    $step = [pscustomobject]@{
        timestamp     = [DateTime]::UtcNow.ToString('o')
        stepName      = $Name
        status        = $Status
        durationMs    = $DurationMs
        message       = $Message
    }
    $steps.Add($step)

    if (Test-Path -LiteralPath $stepsPath -PathType Leaf) {
        Add-Content -LiteralPath $stepsPath -Value ($step | ConvertTo-Json -Compress) -Encoding UTF8
    }
}

function Snapshot-AppLogs {
    param([string]$LogDir)
    $snapshot = @{}
    if (Test-Path -LiteralPath $LogDir -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $LogDir -Filter 'knownfirst-*.jsonl' -File) {
            $snapshot[$file.FullName] = @{
                Length = $file.Length
                LastWriteTimeUtc = $file.LastWriteTimeUtc
            }
        }
    }
    return $snapshot
}

function Capture-RunSpecificLogs {
    param(
        [string]$LogDir,
        [hashtable]$SnapshotBefore,
        [datetime]$LaunchStartUtc,
        [string]$DestinationDir
    )
    $capturedLogs = @()
    if (-not (Test-Path -LiteralPath $LogDir -PathType Container)) {
        return $capturedLogs
    }

    $nowUtc = [DateTime]::UtcNow
    $windowStart = $LaunchStartUtc.AddSeconds(-5)
    $windowEnd = $nowUtc.AddSeconds(5)

    foreach ($file in Get-ChildItem -LiteralPath $LogDir -Filter 'knownfirst-*.jsonl' -File) {
        $isNew = -not $SnapshotBefore.ContainsKey($file.FullName)
        $wasModified = $false
        if ($SnapshotBefore.ContainsKey($file.FullName)) {
            $before = $SnapshotBefore[$file.FullName]
            if (($file.Length -gt $before.Length) -or ($file.LastWriteTimeUtc -gt $before.LastWriteTimeUtc)) {
                $wasModified = $true
            }
        }

        if (($isNew -or $wasModified) -and ($file.LastWriteTimeUtc -ge $windowStart -and $file.LastWriteTimeUtc -le $windowEnd)) {
            $destPath = Join-Path $DestinationDir ("app-$($capturedLogs.Count + 1).log")
            Copy-Item -LiteralPath $file.FullName -Destination $destPath -Force | Out-Null
            $capturedLogs += $destPath
        }
    }
    return $capturedLogs
}

function Get-EnvironmentSnapshot {
    $gitBranch = ''
    $gitHead = ''
    $gitClean = $false

    try {
        $gitBranch = (& git -C $WorkingDirectory rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1).Trim()
        $gitHead = (& git -C $WorkingDirectory rev-parse HEAD 2>$null | Select-Object -First 1).Trim()
        $gitStatus = & git -C $WorkingDirectory status --short 2>$null
        $gitClean = [string]::IsNullOrEmpty(($gitStatus -join ''))
    }
    catch {
        # Git not available or not in repo
    }

    $dotnetVersion = ''
    try {
        $dotnetVersion = (& dotnet --version 2>$null | Select-Object -First 1).Trim()
    }
    catch { }

    return [pscustomobject]@{
        timestamp       = $startedAtUtc.ToString('o')
        dotnetVersion   = $dotnetVersion
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        osVersion       = [Environment]::OSVersion.VersionString
        gitBranch       = $gitBranch
        gitHead         = $gitHead
        gitWorktreeClean = $gitClean
        processId       = $PID
    }
}

function New-ReportZip {
    param(
        [string]$SourceDir,
        [string]$OutputPath
    )

    try {
        if (Test-Path -LiteralPath $OutputPath) {
            Remove-Item -LiteralPath $OutputPath -Force
        }

        # Windows PowerShell 5.1 (.NET Framework) does not implicitly load
        # System.IO.Compression alongside System.IO.Compression.FileSystem: ZipArchiveMode
        # lives in the former, ZipFile in the latter. Both must be loaded before either type
        # is referenced, or type resolution fails with "Unable to find type
        # [System.IO.Compression.ZipArchiveMode]".
        Add-Type -AssemblyName 'System.IO.Compression'
        Add-Type -AssemblyName 'System.IO.Compression.FileSystem'

        $filesToInclude = @(
            'summary.json',
            'steps.jsonl',
            'runner.log',
            'environment.json'
        )

        $screenShotDir = Join-Path $SourceDir 'screenshots'
        if (Test-Path -LiteralPath $screenShotDir -PathType Container) {
            $screenshots = @(Get-ChildItem -LiteralPath $screenShotDir -File)
            $filesToInclude += $screenshots | ForEach-Object { Join-Path 'screenshots' $_.Name }
        }

        $zip = [System.IO.Compression.ZipFile]::Open($OutputPath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $filesToInclude) {
                $fullPath = Join-Path $SourceDir $file
                if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
                    # CreateEntryFromFile is an extension method (ZipFileExtensions). Windows
                    # PowerShell 5.1 cannot resolve it via instance dot-syntax ($zip.CreateEntry...)
                    # - it must be invoked as the static method it actually is.
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $zip, $fullPath, $file, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
            }
        }
        finally {
            $zip.Dispose()
        }

        return $true
    }
    catch {
        Write-Host "Warning: could not create report.zip: $($_.Exception.Message)" -ForegroundColor Yellow
        # A packaging failure is recorded accurately in steps.jsonl without touching
        # summary.json's result, which is already written and reflects only the genuine
        # process/window/startup-event smoke outcome.
        Log-Step -Name 'ReportPackaged' -Status 'Failed' -Message $_.Exception.Message
        return $false
    }
}

# --- Main execution -------

try {
    # Preconditions: validate configuration and paths before creating any directories
    if ($Configuration -ne 'Debug') {
        throw "Configuration '$Configuration' is not supported for GUI tests. Only Debug is supported."
    }

    $resolvedRunDir = [System.IO.Path]::GetFullPath($runDir)
    $resolvedRunsRoot = [System.IO.Path]::GetFullPath($runsRoot)
    $resolvedProfileDir = [System.IO.Path]::GetFullPath($liveProfileDir)
    $resolvedProfilesRoot = [System.IO.Path]::GetFullPath($profilesRoot)

    if (-not $resolvedRunDir.StartsWith($resolvedRunsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Run directory escapes expected root. Expected under $runsRoot, got $runDir"
    }

    if (-not $resolvedProfileDir.StartsWith($resolvedProfilesRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Profile directory escapes expected root. Expected under $profilesRoot, got $liveProfileDir"
    }

    $existingProcesses = @(Get-Process -Name 'KnownFirst' -ErrorAction SilentlyContinue)
    if ($existingProcesses.Count -gt 0) {
        throw 'A KnownFirst process is already running. Close it before running the smoke test.'
    }

    # Preconditions passed: now create directories
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    New-Item -ItemType Directory -Path $liveProfileDir -Force | Out-Null
    New-Item -ItemType File -Path $runnerLogPath -Force | Out-Null
    New-Item -ItemType File -Path $stepsPath -Force | Out-Null

    Write-Host "Run directory: $runDir" -ForegroundColor DarkGray
    Write-Host "Profile directory: $liveProfileDir" -ForegroundColor DarkGray

    Log-Step -Name 'Preconditions' -Status 'Completed' -Message "Configuration: $Configuration, paths validated"

    # Snapshot existing application logs before launching
    $appLogDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnownFirst\Logs'
    $appLogsSnapshot = Snapshot-AppLogs -LogDir $appLogDir
    Log-Step -Name 'AppLogsSnapshot' -Status 'Completed' -Message "Captured $($appLogsSnapshot.Count) existing logs"

    # Save and set KNOWNFIRST_GUI_TEST_ROOT
    $previousEnvValue = [Environment]::GetEnvironmentVariable('KNOWNFIRST_GUI_TEST_ROOT', [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable('KNOWNFIRST_GUI_TEST_ROOT', $resolvedProfileDir, [EnvironmentVariableTarget]::Process)
    Log-Step -Name 'ProfileIsolationActivated' -Status 'Completed' -Message "Profile directory: $resolvedProfileDir"

    # Run the wrapped smoke test
    Log-Step -Name 'SmokeTestStarted' -Status 'Started'
    $smokeTestPath = Join-Path $WorkingDirectory 'scripts\validation\smoke-test-windows.ps1'

    $smokeOutput = @()
    $smokeExitCode = 0
    try {
        & $smokeTestPath -AliveSeconds 12 | ForEach-Object {
            $smokeOutput += $_
            Add-Content -LiteralPath $runnerLogPath -Value $_ -Encoding UTF8
            $_
        }
        $smokeExitCode = $LASTEXITCODE
    }
    catch {
        $smokeExitCode = 1
        $errorMessage = $_.Exception.Message
        Add-Content -LiteralPath $runnerLogPath -Value "ERROR: $errorMessage" -Encoding UTF8
    }

    if ($smokeExitCode -eq 0) {
        $result = 'Passed'
        Log-Step -Name 'SmokeTestCompleted' -Status 'Completed'
    }
    else {
        $result = 'Failed'
        $failedStep = 'SmokeTest'
        if (-not $errorMessage) {
            $errorMessage = 'Smoke test exited with code {0}' -f $smokeExitCode
        }
        Log-Step -Name 'SmokeTestCompleted' -Status 'Failed' -Message $errorMessage
    }

    # Capture run-specific application logs
    $capturedAppLogs = Capture-RunSpecificLogs -LogDir $appLogDir -SnapshotBefore $appLogsSnapshot -LaunchStartUtc $startedAtUtc -DestinationDir $runDir
    if ($capturedAppLogs.Count -gt 0) {
        Log-Step -Name 'AppLogsCollected' -Status 'Completed' -Message "Captured $($capturedAppLogs.Count) run-specific log(s)"
    }
    else {
        Log-Step -Name 'AppLogsCollected' -Status 'Completed' -Message "No run-specific logs found"
    }

    $exitCode = $smokeExitCode
}
catch {
    $result = 'Failed'
    $failedStep = 'Preconditions'
    $errorMessage = $_.Exception.Message
    $exitCode = 1
    Log-Step -Name 'Preconditions' -Status 'Failed' -Message $errorMessage
    if (Test-Path -LiteralPath $runnerLogPath -PathType Leaf) {
        Add-Content -LiteralPath $runnerLogPath -Value "ERROR: $errorMessage" -Encoding UTF8
    }
}
finally {
    # Restore previous KNOWNFIRST_GUI_TEST_ROOT value
    if ($previousEnvValue -eq $null) {
        [Environment]::SetEnvironmentVariable('KNOWNFIRST_GUI_TEST_ROOT', $null, [EnvironmentVariableTarget]::Process)
    }
    else {
        [Environment]::SetEnvironmentVariable('KNOWNFIRST_GUI_TEST_ROOT', $previousEnvValue, [EnvironmentVariableTarget]::Process)
    }

    # Evidence writing (summary.json/report.zip) is deliberately wrapped in its own try/catch:
    # the scenario's pass/fail result and $exitCode were already determined above, and a failure
    # writing evidence (e.g. a locked/full disk) must never prevent the single, authoritative
    # $host.SetShouldExit($exitCode) call below from running with that already-correct value.
    # Without this guard, an unhandled exception here (Set-Content/ConvertTo-Json under
    # $ErrorActionPreference = 'Stop') would terminate the script before that line is ever
    # reached, leaving the process exit code to whatever the host happens to default to instead
    # of the scenario's real result - exactly the "launcher must expose one unambiguous final
    # process exit code" contract this script must uphold.
    try {
        $completedAtUtc = [DateTime]::UtcNow
        $durationMs = [int]($completedAtUtc - $startedAtUtc).TotalMilliseconds

        # Only create artifacts if we created the run directory
        if (Test-Path -LiteralPath $runDir -PathType Container) {
            # Create environment snapshot
            $env = Get-EnvironmentSnapshot
            $env | ConvertTo-Json | Set-Content -LiteralPath $envPath -Encoding UTF8

            # Create summary with run-specific paths
            $summary = [ordered]@{
                schemaVersion         = '1.0'
                scenarioId            = 'StartupSmoke'
                displayName           = 'Startup smoke test'
                startedAtUtc          = $startedAtUtc.ToString('o')
                completedAtUtc        = $completedAtUtc.ToString('o')
                durationMilliseconds  = $durationMs
                configuration         = $Configuration
                branch                = $env.gitBranch
                commitHead            = $env.gitHead
                dirtyWorktree         = -not $env.gitWorktreeClean
                result                = $result
                exitCode              = $exitCode
                failedStep            = $failedStep
                errorMessage          = $errorMessage
                runnerLogPath         = $runnerLogPath
                appLogPaths           = @()
                screenshotPaths       = @()
                profileRoot           = $resolvedProfileDir
                invokedCommand        = "powershell -File scripts/gui-tests/windows/run-scenario-startup-smoke.ps1 -Configuration $Configuration"
            }

            # Add captured application logs to summary
            if ($capturedAppLogs.Count -gt 0) {
                $summary['appLogPaths'] = @($capturedAppLogs)
            }

            Log-Step -Name 'SummaryWritten' -Status 'Completed'

            $summary | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8

            # Create report zip with allowlist
            if (New-ReportZip -SourceDir $runDir -OutputPath $reportZipPath) {
                Log-Step -Name 'ReportPackaged' -Status 'Completed'
            }

            # Print results
            Write-Host ''
            $resultColor = if ($result -eq 'Passed') { 'Green' } else { 'Red' }
            Write-Host "GUI Test Result: $result" -ForegroundColor $resultColor
            Write-Host "Run directory: $runDir"
            Write-Host "Summary: $summaryPath"
            if (Test-Path -LiteralPath $reportZipPath) {
                Write-Host "Report package: $reportZipPath"
            }
            if ($failedStep) {
                Write-Host "Failed step: $failedStep"
            }
        }
    }
    catch {
        # Evidence writing failed after the scenario's own result was already determined: report
        # it, but do not let it change the authoritative exit code below.
        Write-Host "Warning: evidence writing failed after the scenario result was determined: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# The single, authoritative process exit code: always reached, always reflects the scenario's
# actual result (never overridden by an evidence-writing failure above).
$host.SetShouldExit($exitCode)
