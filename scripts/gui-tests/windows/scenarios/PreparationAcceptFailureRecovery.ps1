<#
.SYNOPSIS
    Scenario PreparationAcceptFailureRecovery (KF-MEANING-002 GUI hardening, Part 3).

.DESCRIPTION
    Proves that a deterministic failure inside the Schema-8 acceptance transaction - injected via
    the existing IPreparationFaultInjector seam, after at least one tentative database mutation
    (AfterCardInsert) - rolls back completely, leaves the preparation page usable (inline error,
    no ErrorBoundary, no blank screen, navigation still works, Accept usable again), and that a
    retry (the injector fires only once) then succeeds and advances the workflow.

    Seeding and fault injection are both Debug/GUI-test-only, env-var-gated, and only reachable
    when the isolated GUI test profile is active (GuiTestFaultInjection/GuiTestScenarioSeed).

    Only Windows UI Automation operations that do not simulate physical mouse/keyboard input are
    used, via the wrappers in GuiTestRunnerCore.ps1.
#>

function Invoke-ScenarioPreparationAcceptFailureRecovery {
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
        Add-RunStep -StepNumber $stepNumber -Name 'Start Windows Debug executable with seed scenario and injected fault' -Kind 'action' | Out-Null
        $env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO = 'PreparationAcceptFailureRecovery'
        $env:KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT = 'AfterCardInsert'
        try {
            $launch = Start-KnownFirstUnderTest -ExecutablePath $ExecutablePath -GuiTestRoot $Context.LiveProfileDir
        }
        finally {
            Remove-Item Env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO -ErrorAction SilentlyContinue
            Remove-Item Env:KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT -ErrorAction SilentlyContinue
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

        # --- Navigate to prepare-words and load the seeded candidate --------------------------
        Invoke-UiaInvoke -Selector 'nav-prepare-words' | Out-Null
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to prepare-words' -Kind 'action' | Out-Null
        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'Prepare-words page renders the seeded candidate' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null
        $candidateTerm = Get-UiaElementText -Selector 'preparation-candidate-term'

        # --- Click Accept: the injected fault fires after at least one tentative mutation -----
        $beforeShot = Save-DedupedScreenshot -StepId 'before-accept-with-fault'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Click Accept (fault injected at AfterCardInsert)' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'preparation-accept-button' | Out-Null

        $errorWait = Invoke-UiaWaitFor -Selector 'preparation-save-error' -TimeoutMs 8000
        $afterShot = Save-DedupedScreenshot -StepId 'after-accept-with-fault'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify inline save error and recoverable state' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'A localized inline save error is shown' `
            -Condition $errorWait.Succeeded -Detail 'preparation-save-error selector' | Out-Null

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'The preparation page remains visible (no blank screen, no global ErrorBoundary) after the injected failure' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 5000 | Out-Null

        $sameCandidateStillShown = (Get-UiaElementText -Selector 'preparation-candidate-term') -eq $candidateTerm
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The same candidate is still shown (nothing silently advanced past the failed acceptance)' `
            -Condition $sameCandidateStillShown -Detail "Candidate before: '$candidateTerm'" | Out-Null

        $navHomeInspect = Invoke-UiaInspect -Selector 'nav-home' -Depth 2
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Navigation remains available after the injected failure' `
            -Condition $navHomeInspect.Succeeded -Detail 'nav-home selector' | Out-Null

        $acceptButtonInspect = Invoke-UiaInspect -Selector 'preparation-accept-button' -Depth 2
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The Accept action is present and usable again (not left permanently disabled)' `
            -Condition $acceptButtonInspect.Succeeded -Detail 'preparation-accept-button selector' | Out-Null

        # --- Isolated-database assertions: complete rollback, before the retry ----------------
        $dbPath = Join-Path $Context.LiveProfileDir 'knownfirst.db3'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Inspect the isolated database for complete rollback' -Kind 'assertion' | Out-Null

        $senseCountAfterFailure = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM Senses"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No Sense rows exist after the rolled-back acceptance' `
            -Condition ($senseCountAfterFailure -eq 0) -Detail "Sense count: $senseCountAfterFailure" | Out-Null

        $cardCountAfterFailure = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM LearningCards"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No LearningCards rows exist after the rolled-back acceptance' `
            -Condition ($cardCountAfterFailure -eq 0) -Detail "LearningCards count: $cardCountAfterFailure" | Out-Null

        $variantCountAfterFailure = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM AnswerVariants"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No AnswerVariants rows exist after the rolled-back acceptance' `
            -Condition ($variantCountAfterFailure -eq 0) -Detail "AnswerVariants count: $variantCountAfterFailure" | Out-Null

        $contextSnapshotCountAfterFailure = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM ContextSnapshots"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No ContextSnapshots rows exist after the rolled-back acceptance' `
            -Condition ($contextSnapshotCountAfterFailure -eq 0) -Detail "ContextSnapshots count: $contextSnapshotCountAfterFailure" | Out-Null

        # PRAGMA user_version 8 = Schema 8 (Data/DatabaseSchema.cs). Rollback must never touch it.
        $userVersionAfterFailure = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "PRAGMA user_version"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'PRAGMA user_version remains unchanged (8) after the rolled-back acceptance' `
            -Condition ($userVersionAfterFailure -eq 8) -Detail "user_version: $userVersionAfterFailure" | Out-Null

        # PreparationCandidateStatus.Prepared = 2 (Models/PreparationCandidateStatus.cs); the
        # candidate must not have reached it, so it remains retryable.
        $preparedCandidateCountAfterFailure = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM PreparationCandidates WHERE Status = 2"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No candidate reached Prepared status after the rolled-back acceptance (candidate remains retryable)' `
            -Condition ($preparedCandidateCountAfterFailure -eq 0) -Detail "Prepared candidate count: $preparedCandidateCountAfterFailure" | Out-Null

        # --- Retry: the injector fires only once, so this Accept click must now succeed -------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-retry-accept'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Retry Accept (the injected fault has already fired once and will not fire again)' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'preparation-accept-button' | Out-Null
        Start-Sleep -Milliseconds 500
        $afterShot = Save-DedupedScreenshot -StepId 'after-retry-accept'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify the retry succeeds and advances' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'The page remains rendered after the successful retry (no blank screen, no ErrorBoundary)' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null

        $retryCandidateTerm = Get-UiaElementText -Selector 'preparation-candidate-term'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The retry advanced the workflow past the original candidate' `
            -Condition ($retryCandidateTerm -ne $candidateTerm) `
            -Detail "Original: '$candidateTerm'; now showing: '$retryCandidateTerm'" | Out-Null

        $preparedCandidateCountAfterRetry = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory -Sql "SELECT COUNT(*) FROM PreparationCandidates WHERE Status = 2"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The retried acceptance reached Prepared status durably' `
            -Condition ($preparedCandidateCountAfterRetry -eq 1) -Detail "Prepared candidate count: $preparedCandidateCountAfterRetry" | Out-Null
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Scenario PreparationAcceptFailureRecovery raised an exception: $_" -Level Error
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
