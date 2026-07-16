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
        "Common_Close"
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
