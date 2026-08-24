using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Onboarding;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

/// <summary>
/// Focused contract tests for the optional application-local Display Name. The name is a
/// device-local preference next to the theme, language, onboarding, and What's New markers — never
/// SQLite content, never portable-archive content, and never an account or cloud identity.
/// </summary>
[TestClass]
public sealed class DisplayNameTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new();

        public IReadOnlyCollection<string> Keys => _values.Keys.ToArray();

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);

        public void Remove(string key, string? sharedName = null) => _values.Remove(key);

        public void Clear(string? sharedName = null) => _values.Clear();

        public void Set<T>(string key, T value, string? sharedName = null)
        {
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
    }

    private const string DisplayNameKey = "display_name";

    private static (MauiDisplayNameStore Store, InMemoryPreferences Preferences) CreateStore()
    {
        var preferences = new InMemoryPreferences();
        return (new MauiDisplayNameStore(preferences), preferences);
    }

    // -----------------------------------------------------------------------------------------
    // A. Normalization policy
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void Normalize_NullIsAbsent()
    {
        Assert.IsNull(DisplayNamePolicy.Normalize(null));
    }

    [TestMethod]
    public void Normalize_EmptyStringIsAbsent()
    {
        Assert.IsNull(DisplayNamePolicy.Normalize(string.Empty));
    }

    [TestMethod]
    public void Normalize_WhitespaceOnlyIsAbsent()
    {
        foreach (var whitespaceOnly in new[] { " ", "   ", "\t", "\n", " \t \n " })
        {
            Assert.IsNull(
                DisplayNamePolicy.Normalize(whitespaceOnly),
                "Whitespace-only input must mean absent, not a stored blank name.");
        }
    }

    [TestMethod]
    public void Normalize_TrimsSurroundingWhitespace()
    {
        Assert.AreEqual("Anna", DisplayNamePolicy.Normalize("  Anna  "));
        Assert.AreEqual("Anna", DisplayNamePolicy.Normalize("\tAnna\n"));
    }

    [TestMethod]
    public void Normalize_KeepsMeaningfulTextIncludingInnerSpacing()
    {
        Assert.AreEqual("Anna Schmidt", DisplayNamePolicy.Normalize("  Anna Schmidt "));
        Assert.AreEqual("Анна", DisplayNamePolicy.Normalize(" Анна "));
    }

    // -----------------------------------------------------------------------------------------
    // B. Preference store behavior
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void Store_ReturnsAbsentWhenNoNameHasEverBeenStored()
    {
        var (store, preferences) = CreateStore();

        Assert.IsNull(store.GetDisplayName());
        Assert.IsFalse(preferences.ContainsKey(DisplayNameKey));
    }

    [TestMethod]
    public void Store_RoundTripsAMeaningfulName()
    {
        var (store, preferences) = CreateStore();

        store.SetDisplayName("Anna");

        Assert.AreEqual("Anna", store.GetDisplayName());
        Assert.AreEqual("Anna", preferences.Get(DisplayNameKey, string.Empty));
    }

    [TestMethod]
    public void Store_PersistsTheNormalizedValueRatherThanTheRawInput()
    {
        var (store, preferences) = CreateStore();

        store.SetDisplayName("   Anna   ");

        Assert.AreEqual("Anna", store.GetDisplayName());
        Assert.AreEqual("Anna", preferences.Get(DisplayNameKey, string.Empty));
    }

    [TestMethod]
    public void Store_SettingAbsentInputRemovesThePreferenceEntirely()
    {
        foreach (var absentInput in new string?[] { null, string.Empty, "   ", "\t" })
        {
            var (store, preferences) = CreateStore();
            store.SetDisplayName("Anna");
            Assert.IsTrue(preferences.ContainsKey(DisplayNameKey));

            store.SetDisplayName(absentInput);

            Assert.IsNull(
                store.GetDisplayName(),
                "Saving absent input must remove the name, not store a blank value.");
            Assert.IsFalse(
                preferences.ContainsKey(DisplayNameKey),
                "Removing the name must delete the preference key rather than leave an empty entry.");
        }
    }

    [TestMethod]
    public void Store_UpdatesAnExistingName()
    {
        var (store, _) = CreateStore();

        store.SetDisplayName("Anna");
        store.SetDisplayName(" Boris ");

        Assert.AreEqual("Boris", store.GetDisplayName());
    }

    [TestMethod]
    public void Store_OwnsOnlyTheDisplayNamePreferenceKey()
    {
        var (store, preferences) = CreateStore();

        store.SetDisplayName("Anna");

        Assert.HasCount(1, preferences.Keys);
        Assert.AreEqual(DisplayNameKey, preferences.Keys.Single());
    }

    // -----------------------------------------------------------------------------------------
    // C. Application-settings boundary: Display Name is not an AppSettingsService key
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void ApplicationSettingsReset_DoesNotClearTheDisplayName()
    {
        // AppSettingsService.Reset() removes exactly the preference keys it owns, and it is what
        // the non-destructive "Restore default settings" flow calls. The Display Name deliberately
        // lives in its own store so it survives that operation without any special-case logic.
        var preferences = new InMemoryPreferences();
        var store = new MauiDisplayNameStore(preferences);
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        store.SetDisplayName("Anna");
        appSettings.SetPreparationLimit(50);

        appSettings.Reset();

        Assert.AreEqual("Anna", store.GetDisplayName());
        Assert.IsTrue(preferences.ContainsKey(DisplayNameKey));
    }

    // -----------------------------------------------------------------------------------------
    // D. Destructive full-reset boundary
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void FullResetPreferenceClear_RemovesTheDisplayNameWithoutDisplayNameSpecificResetLogic()
    {
        // Mirrors the real destructive flow in Settings.razor: Database.ResetAsync() then
        // Preferences.Clear() then RestoreDefaultsForFullReset(). The Display Name must disappear
        // purely because the whole preference store was cleared — no Display Name reset code path.
        var preferences = new InMemoryPreferences();
        var store = new MauiDisplayNameStore(preferences);
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var defaults = new SettingsDefaultsService(
            appSettings,
            new NoOpThemePreferenceReset(),
            new NoOpLanguageSelection(),
            new MauiOnboardingStateStore(preferences),
            NullLogger<SettingsDefaultsService>.Instance);

        store.SetDisplayName("Anna");
        Assert.AreEqual("Anna", store.GetDisplayName(), "The name must exist before the destructive reset.");

        preferences.Clear();
        defaults.RestoreDefaultsForFullReset();

        Assert.IsNull(store.GetDisplayName());
        Assert.IsFalse(preferences.ContainsKey(DisplayNameKey));
    }

    [TestMethod]
    public void SettingsDefaultsService_HasNoDisplayNameDependency()
    {
        // Proof that neither reset flow contains Display Name-specific logic: the service cannot
        // reach the store at all.
        var constructors = typeof(SettingsDefaultsService).GetConstructors();
        Assert.HasCount(1, constructors);

        foreach (var parameter in constructors[0].GetParameters())
        {
            var typeName = parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
            Assert.DoesNotContain("DisplayName", typeName, StringComparison.Ordinal);
        }
    }

    // -----------------------------------------------------------------------------------------
    // E. Install-origin boundary
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void DisplayName_IsNotLegacyInstallOriginEvidence()
    {
        // Only an onboarding-capable build ever writes display_name, so its presence can never
        // prove that a pre-onboarding KnownFirst build already ran on this device.
        Assert.DoesNotContain(
            DisplayNameKey,
            InstallOriginClassifier.LegacyPreferenceEvidenceKeys,
            "display_name must never be treated as legacy pre-onboarding preference evidence.");
    }

    // -----------------------------------------------------------------------------------------
    // F. Portable-archive / data-safety boundary
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void BackupModels_ExposeNoDisplayNameMember()
    {
        var backupTypes = typeof(KnownFirst.Models.Backup.BackupSourcePlatform).Assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                && type.Namespace.StartsWith("KnownFirst.Models.Backup", StringComparison.Ordinal))
            .ToArray();

        Assert.IsGreaterThan(0, backupTypes.Length, "The backup model types must be discoverable.");

        foreach (var type in backupTypes)
        {
            foreach (var member in type.GetMembers())
            {
                Assert.DoesNotContain(
                    "DisplayName",
                    member.Name,
                    StringComparison.Ordinal,
                    $"Backup type {type.Name} must not transport a Display Name ({member.Name}).");
            }
        }
    }

    [TestMethod]
    public void DataSafetyServices_CannotReachThePreferenceOrDisplayNameLayer()
    {
        // Archives are database-only. No data-safety type may take IPreferences or the Display
        // Name store, so a portable archive structurally cannot carry device-local preferences.
        var dataSafetyTypes = typeof(KnownFirst.Services.DataSafety.BackupService).Assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                && type.Namespace.StartsWith("KnownFirst.Services.DataSafety", StringComparison.Ordinal))
            .ToArray();

        Assert.IsGreaterThan(0, dataSafetyTypes.Length, "The data-safety types must be discoverable.");

        foreach (var type in dataSafetyTypes)
        {
            foreach (var constructor in type.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    var typeName = parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                    Assert.DoesNotContain("IPreferences", typeName, StringComparison.Ordinal);
                    Assert.DoesNotContain("DisplayName", typeName, StringComparison.Ordinal);
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // G. Startup registration
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void Startup_RegistersTheDisplayNameStoreAsASingleton()
    {
        var startup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", "MauiProgram.cs"));

        Assert.Contains("IDisplayNameStore", startup);
        Assert.Contains("MauiDisplayNameStore", startup);
        Assert.Contains("AddSingleton<KnownFirst.Services.Settings.IDisplayNameStore", startup);
    }

    private sealed class NoOpThemePreferenceReset : IThemePreferenceReset
    {
        public void ResetPreference()
        {
        }
    }

    private sealed class NoOpLanguageSelection : KnownFirst.Core.Language.ILanguageSelectionService
    {
        public event EventHandler? UiLanguageChanged;

        public string CurrentUiLanguage => "en";

        public bool IsSystemPreferenceActive => true;

        public IReadOnlyList<string> SupportedUiLanguages { get; } = ["en", "de", "ru"];

        public void Initialize() => throw new NotSupportedException();

        public void SetUiLanguage(string languageCode) => UiLanguageChanged?.Invoke(this, EventArgs.Empty);

        public void ResetToDeviceLanguage() => UiLanguageChanged?.Invoke(this, EventArgs.Empty);

        public void ReapplyCurrentCulture()
        {
        }
    }
}
