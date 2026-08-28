using KnownFirst.Core.Settings;
using KnownFirst.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class ThemeServicePreviewTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new();

        public IReadOnlyCollection<string> Keys => _values.Keys.ToArray();

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);

        public void Remove(string key, string? sharedName = null) => _values.Remove(key);

        public void Clear(string? sharedName = null) => _values.Clear();

        public int SetCount { get; private set; }

        public void Set<T>(string key, T value, string? sharedName = null)
        {
            SetCount++;
            if (value is null)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
        }

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            if (_values.TryGetValue(key, out var val) && val is T typedVal)
            {
                return typedVal;
            }
            return defaultValue;
        }

        public void ResetSetCount() => SetCount = 0;
    }

    public sealed class TestThemeApplication : IThemeApplication
    {
        public AppTheme UserAppTheme { get; set; } = AppTheme.Unspecified;
        public AppTheme RequestedTheme { get; set; } = AppTheme.Light;
        public event EventHandler? RequestedThemeChanged;

        public void SimulateRequestedThemeChanged(AppTheme newTheme)
        {
            RequestedTheme = newTheme;
            RequestedThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ThemeService CreateAndInitializeService(
        InMemoryPreferences preferences,
        TestThemeApplication app)
    {
        var service = new ThemeService(preferences, NullLogger<ThemeService>.Instance);
        service.Initialize(app);
        return service;
    }

    [TestMethod]
    public void InterfaceAndService_ExposePreviewApi()
    {
        var interfaceType = typeof(IThemeService);
        var serviceType = typeof(ThemeService);

        var ifacePreviewProp = interfaceType.GetProperty("PreviewPreference");
        Assert.IsNotNull(ifacePreviewProp, "IThemeService must expose PreviewPreference property.");
        Assert.AreEqual(typeof(ThemePreference?), ifacePreviewProp.PropertyType);
        Assert.IsTrue(ifacePreviewProp.GetMethod!.IsAbstract, "PreviewPreference getter must be required.");

        var ifaceApplyMethod = interfaceType.GetMethod("ApplyPreviewPreference", [typeof(ThemePreference)]);
        Assert.IsNotNull(ifaceApplyMethod, "IThemeService must expose ApplyPreviewPreference(ThemePreference) method.");
        Assert.IsTrue(ifaceApplyMethod.IsAbstract, "ApplyPreviewPreference must be required.");

        var ifaceClearMethod = interfaceType.GetMethod("ClearPreview", Type.EmptyTypes);
        Assert.IsNotNull(ifaceClearMethod, "IThemeService must expose ClearPreview() method.");
        Assert.IsTrue(ifaceClearMethod.IsAbstract, "ClearPreview must be required.");

        Assert.IsNull(interfaceType.GetMethod("Initialize", [typeof(object)]), "IThemeService must not expose Initialize(object).");
        Assert.IsNull(serviceType.GetMethod("Initialize", [typeof(object)]), "ThemeService must not expose Initialize(object).");
        Assert.IsNull(serviceType.GetNestedType("DuckTypedApplicationAdapter", System.Reflection.BindingFlags.NonPublic), "ThemeService must not contain DuckTypedApplicationAdapter.");

        var svcPreviewProp = serviceType.GetProperty("PreviewPreference");
        Assert.IsNotNull(svcPreviewProp, "ThemeService must implement PreviewPreference property.");

        var svcApplyMethod = serviceType.GetMethod("ApplyPreviewPreference", [typeof(ThemePreference)]);
        Assert.IsNotNull(svcApplyMethod, "ThemeService must implement ApplyPreviewPreference(ThemePreference) method.");

        var svcClearMethod = serviceType.GetMethod("ClearPreview", Type.EmptyTypes);
        Assert.IsNotNull(svcClearMethod, "ThemeService must implement ClearPreview() method.");
    }

    [TestMethod]
    public void TypedThemeApplicationSeam_InitializesAndForwardsSystemThemeEvents()
    {
        var preferences = new InMemoryPreferences();
        IThemeApplication application = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = new ThemeService(preferences, NullLogger<ThemeService>.Instance);
        var changeCount = 0;
        service.ThemeChanged += (_, _) => changeCount++;

        service.Initialize(application);
        ((TestThemeApplication)application).SimulateRequestedThemeChanged(AppTheme.Dark);

        Assert.AreEqual(ThemePreference.Dark, service.EffectiveTheme);
        Assert.AreEqual(1, changeCount, "Typed requested-theme events must reach ThemeService.");

        service.Dispose();
        ((TestThemeApplication)application).SimulateRequestedThemeChanged(AppTheme.Light);

        Assert.AreEqual(ThemePreference.Dark, service.EffectiveTheme, "Dispose must unsubscribe the typed event seam.");
        Assert.AreEqual(1, changeCount);
    }

    [TestMethod]
    public void LightPreview_ChangesNativeAndEffectiveTheme_WithoutWritingPreferences()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Dark };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        service.ApplyPreviewPreference(ThemePreference.Light);

        Assert.AreEqual(ThemePreference.Light, service.PreviewPreference);
        Assert.AreEqual(ThemePreference.System, service.Preference, "Committed Preference must remain System.");
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme);
        Assert.AreEqual(AppTheme.Light, app.UserAppTheme);
        Assert.AreEqual(0, prefs.SetCount, "Preview must perform zero preference writes.");
    }

    [TestMethod]
    public void DarkPreview_ChangesNativeAndEffectiveTheme_WithoutWritingPreferences()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        service.ApplyPreviewPreference(ThemePreference.Dark);

        Assert.AreEqual(ThemePreference.Dark, service.PreviewPreference);
        Assert.AreEqual(ThemePreference.System, service.Preference, "Committed Preference must remain System.");
        Assert.AreEqual(ThemePreference.Dark, service.EffectiveTheme);
        Assert.AreEqual(AppTheme.Dark, app.UserAppTheme);
        Assert.AreEqual(0, prefs.SetCount, "Preview must perform zero preference writes.");
    }

    [TestMethod]
    public void SystemPreview_FollowsRequestedTheme_WithoutWritingPreferences()
    {
        var prefs = new InMemoryPreferences();
        prefs.Set("theme_preference", (int)ThemePreference.Dark);
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        service.ApplyPreviewPreference(ThemePreference.System);

        Assert.AreEqual(ThemePreference.System, service.PreviewPreference);
        Assert.AreEqual(ThemePreference.Dark, service.Preference, "Committed Preference must remain Dark.");
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme, "Effective theme must follow RequestedTheme under System preview.");
        Assert.AreEqual(AppTheme.Unspecified, app.UserAppTheme);
        Assert.AreEqual(0, prefs.SetCount, "Preview must perform zero preference writes.");
    }

    [TestMethod]
    public void RequestedThemeChange_UpdatesEffectiveTheme_WhenPreviewSystemActive()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);

        service.ApplyPreviewPreference(ThemePreference.System);
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme);

        app.SimulateRequestedThemeChanged(AppTheme.Dark);
        Assert.AreEqual(ThemePreference.Dark, service.EffectiveTheme, "EffectiveTheme must update when system requested theme changes during System preview.");
    }

    [TestMethod]
    public void RequestedThemeChange_DoesNotOverride_LightOrDarkPreview()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Dark };
        var service = CreateAndInitializeService(prefs, app);

        service.ApplyPreviewPreference(ThemePreference.Light);
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme);

        app.SimulateRequestedThemeChanged(AppTheme.Dark);
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme, "Explicit Light preview must not be overridden by system requested theme.");
    }

    [TestMethod]
    public void ClearPreview_RestoresCommittedTheme_WithZeroPersistence()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        service.ApplyPreviewPreference(ThemePreference.Dark);
        Assert.AreEqual(ThemePreference.Dark, service.EffectiveTheme);

        service.ClearPreview();

        Assert.IsNull(service.PreviewPreference, "ClearPreview must clear PreviewPreference.");
        Assert.AreEqual(ThemePreference.System, service.Preference);
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme, "Effective theme must restore to committed system resolution.");
        Assert.AreEqual(AppTheme.Unspecified, app.UserAppTheme);
        Assert.AreEqual(0, prefs.SetCount, "ClearPreview must perform zero preference writes.");
    }

    [TestMethod]
    public void SetPreference_WithMatchingActivePreview_StillPersistsTarget()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        // Preview Dark
        service.ApplyPreviewPreference(ThemePreference.Dark);
        Assert.AreEqual(0, prefs.SetCount);
        Assert.AreEqual(ThemePreference.Dark, service.EffectiveTheme);

        // Commit Dark
        var changed = service.SetPreference(ThemePreference.Dark);

        Assert.IsTrue(changed, "SetPreference must return true when committing an active preview target.");
        Assert.AreEqual((int)ThemePreference.Dark, prefs.Get("theme_preference", -1), "SetPreference must write target to preferences.");
        Assert.AreEqual(ThemePreference.Dark, service.Preference);
        Assert.IsNull(service.PreviewPreference, "SetPreference must clear active preview.");
    }

    [TestMethod]
    public void SetPreference_WithDifferentActivePreview_PersistsRequestedTarget()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        // Preview Dark
        service.ApplyPreviewPreference(ThemePreference.Dark);

        // Commit Light
        var changed = service.SetPreference(ThemePreference.Light);

        Assert.IsTrue(changed);
        Assert.AreEqual((int)ThemePreference.Light, prefs.Get("theme_preference", -1));
        Assert.AreEqual(ThemePreference.Light, service.Preference);
        Assert.IsNull(service.PreviewPreference);
        Assert.AreEqual(ThemePreference.Light, service.EffectiveTheme);
    }

    [TestMethod]
    public void SetPreference_NoPreview_AlreadyPersisted_ReturnsFalse()
    {
        var prefs = new InMemoryPreferences();
        prefs.Set("theme_preference", (int)ThemePreference.Dark);
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        // No preview active, preference is already Dark and persisted as Dark
        var changed = service.SetPreference(ThemePreference.Dark);

        Assert.IsFalse(changed, "SetPreference must return false for a genuine no-op with no preview active.");
        Assert.AreEqual(0, prefs.SetCount);
    }

    [TestMethod]
    public void SetPreference_System_WhenPersistenceAbsent_EstablishesPersistedSystem()
    {
        var prefs = new InMemoryPreferences();
        // Do not set theme_preference in prefs
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        var changed = service.SetPreference(ThemePreference.System);

        Assert.IsTrue(changed, "Committing System when no preference key exists must return true.");
        Assert.IsTrue(prefs.ContainsKey("theme_preference"), "Explicit System commit must persist unambiguous System marker.");
        Assert.AreEqual((int)ThemePreference.System, prefs.Get("theme_preference", -1));
    }

    [TestMethod]
    public void ResetPreference_ClearsPreview_AndRemovesPersistentPreference()
    {
        var prefs = new InMemoryPreferences();
        prefs.Set("theme_preference", (int)ThemePreference.Dark);
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);

        service.ApplyPreviewPreference(ThemePreference.Light);
        Assert.IsNotNull(service.PreviewPreference);

        service.ResetPreference();

        Assert.IsNull(service.PreviewPreference, "ResetPreference must clear preview.");
        Assert.IsFalse(prefs.ContainsKey("theme_preference"), "ResetPreference must remove persistent preference.");
        Assert.AreEqual(ThemePreference.System, service.Preference);
    }
}
