using KnownFirst.Core.Language;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Onboarding;
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

    /// <summary>
    /// Mirrors the real <see cref="LanguageSelectionService"/> closely enough for the ordering
    /// contract: ResetToDeviceLanguage persists the "system" language marker, which is exactly the
    /// legacy preference evidence a later install-origin classification would see.
    /// </summary>
    private sealed class RecordingLanguageSelection(InMemoryPreferences? preferences = null) : ILanguageSelectionService
    {
        public int ResetCount { get; private set; }

        public bool OnboardingMarkerExistedWhenLanguageMarkerWasRecreated { get; private set; }

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
            OnboardingMarkerExistedWhenLanguageMarkerWasRecreated =
                preferences?.ContainsKey(OnboardingStateKey) ?? false;
            preferences?.Set(LanguagePreferenceKey, "system");
            IsSystemPreferenceActive = true;
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ReapplyCurrentCulture()
        {
        }
    }

    private const string OnboardingStateKey = "onboarding_state";
    private const string LanguagePreferenceKey = "knownfirst.uiLanguage";

    private static (SettingsDefaultsService Service,
        AppSettingsService AppSettings,
        InMemoryPreferences Preferences,
        RecordingThemePreferenceReset Theme,
        RecordingLanguageSelection Language,
        MauiOnboardingStateStore Onboarding,
        MauiDisplayNameStore DisplayName) CreateService()
    {
        var preferences = new InMemoryPreferences();
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var theme = new RecordingThemePreferenceReset();
        var language = new RecordingLanguageSelection(preferences);
        var onboarding = new MauiOnboardingStateStore(preferences);
        var displayName = new MauiDisplayNameStore(preferences);
        var service = new SettingsDefaultsService(
            appSettings,
            theme,
            language,
            onboarding,
            NullLogger<SettingsDefaultsService>.Instance);

        return (service, appSettings, preferences, theme, language, onboarding, displayName);
    }

    [TestMethod]
    public void RestoreDefaults_RestoresApplicationSettingsToTheirDefaults()
    {
        var (service, appSettings, _, _, _, _, _) = CreateService();

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
        var (service, _, _, theme, language, _, _) = CreateService();

        service.RestoreDefaults();

        Assert.AreEqual(1, theme.ResetCount);
        Assert.AreEqual(1, language.ResetCount);
        Assert.IsTrue(language.IsSystemPreferenceActive);
    }

    [TestMethod]
    public void RestoreDefaults_PreservesGrantedOnlineDictionaryConsent()
    {
        var (service, appSettings, preferences, _, _, _, _) = CreateService();

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
        var (service, appSettings, preferences, _, _, _, _) = CreateService();

        Assert.IsFalse(appSettings.HasOnlineLookupConsent);

        service.RestoreDefaults();

        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
        Assert.IsFalse(preferences.ContainsKey("online_lookup_consent"));
    }

    [TestMethod]
    public void RestoreDefaults_PreservesConsentWhileStillRestoringEveryOtherDefault()
    {
        var (service, appSettings, _, _, _, _, _) = CreateService();

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
        var (service, appSettings, preferences, _, _, _, _) = CreateService();

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
        var (service, appSettings, preferences, _, _, _, _) = CreateService();

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
        var (service, appSettings, _, theme, language, _, _) = CreateService();

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
        var (service, _, preferences, _, _, _, _) = CreateService();

        preferences.Set("whats_new_last_seen_version", "1.0.0-beta.13");

        service.RestoreDefaultsForFullReset();

        Assert.IsTrue(preferences.ContainsKey("whats_new_last_seen_version"));
    }

    [TestMethod]
    public void RestoreDefaults_DoesNotClearUnrelatedPreferenceEntries()
    {
        var (service, appSettings, preferences, _, _, _, _) = CreateService();

        preferences.Set("whats_new_last_seen_version", "1.0.0-beta.13");
        preferences.Set("some_other_durable_marker", 42);
        appSettings.SetPreparationLimit(50);

        service.RestoreDefaults();

        Assert.IsTrue(preferences.ContainsKey("whats_new_last_seen_version"));
        Assert.AreEqual("1.0.0-beta.13", preferences.Get("whats_new_last_seen_version", string.Empty));
        Assert.IsTrue(preferences.ContainsKey("some_other_durable_marker"));
        Assert.AreEqual(42, preferences.Get("some_other_durable_marker", 0));
    }

    // -----------------------------------------------------------------------------------------
    // G. Full reset must establish onboarding Required before legacy evidence can be recreated
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void RestoreDefaultsForFullReset_EstablishesOnboardingRequired()
    {
        var (service, _, preferences, _, _, onboarding, _) = CreateService();

        onboarding.SetState(OnboardingState.Completed);

        service.RestoreDefaultsForFullReset();

        Assert.AreEqual(OnboardingState.Required, onboarding.GetState());
        Assert.AreEqual((int)OnboardingState.Required, preferences.Get(OnboardingStateKey, -1));
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_EstablishesOnboardingRequiredFromAClearedPreferenceStore()
    {
        // Mirrors the real destructive flow: Database.ResetAsync() then Preferences.Clear() then
        // this call. Nothing readable survives the clear, so the state must be written positively.
        var (service, appSettings, preferences, _, _, onboarding, _) = CreateService();

        appSettings.GrantOnlineLookupConsent();
        preferences.Clear();

        service.RestoreDefaultsForFullReset();

        Assert.AreEqual(OnboardingState.Required, onboarding.GetState());
        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_WritesOnboardingRequiredBeforeTheLanguageMarkerIsRecreated()
    {
        // The binding ordering contract. ResetToDeviceLanguage persists knownfirst.uiLanguage,
        // which is legacy preference evidence. If the onboarding marker were written after it, a
        // freshly reset installation would be grandfathered as pre-existing on the next start and
        // would silently never see onboarding again.
        var (service, _, preferences, _, language, _, _) = CreateService();

        preferences.Clear();

        service.RestoreDefaultsForFullReset();

        Assert.AreEqual(1, language.ResetCount);
        Assert.IsTrue(
            preferences.ContainsKey(LanguagePreferenceKey),
            "The recording double must reproduce the real system-language marker write.");
        Assert.IsTrue(
            language.OnboardingMarkerExistedWhenLanguageMarkerWasRecreated,
            "Onboarding state must already be persisted when the language marker is recreated.");
    }

    // -----------------------------------------------------------------------------------------
    // H. Restore Defaults never changes onboarding lifecycle state
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void RestoreDefaults_LeavesEveryOnboardingLifecycleStateUnchanged()
    {
        foreach (var persisted in new[] { OnboardingState.Required, OnboardingState.InProgress, OnboardingState.Completed })
        {
            var (service, _, _, _, _, onboarding, _) = CreateService();
            onboarding.SetState(persisted);

            service.RestoreDefaults();

            Assert.AreEqual(
                persisted,
                onboarding.GetState(),
                $"Restore Defaults must not change onboarding state {persisted}.");
        }
    }

    [TestMethod]
    public void RestoreDefaults_NeverIntroducesAnOnboardingMarkerWhereNoneExisted()
    {
        var (service, _, preferences, _, _, onboarding, _) = CreateService();

        Assert.IsNull(onboarding.GetState());

        service.RestoreDefaults();

        Assert.IsFalse(
            preferences.ContainsKey(OnboardingStateKey),
            "Restore Defaults must neither classify nor create onboarding state.");
    }

    [TestMethod]
    public void RestoreDefaults_KeepsOnboardingStateWhilePreservingGrantedConsent()
    {
        var (service, appSettings, _, _, _, onboarding, _) = CreateService();

        onboarding.SetState(OnboardingState.Completed);
        appSettings.GrantOnlineLookupConsent();

        service.RestoreDefaults();

        Assert.AreEqual(OnboardingState.Completed, onboarding.GetState());
        Assert.IsTrue(appSettings.HasOnlineLookupConsent);
    }

    [TestMethod]
    public void RestoreDefaults_KeepsOnboardingStateWhilePreservingAbsentConsent()
    {
        var (service, appSettings, _, _, _, onboarding, _) = CreateService();

        onboarding.SetState(OnboardingState.Completed);
        Assert.IsFalse(appSettings.HasOnlineLookupConsent);

        service.RestoreDefaults();

        Assert.AreEqual(OnboardingState.Completed, onboarding.GetState());
        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
    }

    // -----------------------------------------------------------------------------------------
    // I. Restore Defaults preserves the optional local Display Name
    // -----------------------------------------------------------------------------------------

    [TestMethod]
    public void RestoreDefaults_PreservesAnOptionalDisplayName()
    {
        // The Display Name lives in its own store precisely so the non-destructive restore cannot
        // erase it: AppSettingsService.Reset() only removes the keys it owns.
        var (service, _, preferences, _, _, _, displayName) = CreateService();

        displayName.SetDisplayName("Anna");

        service.RestoreDefaults();

        Assert.AreEqual(
            "Anna",
            displayName.GetDisplayName(),
            "Restore Defaults must not remove the user's optional local Display Name.");
        Assert.IsTrue(preferences.ContainsKey("display_name"));
    }

    [TestMethod]
    public void RestoreDefaults_LeavesAnAbsentDisplayNameAbsent()
    {
        var (service, _, preferences, _, _, _, displayName) = CreateService();

        Assert.IsNull(displayName.GetDisplayName());

        service.RestoreDefaults();

        Assert.IsNull(displayName.GetDisplayName());
        Assert.IsFalse(preferences.ContainsKey("display_name"));
    }

    [TestMethod]
    public void RestoreDefaults_PreservesTheDisplayNameWhileStillRestoringEveryOtherDefault()
    {
        var (service, appSettings, _, _, _, _, displayName) = CreateService();

        displayName.SetDisplayName("Anna");
        appSettings.SetPreparationLimit(50);
        appSettings.SetLearningMode(LearningMode.Typing);

        service.RestoreDefaults();

        Assert.AreEqual("Anna", displayName.GetDisplayName());
        Assert.AreEqual(PreparationLimitPolicy.DefaultLimit, appSettings.PreparationLimit);
        Assert.AreEqual(LearningModePolicy.DefaultMode, appSettings.LearningMode);
    }

    [TestMethod]
    public void RestoreDefaultsForFullReset_DoesNotItselfRemoveTheDisplayName()
    {
        // Clearing the preference store belongs to the destructive flow in the Settings page, not
        // to this service. The service must contain no Display Name-specific reset logic at all.
        var (service, _, _, _, _, _, displayName) = CreateService();

        displayName.SetDisplayName("Anna");

        service.RestoreDefaultsForFullReset();

        Assert.AreEqual("Anna", displayName.GetDisplayName());
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
