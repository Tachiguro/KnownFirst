namespace KnownFirst.Tests;

[TestClass]
public sealed class UiWorkflowContractTests
{
    [TestMethod]
    public void VocabularyReview_VisibleMarkupOffersKnownAndUnknownButNotIgnore()
    {
        var markup = LoadUi("ReviewWords.razor");
        Assert.Contains("WordStatus.Known", markup);
        Assert.Contains("WordStatus.UnknownBacklog", markup);
        Assert.IsFalse(markup.Contains("WordStatus.Ignored", StringComparison.Ordinal));
    }

    [TestMethod]
    public void VocabularyReview_NavigatesToPreparationIfUnknownWordsExistRegardlessOfStudyState()
    {
        var markup = LoadUi("ReviewWords.razor");
        Assert.Contains("Navigation.NavigateTo(\"prepare-words\")", markup);
        Assert.DoesNotContain("Navigation.NavigateTo(workflow.CanLearn ? string.Empty : \"prepare-words\")", markup);
        Assert.DoesNotContain("WorkflowState.GetSnapshotAsync()", markup);
    }

    [TestMethod]
    public void PrimaryNavigation_UsesLearnPrepareImportSettingsOrder()
    {
        var markup = LoadUi("NavMenu.razor");
        var learn = markup.IndexOf("href=\"learn\"", StringComparison.Ordinal);
        var prepare = markup.IndexOf("href=\"prepare-words\"", StringComparison.Ordinal);
        var import = markup.IndexOf("href=\"import-text\"", StringComparison.Ordinal);
        var settings = markup.IndexOf("href=\"settings\"", StringComparison.Ordinal);
        Assert.IsTrue(learn >= 0 && learn < prepare && prepare < import && import < settings);
    }

    [TestMethod]
    public void PrimaryNavigation_ExposesStatefulPrepareButNotReview()
    {
        var markup = LoadUi("NavMenu.razor");
        Assert.IsFalse(markup.Contains("href=\"review-words\"", StringComparison.Ordinal));
        Assert.Contains("href=\"prepare-words\"", markup);
        Assert.Contains("Navigation_PrepareBlockedByReview", markup);
        Assert.Contains("Navigation_PrepareUnavailable", markup);
        Assert.Contains("Home_ContinuePreparation", markup);
    }

    [TestMethod]
    public void PrimaryNavigation_SettingsIsAlwaysRenderedAndDisabledLearnExplainsWhy()
    {
        var markup = LoadUi("NavMenu.razor");
        Assert.Contains("href=\"settings\"", markup);
        Assert.Contains("Navigation_LearnUnavailable", markup);
        Assert.Contains("Navigation_ImportBlockedByReview", markup);
    }

    [TestMethod]
    public void Settings_WhenReviewIsActiveOffersReturnToActiveReview()
    {
        var markup = LoadUi("Settings.razor");
        Assert.Contains("if (_hasActiveReview)", markup);
        Assert.Contains("BackHref=\"@(_hasActiveReview ? \"review-words\" : string.Empty)\"", markup);
        Assert.Contains("href=\"review-words\"", markup);
        Assert.Contains("Settings_ReturnToReview", markup);
    }

