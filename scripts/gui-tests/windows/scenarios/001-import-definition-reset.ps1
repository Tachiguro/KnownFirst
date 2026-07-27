<#
.SYNOPSIS
    Scenario 001: import-definition-reset.

.DESCRIPTION
    End-to-end Phase 1 scenario: launch the Windows Debug build against an isolated GUI test
    profile, verify build identity, select and place the window on the monitor requested via
    -MonitorTarget (default: the internal laptop display, identified via Windows display
    configuration), import a short German text in Definition mode, confirm the review workflow
    advanced, then reset all application data from Settings and confirm Home returns to the
    empty state.

    Only Windows UI Automation operations that do not simulate physical mouse/keyboard input
    are used (inspect / search / invoke / set-value / get-value / wait-for / window-only
    screenshot), via the wrappers in GuiTestRunnerCore.ps1.
#>

function Invoke-Scenario001 {
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
    $germanSourceOptionName = $null
    $definitionModeOptionName = $null

    try {
        # --- Real-data guard: capture fingerprints before anything else happens -----------
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Fingerprint real KnownFirst data files (before)' -Kind 'guard' | Out-Null
        $realDataBefore = @(Get-RealKnownFirstDataFingerprint)
        Write-RunnerLog "Real-data fingerprint (before): $($realDataBefore.Count) file(s) found."
        foreach ($f in $realDataBefore) { Write-RunnerLog "  $($f.Path) sha256=$($f.Sha256) mtime=$($f.LastWriteTimeUtc)" -Level Trace }

        # --- Step 1: start the existing Windows Debug executable --------------------------
        # No "before" screenshot is possible here: no KnownFirst window exists yet to capture.
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Start Windows Debug executable' -Kind 'action' | Out-Null
        $launch = Start-KnownFirstUnderTest -ExecutablePath $ExecutablePath -GuiTestRoot $Context.LiveProfileDir
        Start-Sleep -Milliseconds 1500

        # First-run "What's New" modal is expected on a brand-new isolated profile; dismiss
        # it if present so it does not block the rest of the scenario.
        $modalWait = Invoke-UiaWaitFor -Selector 'whats-new-modal' -TimeoutMs 4000
        if ($modalWait.Succeeded) {
            Write-RunnerLog "First-run What's New modal detected; dismissing." -Level Info
            Invoke-UiaInvoke -Selector 'whats-new-close-button' -AllowFailure | Out-Null
            Invoke-UiaWaitFor -Selector 'whats-new-modal' -Gone -TimeoutMs 4000 | Out-Null
        }

        # --- Step 2: verify the GUI TEST PROFILE indicator ---------------------------------
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify GUI TEST PROFILE indicator' -Kind 'assertion' | Out-Null
        Invoke-UiaWaitFor -Selector 'gui-test-profile-indicator' -TimeoutMs 8000 | Out-Null
        $indicatorText = Get-UiaElementText -Selector 'gui-test-profile-indicator'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'GUI TEST PROFILE indicator is visible and shows the isolated profile path' `
            -Condition ($indicatorText -like '*GUI TEST PROFILE*' -and $indicatorText -like "*$($Context.LiveProfileDir)*") `
            -Detail "Indicator text: '$indicatorText'" | Out-Null

        # --- Step 3: verify product version, build number, Debug configuration, commit ----
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify build identity (version/build/configuration/commit)' -Kind 'assertion' | Out-Null
        $buildIdentityText = Get-UiaElementText -Selector 'build-identity-text'
        Write-RunnerLog "Build identity text: $buildIdentityText"

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Product version is 1.0.0-beta.12' `
            -Condition ($buildIdentityText -like '*1.0.0-beta.12*') -Detail $buildIdentityText | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Configuration is Debug' `
            -Condition ($buildIdentityText -like '*Debug*') -Detail $buildIdentityText | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description "Commit matches HEAD short hash ($($Context.ShortCommit))" `
            -Condition ($buildIdentityText -like "*$($Context.ShortCommit)*") -Detail $buildIdentityText | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Build number 12 is displayed (authoritative KnownFirstBuildNumber)' `
            -Condition ($buildIdentityText -like '*Build 12*') -Detail $buildIdentityText | Out-Null

        # --- Step 4: detect displays and select the requested monitor ---------------------
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name "Detect displays and select monitor (MonitorTarget=$MonitorTarget)" -Kind 'action' | Out-Null
        $monitors = @(Get-DisplayMonitors)
        Write-RunnerLog 'Detected displays (identity from Windows display configuration):'
        Write-RunnerLog (Format-DisplayMonitorTable -Monitors $monitors)

        $selection = Select-DisplayMonitor -Monitors $monitors -MonitorTarget $MonitorTarget -MonitorDeviceName $MonitorDeviceName
        $selectedMonitor = $selection.Monitor
        $monitorSelectionReason = $selection.Reason
        Write-RunnerLog ("Selected monitor: '{0}' ({1}) Internal={2} Primary={3} Technology={4} WorkArea=({5},{6} {7}x{8}). Reason: {9}" -f `
            $selectedMonitor.FriendlyName, $selectedMonitor.DeviceName, $selectedMonitor.IsInternalCandidate, $selectedMonitor.IsPrimary, `
            $selectedMonitor.OutputTechnology, $selectedMonitor.WorkingArea.X, $selectedMonitor.WorkingArea.Y, `
            $selectedMonitor.WorkingArea.Width, $selectedMonitor.WorkingArea.Height, $monitorSelectionReason)

        # --- Step 5: place KnownFirst exactly inside the selected working area (no maximize) -
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Place KnownFirst inside the selected monitor working area' -Kind 'action' | Out-Null
        $placement = Move-WindowIntoMonitorWorkingArea -Hwnd $script:TargetHwnd -Monitor $selectedMonitor -AllMonitors $monitors
        $finalWindowBounds = $placement.VisibleBounds
        Write-RunnerLog ("KnownFirst bounds before staging: ({0},{1})-({2},{3})" -f `
            $placement.BeforeBounds.Left, $placement.BeforeBounds.Top, $placement.BeforeBounds.Right, $placement.BeforeBounds.Bottom)
        Write-RunnerLog ("KnownFirst bounds after placement (visible): ({0},{1})-({2},{3}) = {4}x{5}" -f `
            $finalWindowBounds.Left, $finalWindowBounds.Top, $finalWindowBounds.Right, $finalWindowBounds.Bottom, `
            $finalWindowBounds.Width, $finalWindowBounds.Height)
        Write-RunnerLog "Corrective passes used: $($placement.CorrectivePassCount); MonitorFromWindow matches selected monitor: $($placement.MonitorFromWindowMatchesSelected); containment: $($placement.Contained)"

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'KnownFirst window is on the selected monitor (MonitorFromWindow) and fully contained within its working area (no part crosses onto another display)' `
            -Condition $placement.Contained `
            -Detail ("WorkArea=({0},{1})-({2},{3}) VisibleBounds=({4},{5})-({6},{7}) MonitorFromWindowMatch={8} CorrectivePasses={9}" -f `
                $placement.WorkingArea.X, $placement.WorkingArea.Y, `
                ($placement.WorkingArea.X + $placement.WorkingArea.Width), ($placement.WorkingArea.Y + $placement.WorkingArea.Height), `
                $finalWindowBounds.Left, $finalWindowBounds.Top, $finalWindowBounds.Right, $finalWindowBounds.Bottom, `
                $placement.MonitorFromWindowMatchesSelected, $placement.CorrectivePassCount) | Out-Null
        if (-not $placement.Contained) {
            $overallSucceeded = $false
        }

        # --- Step 6: capture Home ----------------------------------------------------------
        $homeShot = Save-DedupedScreenshot -StepId 'home'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Capture Home' -Kind 'capture' -AfterScreenshot $homeShot | Out-Null

        # --- Step 7: navigate to Import Text -----------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-import-text-nav'
        Invoke-UiaInvoke -Selector 'nav-import-text' | Out-Null
        Invoke-UiaWaitFor -Selector 'document-title' -TimeoutMs 8000 | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'import-text-page'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to Import Text' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        # --- Step 8: source languages contain English and German, not Russian -------------
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Enumerate source-language options' -Kind 'assertion' | Out-Null
        $sourceOptions = @(Get-ComboBoxOptions -ComboBoxId 'text-language')
        $sourceNames = @($sourceOptions | ForEach-Object { $_.Name })
        Save-UiaDump -Name 'source-language-options' -Content ($sourceNames -join "`r`n") | Out-Null
        Write-RunnerLog "Source language options: $($sourceNames -join ', ')"

        $hasEnglish = @($sourceNames | Where-Object { $_ -in @('English', 'Englisch') }).Count -gt 0
        $hasGerman = @($sourceNames | Where-Object { $_ -in @('German', 'Deutsch') }).Count -gt 0
        $hasRussian = @($sourceNames | Where-Object { $_ -in @('Russian', 'Russisch') }).Count -gt 0
        $germanSourceOptionName = $sourceNames | Where-Object { $_ -in @('German', 'Deutsch') } | Select-Object -First 1

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Source languages contain English and German' `
            -Condition ($hasEnglish -and $hasGerman) -Detail "Options: $($sourceNames -join ', ')" | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Source languages do not contain Russian' `
            -Condition (-not $hasRussian) -Detail "Options: $($sourceNames -join ', ')" | Out-Null

        # --- Step 9: lookup modes contain Definition and Translation, not the combined mode
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Enumerate lookup-mode options' -Kind 'assertion' | Out-Null
        $modeOptions = @(Get-ComboBoxOptions -ComboBoxId 'lookup-mode')
        $modeNames = @($modeOptions | ForEach-Object { $_.Name })
        Save-UiaDump -Name 'lookup-mode-options' -Content ($modeNames -join "`r`n") | Out-Null
        Write-RunnerLog "Lookup mode options: $($modeNames -join ', ')"

        # "Translation" is compared by elimination (present, not equal to "Definition") rather
        # than a hardcoded localized literal, since the accented German text (Uebersetzung)
        # cannot be embedded reliably in a BOM-less .ps1 file under Windows PowerShell 5.1.
        $hasDefinition = @($modeNames | Where-Object { $_ -eq 'Definition' }).Count -gt 0
        $hasTranslation = @($modeNames | Where-Object { $_ -ne 'Definition' }).Count -eq 1
        $definitionModeOptionName = $modeNames | Where-Object { $_ -eq 'Definition' } | Select-Object -First 1

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Lookup modes contain Definition and Translation' `
            -Condition ($hasDefinition -and $hasTranslation) -Detail "Options: $($modeNames -join ', ')" | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Lookup modes do not include a combined Definition+Translation option (exactly 2 options)' `
            -Condition ($modeNames.Count -eq 2) -Detail "Options: $($modeNames -join ', ')" | Out-Null

        # --- Step 10: set title, text, source, mode -----------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-set-fields'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Set document title, content, source language, and lookup mode' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null

        Invoke-UiaSetValue -Selector 'document-title' -Value 'GUI Smoke Definition DE' | Out-Null
        Invoke-UiaSetValue -Selector 'document-content' -Value 'Hallo Welt. Sicherheit ist wichtig.' | Out-Null
        if (-not $germanSourceOptionName) {
            throw 'Could not determine the localized option text for German; cannot set source language.'
        }
        Select-ComboBoxOption -ComboBoxId 'text-language' -OptionText $germanSourceOptionName | Out-Null
        if ($definitionModeOptionName) {
            Select-ComboBoxOption -ComboBoxId 'lookup-mode' -OptionText $definitionModeOptionName | Out-Null
        }

        $afterShot = Save-DedupedScreenshot -StepId 'after-set-fields'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Fields set (result)' -Kind 'action' -AfterScreenshot $afterShot | Out-Null

        $titleValue = Invoke-UiaGetValue -Selector 'document-title' -AllowFailure
        $contentValue = Invoke-UiaGetValue -Selector 'document-content' -AllowFailure
        $sourceValue = Invoke-UiaGetValue -Selector 'text-language' -AllowFailure
        $modeValue = Invoke-UiaGetValue -Selector 'lookup-mode' -AllowFailure

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Document title was set correctly' `
            -Condition ($titleValue -eq 'GUI Smoke Definition DE') -Detail "Actual: '$titleValue'" | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Document content was set correctly' `
            -Condition ($contentValue -eq 'Hallo Welt. Sicherheit ist wichtig.') -Detail "Actual: '$contentValue'" | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Source language is German' `
            -Condition ($sourceValue -eq $germanSourceOptionName) -Detail "Actual: '$sourceValue'" | Out-Null

        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Lookup mode is Definition' `
            -Condition ($modeValue -eq $definitionModeOptionName) -Detail "Actual: '$modeValue'" | Out-Null

        # --- Step 11: target-language control is absent -------------------------------------
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify target-language control is absent for Definition mode' -Kind 'assertion' | Out-Null
        $targetGone = Invoke-UiaWaitFor -Selector 'target-language' -Gone -TimeoutMs 2000
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Target-language control is absent while lookup mode is Definition' `
            -Condition $targetGone.Succeeded -Detail 'target-language selector' | Out-Null

        # --- Step 12: invoke Save and Analyze ------------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-save-and-analyze'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Invoke Save and Analyze' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'save-and-analyze-button' | Out-Null

        # --- Step 13: verify import completed and workflow/navigation state changed --------
        $reviewWait = Invoke-UiaWaitFor -Selector 'review-card' -TimeoutMs 20000
        $afterShot = Save-DedupedScreenshot -StepId 'after-save-and-analyze'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Import completes and navigates to the review workflow' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Import completed and navigation advanced to the review workflow (review-card present)' `
            -Condition $reviewWait.Succeeded -Detail 'review-card selector' | Out-Null

        # --- Step 14: navigate to Settings ---------------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-settings-nav'
        Invoke-UiaInvoke -Selector 'nav-settings' | Out-Null
        Invoke-UiaWaitFor -Selector 'reset-data-button' -TimeoutMs 8000 | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'settings-page'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to Settings' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        # --- Step 15: open the reset confirmation --------------------------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-reset-open'
        Invoke-UiaInvoke -Selector 'reset-data-button' | Out-Null
        $confirmWait = Invoke-UiaWaitFor -Selector 'reset-confirm-confirm-button' -TimeoutMs 5000
        $afterShot = Save-DedupedScreenshot -StepId 'reset-confirmation-open'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Open the reset confirmation' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Reset confirmation dialog opened' `
            -Condition $confirmWait.Succeeded -Detail 'reset-confirm-confirm-button selector' | Out-Null

        # --- Step 16: confirm reset (isolated profile is active) ----------------------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-reset-confirm'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Confirm reset (isolated profile active)' -Kind 'action' -BeforeScreenshot $beforeShot | Out-Null
        Invoke-UiaInvoke -Selector 'reset-confirm-confirm-button' | Out-Null

        # --- Step 17: verify the confirmation closes -----------------------------------------
        $closedWait = Invoke-UiaWaitFor -Selector 'reset-confirm-confirm-button' -Gone -TimeoutMs 8000
        $afterShot = Save-DedupedScreenshot -StepId 'after-reset-confirm'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Reset confirmation closes' -Kind 'assertion' -AfterScreenshot $afterShot | Out-Null
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Reset confirmation dialog closed after confirming' `
            -Condition $closedWait.Succeeded -Detail 'reset-confirm-confirm-button gone' | Out-Null

        # --- Step 18: verify Home/navigation returns to the empty workflow state ------------
        $beforeShot = Save-DedupedScreenshot -StepId 'before-home-after-reset'
        Invoke-UiaInvoke -Selector 'nav-home' | Out-Null
        Invoke-UiaWaitFor -Selector 'stat-document-count' -TimeoutMs 8000 | Out-Null
        $afterShot = Save-DedupedScreenshot -StepId 'home-after-reset'
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Navigate to Home after reset' -Kind 'action' -BeforeScreenshot $beforeShot -AfterScreenshot $afterShot | Out-Null

        $documentCount = Get-UiaElementText -Selector 'stat-document-count'
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Home statistics show zero imported documents after reset' `
            -Condition ($documentCount -eq '0') -Detail "stat-document-count = '$documentCount'" | Out-Null

        $navImportInspect = Invoke-UiaInspect -Selector 'nav-import-text' -Depth 2
        $navImportType = $null
        if ($navImportInspect.Succeeded) {
            try {
                $parsed = $navImportInspect.Output | Out-String | ConvertFrom-Json
                $elements = @($parsed.windows[0].elements)
                $element = $elements | Where-Object { (Get-JsonProperty $_ 'automationId') -eq 'nav-import-text' } | Select-Object -First 1
                if ($element) { $navImportType = Get-JsonProperty $element 'type' }
            }
            catch { Write-RunnerLog "Failed to parse nav-import-text type: $_" -Level Warn }
        }
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Import Text navigation is enabled (Hyperlink, not disabled text) after reset' `
            -Condition ($navImportType -eq 'Hyperlink') -Detail "nav-import-text control type: $navImportType" | Out-Null

        # --- Step 19: verify all test database files remain below the test profile root ----
        $stepNumber++
        Add-RunStep -StepNumber $stepNumber -Name 'Verify test database files remain under the isolated profile root' -Kind 'assertion' | Out-Null
        $isolatedDbPath = Join-Path $Context.LiveProfileDir 'knownfirst.db3'
        $isolatedFiles = Get-ChildItem -LiteralPath $Context.LiveProfileDir -File -ErrorAction SilentlyContinue
        $allUnderRoot = @($isolatedFiles | Where-Object { -not $_.FullName.StartsWith($Context.LiveProfileDir, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0
        $assertionNumber++
        Assert-UiaCondition -Number $assertionNumber -Description 'Isolated database exists under the test profile root and no test file escaped it' `
            -Condition ((Test-Path -LiteralPath $isolatedDbPath) -and $allUnderRoot) `
            -Detail "Profile root: $($Context.LiveProfileDir); files: $(@($isolatedFiles | ForEach-Object { $_.Name }) -join ', ')" | Out-Null

        # Step 20 (real-data comparison) runs after process close in the finally block below,
        # since it must observe the file system after KnownFirst.exe releases its handles.
        # Step 21 (close the process) is also in the finally block so it always runs.
    }
    catch {
        $overallSucceeded = $false
        Write-RunnerLog "Scenario 001 raised an exception: $_" -Level Error
        Save-DedupedScreenshot -StepId 'exception' | Out-Null
    }
    finally {
        # --- Step 21: close only the process launched by this runner -----------------------
        if ($script:TargetPid) {
            $stepNumber++
            Add-RunStep -StepNumber $stepNumber -Name 'Close the KnownFirst process launched by this runner' -Kind 'action' | Out-Null
            Stop-KnownFirstUnderTest -ProcessId $script:TargetPid
        }
    }

    if (@($script:AssertionLog | Where-Object { $_.Result -eq 'Fail' }).Count -gt 0) {
        $overallSucceeded = $false
    }

    # Archive the isolated profile's final contents into the evidence directory.
    if (Test-Path -LiteralPath $Context.LiveProfileDir) {
        Copy-Item -Path (Join-Path $Context.LiveProfileDir '*') -Destination $Context.EvidenceProfileDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # --- Step 20: real-data hashes/timestamps unchanged (checked after process exit) -------
    Start-Sleep -Milliseconds 500
    $realDataAfter = @(Get-RealKnownFirstDataFingerprint)
    $realDataComparison = Test-RealDataUnchanged -Before $realDataBefore -After $realDataAfter
    $assertionNumber++
    Assert-UiaCondition -Number $assertionNumber -Description 'Real KnownFirst database/WAL/SHM files are unchanged (hash + timestamp) before vs. after the run' `
        -Condition $realDataComparison.Unchanged -Detail ($realDataComparison.Differences -join '; ') | Out-Null
    if (-not $realDataComparison.Unchanged) {
        $overallSucceeded = $false
    }

    return [pscustomobject]@{
        Succeeded               = $overallSucceeded
        Monitor                 = $selectedMonitor
        MonitorSelectionReason  = $monitorSelectionReason
        Placement                = $placement
        AllMonitors             = $monitors
        FinalWindowBounds       = $finalWindowBounds
        RealDataUnchanged       = $realDataComparison.Unchanged
        RealDataDifferences     = $realDataComparison.Differences
    }
}
