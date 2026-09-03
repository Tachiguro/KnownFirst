using System.Text.RegularExpressions;
using System.Xml.Linq;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Models;
using KnownFirst.Services;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PrepareWordsConsentIntegrationTests
{
    private static string LoadPrepareWordsMarkup()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Components", "Pages", "PrepareWords.razor"));
        if (File.Exists(projectPath))
        {
            return File.ReadAllText(projectPath);
        }

        var outputPath = Path.Combine(AppContext.BaseDirectory, "Ui", "PrepareWords.razor");
        return File.ReadAllText(outputPath);
    }

    private static string LoadSettingsMarkup()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Components", "Pages", "Settings.razor"));
        if (File.Exists(projectPath))
        {
            return File.ReadAllText(projectPath);
        }

        var outputPath = Path.Combine(AppContext.BaseDirectory, "Ui", "Settings.razor");
        return File.ReadAllText(outputPath);
    }

    private static Dictionary<string, string> LoadResources(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "Localization", fileName);
            if (File.Exists(candidate))
            {
                var doc = XDocument.Load(candidate);
                return doc.Root!.Elements("data")
                    .ToDictionary(
                        e => e.Attribute("name")!.Value,
                        e => e.Element("value")?.Value ?? string.Empty);
            }
        }

        throw new FileNotFoundException($"Could not locate resource file {fileName}.");
    }

    [TestMethod]
    public void ScopeA_ContextualConsentGrant_IsCompletelyRemovedFromPrepareWords_AndPreservedInSettings()
    {
        var markup = LoadPrepareWordsMarkup();
        var settingsMarkup = LoadSettingsMarkup();

        // PrepareWords must never call GrantOnlineLookupConsent
        Assert.IsFalse(
            markup.Contains("GrantOnlineLookupConsent", StringComparison.Ordinal),
            "PrepareWords.razor must not invoke GrantOnlineLookupConsent.");

        // Contextual confirm action must be removed
        Assert.IsFalse(
            markup.Contains("ConfirmOnlineLookupAsync", StringComparison.Ordinal),
            "ConfirmOnlineLookupAsync must be removed from PrepareWords.razor.");

        // Privacy disclosure branch must be removed
        Assert.IsFalse(
            markup.Contains("_showPrivacyDisclosure", StringComparison.Ordinal),
            "_showPrivacyDisclosure must be removed from PrepareWords.razor.");
        Assert.IsFalse(
            markup.Contains("CancelDisclosure", StringComparison.Ordinal),
            "CancelDisclosure must be removed from PrepareWords.razor.");
        Assert.IsFalse(
            markup.Contains("Prepare_StartOnlineLookup", StringComparison.Ordinal),
            "Prepare_StartOnlineLookup must not be used in PrepareWords.razor.");

        // Settings retains the authoritative post-onboarding consent management
        Assert.IsTrue(
            settingsMarkup.Contains("AppSettings.GrantOnlineLookupConsent()", StringComparison.Ordinal),
            "Settings.razor must retain AppSettings.GrantOnlineLookupConsent().");
        Assert.IsTrue(
            settingsMarkup.Contains("Settings_ActivateOnlineConsent", StringComparison.Ordinal),
            "Settings.razor must retain Settings_ActivateOnlineConsent.");
        Assert.IsTrue(
            settingsMarkup.Contains("Settings_RevokeOnlineConsent", StringComparison.Ordinal),
            "Settings.razor must retain Settings_RevokeOnlineConsent.");
        Assert.IsTrue(
            settingsMarkup.Contains("Prepare_OnlineDisclosure", StringComparison.Ordinal),
            "Settings.razor must retain disclosure copy (Prepare_OnlineDisclosure).");
    }

    [TestMethod]
    public void ScopeB_MethodSelector_WhenConsentDisabled_AutomaticCardIsDisabledAndExplanatorySettingsLinkIsPresent()
    {
        var markup = LoadPrepareWordsMarkup();

        // Automatic Online remains present for discoverability
        Assert.IsTrue(
            markup.Contains("Prepare_AutomaticOnline", StringComparison.Ordinal),
            "PrepareWords.razor must visibly present Automatic Online for discoverability.");

        // Automatic Online card is genuinely disabled when HasOnlineLookupConsent is false
        Assert.IsTrue(
            markup.Contains("!AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "Automatic Online button must be disabled when HasOnlineLookupConsent is false.");

        // Manual option remains independently enabled and actionable (does not depend on HasOnlineLookupConsent)
        var manualButtonIndex = markup.IndexOf("StartManualAsync", StringComparison.Ordinal);
        Assert.IsTrue(manualButtonIndex >= 0, "Manual option must be present.");

        // Concise localized explanatory copy is present
        Assert.IsTrue(
            markup.Contains("Prepare_OnlineLookupDisabledMethodNotice", StringComparison.Ordinal),
            "PrepareWords.razor must display concise explanatory text when online lookup is disabled in method selector.");

        // Explicit localized Settings action is present and targets the online lookup section
        Assert.IsTrue(
            markup.Contains("href=\"settings#online-lookup-title\"", StringComparison.Ordinal),
            "PrepareWords.razor must provide a Settings action targeting 'settings#online-lookup-title'.");
        Assert.IsTrue(
            markup.Contains("Prepare_OpenSettings", StringComparison.Ordinal),
            "PrepareWords.razor must use Prepare_OpenSettings for the Settings action.");
    }

    [TestMethod]
    public void PrepareWords_OnlineLookupDisabled_OpenSettingsTargetsOnlineLookupSection()
    {
        var markup = LoadPrepareWordsMarkup();

        // Exactly two occurrences of the deep link to settings#online-lookup-title must be present
        var deepLinkMatches = Regex.Matches(markup, @"href=""settings#online-lookup-title""");
        Assert.AreEqual(2, deepLinkMatches.Count, "PrepareWords.razor must contain exactly two deep links to 'settings#online-lookup-title'.");

        // Obsolete plain href="settings" must not be used for Prepare_OpenSettings actions
        Assert.IsFalse(
            Regex.IsMatch(markup, @"href=""settings""[^>]*>\s*@Localizer\[""Prepare_OpenSettings""\]"),
            "PrepareWords.razor must not contain plain href=\"settings\" links for Prepare_OpenSettings actions.");

        // Both links must use the Prepare_OpenSettings localization key
        var openSettingsMatches = Regex.Matches(markup, @"@Localizer\[""Prepare_OpenSettings""\]");
        Assert.AreEqual(2, openSettingsMatches.Count, "Both Settings links must use the Prepare_OpenSettings localization key.");

        // StartAutomaticAsync defends against absent consent
        var startAutoIndex = markup.IndexOf("private async Task StartAutomaticAsync()", StringComparison.Ordinal);
        Assert.IsTrue(startAutoIndex >= 0);
        var startAutoEnd = markup.IndexOf("private", startAutoIndex + 1, StringComparison.Ordinal);
        var startAutoBody = markup[startAutoIndex..(startAutoEnd > startAutoIndex ? startAutoEnd : markup.Length)];
        Assert.IsTrue(
            startAutoBody.Contains("!AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal)
            && startAutoBody.Contains("return;", StringComparison.Ordinal),
            "StartAutomaticAsync must return immediately if HasOnlineLookupConsent is false.");

        // StartAsync also defends against absent consent
        var startAsyncIndex = markup.IndexOf("private async Task StartAsync(PreparationMethod method)", StringComparison.Ordinal);
        Assert.IsTrue(startAsyncIndex >= 0);
        var startAsyncEnd = markup.IndexOf("private", startAsyncIndex + 1, StringComparison.Ordinal);
        var startAsyncBody = markup[startAsyncIndex..(startAsyncEnd > startAsyncIndex ? startAsyncEnd : markup.Length)];
        Assert.IsTrue(
            startAsyncBody.Contains("!AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "StartAsync must guard against starting AutomaticOnline when HasOnlineLookupConsent is false.");
    }

    [TestMethod]
    public void ScopeC_MethodSelector_WhenConsentEnabled_AutomaticCanBeStartedWithoutDisclosure()
    {
        var markup = LoadPrepareWordsMarkup();

        // Automatic Online calls StartAutomaticAsync which does not show disclosure
        Assert.IsFalse(
            markup.Contains("_showPrivacyDisclosure", StringComparison.Ordinal),
            "PrepareWords must not have a privacy disclosure branch.");
        Assert.IsFalse(
            markup.Contains("ConfirmOnlineLookupAsync", StringComparison.Ordinal),
            "PrepareWords must not have ConfirmOnlineLookupAsync.");
    }

    [TestMethod]
    public void ScopeD_Resume_PendingCandidate_DoesNotInvokeLookup_AndRendersBlockedState_WhileUsableResultRemainsRenderable()
    {
        var markup = LoadPrepareWordsMarkup();

        // LoadAsync must not call LookupAsync when consent is false
        var loadAsyncIndex = markup.IndexOf("private async Task LoadAsync()", StringComparison.Ordinal);
        Assert.IsTrue(loadAsyncIndex >= 0);
        var loadAsyncEnd = markup.IndexOf("private async Task", loadAsyncIndex + 1, StringComparison.Ordinal);
        var loadAsyncBody = markup[loadAsyncIndex..(loadAsyncEnd > loadAsyncIndex ? loadAsyncEnd : markup.Length)];

        Assert.IsTrue(
            loadAsyncBody.Contains("AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "LoadAsync must guard LookupAsync invocation with AppSettings.HasOnlineLookupConsent.");

        // Blocked-online candidate state markup must exist
        Assert.IsTrue(
            markup.Contains("Prepare_OnlineLookupDisabledCandidateNotice", StringComparison.Ordinal),
            "PrepareWords must display Prepare_OnlineLookupDisabledCandidateNotice when automatic candidate is blocked.");
        Assert.IsTrue(
            markup.Contains("Prepare_ManualEntry", StringComparison.Ordinal),
            "Blocked-online state must offer Manual Entry action.");

        // ResultReady with usable data must remain renderable before the blocked state
        var usableResultIndex = markup.IndexOf("_item.Result?.HasUsableData == true", StringComparison.Ordinal);
        Assert.IsTrue(usableResultIndex >= 0, "Usable result check must be present.");
    }

    [TestMethod]
    public void ScopeE_Progression_MovingToNextCandidateWhileConsentDisabled_DoesNotInvokeLookup_AndPreservesBatch()
    {
        var markup = LoadPrepareWordsMarkup();

        var moveNextIndex = markup.IndexOf("private async Task MoveNextAsync(PreparationMethod method)", StringComparison.Ordinal);
        Assert.IsTrue(moveNextIndex >= 0);
        var moveNextEnd = markup.IndexOf("private", moveNextIndex + 1, StringComparison.Ordinal);
        var moveNextBody = markup[moveNextIndex..(moveNextEnd > moveNextIndex ? moveNextEnd : markup.Length)];

        Assert.IsTrue(
            moveNextBody.Contains("AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "MoveNextAsync must guard LookupAsync with AppSettings.HasOnlineLookupConsent.");

        // Existing local actions remain wired
        Assert.IsTrue(markup.Contains("ShowMarkKnownConfirmation", StringComparison.Ordinal));
        Assert.IsTrue(markup.Contains("ShowExcludeConfirmation", StringComparison.Ordinal));
        Assert.IsTrue(markup.Contains("SkipAsync", StringComparison.Ordinal));
        Assert.IsTrue(markup.Contains("RequestCancelPreparationAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ScopeF_Retry_IsUnavailableWhenConsentDisabled_AndDirectInvocationGuardsAgainstLookup()
    {
        var markup = LoadPrepareWordsMarkup();

        // CanRetryLookup must check HasOnlineLookupConsent
        var canRetryIndex = markup.IndexOf("private bool CanRetryLookup", StringComparison.Ordinal);
        Assert.IsTrue(canRetryIndex >= 0);
        var canRetryEnd = markup.IndexOf(';', canRetryIndex);
        var canRetryLine = markup[canRetryIndex..canRetryEnd];

        Assert.IsTrue(
            canRetryLine.Contains("AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "CanRetryLookup must evaluate AppSettings.HasOnlineLookupConsent.");

        // RetryAsync must check HasOnlineLookupConsent
        var retryIndex = markup.IndexOf("private async Task RetryAsync()", StringComparison.Ordinal);
        Assert.IsTrue(retryIndex >= 0);
        var retryEnd = markup.IndexOf("private", retryIndex + 1, StringComparison.Ordinal);
        var retryBody = markup[retryIndex..(retryEnd > retryIndex ? retryEnd : markup.Length)];

        Assert.IsTrue(
            retryBody.Contains("AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "RetryAsync must check AppSettings.HasOnlineLookupConsent.");

        // LookupAsync must check HasOnlineLookupConsent
        var lookupIndex = markup.IndexOf("private async Task LookupAsync()", StringComparison.Ordinal);
        Assert.IsTrue(lookupIndex >= 0);
        var lookupEnd = markup.IndexOf("private", lookupIndex + 1, StringComparison.Ordinal);
        var lookupBody = markup[lookupIndex..(lookupEnd > lookupIndex ? lookupEnd : markup.Length)];

        Assert.IsTrue(
            lookupBody.Contains("AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "LookupAsync must verify AppSettings.HasOnlineLookupConsent before initiating lookup.");
    }

    [TestMethod]
    public void ScopeG_ConsentChangeAndCancellation_SubscribesToEvent_CancelsInFlightLookup_AndUnsubscribesOnDisposal()
    {
        var markup = LoadPrepareWordsMarkup();

        // Component must subscribe to OnlineLookupConsentChanged
        Assert.IsTrue(
            markup.Contains("AppSettings.OnlineLookupConsentChanged +=", StringComparison.Ordinal),
            "PrepareWords must subscribe to AppSettings.OnlineLookupConsentChanged.");

        // Component must unsubscribe on disposal
        Assert.IsTrue(
            markup.Contains("AppSettings.OnlineLookupConsentChanged -=", StringComparison.Ordinal),
            "PrepareWords must unsubscribe from AppSettings.OnlineLookupConsentChanged on DisposeAsync.");

        // Catch blocks in LookupAsync must handle authorization-driven cancellation without CreateLookupFailure
        var lookupIndex = markup.IndexOf("private async Task LookupAsync()", StringComparison.Ordinal);
        Assert.IsTrue(lookupIndex >= 0);
        var lookupEnd = markup.IndexOf("private", lookupIndex + 1, StringComparison.Ordinal);
        var lookupBody = markup[lookupIndex..(lookupEnd > lookupIndex ? lookupEnd : markup.Length)];

        Assert.IsTrue(
            lookupBody.Contains("catch (InvalidOperationException", StringComparison.Ordinal)
            && lookupBody.Contains("!AppSettings.HasOnlineLookupConsent", StringComparison.Ordinal),
            "LookupAsync must handle InvalidOperationException when consent is revoked without treating it as provider failure.");
    }

    [TestMethod]
    public void ScopeH_Localization_NewKeysExistAcrossAllLanguages_WithPlaceholderParity()
    {
        var english = LoadResources("SharedResource.resx");
        var german = LoadResources("SharedResource.de.resx");
        var russian = LoadResources("SharedResource.ru.resx");

        string[] requiredKeys =
        [
            "Prepare_OnlineLookupDisabledMethodNotice",
            "Prepare_OnlineLookupDisabledCandidateNotice",
            "Prepare_OpenSettings",
            "Prepare_OnlineDisclosureTitle",
            "Prepare_OnlineDisclosure",
            "Settings_ActivateOnlineConsent",
            "Settings_RevokeOnlineConsent"
        ];

        foreach (var key in requiredKeys)
        {
            Assert.IsTrue(english.ContainsKey(key), $"English missing resource key: {key}");
            Assert.IsTrue(german.ContainsKey(key), $"German missing resource key: {key}");
            Assert.IsTrue(russian.ContainsKey(key), $"Russian missing resource key: {key}");

            var enValue = english[key];
            var deValue = german[key];
            var ruValue = russian[key];

            Assert.IsFalse(string.IsNullOrWhiteSpace(enValue), $"English empty for key: {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(deValue), $"German empty for key: {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ruValue), $"Russian empty for key: {key}");

            var enMatches = Regex.Matches(enValue, @"\{(\d+)\}");
            var deMatches = Regex.Matches(deValue, @"\{(\d+)\}");
            var ruMatches = Regex.Matches(ruValue, @"\{(\d+)\}");

            Assert.AreEqual(enMatches.Count, deMatches.Count, $"Placeholder count mismatch between EN and DE for key: {key}");
            Assert.AreEqual(enMatches.Count, ruMatches.Count, $"Placeholder count mismatch between EN and RU for key: {key}");
        }
    }
}