    [TestMethod]
    public void Learning_AnswerIsConditionalAndNoSkipActionExists()
    {
        var markup = LoadUi("Learn.razor");
        var hiddenCondition = markup.IndexOf("if (_card.AnswerRevealed)", StringComparison.Ordinal);
        var answer = markup.IndexOf("<AnswerView", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, hiddenCondition);
        Assert.IsGreaterThan(hiddenCondition, answer);
        Assert.Contains("Learn_AcceptedAlias", markup);
        Assert.DoesNotContain("Skip", markup, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Learning_AnswerDisplaysAcronymExpansionBeforeTranslationAndDefinition()
    {
        var markup = LoadUi("AnswerView.razor");
        var acronym = markup.IndexOf("Card.AcronymExpansion", StringComparison.Ordinal);
        var translation = markup.IndexOf("Card.Translation", StringComparison.Ordinal);
        var definition = markup.IndexOf("Card.Definition", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, acronym);
        Assert.IsGreaterThan(acronym, translation);
        Assert.IsGreaterThan(translation, definition);
    }

    [TestMethod]
    public void Preparation_UsesConsentDisclosureAndHasNoApiKeyConfiguration()
    {
        var preparation = LoadUi("PrepareWords.razor");
        var settings = LoadUi("Settings.razor");

        Assert.Contains("Prepare_OnlineDisclosure", preparation);
        Assert.Contains("ConfirmOnlineLookupAsync", preparation);
        Assert.DoesNotContain("API key", preparation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API key", settings, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Preparation_MeaningPickerIsBoundedResponsiveAndDoesNotUseNativeSelect()
    {
        var markup = LoadUi("PrepareWords.razor");
        var styles = LoadUi("PrepareWords.razor.css");

        Assert.DoesNotContain("<select", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role=\"dialog\"", markup);
        Assert.Contains("role=\"listbox\"", markup);
        Assert.Contains("MeaningPreviewPolicy.CreateClosedPreview", markup);
        Assert.Contains("MeaningPreviewPolicy.GetSelectableMeanings", markup);
        Assert.Contains("selectableMeaning.IsTruncated", markup);
        Assert.Contains("Escape", markup);
        Assert.Contains("RegisterDismissibleOverlay", markup);
        Assert.Contains("focusElement", markup);
        Assert.Contains("max-width: 100%", styles);
        Assert.Contains("min-width: 0", styles);
        Assert.Contains("overflow-wrap: break-word;", styles);
        Assert.Contains("-webkit-line-clamp: 2", styles);
        Assert.Contains("env(safe-area-inset-bottom)", styles);
    }

    [TestMethod]
    public void Preparation_MeaningSelectionUpdatesTheResultWhileClosingPreservesIt()
    {
        var markup = LoadUi("PrepareWords.razor");
        var closeStart = markup.IndexOf("private async Task CloseMeaningPickerAsync()", StringComparison.Ordinal);
        var closeEnd = markup.IndexOf("private Task HandleMeaningPickerKeyDown", closeStart, StringComparison.Ordinal);
        var selectStart = markup.IndexOf("private async Task SelectMeaningAsync(int meaningIndex)", StringComparison.Ordinal);
        var selectEnd = markup.IndexOf("private async Task AcceptAsync()", selectStart, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, closeStart);
        Assert.IsGreaterThan(closeStart, closeEnd);
        Assert.IsGreaterThanOrEqualTo(0, selectStart);
        Assert.IsGreaterThan(selectStart, selectEnd);

        var closeMethod = markup[closeStart..closeEnd];
        var selectMethod = markup[selectStart..selectEnd];
        Assert.DoesNotContain("ApplyMeaning", closeMethod);
        Assert.DoesNotContain("SelectMeaningAsync", closeMethod);
        Assert.Contains("_selectedMeaningIndex = meaningIndex", selectMethod);
        Assert.Contains("PreparationService.SelectMeaningAsync", selectMethod);
        Assert.Contains("ApplyMeaning(_item.Result.Meanings[meaningIndex])", selectMethod);
        Assert.Contains("CloseMeaningPickerAsync", selectMethod);
    }

    [TestMethod]
    public void Preparation_ActionsRequireConfirmationAndRetryIsConditional()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("ShowMarkKnownConfirmation", markup);
        Assert.Contains("ShowExcludeConfirmation", markup);
        Assert.Contains("Prepare_MarkKnownConfirmation", markup);
        Assert.Contains("Prepare_DoNotLearnConfirmation", markup);
        Assert.Contains("PreparationService.MarkKnownAsync", markup);
        Assert.Contains("PreparationService.ExcludeAsync", markup);
        Assert.Contains("PreparationService.SkipAsync", markup);
        Assert.Contains("if (CanRetryLookup)", markup);
        Assert.Contains("LexicalLookupOutcomePolicy.CanRetry", markup);
        Assert.Contains("CanRetry(result.Status, result.ErrorCode)", markup);
        Assert.DoesNotContain("Search another source", markup, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Preparation_MapsLexicalErrorCodesToLocalizedUserMessages()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("missing-page", markup);
        Assert.Contains("definition-not-found", markup);
        Assert.Contains("translation-not-found", markup);
        Assert.Contains("language-section-not-found", markup);
        Assert.Contains("network-unavailable", markup);
        Assert.Contains("malformed-json", markup);
        Assert.Contains("Prepare_DictionaryEntryNotFound", markup);
        Assert.Contains("Prepare_DefinitionNotFound", markup);
        Assert.Contains("Prepare_TranslationNotFound", markup);
        Assert.Contains("Prepare_LanguageSectionNotFound", markup);
        Assert.Contains("Prepare_NetworkFailure", markup);
        Assert.Contains("Prepare_ResponseParseFailure", markup);
    }

    [TestMethod]
    public void Preparation_TransitionPreventsDoubleSubmissionAndUsesBoundedNextQuery()
    {
        var markup = LoadUi("PrepareWords.razor");
        var service = LoadUi("PreparationService.cs");

        Assert.Contains("if (_item is null || _isBusy)", markup);
        Assert.Contains("disabled=\"@_isBusy\"", markup);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", markup);
        Assert.Contains("FindCurrentCandidateAsync", service);
        Assert.Contains("FirstOrDefaultAsync", service);
        Assert.DoesNotContain("candidates = await connection.Table<PreparationCandidateEntity>().ToListAsync", service);
    }

    [TestMethod]
    public void Preparation_LoadingStateIsImmediateAndIndependentOfCacheDuration()
    {
        var markup = LoadUi("PrepareWords.razor");
        var lookupStart = markup.IndexOf("private async Task LookupAsync()", StringComparison.Ordinal);
        var lookupEnd = markup.IndexOf("private async Task RetryAsync()", lookupStart, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, lookupStart);
        Assert.IsGreaterThan(lookupStart, lookupEnd);
        var lookupMethod = markup[lookupStart..lookupEnd];
        Assert.Contains("_isLookingUp = true", lookupMethod);
        Assert.Contains("await InvokeAsync(StateHasChanged)", lookupMethod);
        Assert.Contains("PreparationService.LookupCurrentAsync", lookupMethod);
        Assert.IsLessThan(
            lookupMethod.IndexOf("PreparationService.LookupCurrentAsync", StringComparison.Ordinal),
            lookupMethod.IndexOf("await InvokeAsync(StateHasChanged)", StringComparison.Ordinal));
        Assert.Contains("<span class=\"transition-spinner\" aria-hidden=\"true\"></span>", markup);
        Assert.Contains("if (_item is { Method: PreparationMethod.AutomaticOnline, Status: PreparationCandidateStatus.Pending })", markup);
    }

    [TestMethod]
    public void Preparation_LookupBoundaryNormalizesUnexpectedFailureAndStopsAfterDisposal()
    {
        var markup = LoadUi("PrepareWords.razor");
        var lookupStart = markup.IndexOf("private async Task LookupAsync()", StringComparison.Ordinal);
        var lookupEnd = markup.IndexOf("private async Task RetryAsync()", lookupStart, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, lookupStart);
        Assert.IsGreaterThan(lookupStart, lookupEnd);
        var lookupMethod = markup[lookupStart..lookupEnd];
        Assert.Contains("catch (Exception exception)", lookupMethod);
        Assert.Contains("CreateLookupFailure(item)", lookupMethod);
        Assert.Contains("finally", lookupMethod);
        Assert.Contains("_isLookingUp = false", lookupMethod);
        Assert.Contains("if (!_isDisposed)", lookupMethod);
        Assert.Contains("_isDisposed = true", markup);
        Assert.Contains("_lookupCancellation?.Cancel()", markup);
    }

    [TestMethod]
    public void Preparation_ManualEntryActionOpensUsableEditorAndSavesThroughService()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("@onclick=\"EnableManualEntryAsync\"", markup);
        Assert.Contains("PreparationCandidateStatus.Failed && !_showEditor", markup);
        Assert.Contains("Prepare_CanonicalTerm", markup);
        Assert.Contains("Prepare_EncounteredForm", markup);
        Assert.Contains("readonly", markup);
        Assert.Contains("_usefulAnswerMissing", markup);
        Assert.Contains("Prepare_UsefulAnswerRequired", markup);
        Assert.Contains("PreparationService.AcceptAsync", markup);
        Assert.Contains("await MoveNextAsync()", markup);
        Assert.Contains("knownFirst.revealElement", markup);
        Assert.Contains("knownFirst.focusElement", markup);
    }

    [TestMethod]
    public void Preparation_UsefulAnswerValidationIsAdjacentRevealedFocusedAndClearsOnCorrection()
    {
        var markup = LoadUi("PrepareWords.razor");
        var definition = markup.IndexOf("Prepare_Definition", StringComparison.Ordinal);
        var validation = markup.IndexOf("id=\"prepare-useful-answer-error\"", StringComparison.Ordinal);
        var additionalNote = markup.IndexOf("Prepare_AdditionalNote", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, definition);
        Assert.IsGreaterThan(definition, validation);
        Assert.IsGreaterThan(validation, additionalNote);
        Assert.Contains("@ref=\"_usefulAnswerError\"", markup);
        Assert.Contains("aria-invalid=\"@_usefulAnswerMissing\"", markup);
        Assert.Contains("ShowUsefulAnswerValidationAsync", markup);
        Assert.Contains("knownFirst.revealElement\", _usefulAnswerError", markup);
        Assert.Contains("GetUsefulAnswerFocusTarget", markup);
        Assert.Contains("knownFirst.focusElement\", GetUsefulAnswerFocusTarget()", markup);
        Assert.Contains("ClearUsefulAnswerValidationIfCorrected", markup);
        Assert.Contains("@bind=\"AcronymExpansionInput\"", markup);
        Assert.Contains("@bind=\"TranslationInput\"", markup);
        Assert.Contains("@bind=\"DefinitionInput\"", markup);
    }

    [TestMethod]
    public void Preparation_BackPausesWhileCancelEndsTheActiveBatch()
    {
        var markup = LoadUi("PrepareWords.razor");
        var service = LoadUi("PreparationService.cs");

        Assert.Contains("<PageHeader", markup);
        Assert.Contains("RequestCancelPreparationAsync", markup);
        Assert.Contains("Prepare_CancelPreparationConfirmation", markup);
        Assert.Contains("PreparationService.CancelActiveSessionAsync", markup);
        Assert.Contains("PreparationSessionStatus.Cancelled", service);
        Assert.Contains("PreparationCandidateStatus.Cancelled", service);
        Assert.Contains("PreparationState.Unprepared", service);
    }

    [TestMethod]
    public void DestructiveConfirmations_HideTriggersAndUseNeutralThenDangerActions()
    {
        var home = LoadUi("Home.razor");
        var review = LoadUi("ReviewWords.razor");
        var preparation = LoadUi("PrepareWords.razor");
        var learning = LoadUi("Learn.razor");
        var settings = LoadUi("Settings.razor");

        Assert.Contains("if (_confirmationAction is null)", preparation);
        Assert.Contains("class=\"destructive-confirmation preparation-confirmation\"", preparation);
        Assert.Contains("button button-danger cancel-preparation", preparation);
        Assert.Contains("button button-secondary", home);
        Assert.Contains("button button-secondary", review);
        Assert.Contains("button button-secondary", learning);
        Assert.Contains("button button-secondary", settings);
        Assert.Contains("_candidate is not null && !_showDiscardConfirmation", review);
        Assert.Contains("_card is not null && !_showPermanentKnownConfirmation", learning);
        Assert.Contains("_item is not null && _confirmationAction is null && !_cancelConfirmationVisible", preparation);
        Assert.Contains("_confirmationAction is null && !_cancelConfirmationVisible", preparation);
        Assert.Contains("data-destructive-confirm", home);
        Assert.Contains("data-destructive-confirm", review);
        Assert.AreEqual(2, CountOccurrences(preparation, "data-destructive-confirm"));
        Assert.Contains("data-destructive-confirm", learning);
        Assert.Contains("data-destructive-confirm", settings);
    }

    [TestMethod]
    public void DestructiveConfirmations_RevealFocusCancelHandleEscapeAndRestoreTriggerFocus()
    {
        var home = LoadUi("Home.razor");
        var review = LoadUi("ReviewWords.razor");
        var preparation = LoadUi("PrepareWords.razor");
        var learning = LoadUi("Learn.razor");
        var settings = LoadUi("Settings.razor");

        AssertConfirmationNavigation(home, "_discardConfirmationRef", "_discardCancelButtonRef", "_discardButtonRef");
        AssertConfirmationNavigation(review, "_discardConfirmationRef", "_discardCancelButtonRef", "_discardButtonRef");
        AssertConfirmationNavigation(learning, "_permanentKnownConfirmationRef", "_permanentKnownCancelButtonRef", "_permanentKnownButtonRef");
        AssertConfirmationNavigation(settings, "_resetConfirmation", "_cancelResetButton", "_resetButton");
        AssertConfirmationNavigation(preparation, "_cancelConfirmationRef", "_cancelPreparationButtonRef", "_cancelPreparationTriggerRef");
        Assert.Contains("_dispositionConfirmationRef", preparation);
        Assert.Contains("_dispositionCancelButtonRef", preparation);
        Assert.Contains("HandleDispositionKeyDown", preparation);
        Assert.Contains("GetDispositionTrigger", preparation);
    }

    [TestMethod]
    public void Preparation_RevealsRetryAndManualEditorWithUsefulFocusTargets()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("@ref=\"_lookupFailureRef\"", markup);
        Assert.Contains("@ref=\"_retryButtonRef\"", markup);
        Assert.Contains("@ref=\"_manualEntryButtonRef\"", markup);
        Assert.Contains("@ref=\"_editorRef\"", markup);
        Assert.Contains("RevealLookupFailureAsync", markup);
        Assert.Contains("RevealEditorAsync", markup);
        Assert.Contains("knownFirst.revealElement", markup);
        Assert.Contains("knownFirst.focusElement", markup);
        Assert.Contains("GetUsefulAnswerFocusTarget", markup);
    }

    [TestMethod]
    public void JavaScriptInterop_RevealsOnlyWhenNeededFocusesWithoutScrollingAndBlocksDestructiveEnter()
    {
        var script = LoadUi("knownfirst.js");

        Assert.Contains("getBoundingClientRect()", script);
        Assert.Contains("bounds.top >= 0 && bounds.bottom <= viewportHeight", script);
        Assert.Contains("scrollIntoView({ behavior: \"smooth\", block: \"nearest\" })", script);
        Assert.Contains("focus({ preventScroll: true })", script);
        Assert.Contains("event.key === \"Enter\"", script);
        Assert.Contains("closest(\"[data-destructive-confirm]\")", script);
        Assert.Contains("event.preventDefault()", script);
    }

    [TestMethod]
    public void MobileReview_HasCollapsedMetadataAndStableTwoColumnActionBar()
    {
        var markup = LoadUi("ReviewWords.razor");
        var styles = LoadUi("ReviewWords.razor.css");
        var appStyles = LoadUi("app.css");

        Assert.Contains("<details class=\"candidate-details-panel\">", markup);
        Assert.DoesNotContain("<details class=\"candidate-details-panel\" open", markup);
        Assert.Contains("review-action-bar", markup);
        Assert.Contains("Review_Saving", markup);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr))", styles);
        Assert.Contains("env(safe-area-inset-bottom)", appStyles);
        Assert.DoesNotContain("env(safe-area-inset-bottom)", styles);
    }

