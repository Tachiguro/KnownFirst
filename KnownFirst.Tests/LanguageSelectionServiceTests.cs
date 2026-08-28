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
    public void Initialize_WhenNoPreference_ResolvesDeviceCultureOnEveryStartup()
    {
        // A missing preference means System is active. System must re-resolve the device
        // culture on every startup, not freeze the first-resolved language permanently.
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(false, null, operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("de-DE", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual(1, store.SetCount, "The explicit System marker is persisted once on first launch.");
        Assert.AreEqual(1, deviceCultureProvider.CallCount);

        deviceCultureProvider.DeviceCultureName = "en-US";
        service.Initialize();

        Assert.AreEqual("en", service.CurrentUiLanguage, "System must follow the device culture on every start.");
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual(1, store.SetCount, "The System marker itself does not need to be re-persisted.");
        Assert.AreEqual(2, deviceCultureProvider.CallCount);
    }

    [TestMethod]
    public void Initialize_SystemPreferenceWithRussianDeviceCulture_ResolvesToRussian()
    {
        var service = CreateInitializedService(false, null, "ru-RU", out var store, out _, out _);

        Assert.AreEqual("ru", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual("system", store.SavedLanguage);
    }

    [TestMethod]
    public void Initialize_SystemPreferenceWithRegionalRussianDeviceCulture_ResolvesToRussian()
    {
        var service = CreateInitializedService(false, null, "ru-KZ", out _, out _, out _);

        Assert.AreEqual("ru", service.CurrentUiLanguage);
    }

    [TestMethod]
    public void Initialize_SystemPreferenceWithUnsupportedDeviceCulture_FallsBackToEnglish()
    {
        var service = CreateInitializedService(false, null, "fr-FR", out var store, out _, out _);

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
    }

    [TestMethod]
    public void Initialize_MalformedStoredPreference_FailsSafelyToSystemResolution()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "xx-not-a-language", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("de-DE", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);

        service.Initialize();

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
    }

    [TestMethod]
    public void SetUiLanguage_Russian_PersistsAndOverridesDeviceCulture()
    {
        var service = CreateInitializedService(false, null, "en-US", out var store, out var deviceCultureProvider, out var cultureContext);
        deviceCultureProvider.CallCountReset();

        service.SetUiLanguage("ru");

        Assert.AreEqual("ru", service.CurrentUiLanguage);
        Assert.IsFalse(service.IsSystemPreferenceActive);
        Assert.AreEqual("ru", store.SavedLanguage);
        Assert.AreEqual("ru", cultureContext.CurrentUiLanguage);
        Assert.AreEqual(0, deviceCultureProvider.CallCount, "A manual selection must not consult the device culture.");
    }

    [TestMethod]
    public void SetUiLanguage_Russian_UpdatesCultureAndRaisesNotificationExactlyOnce()
    {
        var service = CreateInitializedService(true, "en", "en-US", out _, out _, out _);
        var notificationCount = 0;
        service.UiLanguageChanged += (_, _) => notificationCount++;

        service.SetUiLanguage("ru");

        Assert.AreEqual(1, notificationCount);
        Assert.AreEqual("ru", service.CurrentUiLanguage);
    }

    [TestMethod]
    public void SetUiLanguage_System_RestoresDynamicDeviceCultureResolution()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "de", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("ru-RU", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        var notificationCount = 0;
        service.UiLanguageChanged += (_, _) => notificationCount++;

        service.SetUiLanguage("system");

        Assert.AreEqual("ru", service.CurrentUiLanguage, "Selecting System must immediately apply the current device culture.");
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual("system", store.SavedLanguage);
        Assert.AreEqual(1, notificationCount);

        // Confirm the restored System mode stays dynamic across a later restart.
        deviceCultureProvider.DeviceCultureName = "en-US";
        service.Initialize();
        Assert.AreEqual("en", service.CurrentUiLanguage);
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
    public void Initialize_FirstLaunchFrench_ResolvesEnglishUnderSystemPreference()
    {
        // First launch persists the explicit "system" marker (not a frozen concrete
        // language) so System mode continues to follow the device culture on later starts.
        var service = CreateInitializedService(false, null, "fr-FR", out var store, out _, out _);

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual("system", store.SavedLanguage);
    }

    [TestMethod]
    public void Initialize_FirstLaunchGerman_ResolvesGermanUnderSystemPreference()
    {
        var service = CreateInitializedService(false, null, "de-DE", out var store, out _, out _);

        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual("system", store.SavedLanguage);
    }

    [TestMethod]
    public void Initialize_FirstLaunchEnglish_ResolvesEnglishUnderSystemPreference()
    {
        var service = CreateInitializedService(false, null, "en-US", out var store, out _, out _);

        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual("system", store.SavedLanguage);
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

    [TestMethod]
    public void InterfaceAndService_ExposeLanguagePreviewApi()
    {
        var interfaceType = typeof(ILanguageSelectionService);
        var serviceType = typeof(LanguageSelectionService);

        var ifacePreviewProp = interfaceType.GetProperty("PreviewUiLanguage");
        Assert.IsNotNull(ifacePreviewProp, "ILanguageSelectionService must expose PreviewUiLanguage property.");
        Assert.AreEqual(typeof(string), ifacePreviewProp.PropertyType);
        Assert.IsTrue(ifacePreviewProp.GetMethod!.IsAbstract, "PreviewUiLanguage getter must be required.");

        var ifaceSystemPreviewProp = interfaceType.GetProperty("IsSystemPreviewActive");
        Assert.IsNotNull(ifaceSystemPreviewProp, "ILanguageSelectionService must expose IsSystemPreviewActive property.");
        Assert.AreEqual(typeof(bool), ifaceSystemPreviewProp.PropertyType);
        Assert.IsTrue(ifaceSystemPreviewProp.GetMethod!.IsAbstract, "IsSystemPreviewActive getter must be required.");

        var ifaceApplyMethod = interfaceType.GetMethod("ApplyPreviewLanguage", [typeof(string)]);
        Assert.IsNotNull(ifaceApplyMethod, "ILanguageSelectionService must expose ApplyPreviewLanguage(string) method.");
        Assert.IsTrue(ifaceApplyMethod.IsAbstract, "ApplyPreviewLanguage must be required.");

        var ifaceClearMethod = interfaceType.GetMethod("ClearPreview", Type.EmptyTypes);
        Assert.IsNotNull(ifaceClearMethod, "ILanguageSelectionService must expose ClearPreview() method.");
        Assert.IsTrue(ifaceClearMethod.IsAbstract, "ClearPreview must be required.");

        var svcPreviewProp = serviceType.GetProperty("PreviewUiLanguage");
        Assert.IsNotNull(svcPreviewProp, "LanguageSelectionService must implement PreviewUiLanguage property.");

        var svcSystemPreviewProp = serviceType.GetProperty("IsSystemPreviewActive");
        Assert.IsNotNull(svcSystemPreviewProp, "LanguageSelectionService must implement IsSystemPreviewActive property.");

        var svcApplyMethod = serviceType.GetMethod("ApplyPreviewLanguage", [typeof(string)]);
        Assert.IsNotNull(svcApplyMethod, "LanguageSelectionService must implement ApplyPreviewLanguage(string) method.");

        var svcClearMethod = serviceType.GetMethod("ClearPreview", Type.EmptyTypes);
        Assert.IsNotNull(svcClearMethod, "LanguageSelectionService must implement ClearPreview() method.");
    }

    [TestMethod]
    public void ManualPreview_AppliesLiveCulture_WithoutModifyingCommittedStateOrStore()
    {
        var service = CreateInitializedService(true, "en", "en-US", out var store, out _, out var cultureContext);
        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");

        var notificationCount = 0;
        service.UiLanguageChanged += (_, _) => notificationCount++;

        applyMethod.Invoke(service, ["de"]);

        Assert.AreEqual("de", cultureContext.CurrentUiLanguage, "Live culture must reflect previewed language.");
        Assert.AreEqual(0, store.SetCount, "Preview must perform zero preference writes.");
        Assert.AreEqual("en", service.CurrentUiLanguage, "Committed CurrentUiLanguage must remain unchanged.");
        Assert.IsFalse(service.IsSystemPreferenceActive, "Committed IsSystemPreferenceActive must remain unchanged.");

        var previewProp = service.GetType().GetProperty("PreviewUiLanguage")!;
        Assert.AreEqual("de", previewProp.GetValue(service));

        var systemPreviewProp = service.GetType().GetProperty("IsSystemPreviewActive")!;
        Assert.AreEqual(false, systemPreviewProp.GetValue(service));

        Assert.AreEqual(1, notificationCount, "Preview should notify subscribers of effective culture change.");
    }

    [TestMethod]
    public void SystemPreview_ResolvesDeviceCulture_WithoutModifyingCommittedStateOrStore()
    {
        var service = CreateInitializedService(true, "en", "de-DE", out var store, out _, out var cultureContext);
        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");

        applyMethod.Invoke(service, ["system"]);

        Assert.AreEqual("de", cultureContext.CurrentUiLanguage, "System preview must apply resolved device culture.");
        Assert.AreEqual(0, store.SetCount, "Preview must not write to preference store.");
        Assert.AreEqual("en", service.CurrentUiLanguage, "Committed CurrentUiLanguage must remain unchanged.");
        Assert.IsFalse(service.IsSystemPreferenceActive, "Committed IsSystemPreferenceActive must remain unchanged.");

        var previewProp = service.GetType().GetProperty("PreviewUiLanguage")!;
        Assert.AreEqual("de", previewProp.GetValue(service));

        var systemPreviewProp = service.GetType().GetProperty("IsSystemPreviewActive")!;
        Assert.AreEqual(true, systemPreviewProp.GetValue(service));
    }

    [TestMethod]
    public void ClearPreview_RestoresCommittedCulture_WithZeroPersistence()
    {
        var service = CreateInitializedService(true, "en", "en-US", out var store, out _, out var cultureContext);
        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");
        var clearMethod = service.GetType().GetMethod("ClearPreview", Type.EmptyTypes)
            ?? throw new AssertFailedException("Missing B2 behavior: ClearPreview must exist.");

        applyMethod.Invoke(service, ["de"]);
        Assert.AreEqual("de", cultureContext.CurrentUiLanguage);

        clearMethod.Invoke(service, null);

        Assert.AreEqual("en", cultureContext.CurrentUiLanguage, "ClearPreview must restore committed culture.");
        Assert.AreEqual(0, store.SetCount, "ClearPreview must not perform preference writes.");

        var previewProp = service.GetType().GetProperty("PreviewUiLanguage")!;
        Assert.IsNull(previewProp.GetValue(service));

        var systemPreviewProp = service.GetType().GetProperty("IsSystemPreviewActive")!;
        Assert.AreEqual(false, systemPreviewProp.GetValue(service));
    }

    [TestMethod]
    public void ReapplyCurrentCulture_ReappliesPreview_WhenPreviewActive()
    {
        var operations = new List<string>();
        var store = new InMemoryLanguagePreferenceStore(true, "en", operations);
        var deviceCultureProvider = new FakeDeviceCultureProvider("en-US", operations);
        var cultureContext = new FakeUiCultureContext(operations);
        var service = new LanguageSelectionService(store, deviceCultureProvider, cultureContext);
        service.Initialize();
        operations.Clear();

        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");

        applyMethod.Invoke(service, ["de"]);
        operations.Clear();

        service.ReapplyCurrentCulture();

        Assert.IsTrue(operations.Contains("culture:de"), "ReapplyCurrentCulture must reapply the active preview culture.");
    }

    [TestMethod]
    public void Initialize_ClearsPreview_AndRestoresCommittedPersistedPreference()
    {
        var service = CreateInitializedService(true, "en", "en-US", out var store, out _, out var cultureContext);
        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");

        applyMethod.Invoke(service, ["de"]);
        Assert.AreEqual("de", cultureContext.CurrentUiLanguage);

        service.Initialize();

        var previewProp = service.GetType().GetProperty("PreviewUiLanguage")!;
        Assert.IsNull(previewProp.GetValue(service), "Initialize must clear preview.");
        Assert.AreEqual("en", service.CurrentUiLanguage);
        Assert.AreEqual("en", cultureContext.CurrentUiLanguage);
    }

    [TestMethod]
    public void PreviewGerman_ThenSetUiLanguageGerman_PersistsGerman()
    {
        var service = CreateInitializedService(true, "en", "en-US", out var store, out _, out var cultureContext);
        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");

        // Preview German
        applyMethod.Invoke(service, ["de"]);
        Assert.AreEqual(0, store.SetCount);
        Assert.AreEqual("de", cultureContext.CurrentUiLanguage);

        // Now commit German
        service.SetUiLanguage("de");

        Assert.AreEqual("de", store.SavedLanguage, "SetUiLanguage must persist German even if it was actively previewed.");
        Assert.AreEqual(1, store.SetCount);
        Assert.AreEqual("de", service.CurrentUiLanguage);
        Assert.IsFalse(service.IsSystemPreferenceActive);

        var previewProp = service.GetType().GetProperty("PreviewUiLanguage")!;
        Assert.IsNull(previewProp.GetValue(service), "SetUiLanguage must clear preview.");
    }

    [TestMethod]
    public void PreviewSystem_ThenSetUiLanguageSystem_VerifiesAndPersistsSystemMarker()
    {
        var service = CreateInitializedService(true, "en", "de-DE", out var store, out _, out var cultureContext);
        var applyMethod = service.GetType().GetMethod("ApplyPreviewLanguage", [typeof(string)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewLanguage must exist.");

        // Preview System
        applyMethod.Invoke(service, ["system"]);
        Assert.AreEqual(0, store.SetCount);

        // Now commit System
        service.SetUiLanguage("system");

        Assert.AreEqual("system", store.SavedLanguage, "SetUiLanguage(system) must persist explicit system marker.");
        Assert.IsTrue(service.IsSystemPreferenceActive);
        Assert.AreEqual("de", service.CurrentUiLanguage);

        var previewProp = service.GetType().GetProperty("PreviewUiLanguage")!;
        Assert.IsNull(previewProp.GetValue(service), "SetUiLanguage must clear preview.");
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

        public void CallCountReset() => CallCount = 0;
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
