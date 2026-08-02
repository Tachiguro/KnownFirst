<#
.SYNOPSIS
    Scenario PreparationSelectedMeaning (KF-MEANING-002 GUI hardening, Part 3).

.DESCRIPTION
    End-to-end coverage of the actual selected-meaning-only preparation workflow: launch the
    Windows Debug build against an isolated, deterministically-seeded GUI test profile (one
    preparation candidate with three provider meanings, plus a second candidate), open the
    "Other meaning" picker, select a non-default meaning, accept it, and prove both that the UI
    advances cleanly (no blank screen, no ErrorBoundary) and that the isolated database contains
    exactly the durable rows the selected-meaning-only contract requires - never more.

    Seeding is performed by the app itself before first render (KNOWNFIRST_GUI_TEST_SEED_SCENARIO),
    using its own TextReviewService/PreparationService with an in-process deterministic provider -
    never a hand-written database row and never a network call.

    Only Windows UI Automation operations that do not simulate physical mouse/keyboard input are
    used (inspect / search / invoke / set-value / get-value / wait-for / window-only screenshot),
    via the wrappers in GuiTestRunnerCore.ps1.
#>

function Invoke-ScenarioPreparationSelectedMeaning {
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
        $env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO = 'PreparationSelectedMeaning'
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
        Add-RunStep -StepNumber $stepNumber -Name 'Verify GUI TEST PROFILE indicator' -Kind 'assertion' | Out-Null
        Invoke-UiaWaitFor -Selector 'gui-test-profile-indicator' -TimeoutMs 8000 | Out-Null
        $indicatorText = Get-UiaElementText -Selector 'gui-test-profile-indicator'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'GUI TEST PROFILE indicator is visible and shows the isolated profile path' `
            -Condition ($indicatorText -like '*GUI TEST PROFILE*' -and $indicatorText -like "*$($Context.LiveProfileDir)*") `
            -Detail "Indicator text: '$indicatorText'" | Out-Null

        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name "Detect displays and select monitor (MonitorTarget=$MonitorTarget)" -Kind 'action' | Out-Null
        $monitors = @(Get-DisplayMonitors)
        $selection = Select-DisplayMonitor -Monitors $monitors -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        $selectedMonitor = $selection.Monitor
        $monitorSelectionReason = $selection.Reason

        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Place KnownFirst inside the selected monitor working area' -Kind 'action' | Out-Null
        $placement = Move-WindowIntoMonitorWorkingArea -Hwnd $script:TargetHwnd -Monitor $selectedMonitor -AllMonitors $monitors
        $finalWindowBounds = $placement.VisibleBounds
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'KnownFirst window is on the selected monitor and fully contained within its working area' `
            -Condition $placement.Contained -Detail "CorrectivePasses=$($placement.CorrectivePassCount)" | Out-Null
        if (-not $placement.Contained) { $overallSucceeded = $false }

        # --- Navigate to prepare-words -------------------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-prepare-nav'
        Invoke-UiaInvoke -Selector 'nav-prepare-words' | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'prepare-words-page'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to prepare-words' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'Prepare-words page renders the seeded candidate without a blank screen or ErrorBoundary' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null

        # --- Verify the expected candidate is visible ----------------------------------------
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify the expected candidate is visible' -Kind 'assertion' | Out-Null
        $candidateTerm = Get-UiaElementText -Selector 'preparation-candidate-term'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The seeded candidate term is displayed' `
            -Condition (-not [string]::IsNullOrWhiteSpace($candidateTerm)) -Detail "Candidate term: '$candidateTerm'" | Out-Null

        # --- Open "Other meaning" --------------------------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-open-meaning-picker'
        Invoke-UiaInvoke -Selector 'meaning-picker-trigger' | Out-Null
        $dialogWait = Invoke-UiaWaitFor -Selector 'meaning-picker-dialog' -TimeoutMs 5000
        $afterShot = Save-DedupedScreenshot -StepId 'meaning-picker-open'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Open Other meaning' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The meaning picker dialog opened' `
            -Condition $dialogWait.Succeeded -Detail 'meaning-picker-dialog selector' | Out-Null

        # --- Select a non-default meaning (index 1, the second of three) ----------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-select-meaning'
        Invoke-UiaInvoke -Selector 'meaning-option-1' | Out-Null
        $closedWait = Invoke-UiaWaitFor -Selector 'meaning-picker-dialog' -Gone -TimeoutMs 5000
        $afterShot = Save-DedupedScreenshot -StepId 'after-select-meaning'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Select a non-default meaning' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The picker closed and the chosen meaning is applied' `
            -Condition $closedWait.Succeeded -Detail 'meaning-picker-dialog gone' | Out-Null

        # --- Click the actual Accept button -----------------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-accept'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Click Accept' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'preparation-accept-button' | Out-Null

        # --- Verify advance: original candidate gone, next candidate or batch-complete appears
        Start-Sleep -Milliseconds 500
        $afterShot = Save-DedupedScreenshot -StepId 'after-accept'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify workflow advances after acceptance' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'The page remains rendered and interactive after acceptance (no blank screen, no ErrorBoundary)' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null

        $nextCandidateTerm = Get-UiaElementText -Selector 'preparation-candidate-term'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The next candidate differs from the original (the original candidate disappeared)' `
            -Condition ($nextCandidateTerm -ne $candidateTerm) `
            -Detail "Original: '$candidateTerm'; now showing: '$nextCandidateTerm'" | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No save error is shown after a valid acceptance' `
            -Condition (-not (Invoke-UiaWaitFor -Selector 'preparation-save-error' -TimeoutMs 500).Succeeded) `
            -Detail 'preparation-save-error selector' | Out-Null
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Scenario PreparationSelectedMeaning raised an exception: $_" -Level Error
        Save-DedupedScreenshot -StepId 'exception' | Out-Null
    }
    finally {
        if ($script:TargetPid) {
            $stepNumber++
            Add-RunStep -StepNumber $stepNumber -Name 'Close the KnownFirst process launched by this runner' -Kind 'action' | Out-Null
            Stop-KnownFirstUnderTest -ProcessId $script:TargetPid
        }
    }

    # --- Isolated-database assertions (only after the app has released its file handles) -----
    Start-Sleep -Milliseconds 500
    $dbPath = Join-Path $Context.LiveProfileDir 'knownfirst.db3'
    try {
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Inspect the isolated database for durable selected-meaning-only outcomes' -Kind 'assertion' | Out-Null

        # PreparationCandidateStatus.Prepared = 2 (Models/PreparationCandidateStatus.cs).
        $preparedCandidateCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM PreparationCandidates WHERE Status = 2"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'At least one candidate reached Prepared status (2)' `
            -Condition ($preparedCandidateCount -ge 1) -Detail "Prepared candidate count: $preparedCandidateCount" | Out-Null

        $senseCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM Senses"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Exactly one Sense was persisted for the accepted candidate' `
            -Condition ($senseCount -eq 1) -Detail "Sense count: $senseCount" | Out-Null

        $meaningCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM Meanings"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Exactly one Meaning was persisted (the selected meaning only, not all three seeded provider meanings)' `
            -Condition ($meaningCount -eq 1) -Detail "Meaning count: $meaningCount" | Out-Null

        $selectedGuiTestMeaningPersisted = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM Meanings WHERE SelectedMeaningId = 'gui-test-river-edge'"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The persisted Meaning is the one explicitly selected in the picker (index 1: gui-test-river-edge), not the default' `
            -Condition ($selectedGuiTestMeaningPersisted -eq 1) -Detail "Matching rows: $selectedGuiTestMeaningPersisted" | Out-Null

        $cardCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM LearningCards"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Card count matches the configured global direction (default Both -> 2 cards for the one accepted Sense)' `
            -Condition ($cardCount -eq 2) -Detail "LearningCards count: $cardCount" | Out-Null

        $duplicateVariantCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM (SELECT SenseId, AnswerLanguage, NormalizedText FROM AnswerVariants GROUP BY SenseId, AnswerLanguage, NormalizedText HAVING COUNT(*) > 1)"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No duplicate AnswerVariant rows exist' `
            -Condition ($duplicateVariantCount -eq 0) -Detail "Duplicate variant groups: $duplicateVariantCount" | Out-Null

        $duplicateCardCount = Get-GuiTestSqliteScalarInt -DatabasePath $dbPath -NativeLibraryDirectory $nativeLibraryDirectory `
            -Sql "SELECT COUNT(*) FROM (SELECT SenseId, Direction FROM LearningCards GROUP BY SenseId, Direction HAVING COUNT(*) > 1)"
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No duplicate (SenseId, Direction) LearningCards exist' `
            -Condition ($duplicateCardCount -eq 0) -Detail "Duplicate card groups: $duplicateCardCount" | Out-Null
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Isolated-database inspection raised an exception: $_" -Level Error
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
    Assert-UiaCondition -Number $assertionNumber -Description 'Real KnownFirst database/WAL/SHM files are unchanged (hash + timestamp) before vs. after the run' `
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