    [TestMethod]
    public void MobileTitle_IsNotDuplicatedAndHomeShowsKnownFirstOnce()
    {
        var pageHeaderStyles = LoadUi("PageHeader.razor.css");
        var home = LoadUi("Home.razor");
        var layout = LoadUi("MainLayout.razor");

        Assert.Contains("@media (max-width: 799px)", pageHeaderStyles);
        Assert.Contains("display: none", pageHeaderStyles);
        Assert.DoesNotContain("Localizer[\"App_Name\"]", home);
        Assert.AreEqual(1, CountOccurrences(home, "<h1>@Localizer[\"Home_Title\"]</h1>"));
        Assert.Contains("\"\" => string.Empty", layout);
    }

    [TestMethod]
    public void Layout_UsesSharedSpacingScaleAndBoundedPageMargins()
    {
        var styles = LoadUi("app.css");

        Assert.Contains("--space-1: 0.25rem", styles);
        Assert.Contains("--space-2: 0.5rem", styles);
        Assert.Contains("--space-3: 0.75rem", styles);
        Assert.Contains("--space-4: 1rem", styles);
        Assert.Contains("--space-6: 1.5rem", styles);
        Assert.Contains("--space-8: 2rem", styles);
        Assert.Contains("--space-12: 3rem", styles);
        Assert.DoesNotContain("clamp(1rem, 6vw, 5rem)", styles);
        Assert.DoesNotContain("4.5rem", styles);
    }

    [TestMethod]
    public void Layout_OwnsTheViewportAndWorkflowsUseAvailableShellHeight()
    {
        var layoutStyles = LoadUi("MainLayout.razor.css");
        var appStyles = LoadUi("app.css");

        Assert.Contains("height: 100dvh", layoutStyles);
        Assert.Contains("overflow: hidden", layoutStyles);
        Assert.Contains("min-height: 0", layoutStyles);
        Assert.Contains("overflow-y: auto", layoutStyles);
        Assert.Contains("flex: 1 1 auto", appStyles);
        Assert.DoesNotContain("100dvh - 6.5rem", appStyles);
        Assert.DoesNotContain("100dvh - 5rem", appStyles);
    }

    [TestMethod]
    public void MobileMenu_IsDismissibleOverlayAndLocksOnlyBackgroundContent()
    {
        var layout = LoadUi("MainLayout.razor");
        var styles = LoadUi("MainLayout.razor.css");

        Assert.Contains("navigation-backdrop", layout);
        Assert.Contains("RegisterDismissibleOverlay", layout);
        Assert.Contains("KeyboardEventArgs", layout);
        Assert.Contains("Escape", layout);
        Assert.Contains("inert=", layout);
        Assert.Contains("position: fixed", styles);
        Assert.Contains(".page.menu-open main", styles);
        Assert.Contains(".navigation-backdrop", styles);
    }

    [TestMethod]
    public void AppScrollAreas_HideChromeWithoutChangingTextareaBehavior()
    {
        var styles = LoadUi("app.css");

        Assert.Contains(".content,", styles);
        Assert.Contains(".workflow-scroll-area,", styles);
        Assert.Contains(".nav-scrollable", styles);
        Assert.Contains("scrollbar-width: none", styles);
        Assert.Contains("::-webkit-scrollbar", styles);
        Assert.DoesNotContain("textarea::-webkit-scrollbar", styles);
        Assert.DoesNotContain("*::-webkit-scrollbar", styles);
    }

    [TestMethod]
    public void ResponsiveWorkflows_KeepKeyboardScrollingAndNarrowContextActions()
    {
        var review = LoadUi("ReviewWords.razor");
        var preparation = LoadUi("PrepareWords.razor");
        var learning = LoadUi("Learn.razor");
        var learningStyles = LoadUi("Learn.razor.css");

        Assert.Contains("class=\"workflow-scroll-area\" tabindex=\"0\"", review);
        Assert.Contains("class=\"workflow-scroll-area\" tabindex=\"0\"", preparation);
        Assert.Contains("class=\"workflow-scroll-area\" tabindex=\"0\"", learning);
        Assert.Contains("@media (max-width: 620px)", learningStyles);
        Assert.Contains("grid-template-columns: 1fr 1fr", learningStyles);
        Assert.Contains("@media (max-width: 320px)", learningStyles);
    }

