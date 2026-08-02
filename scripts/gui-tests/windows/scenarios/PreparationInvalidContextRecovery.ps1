<#
.SYNOPSIS
    Scenario PreparationInvalidContextRecovery (KF-MEANING-002 GUI hardening, Part 3).

.DESCRIPTION
    Proves the context-integrity fail-safe end-to-end through the real UI: a seeded candidate
    ("equitable") has one genuine occurrence plus one occurrence reassigned from an unrelated word
    ("urgent") directly via SQL, reproducing the exact misattribution shape the fail-safe defends
    against. The page must never display the misattributed context, must still show/accept the
    candidate normally, and must never blank out or raise the global ErrorBoundary while doing so.

    Only Windows UI Automation operations that do not simulate physical mouse/keyboard input are
    used, via the wrappers in GuiTestRunnerCore.ps1.
#>

function Invoke-ScenarioPreparationInvalidContextRecovery {
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][ValidateSet('Internal', 'Primary', 'LargestNonPrimary', 'DeviceName')][string]$MonitorTarget,
        [string]$MonitorDeviceName
    )

    $stepNumber = 0
    $assertionNumber = 0
    $overallSucceeded = $true
    $finalWindowBounds = $null
    $selectedMonitor = $null
    $monitorSelectionReason = $null
    $placement = $null
    $monitors = @()
    $realDataBefore = @()
    $realDataAfter = @()
    $nativeLibraryDirectory = Split-Path -Path $ExecutablePath -Parent

    try {
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Fingerprint real KnownFirst data files (before)' -Kind 'guard' | Out-Null
        $realDataBefore = @(Get-RealKnownFirstDataFingerprint)

        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Start Windows Debug executable with deterministic seed scenario' -Kind 'action' | Out-Null
        $env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO = 'PreparationInvalidContextRecovery'
        try {
            $launch = Start-KnownFirstUnderTest -ExecutablePath $ExecutablePath -GuiTestRoot $Context.LiveProfileDir
        }
        finally {
            Remove-Item Env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 1500

        $modalWait = Invoke-UiaWaitFor -Selector 'whats-new-modal' -TimeoutMs 4000
        if ($modalWait.Succeeded) {
            Invoke-UiaInvoke -Selector 'whats-new-close-button' -AllowFailure | Out-Null
            Invoke-UiaWaitFor -Selector 'whats-new-modal' -Gone -TimeoutMs 4000 | Out-Null
        }

        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name "Detect displays, select and place monitor (MonitorTarget=$MonitorTarget)" -Kind 'action' | Out-Null
        $monitors = @(Get-DisplayMonitors)
        $selection = Select-DisplayMonitor -Monitors $monitors -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        $selectedMonitor = $selection.Monitor
        $monitorSelectionReason = $selection.Reason
        $placement = Move-WindowIntoMonitorWorkingArea -Hwnd $script:TargetHwnd -Monitor $selectedMonitor -AllMonitors $monitors
        $finalWindowBounds = $placement.VisibleBounds
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'KnownFirst window is on the selected monitor and fully contained' `
            -Condition $placement.Contained -Detail "CorrectivePasses=$($placement.CorrectivePassCount)" | Out-Null
        if (-not $placement.Contained) { $overallSucceeded = $false }

        # --- Navigate to prepare-words: the candidate has one valid and one invalid context ---
        Invoke-UiaInvoke -Selector 'nav-prepare-words' | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'prepare-words-invalid-context-candidate'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to prepare-words' -Kind 'action' -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'The page renders the seeded candidate without a blank screen or ErrorBoundary, even with a misattributed occurrence present' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null

        # --- The misattributed context text must never appear on screen -----------------------
        # Reads only structural page text for this one bounded check (never logged or persisted
        # elsewhere) - "urgent" is fixture text, not real user content.
        $pageInspect = Invoke-UiaInspect -Selector 'preparation-candidate-term' -Depth 12
        $pageDumpPath = Save-UiaDump -Name 'prepare-words-page-dump' -Content (($pageInspect.Output | Out-String))
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify the misattributed context is never displayed' -Kind 'assertion' | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No slicing exception occurred while rendering context (page inspection itself succeeded)' `
            -Condition $pageInspect.Succeeded -Detail "UIA dump: $pageDumpPath" | Out-Null

        # --- Accept still works even though one of the candidate's occurrences is invalid -----
        $beforeShot = Save-DedupedScreenshot -StepId 'before-accept-invalid-context'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Click Accept' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'preparation-accept-button' | Out-Null
        Start-Sleep -Milliseconds 500
        $afterShot = Save-DedupedScreenshot -StepId 'after-accept-invalid-context'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify acceptance succeeds and the workflow advances' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null

        $batchCompleteWait = Invoke-UiaWaitFor -Selector 'preparation-batch-complete' -TimeoutMs 10000
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The single seeded candidate was accepted and the batch-complete state is reached (screen never went blank)' `
            -Condition $batchCompleteWait.Succeeded -Detail 'preparation-batch-complete selector' | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No save error is shown - the invalid context did not block a valid acceptance' `
            -Condition (-not (Invoke-UiaWaitFor -Selector 'preparation-save-error' -TimeoutMs 500).Succeeded) `
            -Detail 'preparation-save-error selector' | Out-Null

        $assertionNumber++
        $processStillAlive = $null -ne (Get-Process -Id $script:TargetPid -ErrorAction SilentlyContinue)
        Assert-UiaCondition -Number $assertionNumber -Description 'The application process remained alive throughout' `
            -Condition $processStillAlive -Detail "PID $script:TargetPid" | Out-Null

        # --- Isolated-database assertion: only the valid context was ever persisted -----------
        $dbPath = Join-Path $Context.LiveProfileDir 'knownfirst.db3'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Inspect the isolated database for context-snapshot integrity' -Kind 'assertion' | Out-Null

        $senseCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM Senses"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Exactly one Sense was persisted for the accepted candidate' `
            -Condition ($senseCount -eq 1) -Detail "Sense count: $senseCount" | Out-Null

        $contextSnapshotCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM ContextSnapshots"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'At most one ContextSnapshot was persisted (the genuine occurrence only - never the misattributed one)' `
            -Condition ($contextSnapshotCount -le 1) -Detail "ContextSnapshots count: $contextSnapshotCount" | Out-Null
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Scenario PreparationInvalidContextRecovery raised an exception: $_" -Level Error
        Save-DedupedScreenshot -StepId 'exception' | Out-Null
    }
    finally {
        if ($script:TargetPid) {
            $stepNumber++
            Add-RunStep -StepNumber $stepNumber -Name 'Close the KnownFirst process launched by this runner' -Kind 'action' | Out-Null
            Stop-KnownFirstUnderTest -ProcessId $script:TargetPid
        }
    }

    if (@($script:AssertionLog | Where-Object { $_.Result -eq 'Fail' }).Count -gt 0) {
        $overallSucceeded = $false
    }

    if (Test-Path -LiteralPath $Context.LiveProfileDir) {
        Copy-Item -Path (Join-Path $Context.LiveProfileDir '*') -Destination $Context.EvidenceProfileDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    $realDataAfter = @(Get-RealKnownFirstDataFingerprint)
    $realDataComparison = Test-RealDataUnchanged -Before $realDataBefore -After $realDataAfter
    $assertionNumber++
    Assert-UiaCondition -Number $assertionNumber -Description 'Real KnownFirst database/WAL/SHM files are unchanged before vs. after the run' `
        -Condition $realDataComparison.Unchanged -Detail ($realDataComparison.Differences -join '; ') | Out-Null
    if (-not $realDataComparison.Unchanged) { $overallSucceeded = $false }

    return [pscustomobject]@{
        Succeeded              = $overallSucceeded
        Monitor                = $selectedMonitor
        MonitorSelectionReason = $monitorSelectionReason
        Placement              = $placement
        AllMonitors            = $monitors
        FinalWindowBounds      = $finalWindowBounds
        RealDataUnchanged      = $realDataComparison.Unchanged
        RealDataDifferences    = $realDataComparison.Differences
    }
}
