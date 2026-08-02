<#
.SYNOPSIS
    Scenario DiagnosticsPopulated (diagnostics-export-import repair GUI hardening).

.DESCRIPTION
    End-to-end proof that opening Debug diagnostics after a real Schema-8 preparation acceptance
    succeeds and shows the prepared candidate's meaning(s) — the exact vertical this repair fixed
    (Services/TextReviewService.cs CreateDiagnosticMeanings used to deserialize
    PreparationCandidates.ResultJson as a raw LexicalResult, producing Meanings == null for a real
    Schema-8 envelope and throwing an uncaught ArgumentNullException out of GetDiagnosticsAsync).

    Seeding is performed by the app itself before first render (KNOWNFIRST_GUI_TEST_SEED_SCENARIO,
    reusing the existing PreparationSelectedMeaning seed), using its own TextReviewService/
    PreparationService with an in-process deterministic provider - never a hand-written database row
    and never a network call. Acceptance itself happens through the real Accept button, so
    PreparationCandidates.ResultJson genuinely holds a codec-written envelope by the time Diagnostics
    is opened.

    Only Windows UI Automation operations that do not simulate physical mouse/keyboard input are
    used (inspect / search / invoke / set-value / get-value / wait-for / window-only screenshot),
    via the wrappers in GuiTestRunnerCore.ps1.
#>

function Invoke-ScenarioDiagnosticsPopulated {
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

        # --- Navigate to prepare-words and accept the seeded candidate with the real Accept button -----
        $beforeShot = Save-DedupedScreenshot -StepId 'before-prepare-nav'
        Invoke-UiaInvoke -Selector 'nav-prepare-words' | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'prepare-words-page'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to prepare-words' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'Prepare-words page renders the seeded candidate without a blank screen or ErrorBoundary' `
            -RouteMarkerSelector 'preparation-candidate-term' -TransitionTimeoutMs 15000 | Out-Null

        $beforeShot = Save-DedupedScreenshot -StepId 'before-accept'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Click Accept (real Schema-8 preparation acceptance)' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'preparation-accept-button' | Out-Null

        Start-Sleep -Milliseconds 500
        $afterShot = Save-DedupedScreenshot -StepId 'after-accept'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify acceptance succeeded with no inline save error' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'No save error is shown after a valid acceptance' `
            -Condition (-not (Invoke-UiaWaitFor -Selector 'preparation-save-error' -TimeoutMs 500).Succeeded) `
            -Detail 'preparation-save-error selector' | Out-Null

        # --- Navigate to Diagnostics and verify it loads the freshly accepted candidate's meaning -----
        $beforeShot = Save-DedupedScreenshot -StepId 'before-diagnostics-nav'
        Invoke-UiaInvoke -Selector 'nav-diagnostics' | Out-Null
        # The Diagnostics page renders many large debug tables (preparation timings, lexical cache,
        # sessions, candidates, prepared meanings, learning cards, ...) after GetDiagnosticsAsync
        # resolves, so first render is noticeably slower than the single-candidate PrepareWords page.
        $diagnosticsLoadedWait = Invoke-UiaWaitFor -Selector 'diagnostics-loaded' -TimeoutMs 30000
        $afterShot = Save-DedupedScreenshot -StepId 'diagnostics-page'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to Diagnostics' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Diagnostics finished loading successfully (GetDiagnosticsAsync did not throw)' `
            -Condition $diagnosticsLoadedWait.Succeeded -Detail 'diagnostics-loaded selector' | Out-Null
        if (-not $diagnosticsLoadedWait.Succeeded) { $overallSucceeded = $false }

        $assertionNumber++
        Assert-NoBlankOrErrorState -Number $assertionNumber -Description 'Diagnostics page renders without a blank screen or ErrorBoundary' `
            -RouteMarkerSelector 'diagnostics-loaded' -TransitionTimeoutMs 30000 | Out-Null

        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Read the prepared candidate meanings cell' -Kind 'assertion' | Out-Null
        $meaningsText = Get-UiaElementText -Selector 'diagnostics-candidate-meanings'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'The prepared candidate shows a real meaning, not the parse-failure fallback' `
            -Condition (-not [string]::IsNullOrWhiteSpace($meaningsText) -and $meaningsText -notlike '*could not be parsed*') `
            -Detail "Meanings cell text: '$meaningsText'" | Out-Null
        if ([string]::IsNullOrWhiteSpace($meaningsText) -or $meaningsText -like '*could not be parsed*') { $overallSucceeded = $false }
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Scenario DiagnosticsPopulated raised an exception: $_" -Level Error
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