    [TestMethod]
    public void ImportText_StylesAreBalancedAndTextareaRemainsBounded()
    {
        var styles = LoadUi("ImportText.razor.css");

        Assert.AreEqual(CountOccurrences(styles, "{"), CountOccurrences(styles, "}"));
        Assert.Contains("overflow-y: auto", styles);
        Assert.Contains("max-height:", styles);
    }

    [TestMethod]
    public void Learning_ContextNavigationIsAdjacentToContextAndRecommendationUsesCount()
    {
        var markup = LoadUi("Learn.razor");

        Assert.AreEqual(2, CountOccurrences(markup, "@ContextNavigation"));
        Assert.Contains("learning-context-block", markup);
        Assert.Contains("Learn_MoreUnknownWaiting\", _summary.RemainingUnpreparedCount", markup);
        Assert.Contains("Learn_PrepareNextWords", markup);
        Assert.Contains("Common_Later", markup);
        Assert.Contains("Prepare_ChangeLimit", markup);
    }

    [TestMethod]
    public void SourceDetails_AreCollapsedAndLearningUsesCompactControl()
    {
        var source = LoadUi("SourceDetails.razor");
        var answer = LoadUi("AnswerView.razor");

        Assert.Contains("<details class=\"source-details", source);
        Assert.DoesNotContain("<details open", source);
        Assert.Contains("SourceProject", source);
        Assert.Contains("PageTitle", source);
        Assert.Contains("RevisionId", source);
        Assert.Contains("Attribution", source);
        Assert.Contains("Source_License", source);
        Assert.Contains("Compact=\"true\"", answer);
    }

    [TestMethod]
    public void SourceDetails_RendersPageAndLicenseLinksWithTargetBlankAndRelNoopener()
    {
        var source = LoadUi("SourceDetails.razor");

        Assert.Contains("SourceReferencePolicy.CreatePageUri", source);
        Assert.Contains("SourceReferencePolicy.GetLicenseReference", source);
        Assert.Contains("SourceReferencePolicy.GetLicenseUri", source);
        Assert.Contains("target=\"_blank\"", source);
        Assert.Contains("rel=\"noopener noreferrer\"", source);
    }

    [TestMethod]
    public void AnalysisDiagnostics_AreDebugEntryPointsAndReleaseExcludesTheDiagnosticsPage()
    {
        var review = LoadUi("ReviewWords.razor");
        var diagnostics = LoadUi("Diagnostics.razor");
        var project = LoadUi("KnownFirst.csproj");

        Assert.Contains("Analysis_Details", review);
        Assert.Contains("IsDebugBuild", review);
        Assert.Contains("analysisDocumentId", review);
        Assert.Contains("Analysis_CopyReport", diagnostics);
        Assert.Contains("Analysis_SentenceSpans", diagnostics);
        Assert.Contains("Analysis_TokenDecisions", diagnostics);
        Assert.Contains("Analysis_CandidateGrouping", diagnostics);
        Assert.Contains("Analysis_ContextSelection", diagnostics);
        Assert.Contains("Components\\Pages\\Diagnostics.razor", project);
        Assert.Contains("Condition=\"'$(Configuration)' != 'Debug'\"", project);
    }

    [TestMethod]
    public void Import_UsesExplicitLookupModeAndRejectsIdenticalTargetLanguage()
    {
        var markup = LoadUi("ImportText.razor");

        Assert.Contains("LexicalLookupMode.Definition", markup);
        Assert.Contains("LexicalLookupMode.Translation", markup);
        Assert.Contains("_lookupLanguageInvalid", markup);
        Assert.Contains("Import_TargetLanguageMustDiffer", markup);
        Assert.DoesNotContain("CurrentUiCulture", markup);
    }

    [TestMethod]
    public void Import_NoLongerOffersTheCombinedDefinitionAndTranslationChoice()
    {
        var markup = LoadUi("ImportText.razor");

        Assert.DoesNotContain("LexicalLookupMode.DefinitionAndTranslation", markup);
        Assert.DoesNotContain("Import_LookupDefinitionAndTranslation", markup);
    }

    [TestMethod]
    public void AndroidBeta_TestPackageIdentitiesAndPublishingContractAreStableAndExternalized()
    {
        var project = LoadUi("KnownFirst.csproj");
        var script = LoadUi("publish-android-test-packages.ps1");

        // The product version and build number are read out of KnownFirst.csproj itself
        // rather than hardcoded here, so this contract stays valid across future version bumps
        // (Beta 10 and beyond) instead of going stale the moment the version changes.
        var productVersionMatch = System.Text.RegularExpressions.Regex.Match(
            project, "<KnownFirstProductVersion>([^<]+)</KnownFirstProductVersion>");
        Assert.IsTrue(productVersionMatch.Success, "KnownFirstProductVersion must be declared in KnownFirst.csproj.");
        var productVersion = productVersionMatch.Groups[1].Value;
        StringAssert.Matches(
            productVersion,
            new System.Text.RegularExpressions.Regex(@"^1\.0\.0-beta\.\d+$"),
            "KnownFirstProductVersion must follow the 1.0.0-beta.N pattern.");

        var buildNumberMatch = System.Text.RegularExpressions.Regex.Match(
            project, "<KnownFirstBuildNumber>([^<]+)</KnownFirstBuildNumber>");
        Assert.IsTrue(buildNumberMatch.Success, "KnownFirstBuildNumber must be declared in KnownFirst.csproj.");
        var buildNumber = buildNumberMatch.Groups[1].Value;
        StringAssert.Matches(
            buildNumber,
            new System.Text.RegularExpressions.Regex(@"^\d+$"),
            "KnownFirstBuildNumber must be numeric.");

        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst</ApplicationId>", project);
        Assert.Contains("<ApplicationVersion>$(KnownFirstBuildNumber)</ApplicationVersion>", project);
        Assert.Contains("<ApplicationDisplayVersion>$(KnownFirstProductVersion)</ApplicationDisplayVersion>", project);
        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst.diagnostic</ApplicationId>", project);
        Assert.Contains("<ApplicationTitle>KnownFirst Diagnostic</ApplicationTitle>", project);
        Assert.DoesNotContain("<ApplicationDisplayVersion>1.0.0-beta.8-diagnostic</ApplicationDisplayVersion>", project);
        Assert.Contains("<DefineConstants>$(DefineConstants);KNOWNFIRST_DIAGNOSTICS</DefineConstants>", project);
        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst.debug</ApplicationId>", project);
        Assert.Contains("<ApplicationTitle>KnownFirst Debug</ApplicationTitle>", project);
        Assert.DoesNotContain("<ApplicationDisplayVersion>1.0.0-beta.8-debug</ApplicationDisplayVersion>", project);
        Assert.DoesNotContain("<ApplicationDisplayVersion>1.0.0-beta.8</ApplicationDisplayVersion>", project);
        Assert.DoesNotContain("<ApplicationVersion>8</ApplicationVersion>", project);
        Assert.Contains("<AndroidUseFastDeployment>false</AndroidUseFastDeployment>", project);
        Assert.Contains("<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>", project);
        Assert.Contains("<RunAOTCompilation>true</RunAOTCompilation>", project);
        Assert.Contains("<DebugType>full</DebugType>", project);
        Assert.Contains("KnownFirst-Secrets", script);
        Assert.Contains("knownfirst-beta.keystore", script);
        Assert.Contains("env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD", script);
        Assert.Contains("AndroidPackageFormats=apk", script);
        // The script resolves the version at run time via $versionInfo.ProductVersion, so the
        // source text never contains the resolved literal - it must contain the interpolation
        // expression itself, proving the artifact names are built dynamically rather than
        // reconstructed from a value this test happens to read from KnownFirst.csproj today.
        Assert.Contains("KnownFirst-$($versionInfo.ProductVersion)-android-release", script);
        Assert.Contains("KnownFirst-$($versionInfo.ProductVersion)-android-diagnostic", script);
        Assert.Contains("KnownFirst-$($versionInfo.ProductVersion)-android-debug", script);
        Assert.Contains("Application version: $($versionInfo.BuildNumber)", script);
        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch(
                script, @"KnownFirst-\d+\.\d+\.\d+-beta\.\d+-android-(release|diagnostic|debug)"),
            "The Android artifact base names must be built from the resolved product version, not a hardcoded literal.");
        Assert.Contains("dotnet clean", script);
        Assert.Contains("apksigner", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("Compress-Archive", script);
        Assert.Contains("artifacts\\android-beta", script);
        Assert.DoesNotContain("storepass ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keypass ", script, StringComparison.OrdinalIgnoreCase);

        // Selective package creation: the script accepts an optional Configuration selection
        // (defaulting to building all three, preserving the pre-selection behavior) and filters
        // by the same Kind values used to build the release/diagnostic/debug artifact names
        // above, without changing package IDs, signing, checksum, or ZIP-content logic.
        Assert.Contains("[ValidateSet('Debug', 'Diagnostic', 'Release', 'DebugRelease', 'All')]", script);
        Assert.Contains("[string]$Configuration = 'All'", script);

        // DebugRelease resolves to exactly Debug and Release, excluding Diagnostic
        Assert.Contains("'DebugRelease' { @('debug', 'release') }", script);

        // Diagnostic remains independently selectable
        Assert.Contains("'Diagnostic' { @('diagnostic') }", script);

        // Debug and Release remain independently selectable
        Assert.Contains("'Debug' { @('debug') }", script);
        Assert.Contains("'Release' { @('release') }", script);

        // Package filtering supports a set of selected kinds rather than assuming exactly one kind
        Assert.Contains("$packages = $packages | Where-Object { $_.Kind -in $selectedKinds }", script);

        // Existing package IDs remain asserted
        Assert.Contains("PackageId = \"com.tachiguro.knownfirst\"", script);
        Assert.Contains("PackageId = \"com.tachiguro.knownfirst.diagnostic\"", script);
        Assert.Contains("PackageId = \"com.tachiguro.knownfirst.debug\"", script);
    }

