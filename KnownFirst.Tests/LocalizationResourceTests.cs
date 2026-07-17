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
        "Settings_OnlineConsentRevoked",
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
        "Prepare_DefinitionRequired",
        "Prepare_SaveAndContinue",
        "Prepare_SeveralMeanings",
        "Prepare_Source",
        "Prepare_AcceptAndContinue",
        "Prepare_ChooseAnotherMeaning",
        "Prepare_Edit",
        "Prepare_CancelPreparation",
        "Prepare_SaveError",
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
        "Diagnostics_DiagnosticLogActionError"
    ];

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
    public void Resources_NoResourceValueIsEmpty()
    {
        var emptyEntries = new[] { "SharedResource.resx", "SharedResource.de.resx" }
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

        foreach (var key in RequiredMilestoneOneKeys.Concat(RequiredAutomaticDictionaryMvpKeys))
        {
            Assert.IsTrue(english.ContainsKey(key), $"The English resource key '{key}' is missing.");
            Assert.IsTrue(german.ContainsKey(key), $"The German resource key '{key}' is missing.");
        }
    }

    [TestMethod]
    public void Resources_OnlineDisclosureMatchesBindingSpecification()
    {
        const string expectedEnglish = "KnownFirst does not send your documents, example sentences, learning history, or personal data to the KnownFirst developer. Only the selected term and the selected language information are sent directly to Wikimedia for dictionary lookup. Wikimedia receives normal network information such as your IP address and the KnownFirst User-Agent. Retrieved dictionary content and your personal learning data are stored locally on this device.";
        const string expectedGerman = "KnownFirst sendet keine Dokumente, Beispielsätze, Lernhistorie oder persönlichen Daten an den Entwickler von KnownFirst. Für die Wörterbuchabfrage werden ausschließlich der ausgewählte Begriff und die gewählten Sprachinformationen direkt an Wikimedia übertragen. Wikimedia erhält dabei übliche Netzwerkdaten wie deine IP-Adresse und den KnownFirst-User-Agent. Abgerufene Wörterbuchinhalte und deine persönlichen Lerndaten werden lokal auf diesem Gerät gespeichert.";

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
}
