using KnownFirst.Core.Language;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LanguageSelectionServiceTests
{
    [TestMethod]
    public void SetUiLanguage_PersistsPreferenceBeforeRefreshNotification()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "en", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("en-US", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        operations.Clear();

        service.UiLanguageChanged += (_, _) =>
        {
            Assert.AreEqual("de", store.SavedLanguage);
            operations.Add("notify");
        };

        service.SetUiLanguage("de");

        CollectionAssert.AreEqual(
            new[] { "persist:de", "culture:de", "notify" },
            operations);
    }

    [TestMethod]
    public void SetUiLanguage_NotifiesExactlyOnce()
    {
        var service = CreateInitializedService(true, "en", "de-DE", out _, out _, out _);
        var notificationCount = 0;
        service.UiLanguageChanged += (_, _) => notificationCount++;

        service.SetUiLanguage("de");

        Assert.AreEqual(1, notificationCount);
    }

    [TestMethod]
    public void SetUiLanguage_WhenAlreadyActive_DoesNotPersistApplyOrNotify()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "en", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("de-DE", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        operations.Clear();
        var notificationCount = 0;
        service.UiLanguageChanged += (_, _) => notificationCount++;

        service.SetUiLanguage(" EN ");

        Assert.AreEqual(0, notificationCount);
        Assert.AreEqual(0, store.SetCount);
        Assert.IsEmpty(operations);
    }

    [TestMethod]
    public void Initialize_WhenPreferenceExists_DoesNotOverwriteItWithDeviceCulture()
    {
        var service = CreateInitializedService(
            true,
            "de",
            "en-US",
            out var store,
            out var deviceCultureProvider,
            out var cultureContext);

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.AreEqual("de", store.SavedLanguage);
        Assert.AreEqual(0, store.SetCount);
        Assert.AreEqual(0, deviceCultureProvider.CallCount);
        Assert.AreEqual("de", cultureContext.CurrentUiLanguage);
    }

    [TestMethod]
    public void Initialize_WhenCalledAgain_KeepsStoredManualPreference()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "de", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("en-US", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        service.SetUiLanguage("en");
        deviceCultureProvider.DeviceCultureName = "de-DE";

        service.Initialize();

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.AreEqual("en", store.SavedLanguage);
        Assert.AreEqual(1, store.SetCount);
        Assert.AreEqual(0, deviceCultureProvider.CallCount);
    }

    [TestMethod]
    public void Initialize_WhenNoPreference_PersistsDeviceDefaultOnlyOnce()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(false, null, operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("de-DE", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        deviceCultureProvider.DeviceCultureName = "en-US";

        service.Initialize();

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.AreEqual("de", store.SavedLanguage);
        Assert.AreEqual(1, store.SetCount);
        Assert.AreEqual(1, deviceCultureProvider.CallCount);
    }

    [TestMethod]
    public void Initialize_WhenStoredEnglishAndDeviceGerman_DoesNotRequestDeviceCulture()
    {
        var service = CreateInitializedService(
            true,
            "en",
            "de-DE",
            out _,
            out var deviceCultureProvider,
            out _);

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.AreEqual(0, deviceCultureProvider.CallCount);
    }

    [TestMethod]
    public void Initialize_WhenStoredGermanAndDeviceEnglish_DoesNotRequestDeviceCulture()
    {
        var service = CreateInitializedService(
            true,
            "de",
            "en-US",
            out _,
            out var deviceCultureProvider,
            out _);

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.AreEqual(0, deviceCultureProvider.CallCount);
    }

    [TestMethod]
    public void Initialize_WhenPreferenceKeyIsAbsent_RequestsDeviceCultureOnce()
    {
        CreateInitializedService(
            false,
            null,
            "en-US",
            out _,
            out var deviceCultureProvider,
            out _);

        Assert.AreEqual(1, deviceCultureProvider.CallCount);
    }

    [TestMethod]
    public void Initialize_FirstLaunchFrench_PersistsEnglish()
    {
        var service = CreateInitializedService(false, null, "fr-FR", out var store, out _, out _);

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.AreEqual("en", store.SavedLanguage);
    }

    [TestMethod]
    public void Initialize_FirstLaunchGerman_PersistsGerman()
    {
        var service = CreateInitializedService(false, null, "de-DE", out var store, out _, out _);

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.AreEqual("de", store.SavedLanguage);
    }

    [TestMethod]
    public void Initialize_FirstLaunchEnglish_PersistsEnglish()
    {
        var service = CreateInitializedService(false, null, "en-US", out var store, out _, out _);

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.AreEqual("en", store.SavedLanguage);
    }

    [TestMethod]
    public void SetUiLanguage_FromGermanToEnglish_PersistsBeforeRefreshNotification()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "de", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("de-DE", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        operations.Clear();
        service.UiLanguageChanged += (_, _) => operations.Add("notify");

        service.SetUiLanguage("en");

        CollectionAssert.AreEqual(
            new[] { "persist:en", "culture:en", "notify" },
            operations);
    }

    private static LanguageSelectionService CreateInitializedService(
        bool hasSavedLanguage,
        string? savedLanguage,
        string deviceCulture,
        out InMemoryLanguagePreferenceStore store,
        out FakeDeviceCultureProvider deviceCultureProvider,
        out FakeUiCultureContext cultureContext)
    {
        var operations = new List<string>();
        store = new InMemoryLanguagePreferenceStore(hasSavedLanguage, savedLanguage, operations);
        deviceCultureProvider = new FakeDeviceCultureProvider(deviceCulture, operations);
        cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        return service;
    }

    private sealed class InMemoryLanguagePreferenceStore(
        bool hasSavedLanguage,
        string? savedLanguage,
        List<string> operations) : ILanguagePreferenceStore
    {
        public bool HasSavedLanguage { get; private set; } = hasSavedLanguage;

        public string? SavedLanguage { get; private set; } = savedLanguage;

        public int SetCount { get; private set; }

        public string? GetSavedLanguage() => HasSavedLanguage ? SavedLanguage : null;

        public void SetSavedLanguage(string languageCode)
        {
            HasSavedLanguage = true;
            SavedLanguage = languageCode;
            SetCount++;
            operations.Add($"persist:{languageCode}");
        }

    }

    private sealed class FakeDeviceCultureProvider(
        string deviceCultureName,
        List<string> operations) : IDeviceCultureProvider
    {
        public string DeviceCultureName { get; set; } = deviceCultureName;

        public int CallCount { get; private set; }

        public string GetDeviceCultureName()
        {
            CallCount++;
            operations.Add($"device:{DeviceCultureName}");
            return DeviceCultureName;
        }
    }

    private sealed class FakeUiCultureContext(List<string> operations) : IUiCultureContext
    {
        public string? CurrentUiLanguage { get; private set; }

        public UiCultureState ApplyUiCulture(string languageCode)
        {
            CurrentUiLanguage = languageCode;
            operations.Add($"culture:{languageCode}");
            var cultureName = languageCode == "de" ? "de-DE" : "en-US";
            return new UiCultureState(cultureName, cultureName, cultureName, cultureName);
        }
    }
}
