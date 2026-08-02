<#
.SYNOPSIS
    KnownFirst Windows GUI test runner (Phase 1).

.DESCRIPTION
    Launches an isolated, deterministic UI Automation test run against the KnownFirst Windows
    app. Uses only `winapp ui` operations that do not simulate physical mouse/keyboard input
    (inspect / search / invoke / set-value / get-value / wait-for / window-only screenshot).

    The app under test is redirected to an isolated database + preferences profile via the
    KNOWNFIRST_GUI_TEST_ROOT environment variable; the real database and preferences are never
    touched, and their SHA-256 hashes/timestamps are compared before and after the run to prove
    it.

    Scenarios live under scripts\gui-tests\windows\scenarios\ and are dot-sourced by name, so
    new scenarios (002-responsive-layouts, 003-translation-targets, ...) can be added without
    touching this entry point.

.PARAMETER Scenario
    Scenario id to run. Only '001-import-definition-reset' exists for Phase 1.

.PARAMETER Configuration
    Windows build configuration to launch. Only 'Debug' is exercised in Phase 1.

.PARAMETER MonitorTarget
    Which monitor to place KnownFirst on: Internal (the laptop's own panel, identified via
    Windows display configuration - default), Primary (the Windows-designated primary display),
    LargestNonPrimary (largest-area non-primary display), or DeviceName (an explicit GDI device
    name given via -MonitorDeviceName). Monitor numbering/device naming is never assumed; see
    GuiTestRunnerCore.ps1 for how each target is resolved.

.PARAMETER MonitorDeviceName
    GDI device name (e.g. '\\.\DISPLAY2') to select, as reported by -ListMonitors. Required only
    when -MonitorTarget is DeviceName.

.PARAMETER ListMonitors
    Print the discovered monitor table (identity from Windows display configuration) and exit
    without launching KnownFirst.

.EXAMPLE
    pwsh -File scripts\gui-tests\windows\Invoke-GuiTestRun.ps1

.EXAMPLE
    pwsh -File scripts\gui-tests\windows\Invoke-GuiTestRun.ps1 -Scenario 001-import-definition-reset -MonitorTarget Internal

.EXAMPLE
    pwsh -File scripts\gui-tests\windows\Invoke-GuiTestRun.ps1 -ListMonitors
#>

[CmdletBinding()]
param(
    [ValidateSet(
        '001-import-definition-reset',
        'PreparationSelectedMeaning',
        'PreparationAcceptFailureRecovery',
        'PreparationInvalidContextRecovery')]
    [string]$Scenario = '001-import-definition-reset',

    [ValidateSet('Debug')]
    [string]$Configuration = 'Debug',

    [ValidateSet('Internal', 'Primary', 'LargestNonPrimary', 'DeviceName')]
    [string]$MonitorTarget = 'Internal',

    [string]$MonitorDeviceName,

    [switch]$ListMonitors
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $scriptRoot '..\..\..')).Path

. (Join-Path $scriptRoot 'lib\GuiTestRunnerCore.ps1')

if ($MonitorTarget -eq 'DeviceName' -and [string]::IsNullOrWhiteSpace($MonitorDeviceName)) {
    throw '-MonitorDeviceName is required when -MonitorTarget is DeviceName.'
}

if ($ListMonitors) {
    $monitors = @(Get-DisplayMonitors)
    Write-Host "Detected displays ($($monitors.Count)):"
    Write-Host (Format-DisplayMonitorTable -Monitors $monitors)
    exit 0
}

$scenarioPath = Join-Path $scriptRoot "scenarios\$Scenario.ps1"
if (-not (Test-Path -LiteralPath $scenarioPath -PathType Leaf)) {
    throw "Scenario script not found: $scenarioPath"
}
. $scenarioPath

$context = New-GuiTestRunContext -ProjectRoot $projectRoot -ScenarioId $Scenario

$targetFramework = 'net10.0-windows10.0.19041.0'
$executablePath = Join-Path $projectRoot "bin\$Configuration\$targetFramework\win-x64\KnownFirst.exe"

$branch = (& git -C $projectRoot rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1).Trim()
$commit = (& git -C $projectRoot rev-parse HEAD 2>$null | Select-Object -First 1).Trim()

Write-RunnerLog "Project root: $projectRoot"
Write-RunnerLog "Executable: $executablePath"
Write-RunnerLog "Branch: $branch, Commit: $commit"

$result = $null
$succeeded = $false
try {
    switch ($Scenario) {
        '001-import-definition-reset' {
            $result = Invoke-Scenario001 -Context $context -ExecutablePath $executablePath `
                -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        }
        'PreparationSelectedMeaning' {
            $result = Invoke-ScenarioPreparationSelectedMeaning -Context $context -ExecutablePath $executablePath `
                -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        }
        'PreparationAcceptFailureRecovery' {
            $result = Invoke-ScenarioPreparationAcceptFailureRecovery -Context $context -ExecutablePath $executablePath `
                -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        }
        'PreparationInvalidContextRecovery' {
            $result = Invoke-ScenarioPreparationInvalidContextRecovery -Context $context -ExecutablePath $executablePath `
                -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        }
        default {
            throw "No dispatcher registered for scenario '$Scenario'."
        }
    }
    $succeeded = [bool]$result.Succeeded
}
finally {
    # Evidence writing is deliberately wrapped in its own try/catch: $succeeded was already
    # determined above, and a failure writing the report/summary (e.g. a locked/full disk) must
    # never prevent the single, authoritative exit-code statement below from running with that
    # already-correct value - the launcher must expose one unambiguous final process exit code.
    try {
        $metadata = @{
            Commit                 = $commit
            Branch                 = $branch
            MonitorTarget          = $MonitorTarget
            MonitorDeviceNameArg   = $MonitorDeviceName
            Monitors               = if ($result) { $result.AllMonitors } else { $null }
            SelectedMonitor        = if ($result) { $result.Monitor } else { $null }
            MonitorSelectionReason = if ($result) { $result.MonitorSelectionReason } else { $null }
            Placement              = if ($result) { $result.Placement } else { $null }
            FinalWindowBounds      = if ($result) { $result.FinalWindowBounds } else { $null }
            RealDataUnchanged      = if ($result) { $result.RealDataUnchanged } else { $false }
            RealDataDifferences    = if ($result) { $result.RealDataDifferences } else { @('Scenario did not complete far enough to compare real data.') }
        }
        Write-GuiTestReport -Context $context -Metadata $metadata -OverallSucceeded $succeeded

        Write-RunnerLog "Run finished. Succeeded = $succeeded"
        Write-RunnerLog "Report: $($context.ReportPath)"
        Write-RunnerLog "Summary: $($context.SummaryPath)"
    }
    catch {
        Write-Host "Warning: evidence writing failed after the scenario result was determined: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# The single, authoritative process exit code: always reached, always reflects the scenario's
# actual result (never overridden by an evidence-writing failure above).
if (-not $succeeded) {
    $host.SetShouldExit(1)
}
else {
    $host.SetShouldExit(0)
}
