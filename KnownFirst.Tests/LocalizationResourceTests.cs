using System.Xml.Linq;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LocalizationResourceTests
{
    private static readonly string[] RequiredMilestoneOneKeys =
    [
        "App_Name",
        "App_Initializing",
        "App_InitializationError",
        "ErrorBoundary_Title",
        "ErrorBoundary_Message",
        "Navigation_Home",
        "Navigation_Menu",
        "Navigation_ImportText",
        "Navigation_ReviewWords",
        "Navigation_PrepareWords",
        "Navigation_Learn",
        "Navigation_Dictionary",
        "Navigation_Settings",
        "Home_Title",
        "Home_Subtitle",
        "Home_ImportedTexts",
        "Home_WordsToReview",
        "Home_KnownWords",
        "Home_UnknownWords",
        "Home_PreparedWords",
        "Home_LoadingStatistics",
        "Home_DashboardError",
        "Common_Save",
        "Common_Cancel",
        "Common_Back",
        "Common_Retry",
        "Settings_Title",
        "Settings_UILanguage",
        "Settings_English",
        "Settings_German",
        "Settings_Appearance",
        "Settings_AppearanceSystem",
        "Settings_AppearanceLight",
        "Settings_AppearanceDark",
        "Settings_LanguageChangedTo",
        "Settings_AppearanceChangedTo",
        "Settings_PreparationLimit",
        "Settings_ResetData",
        "Settings_ResetDescription",
        "Settings_ResetConfirmTitle",
        "Settings_ResetConfirmMessage",
        "Settings_ResetConfirmAction",
        "Settings_PreparationLimitSaved",
        "Settings_ResetSuccess",
        "Settings_ResetError",
        "Settings_SaveError",
        "Placeholder_Message",
        "NotFound_Title",
        "NotFound_Message",
        "Navigation_BackToHome",
        "Navigation_OpenMenu",
        "Navigation_CloseMenu",
        "Footer_DevelopedBy",
        "Footer_TachiguroLogoAlt",
        "Settings_HelpAndSupport",
        "Settings_SupportKnownFirst",
        "Settings_ReportBug",
        "Settings_ReportBugError",
        "Settings_CopyBugReportAddress",
        "Settings_BugReportAddressCopied",
        "Settings_BugReportAddressCopyError",
        "BugReport_Subject",
        "BugReport_PromptWhatHappened",
        "BugReport_PromptWhatExpected",
        "BugReport_PromptReproductionSteps",
        "BugReport_PromptOptionalScreenshots",
        "Common_FeatureComingSoon",
        "Common_Close",
        "Navigation_Diagnostics",
        "ReviewGate_Checking",
        "ReviewGate_Error",
        "Home_ReviewInProgress",
        "Home_ContinueReviewTitle",
        "Home_ContinueReviewDescription",
        "Home_ReviewProgress",
        "Home_ContinueReviewAction",
        "Import_DocumentTitle",
        "Import_TitlePlaceholder",
        "Import_Text",
        "Import_TextPlaceholder",
        "Import_TextLanguage",
        "Import_ExplanationLanguage",
        "Import_SaveAndAnalyze",
        "Import_Analyzing",
        "Import_AnalyzingProgress",
        "Import_TitleRequired",
        "Import_TextRequired",
        "Import_Error",
        "Import_ExactDuplicate",
        "Import_NoNewVocabulary",
        "Review_Loading",
        "Review_LoadError",
        "Review_NoActive",
        "Review_StartImport",
        "Review_ProgressLabel",
        "Review_Progress",
        "Review_Candidate",
        "Review_TokenKind",
        "Review_EncounteredForms",
        "Review_OccurrenceCount",
        "Review_MultipleContextsNotice",
        "Review_Context",
        "Review_PreviousContext",
        "Review_NextContext",
        "Review_ContextPosition",
        "Review_Question",
        "Review_Known",
        "Review_Unknown",
        "Review_Ignore",
        "Review_Undo",
        "Review_DecisionError",
        "Review_CompleteTitle",
        "Review_CompleteDescription",
        "Review_DiscardTitle",
        "Review_DiscardAction",
        "Review_DiscardMessage",
        "Review_DiscardConfirm",
        "Review_DiscardError",
        "Analysis_Details",
        "Analysis_CopyReport",
        "Analysis_ReportCopied",
        "Analysis_Title",
        "Analysis_DocumentSummary",
        "Analysis_IncludedTokens",
        "Analysis_ExcludedDecisions",
        "Analysis_Fingerprint",
        "Analysis_CandidateDetails",
        "Analysis_FormsBefore",
        "Analysis_FormsAfter",
        "Analysis_SentenceSpans",
        "Analysis_BoundaryReason",
        "Analysis_ExactText",
        "Analysis_TokenDecisions",
        "Analysis_RawValue",
        "Analysis_NormalizedValue",
        "Analysis_Decision",
        "Analysis_Reason",
        "Analysis_Included",
        "Analysis_Excluded",
        "Analysis_CandidateGrouping",
        "Analysis_ContextSelection",
        "Analysis_Selected",
        "Analysis_Rejected",
        "Analysis_Target",
        "Analysis_Coordinates",
        "Analysis_RelativeStart",
        "Analysis_Invariants",
        "Analysis_InvariantsPassed",
        "TokenKind_Word",
        "TokenKind_Acronym",
        "TokenKind_Abbreviation",
        "TokenKind_TechnicalTerm",
        "Learn_NextMilestone",
        "Prepare_NextMilestone",
        "Diagnostics_Title",
        "Diagnostics_Refresh",
        "Diagnostics_Loading",
        "Diagnostics_Error",
        "Diagnostics_Database",
        "Diagnostics_ActiveSession",
        "Diagnostics_NoActiveSession",
        "Diagnostics_Documents",
        "Diagnostics_Characters",
        "Diagnostics_Occurrences",
        "Diagnostics_Sentences",
        "Diagnostics_Candidates",
        "Diagnostics_StoredOccurrences",
        "Diagnostics_DocumentId",
        "Diagnostics_WordId",
        "Diagnostics_SentenceId",
        "Diagnostics_Start",
        "Diagnostics_Length",
        "Diagnostics_Order",
        "Diagnostics_Status",
        "Diagnostics_ExplanationTitle",
        "Diagnostics_ExplanationIntro",
        "Diagnostics_TermDocument",
        "Diagnostics_TermDocumentDescription",
        "Diagnostics_TermSession",
        "Diagnostics_TermSessionDescription",
        "Diagnostics_TermSentenceSpan",
        "Diagnostics_TermSentenceSpanDescription",
        "Diagnostics_TermCandidate",
        "Diagnostics_TermCandidateDescription",
        "Diagnostics_TermOccurrence",
        "Diagnostics_TermOccurrenceDescription",
        "Diagnostics_TermStartDescription",
        "Diagnostics_TermLengthDescription",
        "Diagnostics_TermOrderDescription",
        "Diagnostics_TermStatusDescription",
        "Diagnostics_CopyReport",
        "Diagnostics_CopyDatabasePath",
        "Diagnostics_ReportCopied",
        "Diagnostics_DatabasePathCopied",
        "Diagnostics_CopyError",
        "Diagnostics_TitleColumn",
        "Diagnostics_SourceLanguage",
        "Diagnostics_SentenceCount",
        "Diagnostics_ImportDate",
        "Diagnostics_ReviewStatus",
        "Diagnostics_DocumentTitle",
        "Diagnostics_Preview",
        "Diagnostics_CandidateText",
        "Diagnostics_Storage",
        "Diagnostics_Temporary",
        "Diagnostics_Retained",
        "Diagnostics_Sessions",
        "Diagnostics_State",
        "Diagnostics_Reviewed",
        "Diagnostics_Total",
        "Diagnostics_Remaining",
        "Diagnostics_StatusActive",
        "Diagnostics_StatusCompleted",
        "Diagnostics_StatusNoSession",
        "Diagnostics_NoRows",
        "Diagnostics_TechnicalDetails",
        "Diagnostics_RecordType",
        "Diagnostics_RelatedIds",
        "WordStatus_Unreviewed",
        "WordStatus_Prepared",
        "WordStatus_Learning",
        "WordStatus_Mastered"
    ];

    private static readonly string[] RequiredAutomaticDictionaryMvpKeys =
    [
        "Common_Continue",
        "Common_Later",
        "Common_TryAgain",
        "Common_Confirm",
        "Common_ShowFullText",
        "Navigation_LearnUnavailable",
        "Navigation_ImportBlockedByReview",
        "Navigation_PrepareBlockedByReview",
        "Navigation_PrepareUnavailable",
        "Home_ContinuePreparation",
        "Settings_PreparationLimitHelp",
        "Settings_ReturnToReview",
        "Settings_CardDirection",
        "Settings_CardDirectionTermToMeaning",
        "Settings_CardDirectionMeaningToTerm",
        "Settings_CardDirectionBoth",
        "Settings_CardDirectionSaved",
        "Settings_OnlineDictionary",
        "Settings_OnlineConsentGranted",
        "Settings_OnlineConsentNotGranted",
        "Settings_RevokeOnlineConsent",
        "Settings_RevokeOnlineConsentConfirmMessage",
        "Settings_OnlineConsentRevoked",
        "Settings_ActivateOnlineConsent",
        "Settings_OnlineConsentActivated",
        "Settings_EnhancedTermRecognition",
        "Settings_EnhancedTermRecognitionHelp",
        "Settings_EnhancedTermRecognitionOn",
        "Settings_EnhancedTermRecognitionOff",
        "Settings_EnhancedTermRecognitionSaved",
        "Prepare_Loading",
        "Prepare_LoadError",
        "Prepare_OnlineDisclosureTitle",
        "Prepare_OnlineDisclosure",
        "Prepare_StartOnlineLookup",
        "Prepare_NoWordsTitle",
        "Prepare_NoWords",
        "Prepare_MethodQuestion",
        "Prepare_BatchDescription",
        "Prepare_AutomaticOnline",
        "Prepare_AutomaticRecommended",
        "Prepare_Manual",
        "Prepare_MethodManual",
        "Prepare_ManualDescription",
        "Prepare_Progress",
        "Prepare_Candidate",
        "Prepare_LookingUp",
        "Prepare_ManualEntry",
        "Prepare_SkipForNow",
        "Prepare_AcronymExpansion",
        "Prepare_Translation",
        "Prepare_Definition",
        "Prepare_AdditionalNote",
        "Prepare_AcceptedAliases",
        "Prepare_AcceptedAliasesHelp",
        "Prepare_AdvancedOptions",
        "Prepare_DefinitionRequired",
        "Prepare_TranslationRequired",
        "Prepare_AnswerRequired",
        "Prepare_SaveAndContinue",
        "Prepare_SeveralMeanings",
        "Prepare_Source",
        "Prepare_AcceptAndContinue",
        "Prepare_ChooseAnotherMeaning",
        "Prepare_ChooseOneMeaningExplanation",
        "Prepare_Edit",
        "Prepare_CancelPreparation",
        "Prepare_EndPreparation",
        "Prepare_EndPreparationConfirmation",
        "Prepare_SaveError",
        "Prepare_SavedNextLoadFailed",
        "Prepare_RetryNextItem",
        "Prepare_BatchCompleteTitle",
        "Prepare_BatchComplete",
        "Prepare_ChangeLimit",
        "Prepare_StartLearning",
        "Prepare_RateLimited",
        "Prepare_Offline",
        "Prepare_NoResult",
        "Prepare_InText",
        "Prepare_Saving",
        "Prepare_OtherActions",
        "Prepare_MarkKnown",
        "Prepare_MarkKnownConfirmation",
        "Prepare_DoNotLearn",
        "Prepare_DoNotLearnConfirmation",
        "Prepare_TransientFailure",
        "Prepare_ParseFailure",
        "Prepare_PermanentFailure",
        "Prepare_NotFound",
        "Prepare_DictionaryEntryNotFound",
        "Prepare_DefinitionNotFound",
        "Prepare_TranslationNotFound",
        "Prepare_LanguageSectionNotFound",
        "Prepare_NetworkFailure",
        "Prepare_ResponseParseFailure",
        "Review_Details",
        "Review_Saving",
        "Source_Details",
        "Source_Provider",
        "Source_Project",
        "Source_PageTitle",
        "Source_Revision",
        "Source_Attribution",
        "Source_License",
        "Learn_Loading",
        "Learn_LoadError",
        "Learn_Progress",
        "Learn_TermToMeaning",
        "Learn_MeaningToTerm",
        "Learn_RevealAnswer",
        "Learn_YourAnswer",
        "Learn_CheckAnswer",
        "Learn_SpellingCorrect",
        "Learn_SpellingIncorrect",
        "Learn_EnteredAnswer",
        "Learn_CorrectAnswer",
        "Learn_AcceptedAlias",
        "Learn_Difference",
        "Learn_HiddenTarget",
        "Learn_DictionaryExample",
        "Learn_Again",
        "Learn_Hard",
        "Learn_Good",
        "Learn_Easy",
        "Learn_MarkPermanentlyKnown",
        "Learn_PermanentKnownConfirmation",
        "Learn_ActionError",
        "Learn_SessionComplete",
        "Learn_CardsReviewed",
        "Learn_NextDue",
        "Learn_MoreUnknownWaiting",
        "Learn_PrepareNextWords",
        "Learn_NothingDue",
        "Learn_NoCardsTitle",
        "Diagnostics_LexicalCache",
        "Diagnostics_Lemma",
        "Diagnostics_Languages",
        "Diagnostics_Provider",
        "Diagnostics_SourcePage",
        "Diagnostics_Revision",
        "Diagnostics_Fetched",
        "Diagnostics_Preparation",
        "Diagnostics_Method",
        "Diagnostics_Completed",
        "Diagnostics_SelectedMeaning",
        "Diagnostics_AvailableMeanings",
        "Diagnostics_LookupAttempts",
        "Diagnostics_ErrorCode",
        "Diagnostics_PreparedMeanings",
        "Diagnostics_Confirmed",
        "Diagnostics_Learning",
        "Diagnostics_LearningSessions",
        "Diagnostics_Ratings",
        "Diagnostics_LearningCards",
        "Diagnostics_Direction",
        "Diagnostics_Due",
        "Diagnostics_IntervalDays",
        "Diagnostics_EaseFactor",
        "Diagnostics_LastRating",
        "Diagnostics_LearningReviews",
        "Diagnostics_Rating",
        "Diagnostics_ReviewedAt",
        "Diagnostics_CleanupEligibility",
        "Diagnostics_ActiveReview",
        "Diagnostics_HasOccurrences",
        "Diagnostics_ActiveContexts",
        "Diagnostics_Eligible",
        "Diagnostics_Yes",
        "Diagnostics_No",
        "Diagnostics_PreparationTiming",
        "Diagnostics_PreparationTimingDescription",
        "Diagnostics_Sequence",
        "Diagnostics_Operation",
        "Diagnostics_Phase",
        "Diagnostics_DurationMilliseconds",
        "Diagnostics_CandidateId",
        "Diagnostics_TimingPhase_Validation",
        "Diagnostics_TimingPhase_DatabaseTransaction",
        "Diagnostics_TimingPhase_PreparedMeaningSave",
        "Diagnostics_TimingPhase_LearningCardCreation",
        "Diagnostics_TimingPhase_SessionUpdate",
        "Diagnostics_TimingPhase_NextCandidateQuery",
        "Diagnostics_TimingPhase_ContextLoading",
        "Diagnostics_TimingPhase_UiTransition",
        "Diagnostics_TimingPhase_NetworkWork",
        "Diagnostics_LexicalLogTitle",
        "Diagnostics_LexicalLogDescription",
        "Diagnostics_CopyDiagnosticReport",
        "Diagnostics_ExportDiagnosticLog",
        "Diagnostics_ClearDiagnosticLog",
        "Diagnostics_DiagnosticReportCopied",
        "Diagnostics_DiagnosticLogExported",
        "Diagnostics_DiagnosticLogCleared",
        "Diagnostics_DiagnosticLogActionError",
        "Diagnostics_DebugLabel",
        "Diagnostics_DebugLearningTools",
        "Diagnostics_DebugLearningDescription",
        "Diagnostics_DebugCurrentTime",
        "Diagnostics_DebugTimeOffset",
        "Diagnostics_DebugMakeAllCardsDue",
        "Diagnostics_DebugAdvanceMinute",
        "Diagnostics_DebugAdvanceHour",
        "Diagnostics_DebugAdvanceDay",
        "Diagnostics_DebugAdvanceFourDays",
        "Diagnostics_DebugResetTime",
        "Diagnostics_DebugTimeUpdated"
    ];

    [TestMethod]
    public void VisualConsistencySliceOne_ConsentConfirmationKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");
        const string confirmationKey = "Settings_RevokeOnlineConsentConfirmMessage";

        Assert.IsTrue(english.ContainsKey(confirmationKey), "The English consent-revocation confirmation is missing.");
        Assert.IsTrue(german.ContainsKey(confirmationKey), "The German consent-revocation confirmation is missing.");
        Assert.IsTrue(russian.ContainsKey(confirmationKey), "The Russian consent-revocation confirmation is missing.");
    }

    [TestMethod]
    public void VisualConsistencySliceFour_SystemLanguageStatusKeysExistInAllSupportedLanguages()
    {
        var resourceSets = new[]
        {
            LoadResources("SharedResource.resx"),
            LoadResources("SharedResource.de.resx"),
            LoadResources("SharedResource.ru.resx")
        };

        foreach (var resources in resourceSets)
        {
            Assert.IsTrue(resources.ContainsKey("Onboarding_SystemLanguageDetected"));
            Assert.IsTrue(resources.ContainsKey("Onboarding_SystemLanguageUnsupported"));
            Assert.Contains("{0}", resources["Onboarding_SystemLanguageDetected"]);
            Assert.Contains("{1}", resources["Onboarding_SystemLanguageDetected"]);
        }

        Assert.Contains("KnownFirst", resourceSets[0]["Onboarding_SystemLanguageUnsupported"]);
        Assert.Contains("KnownFirst", resourceSets[1]["Onboarding_SystemLanguageUnsupported"]);
        Assert.Contains("KnownFirst", resourceSets[2]["Onboarding_SystemLanguageUnsupported"]);
    }

    [TestMethod]
    public void Resources_EveryEnglishKeyHasGermanCounterpart()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var missingKeys = english.Keys.Except(german.Keys, StringComparer.Ordinal).ToArray();

        Assert.IsEmpty(missingKeys, $"Missing German keys: {string.Join(", ", missingKeys)}");
    }

    [TestMethod]
    public void Resources_EveryGermanKeyHasEnglishCounterpart()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var missingKeys = german.Keys.Except(english.Keys, StringComparer.Ordinal).ToArray();

        Assert.IsEmpty(missingKeys, $"Missing English keys: {string.Join(", ", missingKeys)}");
    }

    [TestMethod]
    public void Resources_EveryEnglishKeyHasRussianCounterpart()
    {
        var english = LoadResources("SharedResource.resx");
        var russian = LoadResources("SharedResource.ru.resx");
        var missingKeys = english.Keys.Except(russian.Keys, StringComparer.Ordinal).ToArray();

        Assert.IsEmpty(missingKeys, $"Missing Russian keys: {string.Join(", ", missingKeys)}");
    }

    [TestMethod]
    public void Resources_EveryRussianKeyHasEnglishCounterpart()
    {
        var english = LoadResources("SharedResource.resx");
        var russian = LoadResources("SharedResource.ru.resx");
        var missingKeys = russian.Keys.Except(english.Keys, StringComparer.Ordinal).ToArray();

        Assert.IsEmpty(missingKeys, $"Missing English keys: {string.Join(", ", missingKeys)}");
    }

    [TestMethod]
    public void Resources_NoResourceValueIsEmpty()
    {
        var emptyEntries = new[] { "SharedResource.resx", "SharedResource.de.resx", "SharedResource.ru.resx" }
            .SelectMany(fileName => LoadResources(fileName)
                .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{fileName}:{entry.Key}"))
            .ToArray();

        Assert.IsEmpty(emptyEntries, $"Empty resource values: {string.Join(", ", emptyEntries)}");
    }

    [TestMethod]
    public void Resources_AllCurrentMilestoneOneUiKeysExist()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredMilestoneOneKeys.Concat(RequiredAutomaticDictionaryMvpKeys))
        {
            Assert.IsTrue(english.ContainsKey(key), $"The English resource key '{key}' is missing.");
            Assert.IsTrue(german.ContainsKey(key), $"The German resource key '{key}' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), $"The Russian resource key '{key}' is missing.");
        }
    }

    [TestMethod]
    public void Resources_HomeGreetingUsesExactLocalizedWordingAndOneNamePlaceholder()
    {
        var resourcesByLanguage = new[]
        {
            (FileName: "SharedResource.resx", Expected: "Welcome, {0}."),
            (FileName: "SharedResource.de.resx", Expected: "Willkommen, {0}."),
            (FileName: "SharedResource.ru.resx", Expected: "Добро пожаловать, {0}.")
        };
        var placeholderPattern = new System.Text.RegularExpressions.Regex(@"\{\d+\}");

        foreach (var resourceLanguage in resourcesByLanguage)
        {
            var resources = LoadResources(resourceLanguage.FileName);

            Assert.IsTrue(
                resources.ContainsKey("Home_Greeting"),
                resourceLanguage.FileName + " is missing Home_Greeting.");
            Assert.AreEqual(resourceLanguage.Expected, resources["Home_Greeting"]);
            Assert.HasCount(
                1,
                placeholderPattern.Matches(resources["Home_Greeting"]),
                resourceLanguage.FileName + " must keep exactly one Display Name placeholder.");
        }
    }

    [TestMethod]
    public void Resources_RussianLanguageOptionKeysExistAndAreDistinctFromGerman()
    {
        var russian = LoadResources("SharedResource.ru.resx");

        Assert.IsTrue(russian.ContainsKey("Settings_Russian"));
        Assert.IsTrue(russian.ContainsKey("Settings_UILanguageSystem"));
        Assert.IsTrue(russian.ContainsKey("Settings_LanguageChangedToRussian"));
        Assert.IsTrue(russian.ContainsKey("Settings_LanguageChangedToSystem"));
        Assert.AreNotEqual(russian["Settings_German"], russian["Settings_Russian"]);
    }

    [TestMethod]
    public void Resources_RussianLocalizationUsesCyrillicLanguageNames()
    {
        var russian = LoadResources("SharedResource.ru.resx");

        Assert.AreEqual("Английский", russian["Settings_English"],
            "Russian resource should use Cyrillic for English language name.");
        Assert.AreEqual("Немецкий", russian["Settings_German"],
            "Russian resource should use Cyrillic for German language name.");
        Assert.AreEqual("Русский", russian["Settings_Russian"],
            "Russian resource should use Cyrillic for Russian language name.");
        Assert.AreEqual("Изучение", russian["Navigation_Learn"],
            "Russian resource should use noun form for Navigation_Learn.");
    }

    [TestMethod]
    public void Resources_PlaceholderCountsMatchAcrossEnglishGermanAndRussian()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");
        var placeholderPattern = new System.Text.RegularExpressions.Regex(@"\{\d+\}");

        var mismatches = english.Keys
            .Select(key => new
            {
                Key = key,
                EnglishCount = placeholderPattern.Matches(english[key]).Count,
                GermanCount = placeholderPattern.Matches(german[key]).Count,
                RussianCount = placeholderPattern.Matches(russian[key]).Count
            })
            .Where(entry => entry.EnglishCount != entry.GermanCount || entry.EnglishCount != entry.RussianCount)
            .Select(entry => $"{entry.Key} (en={entry.EnglishCount}, de={entry.GermanCount}, ru={entry.RussianCount})")
            .ToArray();

        Assert.IsEmpty(mismatches, $"Placeholder count mismatches: {string.Join(", ", mismatches)}");
    }

    [TestMethod]
    public void Resources_OnlineDisclosureMatchesBindingSpecification()
    {
        const string expectedEnglish = "KnownFirst does not send your documents, example sentences, learning history, or personal data to the KnownFirst developer. Only after consent, the selected term and required language information are sent to Wikimedia services. KnownFirst queries Wiktionary first for dictionary content; if no suitable Wiktionary result is found, Wikipedia may be queried for an encyclopedic definition. Wikipedia does not provide translations. Standard network metadata such as your IP address and the KnownFirst User-Agent are transmitted with requests. Retrieved content and your learning data remain stored locally on this device.";
        const string expectedGerman = "KnownFirst sendet keine Dokumente, Beispielsätze, Lernhistorie oder persönlichen Daten an den Entwickler von KnownFirst. Erst nach deiner Einwilligung werden der ausgewählte Begriff und die erforderlichen Sprachinformationen an Wikimedia-Dienste übertragen. KnownFirst fragt zuerst Wiktionary nach Wörterbuchinhalten ab; wird dort kein passendes Ergebnis gefunden, kann Wikipedia für eine enzyklopädische Definition angefragt werden. Wikipedia liefert keine Übersetzungen. Bei Netzwerkanfragen werden übliche Verbindungsdaten wie deine IP-Adresse und der KnownFirst-User-Agent übermittelt. Abgerufene Inhalte und deine Lerndaten werden lokal auf diesem Gerät gespeichert.";

        Assert.AreEqual(expectedEnglish, LoadResources("SharedResource.resx")["Prepare_OnlineDisclosure"]);
        Assert.AreEqual(expectedGerman, LoadResources("SharedResource.de.resx")["Prepare_OnlineDisclosure"]);
    }

    [TestMethod]
    public void Resources_BindingGermanLearningActionsUseSpecifiedLabels()
    {
        var german = LoadResources("SharedResource.de.resx");

        Assert.AreEqual("Nochmal", german["Learn_Again"]);
        Assert.AreEqual("Schwer", german["Learn_Hard"]);
        Assert.AreEqual("Gut", german["Learn_Good"]);
        Assert.AreEqual("Einfach", german["Learn_Easy"]);
        Assert.AreEqual("Dauerhaft als bekannt markieren", german["Learn_MarkPermanentlyKnown"]);
        Assert.AreEqual("Online-Abfrage starten", german["Prepare_StartOnlineLookup"]);
        Assert.AreEqual("Manuell vorbereiten", german["Prepare_Manual"]);
    }

    [TestMethod]
    public void Resources_ManualPreparationActionsAndValidationUseContextSpecificWording()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        Assert.AreEqual("Mark as known", english["Prepare_MarkKnown"]);
        Assert.AreEqual("Exclude from learning", english["Prepare_DoNotLearn"]);
        Assert.AreEqual("End preparation", english["Prepare_EndPreparation"]);
        Assert.AreEqual("Als bekannt markieren", german["Prepare_MarkKnown"]);
        Assert.AreEqual("Vom Lernen ausschließen", german["Prepare_DoNotLearn"]);
        Assert.AreEqual("Vorbereitung beenden", german["Prepare_EndPreparation"]);
        Assert.AreEqual("Отметить как известное", russian["Prepare_MarkKnown"]);
        Assert.AreEqual("Исключить из обучения", russian["Prepare_DoNotLearn"]);
        Assert.AreEqual("Завершить подготовку", russian["Prepare_EndPreparation"]);

        foreach (var resources in new[] { english, german, russian })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources["Prepare_DefinitionRequired"]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources["Prepare_TranslationRequired"]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources["Prepare_AnswerRequired"]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources["Prepare_SavedNextLoadFailed"]));
        }
    }

    [TestMethod]
    public void Resources_LearningDirectionAndRepeatLabelsUseSpecifiedWording()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        Assert.AreEqual("Term → meaning", english["Learn_DirectionTermToMeaning"]);
        Assert.AreEqual("Meaning → term", english["Learn_DirectionMeaningToTerm"]);
        Assert.AreEqual("Repeat", english["Learn_AgainRepeatBadge"]);

        Assert.AreEqual("Wort → Bedeutung", german["Learn_DirectionTermToMeaning"]);
        Assert.AreEqual("Bedeutung → Wort", german["Learn_DirectionMeaningToTerm"]);
        Assert.AreEqual("Wiederholung", german["Learn_AgainRepeatBadge"]);

        Assert.AreEqual("Слово → значение", russian["Learn_DirectionTermToMeaning"]);
        Assert.AreEqual("Значение → слово", russian["Learn_DirectionMeaningToTerm"]);
        Assert.AreEqual("Повторение", russian["Learn_AgainRepeatBadge"]);
    }

    [TestMethod]
    public void Resources_DailyLimitAndPostLearningRecommendationMatchSpecification()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");

        Assert.AreEqual("New words per day", english["Settings_PreparationLimit"]);
        Assert.AreEqual("Neue Wörter pro Tag", german["Settings_PreparationLimit"]);
        Assert.AreEqual(
            "Limits new learning words per day so preparation and study remain manageable. Due reviews do not count.",
            english["Settings_PreparationLimitHelp"]);
        Assert.AreEqual(
            "Begrenzt neue Lernwörter pro Tag, damit Vorbereitung und Lernen überschaubar bleiben. Fällige Wiederholungen zählen nicht dazu.",
            german["Settings_PreparationLimitHelp"]);
        Assert.AreEqual(
            "All current reviews are complete. {0} unknown words are waiting for preparation.",
            english["Learn_MoreUnknownWaiting"]);
        Assert.AreEqual(
            "Alle aktuellen Wiederholungen sind abgeschlossen. {0} unbekannte Wörter warten auf die Vorbereitung.",
            german["Learn_MoreUnknownWaiting"]);
    }

    private static Dictionary<string, string> LoadResources(string fileName)
    {
        var resourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "Localization",
            fileName);
        var document = XDocument.Load(resourcePath);

        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static readonly string[] RequiredSettingsGuiSliceTwoAKeys =
    [
        "Settings_LearningTimezone",
        "Settings_LearningTimezoneHelp",
        "Settings_LearningTimezoneSystem",
        "Settings_LearningTimezoneEffective",
        "Settings_LearningTimezoneSaved",
        "Settings_LearningDayCutoff",
        "Settings_LearningDayCutoffHelp",
        "Settings_LearningDayCutoffSaved",
        "Settings_RestoreDefaults",
        "Settings_RestoreDefaultsDescription",
        "Settings_RestoreDefaultsConfirmMessage",
        "Settings_RestoreDefaultsConfirmAction",
        "Settings_RestoreDefaultsSuccess",
        "Settings_RestoreDefaultsError"
    ];

    private static readonly string[] RequiredDailyBudgetSliceThreeKeys =
    [
        "Settings_PreparationLimitRecommended",
        "Settings_PreparationLimitCustom",
        "Settings_PreparationLimitCustomLabel",
        "Settings_PreparationLimitCustomHelp",
        "Settings_PreparationLimitCustomInvalid",
        "Settings_PreparationLimitHighWarning"
    ];

    [TestMethod]
    public void Resources_DailyBudgetSliceThreeKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredDailyBudgetSliceThreeKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    [TestMethod]
    public void VisualConsistencySliceTwo_DailyBudgetUiUsesExistingLocalizedKeysWithoutLeaks()
    {
        var uiRoot = Path.Combine(AppContext.BaseDirectory, "Ui");
        var settingsMarkup = File.ReadAllText(Path.Combine(uiRoot, "Settings.razor"));
        var onboardingMarkup = File.ReadAllText(Path.Combine(uiRoot, "Steps", "DailyPaceStep.razor"));

        foreach (var markup in new[] { settingsMarkup, onboardingMarkup })
        {
            Assert.Contains("Localizer[\"Settings_PreparationLimitRecommended\"]", markup);
            Assert.Contains("\"Settings_PreparationLimitCustomInvalid\"", markup);
            Assert.Contains("@Localizer[_customLimitValidationError]", markup);
            Assert.DoesNotContain("Settings_PreparationLimitRecommendedTag", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings_PreparationLimitCustomRangeError", markup, StringComparison.Ordinal);
        }

        foreach (var fileName in new[] { "SharedResource.resx", "SharedResource.de.resx", "SharedResource.ru.resx" })
        {
            var resources = LoadResources(fileName);
            Assert.IsTrue(resources.ContainsKey("Settings_PreparationLimitRecommended"));
            Assert.IsTrue(resources.ContainsKey("Settings_PreparationLimitCustomInvalid"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources["Settings_PreparationLimitRecommended"]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources["Settings_PreparationLimitCustomInvalid"]));
            Assert.IsFalse(resources.ContainsKey("Settings_PreparationLimitRecommendedTag"));
            Assert.IsFalse(resources.ContainsKey("Settings_PreparationLimitCustomRangeError"));
        }
    }

    [TestMethod]
    public void Resources_SettingsGuiSliceTwoAKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredSettingsGuiSliceTwoAKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    [TestMethod]
    public void Resources_EveryCuratedTimezoneCityKeyExistsInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var option in KnownFirst.Core.Settings.LearningTimezoneCatalog.Options)
        {
            var key = option.CityResourceKey;

            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    [TestMethod]
    public void Resources_TimezoneCityNamesUseCyrillicInTheRussianResource()
    {
        var russian = LoadResources("SharedResource.ru.resx");

        Assert.AreEqual("Берлин", russian["Timezone_City_Europe_Berlin"]);
        Assert.AreEqual("Нью-Йорк", russian["Timezone_City_America_New_York"]);
        Assert.AreEqual("Токио", russian["Timezone_City_Asia_Tokyo"]);
    }

    [TestMethod]
    public void Resources_TimezoneCityKeysCarryNoFormatPlaceholders()
    {
        var placeholderPattern = new System.Text.RegularExpressions.Regex(@"\{\d+\}");

        foreach (var fileName in new[] { "SharedResource.resx", "SharedResource.de.resx", "SharedResource.ru.resx" })
        {
            var resources = LoadResources(fileName);

            foreach (var option in KnownFirst.Core.Settings.LearningTimezoneCatalog.Options)
            {
                Assert.IsEmpty(
                    placeholderPattern.Matches(resources[option.CityResourceKey]),
                    "Timezone city names must not contain format placeholders: "
                        + fileName + ":" + option.CityResourceKey);
            }
        }
    }

    [TestMethod]
    public void Resources_LearningTimezoneEffectiveLabelKeepsExactlyOnePlaceholder()
    {
        var placeholderPattern = new System.Text.RegularExpressions.Regex(@"\{\d+\}");

        foreach (var fileName in new[] { "SharedResource.resx", "SharedResource.de.resx", "SharedResource.ru.resx" })
        {
            var resources = LoadResources(fileName);

            Assert.HasCount(
                1,
                placeholderPattern.Matches(resources["Settings_LearningTimezoneEffective"]),
                fileName + " must keep exactly one placeholder for Settings_LearningTimezoneEffective.");
        }
    }

    private static readonly string[] RequiredOnboardingSliceFourKeys =
    [
        "Onboarding_WelcomeTitle",
        "Onboarding_WelcomeConcept",
    ];

    [TestMethod]
    public void Resources_OnboardingSliceFourKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredOnboardingSliceFourKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    private static readonly string[] RequiredOnboardingSliceFiveKeys =
    [
        "Onboarding_DisplayNameTitle",
        "Onboarding_DisplayNameDescription",
        "Onboarding_WorkflowTitle",
        "Onboarding_WorkflowStep1",
        "Onboarding_WorkflowStep2",
        "Onboarding_WorkflowStep3",
        "Onboarding_OnlineLookupTitle",
        "Onboarding_OnlineLookupDescription",
        "Onboarding_EnhancedTermRecognitionTitle",
        "Onboarding_EnhancedTermRecognitionDescription",
        "Onboarding_PracticeTitle",
        "Onboarding_PracticeDescription",
    ];

    [TestMethod]
    public void Resources_OnboardingSliceFiveKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredOnboardingSliceFiveKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    private static readonly string[] RequiredOnboardingSliceSixKeys =
    [
        "Onboarding_DailyPaceTitle",
        "Onboarding_DailyPaceDescription",
        "Onboarding_LearningDayTimingTitle",
        "Onboarding_LearningDayTimingDescription",
        "Onboarding_SummaryTitle",
        "Onboarding_SummaryDisplayNameNotSet",
        "Onboarding_SummaryLearningTimezoneSystem",
        "Onboarding_FinishSetup",
    ];

    [TestMethod]
    public void Resources_OnboardingSliceSixKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredOnboardingSliceSixKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    private static readonly string[] RequiredOnboardingParityFeedbackKeys =
    [
        "Onboarding_DisplayNameSkip",
        "Onboarding_OnlineLookupEnable",
        "Onboarding_OnlineLookupKeepDisabled",
        "Onboarding_OnlineLookupServiceWiktionary",
        "Onboarding_OnlineLookupServiceWikipedia",
        "Onboarding_OnlineLookupPrivacyNotice",
        "Onboarding_EnhancedTermRecognitionDescription",
        "Onboarding_SummarySettingsNotice",
    ];

    [TestMethod]
    public void Resources_OnboardingParityFeedbackKeysExistInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in RequiredOnboardingParityFeedbackKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), "The English resource key '" + key + "' is missing.");
            Assert.IsTrue(german.ContainsKey(key), "The German resource key '" + key + "' is missing.");
            Assert.IsTrue(russian.ContainsKey(key), "The Russian resource key '" + key + "' is missing.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), "The English value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), "The German value for '" + key + "' is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), "The Russian value for '" + key + "' is empty.");
        }
    }

    [TestMethod]
    public void ReviewWords_DiscardActionLocalizationMatchesBindingContract()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");
        const string actionKey = "Review_DiscardAction";

        Assert.IsTrue(english.ContainsKey(actionKey), "English resource is missing Review_DiscardAction.");
        Assert.IsTrue(german.ContainsKey(actionKey), "German resource is missing Review_DiscardAction.");
        Assert.IsTrue(russian.ContainsKey(actionKey), "Russian resource is missing Review_DiscardAction.");

        Assert.AreEqual("Discard import", english[actionKey]);
        Assert.AreEqual("Import verwerfen", german[actionKey]);
        Assert.AreEqual("Отменить импорт", russian[actionKey]);
    }

    [TestMethod]
    public void ReviewWords_LiteralLocalizerKeysExistInResourcesWithoutLeaks()
    {
        var uiRoot = Path.Combine(AppContext.BaseDirectory, "Ui");
        var reviewMarkup = File.ReadAllText(Path.Combine(uiRoot, "ReviewWords.razor"));

        var keyMatches = System.Text.RegularExpressions.Regex.Matches(
            reviewMarkup,
            @"Localizer\[""([^""]+)""\]");

        Assert.IsTrue(keyMatches.Count > 0, "No literal Localizer keys found in ReviewWords.razor.");

        var referencedKeys = keyMatches
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        foreach (var key in referencedKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), $"ReviewWords references missing English key: '{key}'");
            Assert.IsTrue(german.ContainsKey(key), $"ReviewWords references missing German key: '{key}'");
            Assert.IsTrue(russian.ContainsKey(key), $"ReviewWords references missing Russian key: '{key}'");
        }

        Assert.Contains("Review_DiscardAction", referencedKeys);
        Assert.IsFalse(referencedKeys.Contains("Review_Discard", StringComparer.Ordinal), "Raw Review_Discard must not be referenced.");
    }

    [TestMethod]
    public void Preparation_DispositionActionLocalizationMatchesBindingContract()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        const string markKnownKey = "Prepare_MarkKnown";
        const string doNotLearnKey = "Prepare_DoNotLearn";
        const string markKnownConfirmKey = "Prepare_MarkKnownConfirmation";
        const string doNotLearnConfirmKey = "Prepare_DoNotLearnConfirmation";

        var resources = new (string Name, Dictionary<string, string> Data)[]
        {
            ("English", english),
            ("German", german),
            ("Russian", russian)
        };

        foreach (var (name, resource) in resources)
        {
            Assert.IsTrue(resource.ContainsKey(markKnownKey), $"{name} resource is missing {markKnownKey}.");
            Assert.IsTrue(resource.ContainsKey(doNotLearnKey), $"{name} resource is missing {doNotLearnKey}.");
            Assert.IsTrue(resource.ContainsKey(markKnownConfirmKey), $"{name} resource is missing {markKnownConfirmKey}.");
            Assert.IsTrue(resource.ContainsKey(doNotLearnConfirmKey), $"{name} resource is missing {doNotLearnConfirmKey}.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(resource[markKnownKey]), $"{name} value for {markKnownKey} is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(resource[doNotLearnKey]), $"{name} value for {doNotLearnKey} is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(resource[markKnownConfirmKey]), $"{name} value for {markKnownConfirmKey} is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(resource[doNotLearnConfirmKey]), $"{name} value for {doNotLearnConfirmKey} is empty.");

            Assert.AreNotEqual(
                resource[markKnownKey],
                resource[doNotLearnKey],
                $"{name} disposition actions {markKnownKey} and {doNotLearnKey} must not be identical.");
        }

        Assert.AreEqual("Mark as known", english[markKnownKey]);
        Assert.AreEqual("Exclude from learning", english[doNotLearnKey]);

        Assert.AreEqual("Als bekannt markieren", german[markKnownKey]);
        Assert.AreEqual("Vom Lernen ausschließen", german[doNotLearnKey]);

        Assert.AreEqual("Отметить как известное", russian[markKnownKey]);
        Assert.AreEqual("Исключить из обучения", russian[doNotLearnKey]);
    }

    [TestMethod]
    public void Resources_CardDirectionHelpExistsInAllSupportedLanguages()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");
        const string key = "Settings_CardDirectionHelp";

        Assert.IsTrue(english.ContainsKey(key), $"English resource is missing '{key}'.");
        Assert.IsTrue(german.ContainsKey(key), $"German resource is missing '{key}'.");
        Assert.IsTrue(russian.ContainsKey(key), $"Russian resource is missing '{key}'.");

        Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), $"English value for '{key}' is empty or whitespace.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(german[key]), $"German value for '{key}' is empty or whitespace.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(russian[key]), $"Russian value for '{key}' is empty or whitespace.");
    }

    [TestMethod]
    public void AllRazorComponents_LiteralLocalizerKeysExistInAllResources()
    {
        var repoRoot = FindRepositoryRoot();
        var componentsDir = Path.Combine(repoRoot, "Components");
        var razorFiles = Directory.GetFiles(componentsDir, "*.razor", SearchOption.AllDirectories);

        Assert.IsTrue(razorFiles.Length > 0, "No Razor components found in repository.");

        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        var failures = new List<string>();

        foreach (var razorFile in razorFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(repoRoot, razorFile);
            var content = File.ReadAllText(razorFile);

            var matches = System.Text.RegularExpressions.Regex.Matches(
                content,
                @"Localizer\[\s*""([A-Za-z0-9_]+)""(?:\s*,|\s*\])");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var key = match.Groups[1].Value;

                if (!english.ContainsKey(key))
                {
                    failures.Add($"{relativePath}: missing English resource key '{key}'");
                }
                if (!german.ContainsKey(key))
                {
                    failures.Add($"{relativePath}: missing German resource key '{key}'");
                }
                if (!russian.ContainsKey(key))
                {
                    failures.Add($"{relativePath}: missing Russian resource key '{key}'");
                }
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            $"Discovered {failures.Count} missing resource key reference(s) in Razor components:\n" +
            string.Join("\n", failures.Distinct(StringComparer.Ordinal)));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the KnownFirst repository root.");
    }
}