    [TestMethod]
    public void GuiTestMenu_StartupSmokeChoicesAlwaysSetDebugConfigurationBeforeInvokingGuiTest()
    {
        var script = LoadUi("knownfirst.ps1");

        // Both interactive StartupSmoke choices ('1' and '2' inside Show-GuiTestMenu) must set
        // Configuration to Debug immediately after selecting the scenario, so the launcher
        // never forwards a null/empty/whitespace Configuration to the scenario runner (whose
        // own ValidateSet('Debug') rejects an explicit blank value before applying its default).
        const string scenarioMarker = "$script:GuiScenario = 'StartupSmoke'";
        var markerIndex = script.IndexOf(scenarioMarker, StringComparison.Ordinal);
        var occurrences = 0;
        while (markerIndex >= 0)
        {
            occurrences++;
            var windowStart = markerIndex + scenarioMarker.Length;
            var windowLength = Math.Min(120, script.Length - windowStart);
            var window = script.Substring(windowStart, windowLength);
            Assert.Contains(
                "$script:Configuration = 'Debug'",
                window,
                $"Menu choice at offset {markerIndex} selects StartupSmoke without immediately setting Configuration to Debug.");

            markerIndex = script.IndexOf(scenarioMarker, windowStart, StringComparison.Ordinal);
        }

        Assert.IsTrue(occurrences >= 2, "Expected at least two StartupSmoke menu choices (options 1 and 2).");
    }

    [TestMethod]
    public void GuiTestAction_NeverForwardsAnEmptyOrWhitespaceConfigurationToTheScenarioRunner()
    {
        var script = LoadUi("knownfirst.ps1");

        // Invoke-GuiTestAction defensively defaults a null/empty/whitespace Configuration to
        // 'Debug' before invoking run-scenario-startup-smoke.ps1, and always invokes with the
        // computed $effectiveConfiguration rather than the raw, possibly-blank $Configuration.
        Assert.Contains(
            "$effectiveConfiguration = if ([string]::IsNullOrWhiteSpace($Configuration)) { 'Debug' } else { $Configuration }",
            script);
        Assert.Contains("-Configuration $effectiveConfiguration -WorkingDirectory $projectRoot", script);
    }

    [TestMethod]
    public void GuiTestRunner_LoadsBothCompressionAssembliesBeforeReferencingCompressionTypes()
    {
        var script = LoadUi("run-scenario-startup-smoke.ps1");

        // Windows PowerShell 5.1 needs System.IO.Compression (ZipArchiveMode) loaded alongside
        // System.IO.Compression.FileSystem (ZipFile) - loading only the latter reproduces
        // "Unable to find type [System.IO.Compression.ZipArchiveMode]".
        var compressionAssemblyIndex = script.IndexOf("Add-Type -AssemblyName 'System.IO.Compression'", StringComparison.Ordinal);
        var fileSystemAssemblyIndex = script.IndexOf("Add-Type -AssemblyName 'System.IO.Compression.FileSystem'", StringComparison.Ordinal);
        var zipArchiveModeUseIndex = script.IndexOf("[System.IO.Compression.ZipArchiveMode]::Create", StringComparison.Ordinal);

        Assert.IsTrue(compressionAssemblyIndex >= 0, "System.IO.Compression must be loaded explicitly.");
        Assert.IsTrue(fileSystemAssemblyIndex >= 0, "System.IO.Compression.FileSystem must be loaded explicitly.");
        Assert.IsTrue(zipArchiveModeUseIndex >= 0, "ZipArchiveMode must still be used to open the report zip.");
        Assert.IsTrue(
            compressionAssemblyIndex < zipArchiveModeUseIndex && fileSystemAssemblyIndex < zipArchiveModeUseIndex,
            "Both compression assemblies must be loaded before ZipArchiveMode is referenced.");

        // CreateEntryFromFile is an extension method (ZipFileExtensions); Windows PowerShell
        // 5.1 cannot resolve it via instance dot-syntax ($zip.CreateEntryFromFile(...)) - it
        // must be invoked as the static method it actually is, confirmed by an isolated
        // Windows PowerShell 5.1 compatibility check exercising this exact API sequence.
        Assert.DoesNotContain("$zip.CreateEntryFromFile(", script);
        Assert.Contains("[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(", script);
    }

    [TestMethod]
    public void GuiTestRunner_ReportZipAllowlistStaysExplicitAndNeverRecursivelyZipsTheRunDirectory()
    {
        var script = LoadUi("run-scenario-startup-smoke.ps1");

        // The report package must only ever contain the four explicitly allowlisted files
        // (plus screenshots), never the run directory wholesale, the profile directory, a
        // database/WAL/SHM, secrets, keystores, launcher state, build output, or report.zip
        // itself.
        Assert.Contains("'summary.json',", script);
        Assert.Contains("'steps.jsonl',", script);
        Assert.Contains("'runner.log',", script);
        Assert.Contains("'environment.json'", script);
        Assert.DoesNotContain("CreateFromDirectory", script);

        var listStart = script.IndexOf("$filesToInclude = @(", StringComparison.Ordinal);
        var listEnd = script.IndexOf(")", listStart, StringComparison.Ordinal);
        Assert.IsTrue(listStart >= 0 && listEnd > listStart, "The explicit $filesToInclude allowlist must be present.");
        var allowlistLiteral = script.Substring(listStart, listEnd - listStart);
        Assert.DoesNotContain("report.zip", allowlistLiteral);

        // report.zip is created only after summary.json is already written to disk (summary.json
        // is itself one of the packaged files, so it must exist before packaging runs).
        var summaryWrittenIndex = script.IndexOf("$summary | ConvertTo-Json | Set-Content -LiteralPath $summaryPath", StringComparison.Ordinal);
        var reportZipCallIndex = script.IndexOf("New-ReportZip -SourceDir $runDir -OutputPath $reportZipPath", StringComparison.Ordinal);
        Assert.IsTrue(summaryWrittenIndex >= 0 && reportZipCallIndex >= 0);
        Assert.IsTrue(summaryWrittenIndex < reportZipCallIndex, "report.zip must be created only after summary.json is written.");
    }

