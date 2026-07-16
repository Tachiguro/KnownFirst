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

        foreach (var key in RequiredMilestoneOneKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), $"The English resource key '{key}' is missing.");
            Assert.IsTrue(german.ContainsKey(key), $"The German resource key '{key}' is missing.");
        }
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
