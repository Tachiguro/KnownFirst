<#
.SYNOPSIS
    Scenario PortableDataExportSelfImport (diagnostics-export-import repair GUI hardening).

.DESCRIPTION
    End-to-end proof, through the real Settings UI, of both:
      1. Data export completes successfully and reports success in the UI, and
      2. Self-importing the exact bytes just exported succeeds (or, since the target profile already
         holds the data that produced the archive, reports the expected merge/no-change outcome) with
         no validation-failure message.

    This is the exact vertical the repair fixed (Services/DataSafety/BackupModelMapper.cs
    ParseLookupDraft used to deserialize PreparationCandidates.ResultJson as a raw LexicalResult,
    producing Meanings == null for a real Schema-8 envelope and being caught as a wrong
    BackupFormatException(DataJsonInvalid) during export).

    The interactive native Save/Open file picker is never automated (that is explicitly out of
    scope): the GUI-test-only KNOWNFIRST_GUI_TEST_ARCHIVE_SEAM marker swaps in
    GuiTestPortableArchiveFileService, which replaces only that picker boundary with a deterministic,
    run-scoped file under the isolated GUI-test profile directory. Every step downstream of that
    boundary - real snapshot capture, the real (now-fixed) external model mapper, the real JSON/ZIP
    archive writer, the real archive validator, and the real import preview/import - is exactly the
    production BackupService, unchanged.

    Seeding (KNOWNFIRST_GUI_TEST_SEED_SCENARIO, reusing the existing PreparationSelectedMeaning seed)
    and the subsequent acceptance (the real Accept button) together produce the same real, codec-
    written Schema-8 accepted candidate DiagnosticsPopulated.ps1 exercises.

    Only Windows UI Automation operations that do not simulate physical mouse/keyboard input are
    used (inspect / search / invoke / set-value / get-value / wait-for / window-only screenshot),
    via the wrappers in GuiTestRunnerCore.ps1.
#>