    [TestMethod]
    public void GuiTestRunner_PackagingFailureIsLoggedWithoutFailingTheScript()
    {
        var script = LoadUi("run-scenario-startup-smoke.ps1");

        // A report.zip packaging failure must be recorded (steps.jsonl) rather than silently
        // dropped, and must not throw out of New-ReportZip (it already returns $false on
        // failure, which the caller treats as non-fatal).
        Assert.Contains("Log-Step -Name 'ReportPackaged' -Status 'Failed' -Message $_.Exception.Message", script);
    }

    [TestMethod]
    public void GuiScripts_ContainNoUnsafeParenthesizedInlineIfUsedAsACommandArgument()
    {
        // (if (...) { ... } else { ... }) used directly as a parenthesized command/property
        // argument is not a valid PowerShell pipeline expression: Windows PowerShell resolves
        // "if" as a command name at runtime and fails with "The term 'if' is not recognized...".
        var unsafePattern = new System.Text.RegularExpressions.Regex(@"\(if\s*\(");

        var launcherScript = LoadUi("knownfirst.ps1");
        Assert.IsFalse(
            unsafePattern.IsMatch(launcherScript),
            "knownfirst.ps1 must not contain an unsafe parenthesized inline-if used as a command argument.");

        var runnerScript = LoadUi("run-scenario-startup-smoke.ps1");
        Assert.IsFalse(
            unsafePattern.IsMatch(runnerScript),
            "run-scenario-startup-smoke.ps1 must not contain an unsafe parenthesized inline-if used as a command argument.");

        // The safe replacement form (assign to a plain variable first) must be present at both
        // previously-broken call sites.
        Assert.Contains("$resultColor = if ($summary.result -eq 'Passed') { 'Green' } else { 'Red' }", launcherScript);
        Assert.Contains("$resultColor = if ($result -eq 'Passed') { 'Green' } else { 'Red' }", runnerScript);
    }

    [TestMethod]
    public void GuiTestMenu_RendersActualResultPropertiesInsteadOfAStringifiedCustomObject()
    {
        var script = LoadUi("knownfirst.ps1");

        // The old expression printed the literal text "@{Status=FAILED}.Status" because the
        // interpolated $(...) subexpression closed before ".Status" was appended. The fix must
        // use real property access on the result object entirely inside the subexpression.
        Assert.DoesNotContain("Select-Object @{Name='Status';Expression={if($_){'PASSED'}else{'FAILED'}}}).Status", script);
        Assert.Contains("Write-Host \"Result: $($result.ActionName) $($result.Status)\"", script);
        Assert.Contains("$($result.RunDirectory)", script);
        Assert.Contains("$($result.SummaryPath)", script);
        Assert.Contains("$($result.ReportZipPath)", script);
    }

    [TestMethod]
    public void GuiTestAction_NeverLabelsTheLauncherLogDirectoryAsTheGuiRunDirectory()
    {
        var script = LoadUi("knownfirst.ps1");

        // The pre-run-failure path (no new directory under artifacts\gui-tests\windows\runs)
        // must not fabricate a run directory from the launcher log's own folder, and the menu
        // must not derive "Run directory" from Split-Path $result.LogPath any longer.
        Assert.DoesNotContain("Write-Host \"Run directory: $(Split-Path $result.LogPath)\"", script);
        Assert.Contains("No GUI run directory was created.", script);

        // The real GUI run directory is discovered by diffing artifacts\gui-tests\windows\runs
        // before/after invocation, never assumed to be the launcher-logs folder.
        Assert.Contains("$priorRunDirNames = @()", script);
        Assert.Contains("Where-Object { $priorRunDirNames -notcontains $_.Name }", script);
    }

    [TestMethod]
    public void GuiTestProfileSupport_IsCompileTimeGatedToWindowsDebugAndBetaDiagnosticOnly()
    {
        var project = LoadUi("KnownFirst.csproj");

        // The exact MSBuild condition that must gate the symbol: Windows platform AND
        // (Debug OR BetaDiagnostic configuration). This is a project-file text assertion so the
        // contract does not depend on which configuration happens to run this test.
        Assert.Contains(
            "Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows' " +
            "and ('$(Configuration)' == 'Debug' or '$(Configuration)' == 'BetaDiagnostic')\"",
            project);

        // The symbol must be defined exactly once in the project, and only inside the
        // Windows-Debug-or-BetaDiagnostic-gated group asserted above - never unconditionally,
        // and never inside a Release-only or Android-only property group.
        Assert.AreEqual(1, CountOccurrences(project, "KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED"));
        Assert.Contains(
            "<DefineConstants>$(DefineConstants);KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED</DefineConstants>",
            project);
    }

    [TestMethod]
    public void Settings_PortableImportPreviewHidesTheNormalDataActionsRowAndRestoresOnCancel()
    {
        var markup = LoadUi("Settings.razor");

        var actionsRowStart = markup.IndexOf("@if (_portableImportPreview is null)", StringComparison.Ordinal);
        var previewStart = markup.IndexOf(
            "@if (_portableImportPreview is { } preview && _portableImportSelection is not null)",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, actionsRowStart);
        Assert.IsGreaterThanOrEqualTo(0, previewStart);
        Assert.IsGreaterThan(actionsRowStart, previewStart);

        var actionsRow = markup[actionsRowStart..previewStart];
        Assert.Contains("Settings_DataExport", actionsRow);
        Assert.Contains("Settings_DataImport", actionsRow);
        Assert.Contains("CancelPortableImportAsync", markup);
        Assert.Contains("_portableImportPreview = null", markup);
    }

    [TestMethod]
    public void Settings_ImportNoLongerClaimsEmptyDatabaseOnlyAndOffersOnePrimaryImportAction()
    {
        var markup = LoadUi("Settings.razor");

        Assert.DoesNotContain("Settings_DataImportEmptyOnly", markup);
        Assert.DoesNotContain("works only on an empty installation", markup, StringComparison.OrdinalIgnoreCase);

        // Exactly one primary user-facing Import entry point: the "Data Import" picker button.
        // There is no separate first-class "Merge" button — Import alone routes to restore or merge.
        Assert.Contains("Settings_DataImport\"]", markup);
        Assert.DoesNotContain("Settings_DataMerge", markup);
        Assert.DoesNotContain(">Merge<", markup);
    }

    [TestMethod]
    public void Settings_ImportPreviewRendersDistinctRestoreMergeAndNoChangeStates()
    {
        var markup = LoadUi("Settings.razor");

        Assert.Contains("GetImportPreviewTitleKey(preview.Disposition)", markup);
        Assert.Contains("GetImportPreviewDescriptionKey(preview.Disposition)", markup);
        Assert.Contains("PortableImportPreviewDisposition.RestoreIntoEmpty => \"Settings_DataImportPreviewRestoreTitle\"", markup);
        Assert.Contains("PortableImportPreviewDisposition.MergeChanges => \"Settings_DataImportPreviewMergeTitle\"", markup);
        Assert.Contains("_ => \"Settings_DataImportPreviewNoChangeTitle\"", markup);
        Assert.Contains("PortableImportPreviewDisposition.RestoreIntoEmpty => \"Settings_DataImportPreviewRestoreDescription\"", markup);
        Assert.Contains("PortableImportPreviewDisposition.MergeChanges => \"Settings_DataImportPreviewMergeDescription\"", markup);
        Assert.Contains("_ => \"Settings_DataImportPreviewNoChangeDescription\"", markup);
    }

