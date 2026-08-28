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
    public void Settings_HasNoUnfinishedSupportControlsOrPlaceholderHandler()
    {
        var markup = LoadUi("Settings.razor");

        Assert.Contains("Settings_ReportBug", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"settings-report-bug-button\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings_SupportKnownFirst", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Common_FeatureComingSoon", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("FeaturePlaceholder", markup, StringComparison.Ordinal);

        Assert.Contains("BuildIdentityService.GetFormattedBuildIdentity()", markup);
    }

    [TestMethod]
    public void Settings_OffersReopenableReleaseNoteHistoryEntryPoint()
    {
        var markup = LoadUi("Settings.razor");

        Assert.Contains("href=\"release-notes\"", markup);
        Assert.Contains("WhatsNew_Title", markup);
        Assert.Contains("BuildIdentityService.GetFormattedBuildIdentity()", markup);

        Assert.DoesNotContain("Settings_SupportKnownFirst", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Common_FeatureComingSoon", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("FeaturePlaceholder", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkSeen", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void MainLayout_MapsReleaseNotesRouteToOwnTitleNotNotFound()
    {
        var markup = LoadUi("MainLayout.razor");

        Assert.Contains("\"release-notes\" => Localizer[\"WhatsNew_Title\"]", markup);
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
    public void Preparation_HasNoContextualConsentGrantOrApiKeyConfiguration()
    {
        var preparation = LoadUi("PrepareWords.razor");
        var settings = LoadUi("Settings.razor");

        Assert.DoesNotContain("ConfirmOnlineLookupAsync", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("GrantOnlineLookupConsent", preparation, StringComparison.Ordinal);
        Assert.Contains("Prepare_OnlineDisclosure", settings);
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
    public void Preparation_HasNoPerItemDirectionCheckboxesAndKeepsSingleSelectMeaningPicker()
    {
        // KF-MEANING-002 runtime/source consistency guard: the tracked preparation page must never grow a
        // per-item direction checkbox pair, or the associated "at least one direction" validation message,
        // that a previously observed runtime did not derive from this repository. Card direction stays a
        // single global AppSettings.CardDirection value fed straight into PreparationService.AcceptAsync.
        var markup = LoadUi("PrepareWords.razor");

        Assert.DoesNotContain("Auf Karteikarten verwenden", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Use on flashcards", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Begriff → Definition", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Definition → Begriff", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Term → Definition", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Definition → Term", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("mindestens eine Richtung", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("at least one direction", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"checkbox\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectAllMeanings", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("select all meanings", markup, StringComparison.OrdinalIgnoreCase);

        var acceptCallStart = markup.IndexOf("await PreparationService.AcceptAsync(", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, acceptCallStart);
        var acceptCallEnd = markup.IndexOf(");", acceptCallStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(acceptCallStart, acceptCallEnd);
        var acceptCall = markup[acceptCallStart..acceptCallEnd];
        Assert.Contains("AppSettings.CardDirection", acceptCall);

        // The meaning picker remains single-select: one selected index, applied through ApplyMeaning.
        Assert.Contains("private int _selectedMeaningIndex", markup);
        Assert.Contains("_selectedMeaningIndex = meaningIndex", markup);
        Assert.Contains("ApplyMeaning(_item.Result.Meanings[meaningIndex])", markup);

        // A concise explanation near the alternative-meaning control states that only the selected
        // meaning is saved.
        Assert.Contains("Prepare_ChooseOneMeaningExplanation", markup);
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
    public void Preparation_ContextStateResetsOnItemChangeAndCannotThrowOrGoStale()
    {
        // KF-MEANING-002 UI state-safety regression: _contextIndex must reset whenever a new candidate is
        // loaded, a stale in-flight lookup must never overwrite the page with a superseded result, and
        // context slicing must never throw on invalid coordinates.
        var markup = LoadUi("PrepareWords.razor");

        Assert.AreEqual(4, CountOccurrences(markup, "_contextIndex = 0;"));
        Assert.Contains("Math.Clamp(_contextIndex, 0, _item.Contexts.Count - 1)", markup);
        Assert.Contains("private static bool IsContextBoundsValid(PreparationContext context)", markup);
        Assert.Contains("CurrentContext is not null && IsContextBoundsValid(CurrentContext)", markup);
        Assert.Contains("ReferenceEquals(_lookupCancellation, cancellation)", markup);
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
    public void Preparation_ManualEditorUsesContextualPrimaryAnswersAndBoundedLegacyException()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("@onclick=\"EnableManualEntryAsync\"", markup);
        Assert.Contains("PreparationCandidateStatus.Failed && !_showEditor", markup);
        var definitionStart = markup.IndexOf("@if (PrimaryAnswerMode == LexicalLookupMode.Definition)", StringComparison.Ordinal);
        var translationStart = markup.IndexOf("else if (PrimaryAnswerMode == LexicalLookupMode.Translation)", definitionStart, StringComparison.Ordinal);
        var combinedStart = markup.IndexOf("data-legacy-combined-answer=\"definition\"", translationStart, StringComparison.Ordinal);
        var advancedStart = markup.IndexOf("<details class=\"preparation-advanced-options\">", combinedStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, definitionStart);
        Assert.IsGreaterThan(definitionStart, translationStart);
        Assert.IsGreaterThan(translationStart, combinedStart);
        Assert.IsGreaterThan(combinedStart, advancedStart);

        var definitionBlock = markup[definitionStart..translationStart];
        var translationBlock = markup[translationStart..combinedStart];
        var combinedBlock = markup[combinedStart..advancedStart];
        Assert.AreEqual(1, CountOccurrences(definitionBlock, "<textarea"));
        Assert.Contains("data-primary-answer=\"definition\"", definitionBlock);
        Assert.DoesNotContain("Prepare_Translation", definitionBlock);
        Assert.AreEqual(1, CountOccurrences(translationBlock, "<textarea"));
        Assert.Contains("data-primary-answer=\"translation\"", translationBlock);
        Assert.DoesNotContain("Prepare_Definition", translationBlock);
        Assert.AreEqual(2, CountOccurrences(combinedBlock, "<textarea"));
        Assert.Contains("data-legacy-combined-answer=\"definition\"", combinedBlock);
        Assert.Contains("data-legacy-combined-answer=\"translation\"", combinedBlock);

        Assert.Contains("PreparationService.AcceptAsync", markup);
        Assert.Contains("ManualInputMode:", markup);
        Assert.Contains("knownFirst.revealElement", markup);
        Assert.Contains("knownFirst.focusElement", markup);
    }

    [TestMethod]
    public void Preparation_ManualEditorHidesRedundantMetadataAndKeepsOnlyApplicableAdvancedOptions()
    {
        var markup = LoadUi("PrepareWords.razor");
        var editorStart = markup.IndexOf("<div class=\"editor-grid\"", StringComparison.Ordinal);
        var editorEnd = markup.IndexOf("else if (_item.Result?.HasUsableData == true)", editorStart, StringComparison.Ordinal);
        var editor = markup[editorStart..editorEnd];

        Assert.DoesNotContain("Prepare_CanonicalTerm", editor);
        Assert.DoesNotContain("Prepare_EncounteredForm", editor);
        Assert.DoesNotContain("readonly", editor);
        Assert.DoesNotContain("Prepare_AdditionalNote", editor);
        Assert.Contains("<details class=\"preparation-advanced-options\">", editor);
        Assert.Contains("IsAcronymOrAbbreviation", editor);
        Assert.Contains("Prepare_AcronymExpansion", editor);
        Assert.Contains("Prepare_AcceptedAliases", editor);
        Assert.Contains("Prepare_AcceptedAliasesHelp", editor);
        Assert.Contains("Prepare_AdvancedOptions", editor);
    }

    [TestMethod]
    public void Preparation_ModeSpecificValidationIsAdjacentReferencedRevealedAndFocused()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("id=\"prepare-definition-error\"", markup);
        Assert.Contains("Prepare_DefinitionRequired", markup);
        Assert.Contains("aria-describedby=\"@(DefinitionInvalid ? \"prepare-definition-error\" : null)\"", markup);
        Assert.Contains("id=\"prepare-translation-error\"", markup);
        Assert.Contains("Prepare_TranslationRequired", markup);
        Assert.Contains("aria-describedby=\"@(TranslationInvalid ? \"prepare-translation-error\" : null)\"", markup);
        Assert.Contains("Prepare_AnswerRequired", markup);
        Assert.Contains("PreparationInputValidationException", markup);
        Assert.Contains("ShowInputValidationAsync", markup);
        Assert.Contains("GetPrimaryAnswerFocusTarget", markup);
        Assert.Contains("ClearInputValidationIfCorrected", markup);
    }

    [TestMethod]
    public void Preparation_SaveFailureAndPostCommitProgressionFailureAreDistinctAndRetrySafe()
    {
        var markup = LoadUi("PrepareWords.razor");
        var accept = ExtractMethodBody(markup, "private async Task AcceptAsync()");
        var recovery = ExtractMethodBody(markup, "private async Task ShowProgressionRecoveryAsync()");
        var retry = ExtractMethodBody(markup, "private async Task RetryProgressionAfterSaveAsync()");

        Assert.Contains("await PreparationService.AcceptAsync", accept);
        Assert.Contains("_saveFailed = true", accept);
        Assert.Contains("_progressionCoordinator.CommitAcceptAndProgressAsync", accept);
        Assert.Contains("_progressionAfterSaveFailed = true", recovery);
        Assert.Contains("Prepare_SavedNextLoadFailed", markup);
        Assert.Contains("Prepare_RetryNextItem", markup);
        Assert.Contains("_progressionCoordinator.RetryProgressionAsync", retry);
        Assert.DoesNotContain("PreparationService.AcceptAsync", retry);
    }

    [TestMethod]
    public void Preparation_InjectsLearningService_AndConsumesReadinessOnAccept_ForLearningTransition()
    {
        var markup = LoadUi("PrepareWords.razor");
        Assert.Contains("@inject ILearningService LearningService", markup);

        var accept = ExtractMethodBody(markup, "private async Task AcceptAsync()");
        Assert.Contains("LearningService.GetPreparationReadinessAsync()", accept);
        Assert.Contains("Navigation.NavigateTo(\"/learn\")", accept);
        Assert.Contains("LoadAfterCommittedActionAsync", accept);

        var skip = ExtractMethodBody(markup, "private async Task SkipAsync()");
        Assert.DoesNotContain("LearningService", skip);
        Assert.DoesNotContain("/learn", skip);

        var disposition = ExtractMethodBody(markup, "private async Task ConfirmDispositionAsync()");
        Assert.DoesNotContain("LearningService", disposition);
        Assert.DoesNotContain("/learn", disposition);

        Assert.DoesNotContain("LearningService.GetOrStartAsync", markup);
    }

    [TestMethod]
    public async Task PreparationProgressionCoordinator_AcceptThenLoadFailureRetryDoesNotAcceptAgain()
    {
        var coordinator = new KnownFirst.Models.PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var progressionCalls = 0;
        var failNextLoad = true;

        Task AcceptAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task LoadNextAsync(KnownFirst.Core.Preparation.PreparationMethod method)
        {
            Assert.AreEqual(KnownFirst.Core.Preparation.PreparationMethod.Manual, method);
            progressionCalls++;
            if (failNextLoad)
            {
                throw new InvalidOperationException("synthetic next-item load failure");
            }

            return Task.CompletedTask;
        }

        var firstProgressionSucceeded = await coordinator.CommitAndProgressAsync(
            KnownFirst.Core.Preparation.PreparationMethod.Manual,
            AcceptAsync,
            LoadNextAsync);

        Assert.IsFalse(firstProgressionSucceeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsTrue(coordinator.ProgressionFailed);
        Assert.IsNotNull(coordinator.LastProgressionException);
        Assert.AreEqual(1, acceptCalls);
        Assert.AreEqual(1, progressionCalls);

        failNextLoad = false;
        var retrySucceeded = await coordinator.RetryProgressionAsync(LoadNextAsync);

        Assert.IsTrue(retrySucceeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.IsNull(coordinator.LastProgressionException);
        Assert.AreEqual(1, acceptCalls, "retry must never invoke acceptance again");
        Assert.AreEqual(2, progressionCalls);
    }

    [TestMethod]
    public void Preparation_EndConfirmationHidesCompetingDispositionActions()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("@if (_confirmationAction is null && !_cancelConfirmationVisible)", markup);
        Assert.Contains("Prepare_MarkKnown", markup);
        Assert.Contains("Prepare_DoNotLearn", markup);
        Assert.Contains("@onclick=\"SkipAsync\"", markup);
    }

    [TestMethod]
    public void Preparation_ProgressionRecoveryIsRevealedAndRetryReceivesPostRenderFocus()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("@ref=\"_progressionRecoveryRef\"", markup);
        Assert.Contains("@ref=\"_progressionRetryButtonRef\"", markup);
        Assert.Contains("_focusProgressionRetryAfterRender", markup);
        Assert.Contains("RevealProgressionRecoveryAsync", markup);
        Assert.Contains("knownFirst.revealElement\", _progressionRecoveryRef", markup);
        Assert.Contains("knownFirst.focusElement\", _progressionRetryButtonRef", markup);
        Assert.Contains("if (_isDisposed", markup);
    }

    [TestMethod]
    public void Preparation_MobileActionRulesHaveNoEquivalentDuplicateBlock()
    {
        var styles = LoadUi("PrepareWords.razor.css");

        Assert.AreEqual(2, CountOccurrences(styles, ".preparation-action-bar {"), "one base and one mobile rule are expected");
        Assert.AreEqual(1, CountOccurrences(styles, ".end-preparation {"), "the mobile End Preparation rule must appear once");
    }

    [TestMethod]
    public void Preparation_SuccessfulManualProgressionTargetsNextPrimaryAnswerField()
    {
        var markup = LoadUi("PrepareWords.razor");
        var moveNext = ExtractMethodBody(markup, "private async Task MoveNextAsync(PreparationMethod method)");

        Assert.Contains("PreparationMethod.Manual", moveNext);
        Assert.Contains("knownFirst.revealElement", moveNext);
        Assert.Contains("GetPrimaryAnswerFocusTarget()", moveNext);
        Assert.Contains("knownFirst.focusElement", moveNext);
    }

    [TestMethod]
    public void Preparation_BackPausesWhileEndPreparationEndsTheActiveBatch()
    {
        var markup = LoadUi("PrepareWords.razor");
        var service = LoadUi("PreparationService.cs");

        Assert.Contains("<PageHeader", markup);
        Assert.Contains("RequestCancelPreparationAsync", markup);
        Assert.Contains("Prepare_EndPreparationConfirmation", markup);
        Assert.Contains("PreparationService.CancelActiveSessionAsync", markup);
        Assert.Contains("PreparationSessionStatus.Cancelled", service);
        Assert.Contains("PreparationCandidateStatus.Cancelled", service);
        Assert.Contains("PreparationState.Unprepared", service);

        var actionBar = markup.IndexOf("<div class=\"workflow-action-bar\">", StringComparison.Ordinal);
        var endAction = markup.IndexOf("Prepare_EndPreparation", actionBar, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, actionBar);
        Assert.IsGreaterThan(actionBar, endAction);
        Assert.Contains("button button-secondary end-preparation", markup);
        Assert.DoesNotContain("button button-danger end-preparation", markup);
        Assert.Contains("class=\"preparation-end-confirmation\"", markup);
    }

    [TestMethod]
    public void DestructiveConfirmations_HideTriggersAndUseNeutralThenDangerActions()
    {
        var home = LoadUi("Home.razor");
        var review = LoadUi("ReviewWords.razor");
        var preparation = LoadUi("PrepareWords.razor");
        var learning = LoadUi("Learn.razor");
        var settings = LoadUi("Settings.razor");

        Assert.Contains("if (_confirmationAction is null && !_cancelConfirmationVisible)", preparation);
        Assert.Contains("class=\"destructive-confirmation preparation-confirmation\"", preparation);
        Assert.DoesNotContain("button button-danger end-preparation", preparation);
        Assert.Contains("button button-secondary", home);
        Assert.Contains("button button-secondary", review);
        Assert.Contains("button button-secondary", learning);
        Assert.Contains("button button-secondary", settings);
        Assert.Contains("if (!_showDiscardConfirmation)", review);
        Assert.Contains("_candidate is not null", review);
        Assert.Contains("_card is not null && !_showPermanentKnownConfirmation", learning);
        Assert.Contains("_item is not null && _confirmationAction is null", preparation);
        Assert.Contains("data-destructive-confirm", home);
        Assert.Contains("data-destructive-confirm", review);
        Assert.AreEqual(1, CountOccurrences(preparation, "data-destructive-confirm"));
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
    public void Preparation_RevealsRetryAndManualEditorWithPrimaryFocusTargets()
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
        Assert.Contains("GetPrimaryAnswerFocusTarget", markup);
    }

    [TestMethod]
    public void Preparation_ActionsKeepPermanentKnownAndDoNotLearnSeparateAndConfirmed()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("Prepare_MarkKnown", markup);
        Assert.Contains("Prepare_DoNotLearn", markup);
        Assert.Contains("ShowMarkKnownConfirmation", markup);
        Assert.Contains("ShowExcludeConfirmation", markup);
        Assert.Contains("PreparationService.MarkKnownAsync", markup);
        Assert.Contains("PreparationService.ExcludeAsync", markup);
        Assert.Contains("class=\"destructive-confirmation preparation-confirmation\"", markup);
    }

    [TestMethod]
    public void ImportAndPreparationUseTheSameSharedMultilineVisualPrimitive()
    {
        var importMarkup = LoadUi("ImportText.razor");
        var preparationMarkup = LoadUi("PrepareWords.razor");
        var importStyles = LoadUi("ImportText.razor.css");
        var appStyles = LoadUi("app.css");

        Assert.Contains("class=\"text-area\"", importMarkup);
        Assert.Contains("class=\"text-area\"", preparationMarkup);
        Assert.Contains(".text-area {", appStyles);
        Assert.Contains("border: 1px solid var(--color-input-border);", appStyles);
        Assert.Contains("background: var(--color-input-background);", appStyles);
        Assert.Contains("resize: vertical;", appStyles);
        Assert.Contains("overflow-y: auto;", appStyles);
        Assert.Contains(".text-area:focus-visible", appStyles);
        Assert.Contains(".text-area:disabled", appStyles);
        Assert.Contains("min-height: 16rem;", importStyles);
        Assert.DoesNotContain("background: var(--color-input-background);", importStyles);
        Assert.DoesNotContain("border: 1px solid var(--color-input-border);", importStyles);
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
    public void MobileReview_HasCollapsedMetadataAndResponsiveActionBar()
    {
        var markup = LoadUi("ReviewWords.razor");
        var styles = LoadUi("ReviewWords.razor.css");
        var appStyles = LoadUi("app.css");

        Assert.Contains("<details class=\"candidate-details-panel\">", markup);
        Assert.DoesNotContain("<details class=\"candidate-details-panel\" open", markup);
        Assert.Contains("review-action-bar", markup);
        Assert.Contains("Review_Saving", markup);
        Assert.Contains(".review-actions-layout", styles);
        Assert.Contains("@media (max-width: 620px)", styles);
        Assert.Contains("env(safe-area-inset-bottom)", appStyles);
        Assert.DoesNotContain("env(safe-area-inset-bottom)", styles);
    }

    [TestMethod]
    public void ReviewWords_DerivedCandidateShowsSourceCompoundNoticeOutsideCollapsedDetails()
    {
        var markup = LoadUi("ReviewWords.razor");

        Assert.Contains("CandidateProvenanceKind.DerivedFromCompound", markup);
        Assert.Contains("_candidate.DerivationEvidence.Count > 0", markup);
        Assert.Contains("class=\"derivation-notice\"", markup);
        Assert.Contains("Review_DerivedFromCompound", markup);
        Assert.Contains("SourceSurfaceForm", markup);
        Assert.DoesNotContain("SourceIdentity", markup);

        var derivationNoticeIndex = markup.IndexOf("derivation-notice", StringComparison.Ordinal);
        var detailsPanelIndex = markup.IndexOf("<details class=\"candidate-details-panel\">", StringComparison.Ordinal);
        Assert.IsTrue(derivationNoticeIndex >= 0, "The derivation notice markup must exist.");
        Assert.IsTrue(detailsPanelIndex >= 0, "The collapsed details panel must exist.");
        Assert.IsTrue(
            derivationNoticeIndex < detailsPanelIndex,
            "The derivation notice must appear before the collapsed details panel so it is visible without expanding it.");
    }

    [TestMethod]
    public void ReviewWords_DiscardActionIsLocalizedAndPositionedInWorkflowEndActionArea()
    {
        var markup = LoadUi("ReviewWords.razor");
        var styles = LoadUi("ReviewWords.razor.css");

        Assert.Contains("Localizer[\"Review_DiscardAction\"]", markup);
        Assert.DoesNotContain("Localizer[\"Review_Discard\"]", markup);
        Assert.DoesNotContain("\"Review_Discard\"", markup);
        Assert.DoesNotContain("class=\"discard-import-section\"", markup);

        var actionBarIndex = markup.IndexOf("<div class=\"workflow-action-bar\">", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, actionBarIndex);

        var discardActionIndex = markup.IndexOf("Review_DiscardAction", actionBarIndex, StringComparison.Ordinal);
        Assert.IsGreaterThan(actionBarIndex, discardActionIndex);

        Assert.Contains("button button-danger end-review-discard", markup);

        var confirmationIndex = markup.IndexOf("class=\"destructive-confirmation", actionBarIndex, StringComparison.Ordinal);
        Assert.IsGreaterThan(actionBarIndex, confirmationIndex);

        Assert.Contains("role=\"alertdialog\"", markup);
        Assert.Contains("aria-labelledby=\"review-discard-confirmation-title\"", markup);
        Assert.Contains("Localizer[\"Review_DiscardTitle\"]", markup);
        Assert.Contains("Localizer[\"Review_DiscardMessage\"]", markup);
        Assert.Contains("Localizer[\"Common_Cancel\"]", markup);
        Assert.Contains("Localizer[\"Review_DiscardConfirm\"]", markup);
        Assert.Contains("data-destructive-confirm", markup);

        Assert.Contains("TextReviewService.DiscardActiveImportAsync()", markup);
        Assert.Contains("WordStatus.Known", markup);
        Assert.Contains("WordStatus.UnknownBacklog", markup);
        Assert.Contains("UndoAsync", markup);
        Assert.Contains("Review_Undo", markup);

        Assert.Contains(".review-action-bar", styles);
        Assert.Contains(".review-actions-layout", styles);
        Assert.Contains("@media (max-width: 620px)", styles);
    }

    [TestMethod]
    public void ReviewWordsCompactActionBar_CommonStandardButtonStructureAndHierarchy()
    {
        var markup = LoadUi("ReviewWords.razor");

        Assert.Contains("class=\"review-actions-layout\"", markup);
        Assert.Contains("class=\"review-primary-actions button-row\"", markup);
        Assert.DoesNotContain("review-actions-grid", markup);
        Assert.DoesNotContain("decision-grid", markup);
        Assert.DoesNotContain("undo-container", markup);
        Assert.DoesNotContain("undo-button", markup);

        var layoutIndex = markup.IndexOf("class=\"review-actions-layout\"", StringComparison.Ordinal);
        var primaryActionsIndex = markup.IndexOf("class=\"review-primary-actions button-row\"", layoutIndex, StringComparison.Ordinal);
        var knownIndex = markup.IndexOf("WordStatus.Known", primaryActionsIndex, StringComparison.Ordinal);
        var unknownIndex = markup.IndexOf("WordStatus.UnknownBacklog", primaryActionsIndex, StringComparison.Ordinal);
        var undoIndex = markup.IndexOf("UndoAsync", primaryActionsIndex, StringComparison.Ordinal);
        var discardIndex = markup.IndexOf("Review_DiscardAction", layoutIndex, StringComparison.Ordinal);

        Assert.IsTrue(layoutIndex >= 0, "review-actions-layout must exist");
        Assert.IsTrue(primaryActionsIndex > layoutIndex, "review-primary-actions button-row must exist within layout");
        Assert.IsTrue(knownIndex > primaryActionsIndex && unknownIndex > knownIndex && undoIndex > unknownIndex && discardIndex > undoIndex,
            "Semantic ordering must be Known -> Unknown -> Undo -> Discard import");

        Assert.Contains("button button-primary", markup);
        Assert.Contains("button button-secondary", markup);
        Assert.Contains("button button-danger end-review-discard", markup);

        var undoSliceStart = markup.IndexOf("@onclick=\"UndoAsync\"", StringComparison.Ordinal);
        var undoButtonTagStart = markup.LastIndexOf("<button", undoSliceStart, StringComparison.Ordinal);
        var undoButtonTag = markup[undoButtonTagStart..undoSliceStart];
        Assert.Contains("class=\"button button-secondary\"", undoButtonTag);
        Assert.Contains("disabled=\"@(!_candidate.CanUndo || _isSaving)\"", undoButtonTag);

        Assert.Contains("@ref=\"_discardButtonRef\"", markup);
    }

    [TestMethod]
    public void ReviewWordsCompactActionBar_DesktopCssContract()
    {
        var styles = LoadUi("ReviewWords.razor.css");

        Assert.Contains(".review-actions-layout {", styles);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto;", styles);
        Assert.Contains("align-items: center;", styles);
        Assert.Contains(".review-primary-actions {", styles);
        Assert.Contains("display: flex;", styles);
        Assert.Contains("flex-wrap: wrap;", styles);

        Assert.DoesNotContain("repeat(4, minmax(0, 1fr))", styles);
        Assert.DoesNotContain("grid-auto-rows: 1fr;", styles);
        Assert.DoesNotContain(".review-actions-grid", styles);
        Assert.DoesNotContain(".decision-grid", styles);
        Assert.DoesNotContain(".undo-container", styles);
        Assert.DoesNotContain(".undo-button", styles);
    }

    [TestMethod]
    public void ReviewWordsCompactActionBar_ResponsiveContract()
    {
        var styles = LoadUi("ReviewWords.razor.css");

        Assert.Contains("@media (max-width: 620px)", styles);
        Assert.Contains(".review-actions-layout {", styles);
        Assert.Contains("grid-template-columns: 1fr;", styles);
        Assert.Contains(".end-review-discard {", styles);
        Assert.Contains("width: 100%;", styles);

        Assert.DoesNotContain(".review-actions-grid", styles);
    }

    [TestMethod]
    public void ReviewWordsCompactActionBar_BehaviorContractsPreserved()
    {
        var markup = LoadUi("ReviewWords.razor");

        Assert.Contains("aria-keyshortcuts=\"Enter\"", markup);
        Assert.Contains("title=\"@Localizer[\"Review_Known\"] (Enter)\"", markup);
        Assert.Contains("@onclick=\"() => DecideAsync(WordStatus.Known)\"", markup);
        Assert.Contains("@onclick=\"() => DecideAsync(WordStatus.UnknownBacklog)\"", markup);
        Assert.Contains("@onclick=\"UndoAsync\"", markup);
        Assert.Contains("@onclick=\"ShowDiscardConfirmationAsync\"", markup);
        Assert.Contains("role=\"alertdialog\"", markup);
        Assert.Contains("data-destructive-confirm", markup);
        Assert.Contains("TextReviewService.DiscardActiveImportAsync()", markup);
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
    public void MobileMenu_BackgroundLockingRemainsActiveBelowDesktopBreakpointOnly()
    {
        var styles = LoadUi("MainLayout.razor.css").Replace("\r\n", "\n", StringComparison.Ordinal);
        const string lockSelector = ".page.menu-open main,\n    .page.menu-open .content";

        var compactBreakpoint = styles.IndexOf("@media (max-width: 799px)", StringComparison.Ordinal);
        var lockRule = styles.IndexOf(lockSelector, StringComparison.Ordinal);
        var desktopBreakpoint = styles.IndexOf("@media (min-width: 800px)", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, compactBreakpoint, "The compact shell breakpoint must be explicit.");
        Assert.IsGreaterThan(compactBreakpoint, lockRule, "Menu locking must be inside the compact breakpoint.");
        Assert.IsGreaterThan(lockRule, desktopBreakpoint, "Menu locking must end before desktop shell rules begin.");
        Assert.Contains("overflow: hidden;", ExtractCssRule(styles, lockSelector));
        Assert.AreEqual(1, CountOccurrences(styles, "@media (max-width: 799px)"));
        Assert.AreEqual(1, CountOccurrences(styles, lockSelector));
    }

    [TestMethod]
    public void JavaScriptInterop_RegistersAndDisposesDesktopEntryMatchMediaBridge()
    {
        var script = LoadUi("knownfirst.js");

        Assert.Contains("registerDesktopEntry", script);
        Assert.Contains("unregisterDesktopEntry", script);
        Assert.Contains("window.matchMedia(\"(min-width: 800px)\")", script);
        Assert.Contains("mediaQuery.addEventListener(\"change\", handler)", script);
        Assert.Contains("mediaQuery.removeEventListener(\"change\", handler)", script);
        Assert.Contains("if (!event.matches)", script);
        Assert.Contains("invokeMethodAsync(\"OnDesktopBreakpointEntered\")", script);
        Assert.DoesNotContain("window.addEventListener(\"resize\"", script);
    }

    [TestMethod]
    public void MainLayout_DesktopEntryReconcilesDrawerThroughCloseMenuAndDisposesInterop()
    {
        var layout = LoadUi("MainLayout.razor");
        var desktopEntry = ExtractMethodBody(
            layout,
            "public Task OnDesktopBreakpointEnteredAsync()");
        var disposal = ExtractMethodBody(
            layout,
            "public async ValueTask DisposeAsync()");

        Assert.Contains("@implements IAsyncDisposable", layout);
        Assert.Contains("@inject IJSRuntime JavaScript", layout);
        Assert.Contains("DotNetObjectReference.Create(this)", layout);
        Assert.Contains("knownFirst.breakpoints.registerDesktopEntry", layout);
        Assert.Contains("[JSInvokable(\"OnDesktopBreakpointEntered\")]", layout);
        Assert.Contains("CloseMenu();", desktopEntry);
        Assert.Contains("StateHasChanged();", desktopEntry);
        Assert.Contains("knownFirst.breakpoints.unregisterDesktopEntry", disposal);
        Assert.Contains("CloseMenu();", disposal);
        Assert.Contains("_desktopBreakpointReference?.Dispose();", disposal);
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
    public void KnownFirstProject_EmbedsGermanLexiconBundleExactlyOnceUnderExplicitResourceName()
    {
        var project = LoadUi("KnownFirst.csproj");

        Assert.AreEqual(
            1,
            CountOccurrences(project, "<EmbeddedResource Include=\"KnownFirst.Core\\Text\\German\\Assets\\german-lexicon.v2.kfgl\""),
            "KnownFirst.csproj must reference the German lexicon bundle exactly once.");
        Assert.Contains(
            $"LogicalName=\"{KnownFirst.Services.Lexical.BundledGermanLexicon.ResourceName}\"",
            project);

        // Not a MauiAsset: MauiAsset ships a loose file read via async platform FileSystem APIs.
        // The bundle must instead be an EmbeddedResource so the production wrapper can open it
        // with one explicit, synchronous, name-based stream open — no async-over-sync on the UI
        // thread, no platform filesystem assumptions, no resource/assembly scanning.
        Assert.DoesNotContain("<MauiAsset Include=\"KnownFirst.Core", project, StringComparison.Ordinal);
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
    public void StartupSmokeLauncher_ExposesOneUnambiguousExitCodeEvenWhenEvidenceWritingFails()
    {
        // Part 2 review finding: the launcher's evidence-writing (summary.json/report.zip) must
        // never be able to prevent the single, authoritative $host.SetShouldExit($exitCode) call
        // from running with the already-determined scenario result.
        var script = LoadUi("run-scenario-startup-smoke.ps1");

        var finallyStart = script.IndexOf("finally {", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, finallyStart);
        var setShouldExitIndex = script.IndexOf("$host.SetShouldExit($exitCode)", StringComparison.Ordinal);
        Assert.IsGreaterThan(finallyStart, setShouldExitIndex);

        var evidenceTryIndex = script.IndexOf("try {", finallyStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, evidenceTryIndex);
        Assert.IsGreaterThan(evidenceTryIndex, setShouldExitIndex);
        var evidenceCatchIndex = script.IndexOf("catch {", evidenceTryIndex, StringComparison.Ordinal);
        Assert.IsGreaterThan(evidenceTryIndex, evidenceCatchIndex);
        Assert.IsGreaterThan(evidenceCatchIndex, setShouldExitIndex);

        // The script's final non-empty line is the single, authoritative exit-code statement,
        // reached only after the finally block (including its own try/catch) has closed.
        Assert.EndsWith("$host.SetShouldExit($exitCode)", script.TrimEnd());
    }

    [TestMethod]
    public void GuiTestScenarioRegistry_RegistersEveryScenarioAndKeepsTheLauncherDispatchInSync()
    {
        // scenarios.json is the scenario registry. A scenario that exists as a script but is not
        // registered - or is registered but not dispatchable - is invisible to the launcher and can
        // never be run through the canonical entry point.
        var registry = LoadUi("gui-test-scenarios.json");
        var launcher = LoadUi("Invoke-GuiTestRun.ps1");

        // StartupSmoke is dispatched by knownfirst.ps1 through its own standalone script, not by
        // the scenario runner, so it is registered with that script as its implementation.
        Assert.Contains("\"id\": \"StartupSmoke\"", registry);
        Assert.Contains(
            "\"implementationScript\": \"scripts/gui-tests/windows/run-scenario-startup-smoke.ps1\"",
            registry);
        Assert.Contains("[ValidateSet('StartupSmoke')]", LoadUi("knownfirst.ps1"));

        // Every scenario dispatched by the scenario runner must be registered, allowed by the
        // runner's own ValidateSet, and reachable through its dispatch switch.
        foreach (var scenarioId in new[]
                 {
                     "001-import-definition-reset",
                     "PreparationSelectedMeaning",
                     "PreparationAcceptFailureRecovery",
                     "PreparationInvalidContextRecovery",
                 })
        {
            Assert.Contains($"\"id\": \"{scenarioId}\"", registry);
            Assert.Contains(
                $"\"implementationScript\": \"scripts/gui-tests/windows/scenarios/{scenarioId}.ps1\"",
                registry);
            Assert.Contains($"'{scenarioId}'", launcher);
            Assert.Contains($"'{scenarioId}' {{", launcher);
        }

        // Each preparation scenario script must define the dispatcher function the runner calls.
        foreach (var scenarioId in PreparationScenarioIds)
        {
            Assert.Contains($"function Invoke-Scenario{scenarioId}", LoadUi($"{scenarioId}.ps1"));
            Assert.Contains($"Invoke-Scenario{scenarioId} -Context $context", launcher);
        }

        // Debug is the only configuration any GUI scenario may declare.
        Assert.DoesNotContain("\"Release\"", registry);
    }

    [TestMethod]
    public void GuiTestRunLauncher_ExposesOneUnambiguousExitCodeEvenWhenEvidenceWritingFails()
    {
        // Same contract as run-scenario-startup-smoke.ps1: the scenario result is decided first,
        // evidence writing runs inside its own try/catch, and the exit-code statements are the very
        // last thing the script does - so a failed report/summary write can never change or suppress
        // the process exit code the scenario actually earned.
        var script = LoadUi("Invoke-GuiTestRun.ps1");

        var finallyStart = script.IndexOf("finally {", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, finallyStart);

        var evidenceTryIndex = script.IndexOf("try {", finallyStart, StringComparison.Ordinal);
        var evidenceCatchIndex = script.IndexOf("catch {", evidenceTryIndex, StringComparison.Ordinal);
        Assert.IsGreaterThan(finallyStart, evidenceTryIndex);
        Assert.IsGreaterThan(evidenceTryIndex, evidenceCatchIndex);

        var failureExitIndex = script.IndexOf("$host.SetShouldExit(1)", StringComparison.Ordinal);
        var successExitIndex = script.IndexOf("$host.SetShouldExit(0)", StringComparison.Ordinal);
        Assert.IsGreaterThan(evidenceCatchIndex, failureExitIndex);
        Assert.IsGreaterThan(failureExitIndex, successExitIndex);

        // Nothing that could fail, log, or re-decide the result may run after the exit code is set.
        var tail = script[successExitIndex..];
        Assert.DoesNotContain("Write-GuiTestReport", tail);
        Assert.DoesNotContain("Invoke-Scenario", tail);
        Assert.DoesNotContain("Write-RunnerLog", tail);
    }

    [TestMethod]
    public void GuiTestRunnerCore_BlankScreenInvariantChecksProcessErrorBoundaryAndRouteMarker()
    {
        var core = LoadUi("GuiTestRunnerCore.ps1");

        var start = core.IndexOf("function Assert-NoBlankOrErrorState", StringComparison.Ordinal);
        var end = core.IndexOf("function Assert-UiaCondition", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        var body = core[start..end];

        // The shared invariant must fail on an unexpected process exit, on the global ErrorBoundary,
        // and on a missing route/page marker within the bounded transition timeout (which is also how
        // an indefinitely stuck transition spinner is caught - the marker never arrives).
        Assert.Contains("Get-Process -Id $script:TargetPid", body);
        Assert.Contains("'app-error-boundary'", body);
        Assert.Contains("Invoke-UiaWaitFor -Selector $RouteMarkerSelector -TimeoutMs $TransitionTimeoutMs", body);
        Assert.Contains("$processAlive -and (-not $errorBoundaryVisible) -and $routeMarkerPresent", body);

        // Failures must route through Assert-UiaCondition so the standard failure screenshot and
        // assertion record are always produced.
        Assert.Contains("return Assert-UiaCondition", body);

        // Every preparation scenario must actually use the shared invariant.
        foreach (var scenarioId in PreparationScenarioIds)
        {
            Assert.Contains("Assert-NoBlankOrErrorState", LoadUi($"{scenarioId}.ps1"));
        }
    }

    [TestMethod]
    public void PreparationGuiScenarios_StayIsolatedAndScopeFaultInjectionToTheRequestingScenario()
    {
        foreach (var scenarioId in PreparationScenarioIds)
        {
            var script = LoadUi($"{scenarioId}.ps1");

            // The app under test is always pointed at this run's isolated profile, every database
            // assertion reads only that profile's database, and the real data files are fingerprinted
            // before and after the run.
            Assert.Contains("-GuiTestRoot $Context.LiveProfileDir", script);
            Assert.Contains("Join-Path $Context.LiveProfileDir 'knownfirst.db3'", script);
            Assert.Contains("Get-RealKnownFirstDataFingerprint", script);
            Assert.Contains("Test-RealDataUnchanged", script);

            // The seed marker is removed from the runner's own environment as soon as the app has
            // been launched, so it can never leak into a later process.
            Assert.Contains("Remove-Item Env:KNOWNFIRST_GUI_TEST_SEED_SCENARIO", script);
        }

        // Fault injection is requested by exactly one scenario, and is likewise scoped to launch.
        var faultScenario = LoadUi("PreparationAcceptFailureRecovery.ps1");
        Assert.Contains("$env:KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT = 'AfterCardInsert'", faultScenario);
        Assert.Contains("Remove-Item Env:KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT", faultScenario);
        Assert.DoesNotContain("KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT", LoadUi("PreparationSelectedMeaning.ps1"));
        Assert.DoesNotContain("KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT", LoadUi("PreparationInvalidContextRecovery.ps1"));
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
    public void VisualConsistencySliceOne_SharedPrimitivesHaveOneGlobalOwnerAndSettingsConsumesThem()
    {
        var markup = LoadUi("Settings.razor");
        var sharedStyles = LoadUi("app.css").Replace("\r\n", "\n", StringComparison.Ordinal);
        var settingsStyles = LoadUi("Settings.razor.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        string[] sharedRuleOwners =
        [
            ".choice-grid {",
            ".choice-button {",
            ".choice-button.active {",
            ".field-group {",
            ".text-input {",
            ".button-row {",
            ".setting-feedback {",
            ".setting-feedback-success {",
            ".setting-feedback-error {",
        ];

        foreach (var ruleOwner in sharedRuleOwners)
        {
            var rootedRuleOwner = "\n" + ruleOwner;
            Assert.Contains(rootedRuleOwner, sharedStyles, "The shared stylesheet must own " + ruleOwner + ".");
            Assert.DoesNotContain(rootedRuleOwner, settingsStyles, "Settings must not duplicate the shared " + ruleOwner + " primitive.");
        }

        Assert.Contains("\n.setting-help,", sharedStyles);
        Assert.Contains("\n.setting-status {", sharedStyles);
        Assert.DoesNotContain("\n.setting-help", settingsStyles);
        Assert.DoesNotContain("\n.setting-status", settingsStyles);

        var activeRule = ExtractCssRule(sharedStyles, ".choice-button.active");
        Assert.Contains("var(--color-primary)", activeRule);
        Assert.Contains("var(--color-primary-soft)", activeRule);
        Assert.Contains(".button:not(:disabled):hover", sharedStyles);
        Assert.Contains(".choice-button:not(:disabled):hover", sharedStyles);
        Assert.DoesNotContain("\n.button:hover", sharedStyles);
        Assert.DoesNotContain("\n.choice-button:hover", sharedStyles);
        Assert.Contains(".button:focus-visible", sharedStyles);
        Assert.Contains(".text-input:focus-visible", sharedStyles);

        Assert.Contains("class=\"choice-grid", markup);
        Assert.Contains("class=\"choice-button", markup);
        Assert.Contains("class=\"field-group", markup);
        Assert.Contains("class=\"text-input\"", markup);
        Assert.Contains("class=\"button-row\"", markup);
        Assert.Contains("class=\"setting-help\"", markup);
        Assert.Contains("class=\"setting-feedback", markup);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_SharedControlsProtectFocusGeometryAndLongLabels()
    {
        var sharedStyles = LoadUi("app.css").Replace("\r\n", "\n", StringComparison.Ordinal);
        var buttonRule = ExtractCssRule(sharedStyles, ".button {");
        var choiceRule = ExtractCssRule(sharedStyles, ".choice-button {");

        Assert.Contains("min-height: 3rem", buttonRule);
        Assert.Contains("min-width: 0", buttonRule);
        Assert.Contains("overflow-wrap: break-word", buttonRule);
        Assert.Contains("white-space: normal", buttonRule);
        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch(buttonRule, @"(?m)^\s*height\s*:"),
            "Shared action buttons must grow from min-height rather than use a fixed height.");

        Assert.Contains("min-height: 3.5rem", choiceRule);
        Assert.Contains("min-width: 0", choiceRule);
        Assert.Contains("overflow-wrap: break-word", choiceRule);
        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch(choiceRule, @"(?m)^\s*height\s*:"),
            "Shared choice buttons must grow from min-height rather than use a fixed height.");

        Assert.Contains(".button:focus-visible,\n.choice-button:focus-visible,\n.text-input:focus-visible,\nselect:focus-visible", sharedStyles);
        Assert.Contains("outline: 3px solid var(--color-focus-ring)", sharedStyles);
        Assert.Contains(".button:disabled,\n.choice-button:disabled,\n.text-input:disabled,\nselect:disabled", sharedStyles);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_SharedBreakpointsCollapseChoicesAndStackStandardActions()
    {
        var sharedStyles = LoadUi("app.css").Replace("\r\n", "\n", StringComparison.Ordinal);
        var settingsStyles = LoadUi("Settings.razor.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("@media (max-width: 520px)", sharedStyles);
        Assert.Contains(".choice-grid-three {\n        grid-template-columns: 1fr;", sharedStyles);
        Assert.Contains("@media (max-width: 380px)", sharedStyles);
        Assert.Contains(".choice-grid-two {\n        grid-template-columns: 1fr;", sharedStyles);
        Assert.Contains(".button-row > .button {\n        width: 100%;", sharedStyles);
        Assert.DoesNotContain(".choice-grid-three", settingsStyles, StringComparison.Ordinal);
        Assert.DoesNotContain(".choice-grid-two", settingsStyles, StringComparison.Ordinal);

        var buttonRowRule = ExtractCssRule(sharedStyles, ".button-row {");
        Assert.Contains("min-width: 0", buttonRowRule);
        Assert.Contains("flex-wrap: wrap", buttonRowRule);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_SettingsRowsReleaseIntrinsicWidthsAtNarrowViewports()
    {
        var styles = LoadUi("Settings.razor.css");
        var pageRule = ExtractCssRule(styles, ".settings-page");
        var cardRule = ExtractCssRule(styles, ".settings-card");
        var customRowRule = ExtractCssRule(styles, ".custom-input-row");
        var customInputRule = ExtractCssRule(styles, ".custom-input-row .text-input");

        Assert.Contains("min-width: 0", pageRule);
        Assert.Contains("min-width: 0", cardRule);
        Assert.Contains("min-width: 0", customRowRule);
        Assert.Contains("flex-wrap: wrap", customRowRule);
        Assert.Contains("flex:", customInputRule);
        Assert.Contains("min-width: 0", customInputRule);
        Assert.DoesNotContain("width: 8rem", customInputRule, StringComparison.Ordinal);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_SettingsLabelsAdvisoryAndDestructiveDialogsRemainAccessible()
    {
        var markup = LoadUi("Settings.razor");
        var dailyPace = ExtractSettingsSection(markup, "preparation-limit-title");
        var onlineLookup = ExtractSettingsSection(markup, "online-lookup-title");
        var resetData = ExtractSettingsSection(markup, "reset-data-title");

        Assert.Contains("<label for=\"display-name\"", markup);
        Assert.Contains("<label for=\"preparation-limit-custom-input\"", markup);
        Assert.Contains("<label for=\"learning-timezone-select\"", markup);
        Assert.Contains("aria-label=\"@Localizer[\"Settings_LearningDayCutoffHours\"]\"", markup);
        Assert.Contains("aria-label=\"@Localizer[\"Settings_LearningDayCutoffMinutes\"]\"", markup);

        Assert.Contains("id=\"preparation-limit-warning\"", dailyPace);
        Assert.Contains("setting-feedback-advisory", dailyPace);
        Assert.Contains("role=\"status\"", dailyPace);
        Assert.DoesNotContain("aria-live=\"assertive\"", dailyPace, StringComparison.Ordinal);

        Assert.Contains("role=\"alertdialog\"", onlineLookup);
        Assert.Contains("aria-labelledby=\"online-consent-revoke-confirmation-message\"", onlineLookup);
        Assert.Contains("id=\"online-consent-revoke-cancel-button\"", onlineLookup);
        Assert.Contains("class=\"button button-secondary\"", onlineLookup);
        Assert.Contains("id=\"online-consent-revoke-confirm-button\"", onlineLookup);
        Assert.Contains("class=\"button button-danger\"", onlineLookup);

        Assert.Contains("role=\"alertdialog\"", resetData);
        Assert.Contains("aria-labelledby=\"reset-confirmation-message\"", resetData);
        Assert.Contains("id=\"reset-confirm-cancel-button\"", resetData);
        Assert.Contains("id=\"reset-confirm-confirm-button\"", resetData);
        Assert.Contains("data-destructive-confirm", resetData);
    }

    [TestMethod]
    public void VisualConsistencySliceOne_SettingsUsesStructuralSpacingWithoutMagicMargins()
    {
        var markup = LoadUi("Settings.razor");
        var styles = LoadUi("Settings.razor.css");
        var pageRule = ExtractCssRule(styles, ".settings-page");
        var cardRule = ExtractCssRule(styles, ".settings-card");

        Assert.Contains("display: grid", pageRule);
        Assert.Contains("gap: var(--space-4)", pageRule);
        Assert.Contains("margin: 0", cardRule);
        Assert.DoesNotContain("margin-top", cardRule, StringComparison.Ordinal);
        Assert.DoesNotContain("calc(-1 *", styles, StringComparison.Ordinal);

        var helpSection = ExtractSettingsSection(markup, "help-and-support-title");
        Assert.DoesNotContain("style=", helpSection, StringComparison.Ordinal);
        Assert.Contains("class=\"build-identity\"", helpSection);
        Assert.Contains("class=\"setting-help bug-report-address\"", helpSection);
    }

    [TestMethod]
    public void VisualConsistencySliceOne_SettingsActionsUseStandardSemanticStyles()
    {
        var markup = LoadUi("Settings.razor");
        var displayNameSection = ExtractSettingsSection(markup, "display-name-title");
        var onlineLookupSection = ExtractSettingsSection(markup, "online-lookup-title");
        var restoreDefaultsSection = ExtractSettingsSection(markup, "restore-defaults-title");
        var resetDataSection = ExtractSettingsSection(markup, "reset-data-title");

        var removeNameButtonStart = displayNameSection.IndexOf("id=\"display-name-remove-button\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, removeNameButtonStart);
        var removeNameButton = displayNameSection[removeNameButtonStart..];
        Assert.Contains("class=\"button button-danger\"", removeNameButton);

        Assert.Contains("id=\"online-consent-revoke-button\"", onlineLookupSection);
        Assert.Contains("class=\"button button-danger\"", onlineLookupSection);
        Assert.Contains("id=\"online-consent-activate-button\"", onlineLookupSection);
        Assert.Contains("class=\"button button-primary\"", onlineLookupSection);

        Assert.DoesNotContain("button-danger", restoreDefaultsSection, StringComparison.Ordinal);
        Assert.Contains("class=\"button button-secondary\"", restoreDefaultsSection);
        Assert.Contains("class=\"button button-danger\"", resetDataSection);
        Assert.Contains("data-destructive-confirm", resetDataSection);
    }

    [TestMethod]
    public void VisualConsistencySliceOne_OnlineConsentRevocationRequiresInlineConfirmation()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "online-lookup-title");

        Assert.Contains("if (!_showOnlineConsentRevokeConfirmation)", section);
        Assert.Contains("id=\"online-consent-revoke-confirmation\"", section);
        Assert.Contains("class=\"destructive-confirmation\"", section);
        Assert.Contains("Settings_RevokeOnlineConsentConfirmMessage", section);
        Assert.Contains("id=\"online-consent-revoke-cancel-button\"", section);
        Assert.Contains("class=\"button button-secondary\"", section);
        Assert.Contains("@onclick=\"CancelOnlineConsentRevocation\"", section);
        Assert.Contains("id=\"online-consent-revoke-confirm-button\"", section);
        Assert.Contains("class=\"button button-danger\"", section);
        Assert.Contains("data-destructive-confirm", section);
        Assert.Contains("@onclick=\"ConfirmOnlineConsentRevocation\"", section);

        var showHandler = ExtractMethodBody(markup, "private void ShowOnlineConsentRevokeConfirmation()");
        var cancelHandler = ExtractMethodBody(markup, "private void CancelOnlineConsentRevocation()");
        var confirmHandler = ExtractMethodBody(markup, "private void ConfirmOnlineConsentRevocation()");

        Assert.DoesNotContain("RevokeOnlineLookupConsent", showHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeOnlineLookupConsent", cancelHandler, StringComparison.Ordinal);
        Assert.Contains("AppSettings.RevokeOnlineLookupConsent();", confirmHandler);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", cancelHandler);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", confirmHandler);
    }

    [TestMethod]
    public void Settings_OffersEnhancedTermRecognitionToggleBoundToExistingSettingWithoutOnlineConsent()
    {
        var markup = LoadUi("Settings.razor");

        var sectionStart = markup.IndexOf("id=\"enhanced-term-recognition-title\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sectionStart);
        var sectionEnd = markup.IndexOf("</section>", sectionStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(sectionStart, sectionEnd);
        var section = markup[sectionStart..sectionEnd];

        Assert.Contains("Settings_EnhancedTermRecognition\"", section);
        Assert.Contains("Settings_EnhancedTermRecognitionHelp", section);
        Assert.Contains("Settings_EnhancedTermRecognitionOn", section);
        Assert.Contains("Settings_EnhancedTermRecognitionOff", section);
        Assert.Contains("Settings_EnhancedTermRecognitionSaved", section);
        Assert.Contains("_enhancedTermRecognitionEnabled", section);
        Assert.Contains("SetEnhancedTermRecognition", section);

        Assert.DoesNotContain("HasOnlineLookupConsent", section, StringComparison.Ordinal);
        Assert.DoesNotContain("GrantOnlineLookupConsent", section, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeOnlineLookupConsent", section, StringComparison.Ordinal);

        Assert.Contains("AppSettings.SetEnhancedTermRecognitionEnabled(enabled)", markup);
        Assert.Contains("AppSettings.EnhancedTermRecognitionEnabled", markup);
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

        AssertDebugOnlyMarkerIsGuardedByIsDebugBuild(navigation, "NavMenu.razor", "nav-link debug-nav-link");
        AssertDebugOnlyMarkerIsGuardedByIsDebugBuild(preparation, "PrepareWords.razor", "lookup-diagnostics debug-tools");
        AssertDebugOnlyMarkerIsGuardedByIsDebugBuild(review, "ReviewWords.razor", "review-debug-tools debug-tools");
    }

    private static void AssertDebugOnlyMarkerIsGuardedByIsDebugBuild(string markup, string fileLabel, string marker)
    {
        var propertyIndex = markup.IndexOf("private static bool IsDebugBuild", StringComparison.Ordinal);
        Assert.IsTrue(propertyIndex >= 0, $"{fileLabel} must define an IsDebugBuild property.");
        var propertyEndIndex = markup.IndexOf("#endif", propertyIndex, StringComparison.Ordinal);
        Assert.IsTrue(propertyEndIndex >= 0 && propertyEndIndex - propertyIndex < 200,
            $"{fileLabel}: IsDebugBuild property body not found within expected bounds.");
        var debugDirectiveIndex = markup.IndexOf("#if DEBUG", propertyIndex, StringComparison.Ordinal);
        Assert.IsTrue(debugDirectiveIndex >= 0 && debugDirectiveIndex < propertyEndIndex,
            $"{fileLabel}: IsDebugBuild must resolve through a '#if DEBUG' compile-time branch.");

        var guardIndex = markup.IndexOf("@if (IsDebugBuild", StringComparison.Ordinal);
        Assert.IsTrue(guardIndex >= 0, $"{fileLabel} must guard debug-only markup with '@if (IsDebugBuild ...)'.");
        var conditionOpenParen = markup.IndexOf('(', guardIndex);
        var conditionCloseParen = FindMatchingClose(markup, conditionOpenParen, '(', ')');
        Assert.IsTrue(conditionCloseParen > conditionOpenParen, $"{fileLabel}: unbalanced IsDebugBuild condition.");
        var blockOpenBrace = markup.IndexOf('{', conditionCloseParen + 1);
        Assert.IsTrue(blockOpenBrace >= 0, $"{fileLabel}: expected a block after the IsDebugBuild condition.");
        var blockCloseBrace = FindMatchingClose(markup, blockOpenBrace, '{', '}');
        Assert.IsTrue(blockCloseBrace > blockOpenBrace, $"{fileLabel}: unbalanced IsDebugBuild block.");

        var markerIndex = markup.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(markerIndex >= 0, $"{fileLabel}: expected marker '{marker}'.");
        Assert.IsTrue(markerIndex > blockOpenBrace && markerIndex < blockCloseBrace,
            $"{fileLabel}: '{marker}' must be inside the '@if (IsDebugBuild ...)' block, not merely present elsewhere in the file.");
    }

    private static int FindMatchingClose(string text, int openIndex, char open, char close)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open) depth++;
            else if (text[i] == close && --depth == 0) return i;
        }
        return -1;
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

    /// <summary>
    /// The preparation GUI scenarios dispatched by Invoke-GuiTestRun.ps1. Each id is simultaneously
    /// the registry id, the scenario script's file name, and the suffix of its dispatcher function.
    /// </summary>
    private static readonly string[] PreparationScenarioIds =
    [
        "PreparationSelectedMeaning",
        "PreparationAcceptFailureRecovery",
        "PreparationInvalidContextRecovery",
    ];

    internal static string LoadUi(string fileName)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Ui", fileName);
        if (File.Exists(outputPath))
        {
            return File.ReadAllText(outputPath);
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidateShared = Path.Combine(directory.FullName, "Components", "Shared", fileName);
            if (File.Exists(candidateShared))
            {
                return File.ReadAllText(candidateShared);
            }

            var candidatePages = Path.Combine(directory.FullName, "Components", "Pages", fileName);
            if (File.Exists(candidatePages))
            {
                return File.ReadAllText(candidatePages);
            }
        }

        return File.ReadAllText(outputPath);
    }

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

    [TestMethod]
    public void Settings_ResetData_UsesInjectedPreferencesClear_NotStaticPreferencesDefault()
    {
        var markup = LoadUi("Settings.razor");
        Assert.IsTrue(
            markup.Contains("@inject IPreferences Preferences", StringComparison.Ordinal)
            || markup.Contains("@inject Microsoft.Maui.Storage.IPreferences Preferences", StringComparison.Ordinal),
            "Settings.razor must inject IPreferences to support profile isolation during reset.");

        var resetStart = markup.IndexOf("private async Task ResetDataAsync()", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, resetStart);
        var resetMethod = markup[resetStart..];

        Assert.Contains("Preferences.Clear()", resetMethod);
        Assert.DoesNotContain("Preferences.Default.Clear()", resetMethod);
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

    private static readonly string[] RequiredSettingsSectionOrder =
    [
        "language-setting-title",
        "appearance-setting-title",
        "display-name-title",
        "preparation-limit-title",
        "learning-timezone-title",
        "learning-day-cutoff-title",
        "card-direction-title",
        "learning-mode-title",
        "enhanced-term-recognition-title",
        "online-lookup-title",
        "portable-data-title",
        "help-and-support-title",
        "restore-defaults-title",
        "reset-data-title"
    ];

    [TestMethod]
    public void Settings_RendersTheRequiredFourteenSectionProductOrder()
    {
        var markup = LoadUi("Settings.razor");

        var previousIndex = -1;
        var previousId = string.Empty;

        foreach (var sectionId in RequiredSettingsSectionOrder)
        {
            var index = markup.IndexOf(
                "aria-labelledby=\"" + sectionId + "\"",
                StringComparison.Ordinal);

            Assert.IsGreaterThanOrEqualTo(
                0,
                index,
                "The Settings section '" + sectionId + "' is missing.");
            Assert.IsGreaterThan(
                previousIndex,
                index,
                "The Settings section '" + sectionId + "' must follow '" + previousId + "'.");

            previousIndex = index;
            previousId = sectionId;
        }
    }

    [TestMethod]
    public void Settings_DisplayNameCardOffersLocalizedOptionalNameEditingAndRemoval()
    {
        var markup = LoadUi("Settings.razor");

        Assert.Contains("aria-labelledby=\"display-name-title\"", markup);
        Assert.Contains("<label for=\"display-name\">", markup);
        Assert.Contains("Settings_DisplayName", markup);
        Assert.Contains("Settings_DisplayNameHelp", markup);
        Assert.Contains("id=\"display-name-help\"", markup);
        Assert.Contains("aria-describedby=\"display-name-help\"", markup);

        // Bounded free-text input, mirroring the established ImportText document-title pattern.
        Assert.Contains("id=\"display-name\"", markup);
        Assert.Contains("class=\"text-input\"", markup);
        Assert.Contains("maxlength=", markup);
        Assert.Contains("@bind=\"_displayNameInput\"", markup);

        // Explicit save and explicit removal, plus the shared success/error feedback pattern.
        Assert.Contains("id=\"display-name-save-button\"", markup);
        Assert.Contains("id=\"display-name-remove-button\"", markup);
        Assert.Contains("SaveDisplayName", markup);
        Assert.Contains("RemoveDisplayName", markup);
        Assert.Contains("Settings_DisplayNameSaved", markup);
        Assert.Contains("Settings_DisplayNameRemoved", markup);
        Assert.Contains("IDisplayNameStore", markup);
    }

    [TestMethod]
    public void Settings_DisplayNameCardUsesNeutralLocalWordingWithoutAccountOrCloudIdentity()
    {
        var markup = LoadUi("Settings.razor");

        foreach (var forbidden in new[] { "Settings_DisplayNameAccount", "Settings_DisplayNameProfile", "Settings_DisplayNameCloud" })
        {
            Assert.DoesNotContain(forbidden, markup, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void Home_PersonalizedGreetingConsumesTheNormalizedDisplayNameContract()
    {
        var markup = LoadUi("Home.razor");

        Assert.Contains("@inject KnownFirst.Services.Settings.IDisplayNameStore DisplayNameStore", markup);
        Assert.Contains("_displayName = DisplayNameStore.GetDisplayName();", markup);
        Assert.Contains("@if (_displayName is not null)", markup);
        Assert.Contains("Localizer[\"Home_Greeting\", _displayName]", markup);
    }

    [TestMethod]
    public void Home_PersonalizedGreetingPreservesTheSubtitleAndHasNoBlankFallbackPath()
    {
        var markup = LoadUi("Home.razor");
        var subtitleStart = markup.IndexOf("<p class=\"subtitle\">", StringComparison.Ordinal);
        var subtitleEnd = markup.IndexOf("</p>", subtitleStart, StringComparison.Ordinal);

        Assert.IsTrue(subtitleStart >= 0 && subtitleEnd > subtitleStart);

        var subtitle = markup[subtitleStart..subtitleEnd];
        var guardIndex = subtitle.IndexOf("@if (_displayName is not null)", StringComparison.Ordinal);
        var greetingIndex = subtitle.IndexOf("@($\"{Localizer[\"Home_Greeting\", _displayName]} \")", StringComparison.Ordinal);
        var existingSubtitleIndex = subtitle.IndexOf("@Localizer[\"Home_Subtitle\"]", StringComparison.Ordinal);

        Assert.IsTrue(guardIndex >= 0 && guardIndex < greetingIndex);
        Assert.IsTrue(greetingIndex < existingSubtitleIndex);
        Assert.AreEqual(1, CountOccurrences(subtitle, "Home_Greeting"));
        Assert.AreEqual(1, CountOccurrences(subtitle, "Home_Subtitle"));
    }

    [TestMethod]
    public void Home_ResponsiveLayoutDefinesPageContainerAndReducedHeightCompacting()
    {
        var markup = LoadUi("Home.razor");
        var styles = LoadUi("Home.razor.css");

        Assert.Contains("<section class=\"home-page\">", markup);
        Assert.Contains(".home-page", styles);
        Assert.Contains("width: min(100%, 76rem);", styles);
        Assert.Contains("min-width: 0;", styles);
        Assert.Contains("@media (max-height: 600px)", styles);
        Assert.Contains("(orientation: landscape)", styles);
    }

    [TestMethod]
    public void Settings_RestoreDefaultsFollowsHelpAndSupportAndPrecedesResetData()
    {
        var markup = LoadUi("Settings.razor");

        var helpAndSupport = markup.IndexOf(
            "aria-labelledby=\"help-and-support-title\"",
            StringComparison.Ordinal);
        var restoreDefaults = markup.IndexOf(
            "aria-labelledby=\"restore-defaults-title\"",
            StringComparison.Ordinal);
        var resetData = markup.IndexOf(
            "aria-labelledby=\"reset-data-title\"",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, helpAndSupport);
        Assert.IsGreaterThan(helpAndSupport, restoreDefaults);
        Assert.IsGreaterThan(restoreDefaults, resetData);
    }

    [TestMethod]
    public void Settings_CardDirectionsRendersDefaultFirstOrder()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "card-direction-title");

        var both = section.IndexOf("CardDirectionPreference.Both", StringComparison.Ordinal);
        var termToMeaning = section.IndexOf("CardDirectionPreference.TermToMeaning", StringComparison.Ordinal);
        var meaningToTerm = section.IndexOf("CardDirectionPreference.MeaningToTerm", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, both);
        Assert.IsGreaterThan(both, termToMeaning);
        Assert.IsGreaterThan(termToMeaning, meaningToTerm);

        var bothLabel = section.IndexOf("Settings_CardDirectionBoth", StringComparison.Ordinal);
        var termToMeaningLabel = section.IndexOf("Settings_CardDirectionTermToMeaning", StringComparison.Ordinal);
        var meaningToTermLabel = section.IndexOf("Settings_CardDirectionMeaningToTerm", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, bothLabel);
        Assert.IsGreaterThan(bothLabel, termToMeaningLabel);
        Assert.IsGreaterThan(termToMeaningLabel, meaningToTermLabel);
    }

    [TestMethod]
    public void Settings_LearningModeRendersDefaultFirstOrder()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "learning-mode-title");

        var automatic = section.IndexOf("LearningMode.Automatic", StringComparison.Ordinal);
        var reading = section.IndexOf("LearningMode.Reading", StringComparison.Ordinal);
        var typing = section.IndexOf("LearningMode.Typing", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, automatic);
        Assert.IsGreaterThan(automatic, reading);
        Assert.IsGreaterThan(reading, typing);

        var automaticLabel = section.IndexOf("Settings_LearningModeAutomatic", StringComparison.Ordinal);
        var readingLabel = section.IndexOf("Settings_LearningModeReading", StringComparison.Ordinal);
        var typingLabel = section.IndexOf("Settings_LearningModeTyping", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, automaticLabel);
        Assert.IsGreaterThan(automaticLabel, readingLabel);
        Assert.IsGreaterThan(readingLabel, typingLabel);
    }

    [TestMethod]
    public void Settings_LearningTimezoneEffectiveStatusHasExplicitSpacingSeparation()
    {
        var markup = LoadUi("Settings.razor");
        var styles = LoadUi("app.css");
        var section = ExtractSettingsSection(markup, "learning-timezone-title");

        Assert.Contains("id=\"learning-timezone-effective\"", section);
        Assert.IsTrue(
            section.Contains("class=\"setting-status\"", StringComparison.Ordinal)
            || section.Contains("class=\"setting-help setting-status\"", StringComparison.Ordinal)
            || section.Contains("class=\"setting-effective\"", StringComparison.Ordinal),
            "The effective timezone element must declare a dedicated status/effective class for spacing separation.");

        Assert.IsTrue(
            styles.Contains(".setting-status", StringComparison.Ordinal)
            || styles.Contains("#learning-timezone-effective", StringComparison.Ordinal)
            || styles.Contains(".setting-effective", StringComparison.Ordinal),
            "The shared stylesheet must define explicit spacing rules for the effective timezone status line.");
        Assert.IsTrue(
            styles.Contains("var(--space-3)", StringComparison.Ordinal)
            || styles.Contains("var(--space-4)", StringComparison.Ordinal),
            "The shared stylesheet must use an existing spacing scale variable for the status separation.");
    }

    [TestMethod]
    public void Settings_OffersACuratedLearningTimezoneControlWithoutLocationOrNetworkDependencies()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "learning-timezone-title");

        Assert.Contains("Settings_LearningTimezone\"", section);
        Assert.Contains("Settings_LearningTimezoneHelp", section);
        Assert.Contains("Settings_LearningTimezoneSystem", section);
        Assert.Contains("id=\"learning-timezone-select\"", section);
        Assert.Contains("LearningTimezoneCatalog.Options", section);
        Assert.Contains("SelectLearningTimezone", section);

        Assert.Contains("AppSettings.SetLearningTimezoneMode", markup);
        Assert.Contains("AppSettings.SetExplicitLearningTimezoneId", markup);
        Assert.Contains("LearningTimezoneMode.Explicit", markup);
        Assert.Contains("LearningTimezoneMode.System", markup);

        Assert.DoesNotContain("Geolocation", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Permissions.", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_LearningTimezoneLabelsAreComputedForTheCurrentInstantAndNotPersistedOffsets()
    {
        var markup = LoadUi("Settings.razor");

        Assert.Contains("LearningTimezoneLabelFormatter", markup);
        Assert.Contains("DateTime.UtcNow", markup);
        Assert.DoesNotContain("SetExplicitLearningTimezoneId(offset", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("BaseUtcOffset.ToString", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_OffersAMinutePrecisionLearningDayCutoffControl()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "learning-day-cutoff-title");

        Assert.Contains("Settings_LearningDayCutoff\"", section);
        Assert.Contains("Settings_LearningDayCutoffHelp", section);
        Assert.Contains("id=\"learning-day-cutoff-hour\"", section);
        Assert.Contains("id=\"learning-day-cutoff-minute\"", section);
        Assert.Contains("class=\"time-separator\"", section);
        Assert.DoesNotContain("type=\"time\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("AM", section, StringComparison.Ordinal);
        Assert.DoesNotContain("PM", section, StringComparison.Ordinal);

        // Verify 24-hour and 60-minute loops with D2 invariant formatting
        Assert.Contains("h < 24", section);
        Assert.Contains("m < 60", section);
        Assert.Contains("Settings_LearningDayCutoffHours", section);
        Assert.Contains("Settings_LearningDayCutoffMinutes", section);
        Assert.Contains("_selectedLearningDayCutoffHour", section);
        Assert.Contains("_selectedLearningDayCutoffMinute", section);

        Assert.Contains("AppSettings.SetLearningDayCutoffMinutes", markup);
        Assert.Contains("SyncLearningDayCutoffFields", markup);
    }

    [TestMethod]
    public void Settings_RestoreDefaultsIsANonDestructiveActionSeparateFromFullReset()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "restore-defaults-title");

        Assert.Contains("Settings_RestoreDefaults\"", section);
        Assert.Contains("Settings_RestoreDefaultsDescription", section);
        Assert.Contains("id=\"restore-defaults-button\"", section);
        Assert.Contains("SettingsDefaults.RestoreDefaults()", markup);

        Assert.DoesNotContain("danger-zone", section, StringComparison.Ordinal);
        Assert.DoesNotContain("button-danger", section, StringComparison.Ordinal);
        Assert.DoesNotContain("data-destructive-confirm", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Database.ResetAsync", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Clear", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetDataAsync", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings_ResetData\"", section, StringComparison.Ordinal);

        var restoreHandler = ExtractMethodBody(markup, "private void RestoreDefaults()");
        Assert.Contains("SettingsDefaults.RestoreDefaults()", restoreHandler);
        Assert.DoesNotContain("Database", restoreHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Clear", restoreHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowChangeNotifier", restoreHandler, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_RestoreDefaultsNeverAltersOnlineDictionaryConsent()
    {
        var markup = LoadUi("Settings.razor");

        var restoreHandler = ExtractMethodBody(markup, "private void RestoreDefaults()");

        Assert.DoesNotContain("OnlineLookupConsent", restoreHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_onlineConsentRevoked", restoreHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_onlineConsentActivated", restoreHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreDefaultsForFullReset", restoreHandler, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_FullResetRevokesOnlineDictionaryConsentThroughItsOwnDedicatedPath()
    {
        var markup = LoadUi("Settings.razor");

        var resetHandler = ExtractMethodBody(markup, "private async Task ResetDataAsync()");

        Assert.Contains("SettingsDefaults.RestoreDefaultsForFullReset()", resetHandler);
        Assert.DoesNotContain("SettingsDefaults.RestoreDefaults()", resetHandler, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_FullResetRemainsLastDestructiveAndStructurallySeparateFromRestoreDefaults()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "reset-data-title");

        Assert.Contains("danger-zone", section);
        Assert.Contains("button button-danger", section);
        Assert.Contains("data-destructive-confirm", section);
        Assert.Contains("ResetDataAsync", section);
        Assert.DoesNotContain("RestoreDefaults", section, StringComparison.Ordinal);

        var resetHandler = ExtractMethodBody(markup, "private async Task ResetDataAsync()");
        Assert.Contains("await Database.ResetAsync();", resetHandler);
        Assert.Contains("Preferences.Clear();", resetHandler);
        Assert.Contains("WorkflowChangeNotifier.NotifyChanged();", resetHandler);

        var resetSectionIndex = markup.LastIndexOf(
            "<section class=\"settings-card danger-zone\" aria-labelledby=\"reset-data-title\"",
            StringComparison.Ordinal);
        var lastSectionIndex = markup.LastIndexOf(
            "<section class=\"settings-card",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, resetSectionIndex);
        Assert.AreEqual(
            lastSectionIndex,
            resetSectionIndex,
            "The destructive full reset must remain the last Settings section.");
    }

    [TestMethod]
    public void Settings_DefaultsComeFromRepositoryPoliciesRatherThanRazorLiterals()
    {
        var markup = LoadUi("Settings.razor");

        Assert.Contains("PreparationLimitPolicy", markup);
        Assert.Contains("AppSettings.PreparationLimit", markup);
        Assert.Contains("AppSettings.CardDirection", markup);
        Assert.Contains("AppSettings.LearningMode", markup);
        Assert.Contains("AppSettings.EnhancedTermRecognitionEnabled", markup);
        Assert.Contains("AppSettings.LearningDayCutoffMinutes", markup);
        Assert.Contains("AppSettings.LearningTimezoneMode", markup);
        Assert.Contains("AppSettings.ExplicitLearningTimezoneId", markup);
    }

    [TestMethod]
    public void Settings_DailyBudgetCardOffersPresetsOneFiveTenAndCustom()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "preparation-limit-title");

        Assert.Contains("id=\"preparation-limit-preset-1\"", section);
        Assert.Contains("id=\"preparation-limit-preset-5\"", section);
        Assert.Contains("id=\"preparation-limit-preset-10\"", section);
        Assert.Contains("id=\"preparation-limit-custom\"", section);

        Assert.Contains("Settings_PreparationLimitRecommended", section);
        Assert.Contains("Settings_PreparationLimitCustom", section);
        Assert.Contains("SelectPreset", markup);
        Assert.Contains("SelectCustom", markup);
    }

    [TestMethod]
    public void VisualConsistencySliceTwo_SettingsDailyBudgetUsesBindingOrderAndSharedStateVocabulary()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "preparation-limit-title");

        var recommended = section.IndexOf("id=\"preparation-limit-preset-5\"", StringComparison.Ordinal);
        var one = section.IndexOf("id=\"preparation-limit-preset-1\"", StringComparison.Ordinal);
        var ten = section.IndexOf("id=\"preparation-limit-preset-10\"", StringComparison.Ordinal);
        var custom = section.IndexOf("id=\"preparation-limit-custom\"", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, recommended);
        Assert.IsGreaterThan(recommended, one, "The recommended value 5 must render before preset 1.");
        Assert.IsGreaterThan(one, ten, "Preset 1 must render before preset 10.");
        Assert.IsGreaterThan(ten, custom, "Preset 10 must render before Custom.");

        Assert.Contains("class=\"choice-button choice-button-recommended @(IsPresetActive(5) ? \"active\" : null)\"", section);
        Assert.Contains("aria-pressed=\"@IsPresetActive(5)\"", section);
        Assert.Contains("class=\"choice-button @(IsPresetActive(1) ? \"active\" : null)\"", section);
        Assert.Contains("aria-pressed=\"@IsPresetActive(1)\"", section);
        Assert.Contains("class=\"choice-button @(IsPresetActive(10) ? \"active\" : null)\"", section);
        Assert.Contains("aria-pressed=\"@IsPresetActive(10)\"", section);
        Assert.Contains("class=\"choice-button @(_isCustomActive ? \"active\" : null)\"", section);
        Assert.Contains("aria-pressed=\"@_isCustomActive\"", section);
        Assert.Contains("Localizer[\"Settings_PreparationLimitRecommended\"]", section);
        Assert.Contains("Localizer[\"Settings_PreparationLimitCustom\"]", section);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(5, false)]
    [DataRow(10, false)]
    [DataRow(20, true)]
    public void VisualConsistencySliceTwo_SettingsCustomCanonicalizesOnlyAtSaveBoundary(
        int committedValue,
        bool remainsCustom)
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "preparation-limit-title");
        var selectCustom = ExtractMethodBody(markup, "private void SelectCustom()");
        var saveCustom = ExtractMethodBody(markup, "private void SaveCustomPreparationLimit()");

        Assert.AreEqual(remainsCustom, !KnownFirst.Core.Settings.PreparationLimitPolicy.IsPreset(committedValue));
        Assert.Contains("@bind:event=\"oninput\"", section);
        Assert.DoesNotContain("SetPreparationLimit", selectCustom, StringComparison.Ordinal);
        Assert.DoesNotContain("PreparationLimitPolicy.IsPreset", selectCustom, StringComparison.Ordinal);
        Assert.Contains("AppSettings.SetPreparationLimit(customValue);", saveCustom);
        Assert.Contains("_selectedPreparationLimit = AppSettings.PreparationLimit;", saveCustom);
        Assert.Contains("_isCustomActive = !PreparationLimitPolicy.IsPreset(_selectedPreparationLimit);", saveCustom);
    }

    [TestMethod]
    public void VisualConsistencySliceTwo_SettingsCustomEditorIsConditionalBoundedAndHighValuesRemainAdvisory()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "preparation-limit-title");
        var customCondition = section.IndexOf("@if (_isCustomActive)", StringComparison.Ordinal);
        var customInput = section.IndexOf("id=\"preparation-limit-custom-input\"", StringComparison.Ordinal);
        var saveCustom = ExtractMethodBody(markup, "private void SaveCustomPreparationLimit()");

        Assert.IsGreaterThanOrEqualTo(0, customCondition);
        Assert.IsGreaterThan(customCondition, customInput, "The numeric editor must exist only inside the Custom-state block.");
        Assert.Contains("class=\"text-input\"", section);
        Assert.Contains("type=\"number\"", section);
        Assert.Contains("min=\"1\"", section);
        Assert.Contains("max=\"50\"", section);
        Assert.Contains("step=\"1\"", section);
        Assert.Contains("PreparationLimitPolicy.IsValid(customValue)", saveCustom);
        Assert.DoesNotContain("RequiresHighBudgetWarning", saveCustom, StringComparison.Ordinal);
        Assert.IsFalse(KnownFirst.Core.Settings.PreparationLimitPolicy.RequiresHighBudgetWarning(15));
        Assert.IsTrue(KnownFirst.Core.Settings.PreparationLimitPolicy.RequiresHighBudgetWarning(16));
        Assert.IsTrue(KnownFirst.Core.Settings.PreparationLimitPolicy.RequiresHighBudgetWarning(20));
        Assert.IsTrue(KnownFirst.Core.Settings.PreparationLimitPolicy.RequiresHighBudgetWarning(50));
        Assert.IsFalse(KnownFirst.Core.Settings.PreparationLimitPolicy.RequiresHighBudgetWarning(51));
    }

    [TestMethod]
    public void Settings_DailyBudgetCardCustomInputEnforcesOneToFiftyBoundsAndValidation()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "preparation-limit-title");

        Assert.Contains("id=\"preparation-limit-custom-input\"", section);
        Assert.Contains("id=\"preparation-limit-custom-save-button\"", section);
        Assert.Contains("type=\"number\"", section);
        Assert.Contains("min=\"1\"", section);
        Assert.Contains("max=\"50\"", section);
        Assert.Contains("step=\"1\"", section);
        Assert.Contains("aria-describedby=\"preparation-limit-custom-help\"", section);
        Assert.Contains("Settings_PreparationLimitCustomLabel", section);
        Assert.Contains("Settings_PreparationLimitCustomHelp", section);
        Assert.Contains("Settings_PreparationLimitCustomInvalid", markup);
        Assert.Contains("SaveCustomPreparationLimit", markup);
    }

    [TestMethod]
    public void Settings_DailyBudgetCardExposesNonBlockingAdvisoryWarningForHighBudgets()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "preparation-limit-title");

        Assert.Contains("id=\"preparation-limit-warning\"", section);
        Assert.Contains("role=\"status\"", section);
        Assert.Contains("setting-feedback-advisory", section);
        Assert.Contains("Settings_PreparationLimitHighWarning", section);
        Assert.Contains("RequiresHighBudgetWarning", markup);
    }

    [TestMethod]
    public void Settings_ExistingReleaseNoteHistoryAndSupportContractsRemainIntact()
    {
        var markup = LoadUi("Settings.razor");
        var section = ExtractSettingsSection(markup, "help-and-support-title");

        Assert.Contains("id=\"settings-release-notes-link\"", section);
        Assert.Contains("href=\"release-notes\"", section);
        Assert.Contains("WhatsNew_Title", section);
        Assert.Contains("id=\"settings-report-bug-button\"", section);
        Assert.Contains("BuildIdentityService.GetFormattedBuildIdentity()", section);
    }

    [TestMethod]
    public void Routes_BranchesOnboardingHostBeforeRouterWhenOnboardingActive()
    {
        var markup = LoadUi("Routes.razor");

        Assert.Contains("OnboardingHost", markup);
        Assert.Contains("OnboardingStateStore", markup);
        Assert.Contains("<Router", markup);

        var onboardingIndex = markup.IndexOf("OnboardingHost", StringComparison.Ordinal);
        var routerIndex = markup.IndexOf("<Router", StringComparison.Ordinal);
        Assert.IsTrue(routerIndex > onboardingIndex, "OnboardingHost must appear before <Router in Routes.razor");
    }

    [TestMethod]
    public void OnboardingHost_DoesNotRenderNavigationChromeOrWhatsNewModal()
    {
        var markup = LoadUi("OnboardingHost.razor");

        Assert.DoesNotContain("NavMenu", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsNewModal", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("mobile-header", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("navigation-backdrop", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("sidebar", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void OnboardingHost_WelcomeScreenPresentsTitleConceptLanguageAndContinue()
    {
        var markup = LoadUi("OnboardingHost.razor");

        Assert.Contains("Onboarding_WelcomeTitle", markup);
        Assert.Contains("Onboarding_WelcomeConcept", markup);
        Assert.Contains("LanguageSelection", markup);
        Assert.Contains("IOnboardingProgressStore", markup);
        Assert.Contains("IOnboardingStateStore", markup);
        Assert.Contains("AdvanceToNextStep", markup);
    }

    [TestMethod]
    public void Learning_RatingButtons_AgainDoesNotUseDestructiveDangerTokens()
    {
        var styles = LoadUi("RatingButtons.razor.css");

        Assert.IsFalse(
            styles.Contains("--color-danger", StringComparison.Ordinal),
            "RatingButtons.razor.css must not reference destructive --color-danger tokens.");
        Assert.IsFalse(
            styles.Contains(".button-danger", StringComparison.Ordinal),
            "RatingButtons.razor.css must not reference .button-danger.");
        Assert.IsFalse(
            styles.Contains("#c53030", StringComparison.Ordinal),
            "RatingButtons.razor.css must not hardcode destructive red hex codes.");

        var againRule = ExtractCssRule(styles, ".rating-again");
        Assert.Contains("var(--color-surface-muted)", againRule);
        Assert.Contains("var(--color-border-strong)", againRule);
        Assert.Contains("var(--color-text)", againRule);
    }

    [TestMethod]
    public void Learning_RatingButtons_ExposeDistinctSemanticClassesAndPreserveBindings()
    {
        var markup = LoadUi("RatingButtons.razor");

        Assert.Contains("rating-again", markup);
        Assert.Contains("rating-hard", markup);
        Assert.Contains("rating-good", markup);
        Assert.Contains("rating-easy", markup);

        Assert.Contains("@Localizer[\"Learn_Again\"]", markup);
        Assert.Contains("@Localizer[\"Learn_Hard\"]", markup);
        Assert.Contains("@Localizer[\"Learn_Good\"]", markup);
        Assert.Contains("@Localizer[\"Learn_Easy\"]", markup);

        Assert.Contains("OnRate.InvokeAsync(ReviewRating.Again)", markup);
        Assert.Contains("OnRate.InvokeAsync(ReviewRating.Hard)", markup);
        Assert.Contains("OnRate.InvokeAsync(ReviewRating.Good)", markup);
        Assert.Contains("OnRate.InvokeAsync(ReviewRating.Easy)", markup);

        Assert.IsTrue(
            markup.Contains("button-primary rating-good", StringComparison.Ordinal)
            || (markup.Contains("button-primary", StringComparison.Ordinal) && markup.Contains("rating-good", StringComparison.Ordinal)),
            "Good must retain primary solid button styling.");

        Assert.IsFalse(
            markup.Contains("button-secondary\" disabled=\"@Disabled\" @onclick=\"() => OnRate.InvokeAsync(ReviewRating.Hard)\"", StringComparison.Ordinal)
            && markup.Contains("button-secondary\" disabled=\"@Disabled\" @onclick=\"() => OnRate.InvokeAsync(ReviewRating.Easy)\"", StringComparison.Ordinal),
            "Hard and Easy must not share identical undifferentiated button-secondary markup.");
    }

    [TestMethod]
    public void Learning_RatingButtons_StylesEstablishNonColorHierarchy()
    {
        var styles = LoadUi("RatingButtons.razor.css");

        var hardRule = ExtractCssRule(styles, ".rating-hard");
        Assert.IsTrue(
            hardRule.Contains("dashed", StringComparison.Ordinal),
            "Hard must declare a dashed border as a non-color cue.");
        Assert.Contains("#9a6a32", hardRule, "Hard must use the approved warm border color #9a6a32.");
        Assert.Contains("var(--color-surface-elevated)", hardRule);
        Assert.Contains("var(--color-text)", hardRule);

        var easyRule = ExtractCssRule(styles, ".rating-easy");
        Assert.IsTrue(
            easyRule.Contains("2px solid", StringComparison.Ordinal) || (easyRule.Contains("2px") && easyRule.Contains("solid")),
            "Easy must declare a 2px solid border as a non-color cue.");
        Assert.Contains("var(--color-success)", easyRule);
        Assert.Contains("var(--color-success-surface)", easyRule);

        var goodRule = ExtractCssRule(styles, ".rating-good");
        Assert.Contains("var(--color-primary)", goodRule);
    }

    private static string ExtractSettingsSection(string markup, string sectionTitleId)
    {
        var titleIndex = markup.IndexOf(
            "aria-labelledby=\"" + sectionTitleId + "\"",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(
            0,
            titleIndex,
            "The Settings section '" + sectionTitleId + "' is missing.");

        var start = markup.LastIndexOf("<section", titleIndex, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(
            0,
            start,
            "The Settings section '" + sectionTitleId + "' has no opening element.");

        var end = markup.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.IsGreaterThan(start, end);

        return markup[start..end];
    }

    private static string ExtractMethodBody(string markup, string signature)
    {
        var start = markup.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, "The method '" + signature + "' is missing.");

        var braceStart = markup.IndexOf('{', start);
        Assert.IsGreaterThan(start, braceStart);

        var depth = 0;
        for (var index = braceStart; index < markup.Length; index++)
        {
            if (markup[index] == '{')
            {
                depth++;
            }
            else if (markup[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return markup[braceStart..(index + 1)];
                }
            }
        }

        Assert.Fail("The method '" + signature + "' is not correctly delimited.");
        return string.Empty;
    }

    private static string ExtractCssRule(string styles, string selector)
    {
        var start = styles.IndexOf(selector, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, "The CSS selector '" + selector + "' is missing.");

        var end = styles.IndexOf('}', start);
        Assert.IsGreaterThan(start, end, "The CSS selector '" + selector + "' has no closing brace.");
        return styles[start..(end + 1)];
    }
}
