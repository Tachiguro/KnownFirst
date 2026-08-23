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
    private const string Beta11 = "1.0.0-beta.11";
    private const string Beta12 = "1.0.0-beta.12";
    private const string Beta13 = "1.0.0-beta.13";

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
    public void Catalog_Beta10RemainsAvailableAndUnchangedAfterBeta11Added()
    {
        var entry = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta10);

        Assert.AreEqual("WhatsNew_Title", entry.TitleResourceKey);
        CollectionAssert.AreEqual(
            new[]
            {
                "WhatsNew_Beta10_Bullet1",
                "WhatsNew_Beta10_Bullet2",
                "WhatsNew_Beta10_Bullet3",
                "WhatsNew_Beta10_Bullet4"
            },
            entry.BulletResourceKeys.ToArray());
    }

    [TestMethod]
    public void Catalog_Beta11HasReleaseNoteEntry()
    {
        var entry = ReleaseNotesService.DefaultCatalog.SingleOrDefault(
            candidate => candidate.Version == Beta11);

        Assert.IsNotNull(entry);
        Assert.IsFalse(string.IsNullOrWhiteSpace(entry.TitleResourceKey));
        Assert.IsGreaterThan(0, entry.BulletResourceKeys.Count);
    }

    [TestMethod]
    public void Catalog_Beta11RemainsAvailableAndUnchangedAfterBeta12Added()
    {
        var entry = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta11);

        Assert.AreEqual("WhatsNew_Title", entry.TitleResourceKey);
        CollectionAssert.AreEqual(
            new[]
            {
                "WhatsNew_Beta11_Bullet1",
                "WhatsNew_Beta11_Bullet2",
                "WhatsNew_Beta11_Bullet3",
                "WhatsNew_Beta11_Bullet4",
                "WhatsNew_Beta11_Bullet5",
                "WhatsNew_Beta11_Bullet6"
            },
            entry.BulletResourceKeys.ToArray());
    }

    [TestMethod]
    public void Catalog_Beta12HasReleaseNoteEntry()
    {
        var entry = ReleaseNotesService.DefaultCatalog.SingleOrDefault(
            candidate => candidate.Version == Beta12);

        Assert.IsNotNull(entry);
        Assert.IsFalse(string.IsNullOrWhiteSpace(entry.TitleResourceKey));
        Assert.IsGreaterThan(0, entry.BulletResourceKeys.Count);
    }

    [TestMethod]
    public void Catalog_Beta13HasReleaseNoteEntry()
    {
        var entry = ReleaseNotesService.DefaultCatalog.SingleOrDefault(
            candidate => candidate.Version == Beta13);

        Assert.IsNotNull(entry);
        Assert.IsFalse(string.IsNullOrWhiteSpace(entry.TitleResourceKey));
        Assert.IsGreaterThan(0, entry.BulletResourceKeys.Count);
    }

    // --- Active release identity (Beta 13) ---------------------------------------------

    [TestMethod]
    public void ActiveProductIdentity_IsBeta13Build14()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "KnownFirst.csproj"));

        Assert.Contains("<KnownFirstProductVersion>1.0.0-beta.13</KnownFirstProductVersion>", project);
        Assert.Contains("<KnownFirstBuildNumber>14</KnownFirstBuildNumber>", project);
    }

    [TestMethod]
    public void WhatsNewBeta13Entry_IsActiveForTheCurrentProductVersion()
    {
        var activeVersion = ReadActiveProductVersion();
        Assert.AreEqual(
            Beta13,
            activeVersion,
            "The Beta 13 catalog entry is only meaningful while Beta 13 is the active product version.");

        var entry = ReleaseNotesService.DefaultCatalog.SingleOrDefault(
            candidate => candidate.Version == activeVersion);
        Assert.IsNotNull(entry, "The active product version must have a matching What's New catalog entry.");
    }

    [TestMethod]
    public void WhatsNewActiveEntry_DisplaysOnceThenStaysDismissed()
    {
        var activeVersion = ReadActiveProductVersion();
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(activeVersion, store);

        var firstEvaluation = service.GetUnseenReleaseNotes();
        Assert.IsNotNull(firstEvaluation, "The active version's notice must display when unseen.");
        Assert.AreEqual(activeVersion, firstEvaluation.Version);

        service.MarkSeen(firstEvaluation.Version);

        Assert.IsNull(
            service.GetUnseenReleaseNotes(),
            "The active version's notice must stay dismissed once marked seen.");
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

    [TestMethod]
    public void Resources_WhatsNewBeta11KeysAreCompleteAndParallelAcrossAllLocales()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var russian = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));

        var beta10 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta10);
        var beta11 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta11);

        Assert.AreEqual(
            Beta10,
            beta10.Version,
            "The Beta 10 entry's version identifier must remain unchanged.");
        Assert.AreEqual(
            Beta11,
            beta11.Version,
            "The Beta 11 entry's version identifier must be the new product version.");
        Assert.AreEqual(
            beta10.TitleResourceKey,
            beta11.TitleResourceKey,
            "All release-note entries share the same generic title resource key.");

        foreach (var bulletKey in beta11.BulletResourceKeys)
        {
            Assert.Contains(bulletKey, english.Keys);
            Assert.Contains(bulletKey, german.Keys);
            Assert.Contains(bulletKey, russian.Keys);
            Assert.IsFalse(string.IsNullOrWhiteSpace(english[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[bulletKey]));
        }
    }

    [TestMethod]
    public void WhatsNewBeta11Content_CommunicatesRequiredFactsInEnglishGermanAndRussian()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var russian = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));
        var beta11 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta11);

        var englishText = string.Join(" ", beta11.BulletResourceKeys.Select(key => english[key]));
        var germanText = string.Join(" ", beta11.BulletResourceKeys.Select(key => german[key]));
        var russianText = string.Join(" ", beta11.BulletResourceKeys.Select(key => russian[key]));

        foreach (var fact in new[] { "Russian", "System language", "translation target", "direction", "Repeat", "not yet supported" })
        {
            StringAssert.Contains(englishText, fact);
        }

        foreach (var fact in new[] { "russische", "System", "Übersetzungsziel", "Richtung", "Wiederholung", "noch nicht unterstützt" })
        {
            StringAssert.Contains(germanText, fact);
        }

        foreach (var fact in new[] { "русский", "Система", "перевода", "направление", "Повторение", "не поддерживается" })
        {
            StringAssert.Contains(russianText, fact);
        }
    }

    [TestMethod]
    public void Resources_WhatsNewBeta12KeysAreCompleteAndParallelAcrossAllLocales()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var russian = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));

        var beta11 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta11);
        var beta12 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta12);

        Assert.AreEqual(
            Beta11,
            beta11.Version,
            "The Beta 11 entry's version identifier must remain unchanged.");
        Assert.AreEqual(
            Beta12,
            beta12.Version,
            "The Beta 12 entry's version identifier must be the new product version.");
        Assert.AreEqual(
            beta11.TitleResourceKey,
            beta12.TitleResourceKey,
            "All release-note entries share the same generic title resource key.");

        foreach (var bulletKey in beta12.BulletResourceKeys)
        {
            Assert.Contains(bulletKey, english.Keys);
            Assert.Contains(bulletKey, german.Keys);
            Assert.Contains(bulletKey, russian.Keys);
            Assert.IsFalse(string.IsNullOrWhiteSpace(english[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[bulletKey]));
        }
    }

    [TestMethod]
    public void Resources_WhatsNewBeta13KeysAreCompleteAndParallelAcrossAllLocales()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var russian = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));

        var beta12 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta12);
        var beta13 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta13);

        Assert.AreEqual(
            Beta12,
            beta12.Version,
            "The Beta 12 entry's version identifier must remain unchanged.");
        Assert.AreEqual(
            Beta13,
            beta13.Version,
            "The Beta 13 entry's version identifier must be the new product version.");
        Assert.AreEqual(
            beta12.TitleResourceKey,
            beta13.TitleResourceKey,
            "All release-note entries share the same generic title resource key.");

        foreach (var bulletKey in beta13.BulletResourceKeys)
        {
            Assert.Contains(bulletKey, english.Keys);
            Assert.Contains(bulletKey, german.Keys);
            Assert.Contains(bulletKey, russian.Keys);
            Assert.IsFalse(string.IsNullOrWhiteSpace(english[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(german[bulletKey]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(russian[bulletKey]));
        }
    }

    [TestMethod]
    public void WhatsNewBeta12Content_CommunicatesRequiredFactsInEnglishGermanAndRussian()
    {
        var root = FindRepositoryRoot();
        var english = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var russian = LoadResources(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));
        var beta12 = ReleaseNotesService.DefaultCatalog.Single(candidate => candidate.Version == Beta12);

        var englishText = string.Join(" ", beta12.BulletResourceKeys.Select(key => english[key]));
        var germanText = string.Join(" ", beta12.BulletResourceKeys.Select(key => german[key]));
        var russianText = string.Join(" ", beta12.BulletResourceKeys.Select(key => russian[key]));

        foreach (var fact in new[] { "Russian translation targets", "correctly", "Definition or Translation", "not supported" })
        {
            StringAssert.Contains(englishText, fact);
        }

        foreach (var fact in new[] { "Russisch als Übersetzungsziel", "korrekt", "Definition und Übersetzung", "nicht unterstützt" })
        {
            StringAssert.Contains(germanText, fact);
        }

        foreach (var fact in new[] { "Русский как язык перевода", "корректно", "Определение или Перевод", "не поддерживается" })
        {
            StringAssert.Contains(russianText, fact);
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

    [TestMethod]
    public void GetUnseenReleaseNotes_FirstEvaluationOfBeta11_ReturnsEntry()
    {
        var service = CreateService(Beta11, new FakeWhatsNewPreferenceStore());

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(Beta11, entry.Version);
    }

    [TestMethod]
    public void MarkSeen_ForBeta11_RecordsExactVersionString()
    {
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(Beta11, store);

        service.MarkSeen(Beta11);

        Assert.AreEqual(Beta11, store.GetSeenVersion());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_AfterMarkSeenBeta11_SecondEvaluationReturnsNull()
    {
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(Beta11, store);

        var firstEntry = service.GetUnseenReleaseNotes();
        Assert.IsNotNull(firstEntry);
        service.MarkSeen(firstEntry.Version);

        Assert.IsNull(service.GetUnseenReleaseNotes());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_Beta10SeenDoesNotSuppressBeta11()
    {
        var store = new FakeWhatsNewPreferenceStore();
        store.SetSeenVersion(Beta10);
        var service = CreateService(Beta11, store);

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(Beta11, entry.Version);
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_FirstEvaluationOfBeta12_ReturnsEntry()
    {
        var service = CreateService(Beta12, new FakeWhatsNewPreferenceStore());

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(Beta12, entry.Version);
    }

    [TestMethod]
    public void MarkSeen_ForBeta12_RecordsExactVersionString()
    {
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(Beta12, store);

        service.MarkSeen(Beta12);

        Assert.AreEqual(Beta12, store.GetSeenVersion());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_AfterMarkSeenBeta12_SecondEvaluationReturnsNull()
    {
        var store = new FakeWhatsNewPreferenceStore();
        var service = CreateService(Beta12, store);

        var firstEntry = service.GetUnseenReleaseNotes();
        Assert.IsNotNull(firstEntry);
        service.MarkSeen(firstEntry.Version);

        Assert.IsNull(service.GetUnseenReleaseNotes());
    }

    [TestMethod]
    public void GetUnseenReleaseNotes_Beta11SeenDoesNotSuppressBeta12()
    {
        var store = new FakeWhatsNewPreferenceStore();
        store.SetSeenVersion(Beta11);
        var service = CreateService(Beta12, store);

        var entry = service.GetUnseenReleaseNotes();

        Assert.IsNotNull(entry);
        Assert.AreEqual(Beta12, entry.Version);
    }

    // --- Reopenable release-note history (Milestone 14B) ---------------------------------

    [TestMethod]
    public void ReleaseNoteHistory_ApiExposesEveryCatalogEntryNewestFirst()
    {
        var service = CreateService(Beta12, new FakeWhatsNewPreferenceStore());

        var historyMethod = typeof(IReleaseNotesService).GetMethod("GetReleaseNoteHistory");
        Assert.IsNotNull(
            historyMethod,
            "IReleaseNotesService must expose GetReleaseNoteHistory() for the Milestone 14B reopenable release-note history.");

        var history = historyMethod.Invoke(service, null) as IEnumerable<ReleaseNoteEntry>;
        Assert.IsNotNull(history, "GetReleaseNoteHistory() must return the release-note entries.");

        CollectionAssert.AreEqual(
            new[] { Beta13, Beta12, Beta11, Beta10 },
            history.Select(entry => entry.Version).ToArray(),
            "Release-note history must contain every catalog entry, newest first.");
    }

    [TestMethod]
    public void ReleaseNoteHistory_IsIndependentOfSeenVersionAndLeavesItUnchanged()
    {
        var store = new FakeWhatsNewPreferenceStore();
        store.SetSeenVersion(Beta12);
        var service = CreateService(Beta12, store);

        var historyMethod = typeof(IReleaseNotesService).GetMethod("GetReleaseNoteHistory");
        Assert.IsNotNull(
            historyMethod,
            "IReleaseNotesService must expose GetReleaseNoteHistory() for the Milestone 14B reopenable release-note history.");

        var history = historyMethod.Invoke(service, null) as IEnumerable<ReleaseNoteEntry>;
        Assert.IsNotNull(history, "GetReleaseNoteHistory() must return the release-note entries.");

        CollectionAssert.AreEqual(
            new[] { Beta13, Beta12, Beta11, Beta10 },
            history.Select(entry => entry.Version).ToArray(),
            "A seen active version must not remove any entry from the reopenable history.");
        Assert.AreEqual(
            Beta12,
            store.GetSeenVersion(),
            "Reading the release-note history must not mutate the seen-version preference.");
        Assert.IsNull(
            service.GetUnseenReleaseNotes(),
            "Reading the release-note history must not resurrect the automatic one-time notice.");
    }

    [TestMethod]
    public void ReleaseNotesPage_ExistsWithHistoryRouteAndConsumesHistoryNotUnseenNotes()
    {
        var pagePath = Path.Combine(FindRepositoryRoot(), "Components", "Pages", "ReleaseNotes.razor");
        Assert.IsTrue(
            File.Exists(pagePath),
            "Components/Pages/ReleaseNotes.razor must exist for the Milestone 14B reopenable release-note history page.");

        var markup = File.ReadAllText(pagePath);

        Assert.Contains("@page \"/release-notes\"", markup);
        Assert.Contains("GetReleaseNoteHistory", markup);
        Assert.Contains("WhatsNew_VersionLabel", markup);
        Assert.Contains("BulletResourceKeys", markup);
        Assert.Contains("<ul", markup);

        Assert.DoesNotContain("GetUnseenReleaseNotes", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkSeen", markup, StringComparison.Ordinal);
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
        Assert.Contains("Preferences.Clear()", settingsSource);
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

    private static string ReadActiveProductVersion()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "KnownFirst.csproj"));
        var match = System.Text.RegularExpressions.Regex.Match(
            project, "<KnownFirstProductVersion>([^<]+)</KnownFirstProductVersion>");
        Assert.IsTrue(match.Success, "KnownFirstProductVersion must be declared in KnownFirst.csproj.");
        return match.Groups[1].Value;
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