    [TestMethod]
    public void Settings_MergePreviewExposesAllFourAggregateCountCategories()
    {
        var markup = LoadUi("Settings.razor");

        var mergeBlockStart = markup.IndexOf(
            "if (preview.Disposition == PortableImportPreviewDisposition.MergeChanges)",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, mergeBlockStart);
        var mergeBlockEnd = markup.IndexOf("Settings_DataImportPreviewMergeSafetyCopyNotice", mergeBlockStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(mergeBlockStart, mergeBlockEnd);
        var mergeBlock = markup[mergeBlockStart..mergeBlockEnd];

        Assert.Contains("Settings_DataImportPreviewCountsNew", mergeBlock);
        Assert.Contains("preview.InsertedCount", mergeBlock);
        Assert.Contains("Settings_DataImportPreviewCountsEnriched", mergeBlock);
        Assert.Contains("preview.EnrichedCount", mergeBlock);
        Assert.Contains("Settings_DataImportPreviewCountsPreserved", mergeBlock);
        Assert.Contains("preview.PreservedVariantCount", mergeBlock);
        Assert.Contains("Settings_DataImportPreviewCountsSkipped", mergeBlock);
        Assert.Contains("preview.SkippedCount", mergeBlock);
    }

    [TestMethod]
    public void Settings_NoChangePreviewHasNoMutatingConfirmationButtonOnlyCloseOrDone()
    {
        var markup = LoadUi("Settings.razor");

        var buttonRowStart = markup.IndexOf("@if (preview.CanConfirm)", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, buttonRowStart);
        var elseStart = markup.IndexOf("else", buttonRowStart, StringComparison.Ordinal);
        var elseBlockEnd = markup.IndexOf("</div>", elseStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(buttonRowStart, elseStart);
        Assert.IsGreaterThan(elseStart, elseBlockEnd);
        var noChangeBlock = markup[elseStart..elseBlockEnd];

        Assert.Contains("Common_Close", noChangeBlock);
        Assert.Contains("CancelPortableImportAsync", noChangeBlock);
        Assert.DoesNotContain("ConfirmPortableImportAsync", noChangeBlock);
        Assert.DoesNotContain("button-primary", noChangeBlock);
    }

    [TestMethod]
    public void Settings_SelectionLifecycleDisposesRetainedResourcesOnEveryPath()
    {
        var markup = LoadUi("Settings.razor");

        // Choosing a new file always disposes the previously retained selection first.
        var chooseStart = markup.IndexOf("private async Task ChoosePortableImportAsync()", StringComparison.Ordinal);
        var confirmStart = markup.IndexOf("private async Task ConfirmPortableImportAsync()", chooseStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, chooseStart);
        Assert.IsGreaterThan(chooseStart, confirmStart);
        var chooseMethod = markup[chooseStart..confirmStart];
        Assert.Contains("await DisposePortableImportSelectionAsync();", chooseMethod);

        // Confirm always disposes the selection in its finally block.
        var cancelStart = markup.IndexOf("private async Task CancelPortableImportAsync()", confirmStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, cancelStart);
        var confirmMethod = markup[confirmStart..cancelStart];
        var confirmFinallyStart = confirmMethod.IndexOf("finally", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, confirmFinallyStart);
        Assert.Contains("await DisposePortableImportSelectionAsync();", confirmMethod[confirmFinallyStart..]);

        // Cancel/Close disposes the selection.
        var handleKeyDownStart = markup.IndexOf("private void HandleImportPreviewKeyDown", cancelStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, handleKeyDownStart);
        var cancelMethod = markup[cancelStart..handleKeyDownStart];
        Assert.Contains("await DisposePortableImportSelectionAsync();", cancelMethod);

        // Page disposal disposes any retained selection.
        Assert.Contains("async ValueTask IAsyncDisposable.DisposeAsync() =>", markup);
        Assert.Contains("await DisposePortableImportSelectionAsync();", markup);
    }

    [TestMethod]
    public void WorkflowChangeNotifier_NavMenuAndHomeSubscribeReloadAndUnsubscribeOnDisposal()
    {
        var navMenu = LoadUi("NavMenu.razor");
        var home = LoadUi("Home.razor");

        Assert.Contains("@implements IDisposable", navMenu);
        Assert.Contains("@implements IDisposable", home);
        Assert.Contains("IWorkflowChangeNotifier WorkflowChangeNotifier", navMenu);
        Assert.Contains("IWorkflowChangeNotifier WorkflowChangeNotifier", home);
        Assert.Contains("WorkflowChangeNotifier.Changed += OnWorkflowChanged", navMenu);
        Assert.Contains("WorkflowChangeNotifier.Changed += OnWorkflowChanged", home);
        Assert.Contains("WorkflowChangeNotifier.Changed -= OnWorkflowChanged", navMenu);
        Assert.Contains("WorkflowChangeNotifier.Changed -= OnWorkflowChanged", home);
        Assert.Contains("await LoadWorkflowAsync()", navMenu);
        Assert.Contains("await LoadStatisticsAsync()", home);
        Assert.Contains("StateHasChanged()", navMenu);
        Assert.Contains("StateHasChanged()", home);
    }

    [TestMethod]
    public void WorkflowChangeNotifier_PublishesOnlyAfterSuccessfulImportOrReset()
    {
        var markup = LoadUi("Settings.razor");

        var importStart = markup.IndexOf("private async Task ConfirmPortableImportAsync()", StringComparison.Ordinal);
        var importEnd = markup.IndexOf("private async Task CancelPortableImportAsync()", importStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, importStart);
        Assert.IsGreaterThan(importStart, importEnd);
        var importMethod = markup[importStart..importEnd];

        // Only the two mutation dispositions (restore or applied merge) publish — MergeNoChange never does.
        Assert.Contains("result.Summary?.Disposition is", importMethod);
        Assert.Contains(
            "PortableImportDisposition.RestoredIntoEmpty or PortableImportDisposition.MergeApplied",
            importMethod);
        Assert.Contains("WorkflowChangeNotifier.NotifyChanged()", importMethod);
        Assert.DoesNotContain("PortableImportDisposition.MergeNoChange", importMethod);

        var resetStart = markup.IndexOf("private async Task ResetDataAsync()", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, resetStart);
        var resetMethod = markup[resetStart..];
        var successIndex = resetMethod.IndexOf("_resetSucceeded = true", StringComparison.Ordinal);
        var notifyIndex = resetMethod.IndexOf("WorkflowChangeNotifier.NotifyChanged()", StringComparison.Ordinal);
        var catchIndex = resetMethod.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, successIndex);
        Assert.IsGreaterThanOrEqualTo(0, notifyIndex);
        Assert.IsGreaterThan(successIndex, notifyIndex);
        Assert.IsGreaterThan(notifyIndex, catchIndex);
    }

    [TestMethod]
    public void Settings_KnownImportFailureCategoriesMapToDistinctLocalizedMessages()
    {
        var markup = LoadUi("Settings.razor");

        var mapStart = markup.IndexOf("private static (string MessageKey, bool IsError) MapImportResultMessage", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, mapStart);
        var mapEnd = markup.IndexOf(
            "private static string MapFailedErrorCodeMessageKey", mapStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(mapStart, mapEnd);
        var resultMap = markup[mapStart..mapEnd];

        Assert.Contains("Settings_DataImportResultRestored", resultMap);
        Assert.Contains("Settings_DataImportResultMergeApplied", resultMap);
        Assert.Contains("Settings_DataImportResultMergeNoChange", resultMap);
        Assert.Contains("Settings_DataImportTargetNotEmpty", resultMap);
        Assert.Contains("Settings_DataImportValidationFailure", resultMap);
        Assert.Contains("Settings_DataImportFailureCancelled", resultMap);

        var codeMapStart = mapEnd;
        var codeMapEnd = markup.IndexOf("private async Task DisposePortableImportSelectionAsync", codeMapStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(codeMapStart, codeMapEnd);
        var codeMap = markup[codeMapStart..codeMapEnd];

        Assert.Contains("BackupErrorCodes.ActiveWorkflowUnsupported => \"Settings_DataImportFailureActiveWorkflow\"", codeMap);
        Assert.Contains("MergeWriterErrorCodes.StalePlan => \"Settings_DataImportFailureStalePlan\"", codeMap);
        Assert.Contains("Settings_DataImportFailureSafetyCopy", codeMap);

        // No raw error code or exception text is ever interpolated into user-visible output.
        Assert.DoesNotContain("@result.ErrorCode", markup);
        Assert.DoesNotContain("exception.Message", markup);
    }

    [TestMethod]
    public void Settings_PortableDataExportBehaviorIsUnchangedBySliceNineImportPreviewWork()
    {
        var markup = LoadUi("Settings.razor");

        var exportStart = markup.IndexOf("private async Task ExportPortableDataAsync()", StringComparison.Ordinal);
        var exportEnd = markup.IndexOf("private async Task ChoosePortableImportAsync()", exportStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, exportStart);
        Assert.IsGreaterThan(exportStart, exportEnd);
        var exportMethod = markup[exportStart..exportEnd];

        Assert.Contains("PortableArchiveFiles.ExportAsync(", exportMethod);
        Assert.Contains("BackupService.CreatePortableArchiveAsync,", exportMethod);
        Assert.Contains("Settings_DataExportSuccess", exportMethod);
        Assert.Contains("Settings_DataExportCancelled", exportMethod);
        Assert.Contains("Settings_DataExportFailure", exportMethod);
    }

    [TestMethod]
    public void Settings_OfferOnlineDictionaryActivationWithDisclosureWhenConsentIsAbsent()
    {
        var markup = LoadUi("Settings.razor");

        var sectionStart = markup.IndexOf("id=\"online-lookup-title\"", StringComparison.Ordinal);
        var sectionEnd = markup.IndexOf("<section class=\"settings-card\" aria-labelledby=\"portable-data-title\">", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sectionStart);
        Assert.IsGreaterThan(sectionStart, sectionEnd);
        var section = markup[sectionStart..sectionEnd];

        Assert.Contains("Prepare_OnlineDisclosureTitle", section);
        Assert.Contains("Prepare_OnlineDisclosure", section);
        Assert.Contains("Settings_ActivateOnlineConsent", section);
        Assert.Contains("ActivateOnlineConsent", section);
        Assert.Contains("Settings_RevokeOnlineConsent", section);
        Assert.Contains("RevokeOnlineConsent", section);
        Assert.Contains("AppSettings.HasOnlineLookupConsent", section);

        Assert.Contains("AppSettings.GrantOnlineLookupConsent()", markup);
        Assert.Contains("_onlineConsentActivated = true", markup);
        Assert.Contains("_onlineConsentRevoked = false", markup);
    }

    [TestMethod]
    public void PortableArchive_NeverCapturesOrRestoresOnlineLookupConsentOrPreferences()
    {
        var snapshotRepository = LoadUi("BackupSnapshotRepository.cs");
        var importRepository = LoadUi("BackupImportRepository.cs");

        Assert.DoesNotContain("Consent", snapshotRepository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Consent", importRepository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preferences.Default", snapshotRepository);
        Assert.DoesNotContain("Preferences.Default", importRepository);
    }

    [TestMethod]
    public void LexicalDiagnostics_AreGuardedAndExposeOnlyTheRequestedSettingsActions()
    {
        var settings = LoadUi("Settings.razor");

        Assert.Contains("#if DEBUG || KNOWNFIRST_DIAGNOSTICS", settings);
        Assert.Contains("Diagnostics_CopyDiagnosticReport", settings);
        Assert.Contains("Diagnostics_ExportDiagnosticLog", settings);
        Assert.Contains("Diagnostics_ClearDiagnosticLog", settings);
    }

    [TestMethod]
    public void DebugLearningTools_AreClearlyMarkedAndExcludedFromRelease()
    {
        var diagnostics = LoadUi("Diagnostics.razor");
        var settings = LoadUi("Settings.razor");
        var preparation = LoadUi("PrepareWords.razor");
        var review = LoadUi("ReviewWords.razor");
        var navigation = LoadUi("NavMenu.razor");
        var styles = LoadUi("app.css");
        var project = LoadUi("KnownFirst.csproj");
        var startup = LoadUi("MauiProgram.cs");

        Assert.Contains("@inject DebugLearningClock DebugLearningClock", diagnostics);
        Assert.Contains("Diagnostics_DebugLearningTools", diagnostics);
        Assert.Contains("AdvanceDebugTime", diagnostics);
        Assert.Contains("MakeAllCardsDue", diagnostics);
        Assert.Contains("ResetDebugTime", diagnostics);
        Assert.Contains("class=\"debug-label\"", diagnostics);
        Assert.Contains("class=\"button button-debug\"", diagnostics);
        Assert.Contains("settings-card debug-tools", settings);
        Assert.Contains("lookup-diagnostics debug-tools", preparation);
        Assert.Contains("review-debug-tools debug-tools", review);
        Assert.Contains("class=\"button button-debug\"", review);
        Assert.Contains("nav-link debug-nav-link", navigation);
        Assert.Contains("Diagnostics_DebugLabel", navigation);
        Assert.Contains(".button-debug", styles);
        Assert.Contains("--color-debug-text", styles);
        Assert.Contains("#if DEBUG", startup);
        Assert.Contains("AddSingleton<DebugLearningClock>", startup);
        Assert.Contains("AddSingleton<IClock, SystemClock>", startup);
        Assert.Contains("<Compile Remove=\"Services\\Study\\DebugLearningClock.cs\" />", project);
        Assert.Contains("Condition=\"'$(Configuration)' != 'Debug'\"", project);
    }

    [TestMethod]
    public void GuiTestMatrix_DefinesEveryRequiredViewportStateCheckAndScenario()
    {
        var matrix = LoadDocument("GUI_TEST_MATRIX.md");
        var viewports = new[]
        {
            "1440 × 900",
            "1280 × 900",
            "960 × 900",
            "600 × 900",
            "480 × 900",
            "412 × 915",
            "360 × 800",
            "320 × 700"
        };
        var states = new[]
        {
            "Home",
            "Text Import — Empty",
            "Text Import — Long Text",
            "Vocabulary Review",
            "Preparation — Mode Selection",
            "Preparation — Online Loading",
            "Preparation — Automatic Result",
            "Preparation — Manual Editing",
            "Preparation — Choose Another Meaning",
            "Preparation — Finish",
            "Learning — Answer Hidden",
            "Learning — Answer Visible",
            "Learning — Source Details Expanded",
            "Settings",
            "Settings — Reset Confirmation",
            "Diagnostics",
            "Burger Menu — Open",
            "Burger Menu — Closed"
        };
        var checks = new[]
        {
            "Setup",
            "Action",
            "Expected visible state",
            "Overlap check",
            "Spacing check",
            "Scroll check",
            "Focus check",
            "Screenshot",
            "Android retest"
        };
        var scenarios = new[]
        {
            "A. House — English definition",
            "B. Tree — English to German",
            "C. Haus — German definition",
            "D. Baum — German to English",
            "E. Existing learning cards plus a new import",
            "F. The same text in English and German",
            "G. Invented word",
            "H. Missing online consent",
            "I. Cache hit and cache miss",
            "J. Close and reopen the app"
        };

        foreach (var viewport in viewports)
        {
            Assert.Contains(viewport, matrix);
        }

        foreach (var state in states)
        {
            Assert.Contains(state, matrix);
        }

        foreach (var check in checks)
        {
            Assert.Contains(check, matrix);
        }

        foreach (var scenario in scenarios)
        {
            Assert.Contains(scenario, matrix);
        }

        Assert.Contains("Run every state at every required viewport", matrix);
        Assert.Contains("Never confirm an automated full-data reset", matrix);
        Assert.Contains("outside the repository", matrix);
    }

    private static string LoadUi(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Ui",
        fileName));

    private static string LoadDocument(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Docs",
        fileName));

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static void AssertConfirmationNavigation(
        string markup,
        string confirmationReference,
        string cancelReference,
        string triggerReference)
    {
        Assert.Contains(confirmationReference, markup);
        Assert.Contains(cancelReference, markup);
        Assert.Contains(triggerReference, markup);
        Assert.Contains("knownFirst.revealElement", markup);
        Assert.IsTrue(
            markup.Contains("knownFirst.focusElement", StringComparison.Ordinal)
            || markup.Contains("FocusAsync", StringComparison.Ordinal));
        Assert.Contains("Escape", markup);
    }
}