function Invoke-ScenarioPortableDataExportSelfImport {
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

    try {
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Fingerprint real KnownFirst data files (before)' -Kind 'guard' | Out-Null
        $realDataBefore = @(Get-RealKnownFirstDataFingerprint)

        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Start Windows Debug executable with seed scenario and archive seam active' -Kind 'action' | Out-Null
        $env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO = 'PreparationSelectedMeaning'
        $env:KNOWNFIRST_GUI_TEST_ARCHIVE_SEAM = '1'
        try {
            $launch = Start-KnownFirstUnderTest -ExecutablePath $ExecutablePath -GuiTestRoot $Context.LiveProfileDir
        }
        finally {
            Remove-Item Env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO -ErrorAction SilentlyContinue
            Remove-Item Env:KNOWNFIRST_GUI_TEST_ARCHIVE_SEAM -ErrorAction SilentlyContinue
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

        # --- Navigate to prepare-words and accept the seeded candidate with the real Accept button -----
        Invoke-UiaInvoke -Selector 'nav-prepare-words' | Out-Null
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to prepare-words' -Kind 'action' | Out-Null
        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'Prepare-words page renders the seeded candidate without a blank screen or ErrorBoundary' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null

        # The PreparationSelectedMeaning seed starts a two-candidate AutomaticOnline batch. Both
        # candidates are accepted here (not just the first) so no preparation workflow remains
        # active by the time Settings is reached: an active workflow legitimately blocks a merge
        # import (MergePreflightStatus.BlockedByActiveWorkflow) - a genuine, separate business rule
        # this scenario must not trip over while proving the self-import path itself.
        $batchComplete = $false
        for ($candidateIndex = 0; $candidateIndex -lt 2 -and -not $batchComplete; $candidateIndex++) {
            $stepNumber++
            Add-RunStep -StepNumber $stepNumber -Name "Click Accept for candidate $($candidateIndex + 1) of 2 (real Schema-8 preparation acceptance)" -Kind 'action' | Out-Null
            Invoke-UiaInvoke -Selector 'preparation-accept-button' | Out-Null
            Start-Sleep -Milliseconds 500
            $assertionNumber++
            Assert-UiaCondition -Number $assertionNumber -Description "No save error is shown after accepting candidate $($candidateIndex + 1)" `
                -Condition (-not (Invoke-UiaWaitFor -Selector 'preparation-save-error' -TimeoutMs 500).Succeeded) `
                -Detail 'preparation-save-error selector' | Out-Null

            $batchComplete = (Invoke-UiaWaitFor -Selector 'preparation-batch-complete' -TimeoutMs 8000).Succeeded
        }
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The seeded batch finished (no preparation workflow remains active) before export' `
            -Condition $batchComplete -Detail 'preparation-batch-complete selector' | Out-Null
        if (-not $batchComplete) { $overallSucceeded = $false }

        # --- Navigate to Settings and export -----------------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-settings-nav'
        Invoke-UiaInvoke -Selector 'nav-settings' | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'settings-page'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to Settings' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'Settings page renders without a blank screen or ErrorBoundary' `
            -RouteMarkerSelector 'portable-data-export-button' -TransitionTimeoutMs 10000 | Out-Null

        $beforeShot = Save-DedupedScreenshot -StepId 'before-export'
        Invoke-UiaInvoke -Selector 'portable-data-export-button' | Out-Null
        $exportSuccessWait = Invoke-UiaWaitFor -Selector 'portable-data-result-message-success' -TimeoutMs 15000
        $afterShot = Save-DedupedScreenshot -StepId 'after-export'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Click Data export' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Export completed successfully and reported success in the UI' `
            -Condition $exportSuccessWait.Succeeded -Detail 'portable-data-result-message-success selector' | Out-Null
        if (-not $exportSuccessWait.Succeeded) { $overallSucceeded = $false }

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No export error message is shown' `
            -Condition (-not (Invoke-UiaWaitFor -Selector 'portable-data-result-message-error' -TimeoutMs 500).Succeeded) `
            -Detail 'portable-data-result-message-error selector' | Out-Null

        # --- Self-import the exact bytes just exported -------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-import'
        Invoke-UiaInvoke -Selector 'portable-data-import-button' | Out-Null

        # PreviewPortableImportAsync runs a real merge preflight against the target's current
        # state, which is measurably slower than the simple button clicks above.
        $previewWait = Invoke-UiaWaitFor -Selector 'portable-import-preview-panel' -TimeoutMs 20000
        $importFailureWait = Invoke-UiaWaitFor -Selector 'portable-data-result-message-error' -TimeoutMs 500
        $afterShot = Save-DedupedScreenshot -StepId 'after-import-pick'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Click Data import (self-import of the exact exported bytes)' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No validation-failure message is shown for the self-import' `
            -Condition (-not $importFailureWait.Succeeded) -Detail 'portable-data-result-message-error selector' | Out-Null
        if ($importFailureWait.Succeeded) { $overallSucceeded = $false }

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The import preview appeared for the self-imported archive (restore-into-empty, merge-changes, or merge-no-change - never a validation failure)' `
            -Condition $previewWait.Succeeded -Detail 'portable-import-preview-panel selector' | Out-Null
        if (-not $previewWait.Succeeded) { $overallSucceeded = $false }

        if ($previewWait.Succeeded) {
            # The target profile already holds every record the archive contains (true self-import),
            # so a Confirm button may or may not be offered depending on the exact preflight
            # disposition (MergeNoChange offers only Close). Either affordance is an accepted, valid
            # outcome; the confirm branch is exercised whenever it is offered, proving the confirm
            # path itself completes cleanly too.
            $confirmWait = Invoke-UiaWaitFor -Selector 'portable-import-confirm-button' -TimeoutMs 1000
            if ($confirmWait.Succeeded) {
                $beforeShot = Save-DedupedScreenshot -StepId 'before-import-confirm'
                Invoke-UiaInvoke -Selector 'portable-import-confirm-button' | Out-Null
                $resultWait = Invoke-UiaWaitFor -Selector 'portable-data-result-message-success' -TimeoutMs 15000
                $afterShot = Save-DedupedScreenshot -StepId 'after-import-confirm'
                $stepNumber++
                Add-RunStep -StepNumber $stepNumber -Name 'Confirm the import' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

                $assertionNumber++
                Assert-UiaCondition -Number $assertionNumber -Description 'The confirmed self-import completed successfully' `
                    -Condition $resultWait.Succeeded -Detail 'portable-data-result-message-success selector' | Out-Null
                if (-not $resultWait.Succeeded) { $overallSucceeded = $false }
            }
            else {
                $stepNumber++
                Add-RunStep -StepNumber $stepNumber -Name 'Close the no-change import preview (no confirm offered)' -Kind 'action' | Out-Null
                Invoke-UiaInvoke -Selector 'portable-import-cancel-button' -AllowFailure | Out-Null
            }
        }
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Scenario PortableDataExportSelfImport raised an exception: $_" -Level Error
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
