using System.Xml.Linq;
using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnownFirst.Tests;

[TestClass]
public sealed class ReleaseNotesTests
{
    private const string Beta9 = "1.0.0-beta.9";
    private const string Beta10 = "1.0.0-beta.10";

    // --- Catalog contract -------------------------------------------------------------

    [TestMethod]
    public void Catalog_Beta10HasReleaseNoteEntry()
    {
        var entry = ReleaseNotesService.DefaultCatalog.SingleOrDefault(
            candidate => candidate.Version == Beta10);

        Assert.IsNotNull(entry);
        Assert.IsFalse(string.IsNullOrWhiteSpace(entry.TitleResourceKey));
        Assert.IsGreaterThan(0, entry.BulletResourceKeys.Count);
    }

    [TestMethod]
    public void Resources_WhatsNewKeysAreCompleteAndParallelInEnglishAndGerman()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));

        var englishKeys = english.Keys.Where(key => key.StartsWith("WhatsNew_", StringComparison.Ordinal)).ToHashSet();
        var germanKeys = german.Keys.Where(key => key.StartsWith("WhatsNew_", StringComparison.Ordinal)).ToHashSet();

        CollectionAssert.AreEquivalent(englishKeys.ToArray(), germanKeys.ToArray());
        Assert.Contains("WhatsNew_Title", englishKeys);
        Assert.Contains("WhatsNew_VersionLabel", englishKeys);
        Assert.Contains("WhatsNew_Close", englishKeys);

        var beta10 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta10);
        Assert.Contains(beta10.TitleResourceKey, englishKeys);
        foreach (var bulletKey in beta10.BulletResourceKeys)
        {
            Assert.Contains(bulletKey, englishKeys);
            Assert.Contains(bulletKey, germanKeys);
            Assert.IsFalse(string.IsNullOrWhiteSpace(english[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[bulletKey]));
        }
    }

    [TestMethod]
    public void WhatsNewBeta10Content_CommunicatesRequiredFactsInEnglishAndGerman()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var beta10 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta10);

        var englishText = string.Join(" ", beta10.BulletResourceKeys.Select(key => english[key]));
        var germanText = string.Join(" ", beta10.BulletResourceKeys.Select(key => german[key]));

        foreach (var fact in new[] { ".kfarchive", "empty", "Save dialog", "Open dialog", "Windows and Android", "never merged", "learning history", "not encrypted" })
        {
            StringAssert.Contains(englishText, fact);
        }

        foreach (var fact in new[] { ".kfarchive", "leere", "Speichern-Dialog", "Öffnen-Dialog", "Windows und Android", "niemals zusammengeführt", "Lernverlauf", "nicht verschlüsselt" })
        {
            StringAssert.Contains(germanText, fact);
        }
    }

    // --- Display/seen behavior ---------------------------------------------------------

    [TestMethod]
    public void GetUnseenReleaseNotes_ForBeta9_ReturnsNull()
    {
        var service = CreateService(Beta9, new FakeWhatsNewPreferenceStore());

        Assert.IsNull(service.GetUnseenReleaseNotes());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_FirstEvaluationOfBeta10_ReturnsEntry()
    {
        var service = CreateService(Beta10, new FakeWhatsNewPreferenceStore());

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(Beta10, entry.Version);
    }

    [TestMethod]
    public void MarkSeen_ForBeta10_RecordsExactVersionString()
    {
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(Beta10, store);

        service.MarkSeen(Beta10);

        Assert.AreEqual(Beta10, store.GetSeenVersion());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_AfterMarkSeen_SecondEvaluationReturnsNull()
    {
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(Beta10, store);

        var firstEntry = service.GetUnseenReleaseNotes();
        Assert.IsNotNull(firstEntry);
        service.MarkSeen(firstEntry.Version);

        Assert.IsNull(service.GetUnseenReleaseNotes());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_LaterVersionWithOwnEntry_DisplaysIndependently()
    {
        const string beta11 = "1.0.0-beta.11";
        var catalog = new[]
        {
            new ReleaseNoteEntry(Beta10, "WhatsNew_Title", ["WhatsNew_Beta10_Bullet1"]),
            new ReleaseNoteEntry(beta11, "WhatsNew_Title", ["WhatsNew_Beta10_Bullet1"])
        };
        var store = new FakeWhatsNewPreferenceStore();
        store.SetSeenVersion(Beta10);
        var service = CreateService(beta11, store, catalog);

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(beta11, entry.Version);
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_VersionWithoutEntry_ReturnsNullNotEmptyModal()
    {
        var service = CreateService("9.9.9-no-notes", new FakeWhatsNewPreferenceStore());

        Assert.IsNull(service.GetUnseenReleaseNotes());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_PreviouslySeenOlderVersion_DoesNotSuppressNewerVersion()
    {
        var store = new FakeWhatsNewPreferenceStore();
        store.SetSeenVersion(Beta9);
        var service = CreateService(Beta10, store);

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(Beta10, entry.Version);
    }

    // --- Fault tolerance -----------------------------------------------------------------

    [TestMethod]
    public void GetUnseenReleaseNotes_WhenPreferenceReadFails_ReturnsNullWithoutThrowing()
    {
        var store = new FakeWhatsNewPreferenceStore
        {
            GetSeenVersionOverride = () => throw new IOException("Simulated preference read failure.")
        };
        var service = CreateService(Beta10, store);

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNull(entry);
    }

    [TestMethod]
    public void MarkSeen_WhenPreferenceWriteFails_DoesNotThrow()
    {
        var store = new FakeWhatsNewPreferenceStore
        {
            SetSeenVersionOverride = _ => throw new IOException("Simulated preference write failure.")
        };
        var service = CreateService(Beta10, store);

        service.MarkSeen(Beta10);
    }

    // --- Archive isolation ---------------------------------------------------------------

    [TestMethod]
    public void ArchivePipeline_NeverReferencesWhatsNewPreferenceStorage()
    {
        var root = FindRepositoryRoot();
        var snapshotSource = File.ReadAllText(Path.Combine(root, "Data", "BackupSnapshotRepository.cs"));
        var importSource = File.ReadAllText(Path.Combine(root, "Data", "BackupImportRepository.cs"));
        var backupServiceSource = File.ReadAllText(Path.Combine(root, "Services", "DataSafety", "BackupService.cs"));

        foreach (var source in new[] { snapshotSource, importSource, backupServiceSource })
        {
            Assert.DoesNotContain("Preferences", source);
            Assert.DoesNotContain("ReleaseNotes", source);
            Assert.DoesNotContain("whats_new_seen_version", source);
        }
    }

    [TestMethod]
    public void SettingsImportWorkflow_NeverMarksReleaseNotesSeen_AndFullResetClearsPreferences()
    {
        var root = FindRepositoryRoot();
        var settingsSource = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Settings.razor"));

        Assert.DoesNotContain("MarkSeen", settingsSource);
        Assert.Contains("Preferences.Default.Clear()", settingsSource);
    }

    // --- Modal accessibility contract -----------------------------------------------------

    [TestMethod]
    public void WhatsNewModal_HasAccessibleDialogSemanticsTitleVersionCloseEscapeAndFocus()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Components", "Shared", "WhatsNewModal.razor"));

        Assert.Contains("role=\"dialog\"", markup);
        Assert.Contains("aria-modal=\"true\"", markup);
        Assert.Contains("_entry.TitleResourceKey", markup);
        Assert.Contains("BuildIdentityService.Identity.Version", markup);
        Assert.Contains("WhatsNew_Close", markup);
        Assert.Contains("\"Escape\"", markup);
        Assert.Contains("_closeButton.FocusAsync", markup);
    }

    [TestMethod]
    public void MainLayout_MountsWhatsNewModalOnce()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));

        var occurrences = System.Text.RegularExpressions.Regex.Matches(markup, "<WhatsNewModal\\s*/>").Count;
        Assert.AreEqual(1, occurrences);
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static ReleaseNotesService CreateService(
        string currentVersion,
        IWhatsNewPreferenceStore store,
        IReadOnlyList<ReleaseNoteEntry>? catalog = null)
    {
        var buildIdentity = new FakeBuildIdentityService(currentVersion);
        var logger = NullLogger<ReleaseNotesService>.Instance;
        return catalog is null
            ? new ReleaseNotesService(buildIdentity, store, logger)
            : new ReleaseNotesService(buildIdentity, store, logger, catalog);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KnownFirst.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("KnownFirst repository root was not found.");
    }

    private static Dictionary<string, string> LoadResources(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);

    private sealed class FakeBuildIdentityService(string version) : IBuildIdentityService
    {
        public BuildIdentity Identity { get; } = new(
            Product: "KnownFirst",
            Version: version,
            BuildNumber: "9",
            PackageId: "com.tachiguro.knownfirst",
            Configuration: "Debug",
            CommitHash: "unknown",
            ShortCommitHash: "unknown",
            Branch: "unknown",
            OS: "test",
            OSVersion: "test",
            Device: "test",
            Runtime: "test",
            SessionId: "test",
            IsDirty: false);

        public string FormatHeader() => string.Empty;

        public string GetFormattedBuildIdentity() => string.Empty;
    }

    private sealed class FakeWhatsNewPreferenceStore : IWhatsNewPreferenceStore
    {
        private string _seenVersion = string.Empty;

        public Func<string>? GetSeenVersionOverride { get; set; }

        public Action<string>? SetSeenVersionOverride { get; set; }

        public string GetSeenVersion() => GetSeenVersionOverride is not null
            ? GetSeenVersionOverride()
            : _seenVersion;

        public void SetSeenVersion(string version)
        {
            if (SetSeenVersionOverride is not null)
            {
                SetSeenVersionOverride(version);
                return;
            }

            _seenVersion = version;
        }
    }
}
