using KnownFirst.Core.Language;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

/// <summary>
/// Focused contract tests for the non-destructive "Restore default settings" action. The service
/// must restore application settings, theme preference, and language preference while never
/// touching the database, the full preference store, or any user learning content.
/// </summary>
[TestClass]
public sealed class SettingsDefaultsServiceTests
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
        public int ResetCount { get; private set; }

        public void ResetPreference() => ResetCount++;
    }

    private sealed class RecordingLanguageSelection : ILanguageSelectionService
    {
        public int ResetCount { get; private set; }

        public event EventHandler? UiLanguageChanged;

        public string CurrentUiLanguage { get; private set; } = "de";

        public bool IsSystemPreferenceActive { get; private set; }

        public IReadOnlyList<string> SupportedUiLanguages { get; } = ["en", "de", "ru"];

        public void Initialize() => throw new NotSupportedException();

        public void SetUiLanguage(string languageCode)
        {
            CurrentUiLanguage = languageCode;
            IsSystemPreferenceActive = false;
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetToDeviceLanguage()
        {
            ResetCount++;
            IsSystemPreferenceActive = true;
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ReapplyCurrentCulture()
        {
        }
    }

    private static (SettingsDefaultsService Service,
        AppSettingsService AppSettings,
        InMemoryPreferences Preferences,
        RecordingThemePreferenceReset Theme,
        RecordingLanguageSelection Language) CreateService()
    {
        var preferences = new InMemoryPreferences();
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var theme = new RecordingThemePreferenceReset();
        var language = new RecordingLanguageSelection();
        var service = new SettingsDefaultsService(
            appSettings,
            theme,
            language,
            NullLogger<SettingsDefaultsService>.Instance);

        return (service, appSettings, preferences, theme, language);
    }

    [TestMethod]
    public void RestoreDefaults_RestoresApplicationSettingsToTheirDefaults()
    {
        var (service, appSettings, _, _, _) = CreateService();

        appSettings.SetPreparationLimit(50);
        appSettings.SetCardDirection(CardDirectionPreference.MeaningToTerm);
        appSettings.SetLearningMode(LearningMode.Typing);
        appSettings.SetEnhancedTermRecognitionEnabled(false);
        appSettings.SetLearningTimezoneMode(LearningTimezoneMode.Explicit);
        appSettings.SetExplicitLearningTimezoneId("Asia/Tokyo");
        appSettings.SetLearningDayCutoffMinutes(457);

        service.RestoreDefaults();

        Assert.AreEqual(PreparationLimitPolicy.DefaultLimit, appSettings.PreparationLimit);
        Assert.AreEqual(CardDirectionPreferencePolicy.DefaultPreference, appSettings.CardDirection);
        Assert.AreEqual(LearningModePolicy.DefaultMode, appSettings.LearningMode);
        Assert.IsTrue(appSettings.EnhancedTermRecognitionEnabled);
        Assert.AreEqual(LearningTimezoneMode.System, appSettings.LearningTimezoneMode);
        Assert.IsNull(appSettings.ExplicitLearningTimezoneId);
        Assert.AreEqual(LearningDayConfiguration.DefaultCutoffMinutes, appSettings.LearningDayCutoffMinutes);
    }

    [TestMethod]
    public void RestoreDefaults_ResetsThemeAndLanguagePreferencesExactlyOnce()
    {
        var (service, _, _, theme, language) = CreateService();

        service.RestoreDefaults();

        Assert.AreEqual(1, theme.ResetCount);
        Assert.AreEqual(1, language.ResetCount);
        Assert.IsTrue(language.IsSystemPreferenceActive);
    }

    [TestMethod]
    public void RestoreDefaults_PreservesGrantedOnlineDictionaryConsent()
    {
        var (service, appSettings, preferences, _, _) = CreateService();

        appSettings.GrantOnlineLookupConsent();
        Assert.IsTrue(appSettings.HasOnlineLookupConsent);

        service.RestoreDefaults();

        Assert.IsTrue(
            appSettings.HasOnlineLookupConsent,
            "Restoring default settings must not withdraw a consent the user actively granted.");
        Assert.IsTrue(preferences.ContainsKey("online_lookup_consent"));
        Assert.IsTrue(preferences.Get("online_lookup_consent", false));
    }

    [TestMethod]
    public void RestoreDefaults_PreservesNotGrantedOnlineDictionaryConsent()
    {
        var (service, appSettings, preferences, _, _) = CreateService();

        Assert.IsFalse(appSettings.HasOnlineLookupConsent);

        service.RestoreDefaults();

        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
        Assert.IsFalse(preferences.ContainsKey("online_lookup_consent"));
    }

    [TestMethod]
    public void RestoreDefaults_PreservesConsentWhileStillRestoringEveryOtherDefault()
    {
        var (service, appSettings, _, _, _) = CreateService();

        appSettings.GrantOnlineLookupConsent();
        appSettings.SetPreparationLimit(50);
        appSettings.SetCardDirection(CardDirectionPreference.MeaningToTerm);
        appSettings.SetLearningMode(LearningMode.Typing);
        appSettings.SetEnhancedTermRecognitionEnabled(false);
        appSettings.SetLearningTimezoneMode(LearningTimezoneMode.Explicit);
        appSettings.SetExplicitLearningTimezoneId("Asia/Tokyo");
        appSettings.SetLearningDayCutoffMinutes(457);

        service.RestoreDefaults();

        Assert.IsTrue(appSettings.HasOnlineLookupConsent);
        Assert.AreEqual(PreparationLimitPolicy.DefaultLimit, appSettings.PreparationLimit);
        Assert.AreEqual(CardDirectionPreferencePolicy.DefaultPreference, appSettings.CardDirection);
        Assert.AreEqual(LearningModePolicy.DefaultMode, appSettings.LearningMode);
        Assert.IsTrue(appSettings.EnhancedTermRecognitionEnabled);
        Assert.AreEqual(LearningTimezoneMode.System, appSettings.LearningTimezoneMode);
        Assert.IsNull(appSettings.ExplicitLearningTimezoneId);
        Assert.AreEqual(LearningDayConfiguration.DefaultCutoffMinutes, appSettings.LearningDayCutoffMinutes);
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_LeavesOnlineDictionaryConsentRevoked()
    {
        var (service, appSettings, preferences, _, _) = CreateService();

        appSettings.GrantOnlineLookupConsent();
        Assert.IsTrue(appSettings.HasOnlineLookupConsent);

        service.RestoreDefaultsForFullReset();

        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
        Assert.IsFalse(preferences.ContainsKey("online_lookup_consent"));
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_IsDeterministicWhenConsentWasGrantedImmediatelyBefore()
    {
        // Mirrors the real destructive flow: the preference store is cleared first, which leaves the
        // already-loaded in-memory consent value stale. The full-reset path must not read it back.
        var (service, appSettings, preferences, _, _) = CreateService();

        appSettings.GrantOnlineLookupConsent();
        Assert.IsTrue(appSettings.HasOnlineLookupConsent);

        preferences.Clear();

        service.RestoreDefaultsForFullReset();

        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
        Assert.IsFalse(preferences.ContainsKey("online_lookup_consent"));
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_StillRestoresTheSameSharedDefaults()
    {
        var (service, appSettings, _, theme, language) = CreateService();

        appSettings.SetPreparationLimit(50);
        appSettings.SetCardDirection(CardDirectionPreference.MeaningToTerm);
        appSettings.SetLearningMode(LearningMode.Typing);
        appSettings.SetEnhancedTermRecognitionEnabled(false);
        appSettings.SetLearningTimezoneMode(LearningTimezoneMode.Explicit);
        appSettings.SetExplicitLearningTimezoneId("Asia/Tokyo");
        appSettings.SetLearningDayCutoffMinutes(457);

        service.RestoreDefaultsForFullReset();

        Assert.AreEqual(PreparationLimitPolicy.DefaultLimit, appSettings.PreparationLimit);
        Assert.AreEqual(CardDirectionPreferencePolicy.DefaultPreference, appSettings.CardDirection);
        Assert.AreEqual(LearningModePolicy.DefaultMode, appSettings.LearningMode);
        Assert.IsTrue(appSettings.EnhancedTermRecognitionEnabled);
        Assert.AreEqual(LearningTimezoneMode.System, appSettings.LearningTimezoneMode);
        Assert.IsNull(appSettings.ExplicitLearningTimezoneId);
        Assert.AreEqual(LearningDayConfiguration.DefaultCutoffMinutes, appSettings.LearningDayCutoffMinutes);
        Assert.AreEqual(1, theme.ResetCount);
        Assert.AreEqual(1, language.ResetCount);
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_DoesNotClearUnrelatedPreferenceEntriesItself()
    {
        // The destructive clearing of the whole preference store belongs to the full reset flow in
        // the Settings page, not to this service.
        var (service, _, preferences, _, _) = CreateService();

        preferences.Set("whats_new_last_seen_version", "1.0.0-beta.13");

        service.RestoreDefaultsForFullReset();

        Assert.IsTrue(preferences.ContainsKey("whats_new_last_seen_version"));
    }

    [TestMethod]
    public void RestoreDefaults_DoesNotClearUnrelatedPreferenceEntries()
    {
        var (service, appSettings, preferences, _, _) = CreateService();

        preferences.Set("whats_new_last_seen_version", "1.0.0-beta.13");
        preferences.Set("some_other_durable_marker", 42);
        appSettings.SetPreparationLimit(50);

        service.RestoreDefaults();

        Assert.IsTrue(preferences.ContainsKey("whats_new_last_seen_version"));
        Assert.AreEqual("1.0.0-beta.13", preferences.Get("whats_new_last_seen_version", string.Empty));
        Assert.IsTrue(preferences.ContainsKey("some_other_durable_marker"));
        Assert.AreEqual(42, preferences.Get("some_other_durable_marker", 0));
    }

    [TestMethod]
    public void SettingsDefaultsService_HasNoDatabaseOrPreferenceStoreDependency()
    {
        var constructors = typeof(SettingsDefaultsService).GetConstructors();
        Assert.HasCount(1, constructors);

        var parameterTypeNames = constructors[0]
            .GetParameters()
            .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name)
            .ToArray();

        foreach (var typeName in parameterTypeNames)
        {
            Assert.DoesNotContain("Database", typeName, StringComparison.Ordinal);
            Assert.DoesNotContain("IPreferences", typeName, StringComparison.Ordinal);
            Assert.DoesNotContain("SQLite", typeName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
