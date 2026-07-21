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
    public void Preparation_ManualEntryActionOpensUsableEditorAndSavesThroughService()
    {
        var markup = LoadUi("PrepareWords.razor");

        Assert.Contains("@onclick=\"EnableManualEntry\"", markup);
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
        Assert.Contains("LexicalLookupMode.DefinitionAndTranslation", markup);
        Assert.Contains("_lookupLanguageInvalid", markup);
        Assert.Contains("Import_TargetLanguageMustDiffer", markup);
        Assert.DoesNotContain("CurrentUiCulture", markup);
    }

    [TestMethod]
    public void AndroidBeta_TestPackageIdentitiesAndPublishingContractAreStableAndExternalized()
    {
        var project = LoadUi("KnownFirst.csproj");
        var script = LoadUi("publish-android-test-packages.ps1");

        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst</ApplicationId>", project);
        Assert.Contains("<ApplicationDisplayVersion>1.0.0-beta.6</ApplicationDisplayVersion>", project);
        Assert.Contains("<ApplicationVersion>6</ApplicationVersion>", project);
        Assert.Contains("<PackageVersion>1.0.0-beta.6</PackageVersion>", project);
        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst.diagnostic</ApplicationId>", project);
        Assert.Contains("<ApplicationTitle>KnownFirst Diagnostic</ApplicationTitle>", project);
        Assert.Contains("<ApplicationDisplayVersion>1.0.0-beta.6-diagnostic</ApplicationDisplayVersion>", project);
        Assert.Contains("<DefineConstants>$(DefineConstants);KNOWNFIRST_DIAGNOSTICS</DefineConstants>", project);
        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst.debug</ApplicationId>", project);
        Assert.Contains("<ApplicationTitle>KnownFirst Debug</ApplicationTitle>", project);
        Assert.Contains("<ApplicationDisplayVersion>1.0.0-beta.6-debug</ApplicationDisplayVersion>", project);
        Assert.Contains("<AndroidUseFastDeployment>false</AndroidUseFastDeployment>", project);
        Assert.Contains("<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>", project);
        Assert.Contains("<RunAOTCompilation>true</RunAOTCompilation>", project);
        Assert.Contains("<DebugType>full</DebugType>", project);
        Assert.Contains("KnownFirst-Secrets", script);
        Assert.Contains("knownfirst-beta.keystore", script);
        Assert.Contains("env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD", script);
        Assert.Contains("AndroidPackageFormats=apk", script);
        Assert.Contains("KnownFirst-1.0.0-beta.6-android-release", script);
        Assert.Contains("KnownFirst-1.0.0-beta.6-android-diagnostic", script);
        Assert.Contains("KnownFirst-1.0.0-beta.6-android-debug", script);
        Assert.Contains("dotnet clean", script);
        Assert.Contains("apksigner", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("Compress-Archive", script);
        Assert.Contains("artifacts\\android-beta", script);
        Assert.DoesNotContain("storepass ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keypass ", script, StringComparison.OrdinalIgnoreCase);
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

    private static string LoadUi(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Ui",
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
}
