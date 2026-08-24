using KnownFirst.Core.Language;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Onboarding;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PreparationLimitCompatibilityTests
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

    private sealed class RecordingThemePreferenceReset : IThemePreferenceReset
    {
        public void ResetPreference() { }
    }

    private sealed class RecordingLanguageSelection(InMemoryPreferences preferences) : ILanguageSelectionService
    {
        public event EventHandler? UiLanguageChanged;
        public string CurrentUiLanguage => "en";
        public bool IsSystemPreferenceActive => true;
        public IReadOnlyList<string> SupportedUiLanguages => ["en", "de", "ru"];
        public void Initialize() { }
        public void SetUiLanguage(string languageCode) { }
        public void ResetToDeviceLanguage()
        {
            preferences.Set("knownfirst.uiLanguage", "system");
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        public void ReapplyCurrentCulture() { }
    }

    private const string PreparationLimitKey = "preparation_limit";

    private static (InstallOriginClassifier Classifier, InMemoryPreferences Preferences, MauiOnboardingStateStore Onboarding, MauiDisplayNameStore DisplayName) CreateEnvironment()
    {
        var preferences = new InMemoryPreferences();
        var onboarding = new MauiOnboardingStateStore(preferences);
        var displayName = new MauiDisplayNameStore(preferences);
        var classifier = new InstallOriginClassifier(
            preferences,
            onboarding,
            NullLogger<InstallOriginClassifier>.Instance);

        return (classifier, preferences, onboarding, displayName);
    }

    [TestMethod]
    public void Scenario1_DirectUpgradeFromPreSlice1_GrandfathersAndPinsTen()
    {
        var (classifier, preferences, onboarding, _) = CreateEnvironment();
        preferences.Set("knownfirst.uiLanguage", "de");

        var state = classifier.EnsureClassified();

        Assert.AreEqual(OnboardingState.Completed, state);
        Assert.AreEqual(OnboardingState.Completed, onboarding.GetState());
        Assert.IsTrue(preferences.ContainsKey(PreparationLimitKey));
        Assert.AreEqual(10, preferences.Get(PreparationLimitKey, -1));

        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        Assert.AreEqual(10, appSettings.PreparationLimit);
    }

    [TestMethod]
    public void Scenario2_ExistingInstallationWithMissingLimit_AcrossVariousLegacyKeys_PinsTen()
    {
        string[] sampleLegacyKeys =
        [
            "theme_preference",
            "card_direction",
            "learning_mode",
            "online_lookup_consent",
            "enhanced_term_recognition_enabled",
            "learning_timezone_mode",
            "explicit_learning_timezone_id",
            "learning_day_cutoff_minutes"
        ];

        foreach (var key in sampleLegacyKeys)
        {
            var (classifier, preferences, onboarding, _) = CreateEnvironment();
            preferences.Set(key, "marker");

            var state = classifier.EnsureClassified();

            Assert.AreEqual(OnboardingState.Completed, state);
            Assert.AreEqual(10, preferences.Get(PreparationLimitKey, -1), $"Legacy key '{key}' must trigger 10 pin.");

            var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
            Assert.AreEqual(10, appSettings.PreparationLimit);
        }
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(10)]
    [DataRow(20)]
    [DataRow(25)]
    [DataRow(30)]
    [DataRow(50)]
    public void Scenario3_ExistingExplicitValues_PreservesValueWithoutRewrite(int explicitLimit)
    {
        var (classifier, preferences, onboarding, _) = CreateEnvironment();
        preferences.Set("knownfirst.uiLanguage", "de");
        preferences.Set(PreparationLimitKey, explicitLimit);

        var state = classifier.EnsureClassified();

        Assert.AreEqual(OnboardingState.Completed, state);
        Assert.AreEqual(explicitLimit, preferences.Get(PreparationLimitKey, -1));

        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        Assert.AreEqual(explicitLimit, appSettings.PreparationLimit);
        Assert.AreEqual(explicitLimit, preferences.Get(PreparationLimitKey, -1), "Reading custom value must not rewrite stored preference.");
    }

    [TestMethod]
    public void Scenario4_GenuineFreshInstall_ClassifiesRequiredAndDoesNotPin_EffectiveLimitIsFive()
    {
        var (classifier, preferences, onboarding, _) = CreateEnvironment();

        var state = classifier.EnsureClassified();

        Assert.AreEqual(OnboardingState.Required, state);
        Assert.AreEqual(OnboardingState.Required, onboarding.GetState());
        Assert.IsFalse(preferences.ContainsKey(PreparationLimitKey), "Fresh install must not write legacy pin.");

        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        Assert.AreEqual(5, appSettings.PreparationLimit);
    }

    [TestMethod]
    public void Scenario5_FullDestructiveReset_EstablishesRequiredAndEffectiveLimitIsFive()
    {
        var (classifier, preferences, onboarding, displayName) = CreateEnvironment();
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var defaultsService = new SettingsDefaultsService(
            appSettings,
            new RecordingThemePreferenceReset(),
            new RecordingLanguageSelection(preferences),
            onboarding,
            NullLogger<SettingsDefaultsService>.Instance);

        preferences.Clear();
        defaultsService.RestoreDefaultsForFullReset();

        Assert.AreEqual(OnboardingState.Required, onboarding.GetState());
        Assert.IsFalse(preferences.ContainsKey(PreparationLimitKey));
        Assert.AreEqual(5, appSettings.PreparationLimit);

        var nextStartupState = classifier.EnsureClassified();
        Assert.AreEqual(OnboardingState.Required, nextStartupState);
        Assert.IsFalse(preferences.ContainsKey(PreparationLimitKey), "Subsequent startup must not repin to legacy 10 after full reset.");
        Assert.AreEqual(5, appSettings.PreparationLimit);
    }

    [TestMethod]
    public void Scenario6_RestoreDefaultSettings_ResetsCustomLimitToFive_PreservesCompletedAndDisplayName()
    {
        var (classifier, preferences, onboarding, displayName) = CreateEnvironment();
        onboarding.SetState(OnboardingState.Completed);
        displayName.SetDisplayName("Anna");
        preferences.Set(PreparationLimitKey, 20);

        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        appSettings.GrantOnlineLookupConsent();
        Assert.AreEqual(20, appSettings.PreparationLimit);

        var defaultsService = new SettingsDefaultsService(
            appSettings,
            new RecordingThemePreferenceReset(),
            new RecordingLanguageSelection(preferences),
            onboarding,
            NullLogger<SettingsDefaultsService>.Instance);

        defaultsService.RestoreDefaults();

        Assert.AreEqual(5, appSettings.PreparationLimit);
        Assert.AreEqual(OnboardingState.Completed, onboarding.GetState());
        Assert.IsTrue(appSettings.HasOnlineLookupConsent);
        Assert.AreEqual("Anna", displayName.GetDisplayName());
    }
}
