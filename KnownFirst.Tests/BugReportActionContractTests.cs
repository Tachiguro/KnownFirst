using System.Xml.Linq;

namespace KnownFirst.Tests;

[TestClass]
public sealed class BugReportActionContractTests
{
    [TestMethod]
    public void Settings_ExposesFunctionalReportBugActionControl()
    {
        var markup = ReadRepositoryFile("Components", "Pages", "Settings.razor");

        Assert.Contains("id=\"settings-report-bug-button\"", markup, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Settings_ReportBug\"]", markup, StringComparison.Ordinal);
        Assert.Contains("ReportBugAsync", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"bug-report-feedback-error\"", markup, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Settings_ReportBugError\"]", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_ExposesCopyAddressFallbackAndHandlesCopyState()
    {
        var markup = ReadRepositoryFile("Components", "Pages", "Settings.razor");

        Assert.Contains("id=\"settings-copy-bug-report-address-button\"", markup, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Settings_CopyBugReportAddress\"]", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"bug-report-address\"", markup, StringComparison.Ordinal);
        Assert.Contains("BugReportLauncher.RecipientEmail", markup, StringComparison.Ordinal);
        Assert.Contains("CopyBugReportAddressAsync", markup, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Settings_BugReportAddressCopied\"]", markup, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Settings_BugReportAddressCopyError\"]", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Settings_SupportKnownFirstAndComingSoonPlaceholdersRemainAbsent()
    {
        var markup = ReadRepositoryFile("Components", "Pages", "Settings.razor");

        Assert.DoesNotContain("Settings_SupportKnownFirst", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Common_FeatureComingSoon", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("FeaturePlaceholder", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void BugReportService_RecipientPreservesPlusCharacterAndMatchesExpectedAddress()
    {
        var serviceFile = Path.Combine(FindRepositoryRoot(), "Services", "Diagnostics", "BugReportLauncherService.cs");
        Assert.IsTrue(File.Exists(serviceFile), "BugReportLauncherService.cs must exist.");

        var source = File.ReadAllText(serviceFile);
        Assert.Contains("Tachiguro+KnownFirst_BugReport@gmail.com", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tachiguro KnownFirst_BugReport@gmail.com", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tachiguro%20KnownFirst_BugReport@gmail.com", source, StringComparison.Ordinal);

        var interfaceFile = Path.Combine(FindRepositoryRoot(), "Services", "Diagnostics", "IBugReportLauncherService.cs");
        var interfaceSource = File.ReadAllText(interfaceFile);
        Assert.Contains("RecipientEmail", interfaceSource, StringComparison.Ordinal);
    }

    [TestMethod]
    public void BugReportService_BodyContainsStructuredLocalizedPromptsAndTechnicalFooter()
    {
        var serviceFile = Path.Combine(FindRepositoryRoot(), "Services", "Diagnostics", "BugReportLauncherService.cs");
        var source = File.ReadAllText(serviceFile);

        Assert.Contains("BugReport_Subject", source, StringComparison.Ordinal);
        Assert.Contains("BugReport_PromptWhatHappened", source, StringComparison.Ordinal);
        Assert.Contains("BugReport_PromptWhatExpected", source, StringComparison.Ordinal);
        Assert.Contains("BugReport_PromptReproductionSteps", source, StringComparison.Ordinal);
        Assert.Contains("BugReport_PromptOptionalScreenshots", source, StringComparison.Ordinal);

        Assert.Contains("Version:", source, StringComparison.Ordinal);
        Assert.Contains("Build:", source, StringComparison.Ordinal);
        Assert.Contains("Configuration:", source, StringComparison.Ordinal);
        Assert.Contains("OS:", source, StringComparison.Ordinal);
        Assert.Contains("Device:", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void BugReportService_RegisteredInDependencyInjection()
    {
        var mauiProgram = ReadRepositoryFile("MauiProgram.cs");

        Assert.Contains("IBugReportLauncherService", mauiProgram, StringComparison.Ordinal);
        Assert.Contains("BugReportLauncherService", mauiProgram, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Localization_AllBugReportKeysArePresentInEnDeRu()
    {
        var root = FindRepositoryRoot();
        var enResx = ReadResxKeys(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var deResx = ReadResxKeys(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var ruResx = ReadResxKeys(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));

        string[] requiredKeys =
        [
            "Settings_ReportBug",
            "Settings_ReportBugError",
            "Settings_CopyBugReportAddress",
            "Settings_BugReportAddressCopied",
            "Settings_BugReportAddressCopyError",
            "BugReport_Subject",
            "BugReport_PromptWhatHappened",
            "BugReport_PromptWhatExpected",
            "BugReport_PromptReproductionSteps",
            "BugReport_PromptOptionalScreenshots"
        ];

        foreach (var key in requiredKeys)
        {
            Assert.IsTrue(enResx.ContainsKey(key), $"SharedResource.resx must contain {key}.");
            Assert.IsTrue(deResx.ContainsKey(key), $"SharedResource.de.resx must contain {key}.");
            Assert.IsTrue(ruResx.ContainsKey(key), $"SharedResource.ru.resx must contain {key}.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(enResx[key]), $"SharedResource.resx value for {key} must not be empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(deResx[key]), $"SharedResource.de.resx value for {key} must not be empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ruResx[key]), $"SharedResource.ru.resx value for {key} must not be empty.");
        }
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static Dictionary<string, string> ReadResxKeys(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Root!
            .Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => (string)e.Attribute("name")!,
                e => (string?)e.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "KnownFirst.slnx")) ||
                File.Exists(Path.Combine(directory, "KnownFirst.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
