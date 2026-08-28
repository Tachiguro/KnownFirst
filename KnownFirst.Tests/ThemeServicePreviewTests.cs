using System.Reflection;
using KnownFirst.Core.Settings;
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

    public sealed class TestThemeApplication
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

    private static Type GetThemeServiceType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("KnownFirst.Services.ThemeService");
            if (t != null) return t;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "KnownFirst.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "KnownFirst.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "bin", "Debug", "net10.0-android", "KnownFirst.dll")
        };

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                try
                {
                    var asm = Assembly.LoadFrom(fullPath);
                    var t = asm.GetType("KnownFirst.Services.ThemeService");
                    if (t != null) return t;
                }
                catch
                {
                    // Continue searching
                }
            }
        }

        throw new AssertFailedException("Missing B2 behavior: ThemeService type must exist.");
    }

    private static Type GetIThemeServiceType()
    {
        var themeServiceType = GetThemeServiceType();
        return themeServiceType.Assembly.GetType("KnownFirst.Services.IThemeService")
            ?? throw new AssertFailedException("Missing B2 behavior: IThemeService type must exist.");
    }

    private static Type? GetIThemeApplicationType()
    {
        var themeServiceType = GetThemeServiceType();
        return themeServiceType.Assembly.GetType("KnownFirst.Services.IThemeApplication");
    }

    private static object CreateService(InMemoryPreferences preferences)
    {
        var serviceType = GetThemeServiceType();
        var loggerType = typeof(NullLogger<>).MakeGenericType(serviceType);
        var logger = loggerType.GetField("Instance")?.GetValue(null)
            ?? loggerType.GetProperty("Instance")?.GetValue(null)
            ?? NullLogger.Instance;
        return Activator.CreateInstance(serviceType, preferences, logger)!;
    }

    private static object CreateAndInitializeService(
        InMemoryPreferences preferences,
        TestThemeApplication app)
    {
        var service = CreateService(preferences);
        var serviceType = service.GetType();

        var initMethod = serviceType.GetMethods()
            .FirstOrDefault(m => m.Name == "Initialize" && m.GetParameters().Length == 1 && (m.GetParameters()[0].ParameterType == typeof(object) || m.GetParameters()[0].ParameterType.Name == "IThemeApplication"))
            ?? throw new AssertFailedException("Missing B2 behavior: Initialize method must exist on ThemeService.");

        initMethod.Invoke(service, [app]);
        return service;
    }

    private static void ApplyPreview(object service, ThemePreference preference)
    {
        var method = service.GetType().GetMethod("ApplyPreviewPreference", [typeof(ThemePreference)])
            ?? throw new AssertFailedException("Missing B2 behavior: ApplyPreviewPreference must exist.");
        method.Invoke(service, [preference]);
    }

    private static void ClearPreview(object service)
    {
        var method = service.GetType().GetMethod("ClearPreview", Type.EmptyTypes)
            ?? throw new AssertFailedException("Missing B2 behavior: ClearPreview must exist.");
        method.Invoke(service, null);
    }

    private static bool SetPreference(object service, ThemePreference preference)
    {
        var method = service.GetType().GetMethod("SetPreference", [typeof(ThemePreference)])
            ?? throw new AssertFailedException("SetPreference must exist.");
        return (bool)method.Invoke(service, [preference])!;
    }

    private static void ResetPreference(object service)
    {
        var method = service.GetType().GetMethod("ResetPreference", Type.EmptyTypes)
            ?? throw new AssertFailedException("ResetPreference must exist.");
        method.Invoke(service, null);
    }

    private static ThemePreference GetPreference(object service) =>
        (ThemePreference)service.GetType().GetProperty("Preference")!.GetValue(service)!;

    private static ThemePreference? GetPreviewPreference(object service) =>
        (ThemePreference?)service.GetType().GetProperty("PreviewPreference")!.GetValue(service);

    private static ThemePreference GetEffectiveTheme(object service) =>
        (ThemePreference)service.GetType().GetProperty("EffectiveTheme")!.GetValue(service)!;

    [TestMethod]
    public void InterfaceAndService_ExposePreviewApi()
    {
        var interfaceType = GetIThemeServiceType();
        var serviceType = GetThemeServiceType();

        var ifacePreviewProp = interfaceType.GetProperty("PreviewPreference");
        Assert.IsNotNull(ifacePreviewProp, "IThemeService must expose PreviewPreference property.");
        Assert.AreEqual(typeof(ThemePreference?), ifacePreviewProp.PropertyType);

        var ifaceApplyMethod = interfaceType.GetMethod("ApplyPreviewPreference", [typeof(ThemePreference)]);
        Assert.IsNotNull(ifaceApplyMethod, "IThemeService must expose ApplyPreviewPreference(ThemePreference) method.");

        var ifaceClearMethod = interfaceType.GetMethod("ClearPreview", Type.EmptyTypes);
        Assert.IsNotNull(ifaceClearMethod, "IThemeService must expose ClearPreview() method.");

        var svcPreviewProp = serviceType.GetProperty("PreviewPreference");
        Assert.IsNotNull(svcPreviewProp, "ThemeService must implement PreviewPreference property.");

        var svcApplyMethod = serviceType.GetMethod("ApplyPreviewPreference", [typeof(ThemePreference)]);
        Assert.IsNotNull(svcApplyMethod, "ThemeService must implement ApplyPreviewPreference(ThemePreference) method.");

        var svcClearMethod = serviceType.GetMethod("ClearPreview", Type.EmptyTypes);
        Assert.IsNotNull(svcClearMethod, "ThemeService must implement ClearPreview() method.");
    }

    [TestMethod]
    public void LightPreview_ChangesNativeAndEffectiveTheme_WithoutWritingPreferences()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Dark };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        ApplyPreview(service, ThemePreference.Light);

        Assert.AreEqual(ThemePreference.Light, GetPreviewPreference(service));
        Assert.AreEqual(ThemePreference.System, GetPreference(service), "Committed Preference must remain System.");
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service));
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

        ApplyPreview(service, ThemePreference.Dark);

        Assert.AreEqual(ThemePreference.Dark, GetPreviewPreference(service));
        Assert.AreEqual(ThemePreference.System, GetPreference(service), "Committed Preference must remain System.");
        Assert.AreEqual(ThemePreference.Dark, GetEffectiveTheme(service));
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

        ApplyPreview(service, ThemePreference.System);

        Assert.AreEqual(ThemePreference.System, GetPreviewPreference(service));
        Assert.AreEqual(ThemePreference.Dark, GetPreference(service), "Committed Preference must remain Dark.");
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service), "Effective theme must follow RequestedTheme under System preview.");
        Assert.AreEqual(AppTheme.Unspecified, app.UserAppTheme);
        Assert.AreEqual(0, prefs.SetCount, "Preview must perform zero preference writes.");
    }

    [TestMethod]
    public void RequestedThemeChange_UpdatesEffectiveTheme_WhenPreviewSystemActive()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);

        ApplyPreview(service, ThemePreference.System);
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service));

        app.SimulateRequestedThemeChanged(AppTheme.Dark);
        Assert.AreEqual(ThemePreference.Dark, GetEffectiveTheme(service), "EffectiveTheme must update when system requested theme changes during System preview.");
    }

    [TestMethod]
    public void RequestedThemeChange_DoesNotOverride_LightOrDarkPreview()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Dark };
        var service = CreateAndInitializeService(prefs, app);

        ApplyPreview(service, ThemePreference.Light);
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service));

        app.SimulateRequestedThemeChanged(AppTheme.Dark);
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service), "Explicit Light preview must not be overridden by system requested theme.");
    }

    [TestMethod]
    public void ClearPreview_RestoresCommittedTheme_WithZeroPersistence()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        ApplyPreview(service, ThemePreference.Dark);
        Assert.AreEqual(ThemePreference.Dark, GetEffectiveTheme(service));

        ClearPreview(service);

        Assert.IsNull(GetPreviewPreference(service), "ClearPreview must clear PreviewPreference.");
        Assert.AreEqual(ThemePreference.System, GetPreference(service));
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service), "Effective theme must restore to committed system resolution.");
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
        ApplyPreview(service, ThemePreference.Dark);
        Assert.AreEqual(0, prefs.SetCount);
        Assert.AreEqual(ThemePreference.Dark, GetEffectiveTheme(service));

        // Commit Dark
        var changed = SetPreference(service, ThemePreference.Dark);

        Assert.IsTrue(changed, "SetPreference must return true when committing an active preview target.");
        Assert.AreEqual((int)ThemePreference.Dark, prefs.Get("theme_preference", -1), "SetPreference must write target to preferences.");
        Assert.AreEqual(ThemePreference.Dark, GetPreference(service));
        Assert.IsNull(GetPreviewPreference(service), "SetPreference must clear active preview.");
    }

    [TestMethod]
    public void SetPreference_WithDifferentActivePreview_PersistsRequestedTarget()
    {
        var prefs = new InMemoryPreferences();
        var app = new TestThemeApplication { RequestedTheme = AppTheme.Light };
        var service = CreateAndInitializeService(prefs, app);
        prefs.ResetSetCount();

        // Preview Dark
        ApplyPreview(service, ThemePreference.Dark);

        // Commit Light
        var changed = SetPreference(service, ThemePreference.Light);

        Assert.IsTrue(changed);
        Assert.AreEqual((int)ThemePreference.Light, prefs.Get("theme_preference", -1));
        Assert.AreEqual(ThemePreference.Light, GetPreference(service));
        Assert.IsNull(GetPreviewPreference(service));
        Assert.AreEqual(ThemePreference.Light, GetEffectiveTheme(service));
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
        var changed = SetPreference(service, ThemePreference.Dark);

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

        var changed = SetPreference(service, ThemePreference.System);

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

        ApplyPreview(service, ThemePreference.Light);
        Assert.IsNotNull(GetPreviewPreference(service));

        ResetPreference(service);

        Assert.IsNull(GetPreviewPreference(service), "ResetPreference must clear preview.");
        Assert.IsFalse(prefs.ContainsKey("theme_preference"), "ResetPreference must remove persistent preference.");
        Assert.AreEqual(ThemePreference.System, GetPreference(service));
    }
}
