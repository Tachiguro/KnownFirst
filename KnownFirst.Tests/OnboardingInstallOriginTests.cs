using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Services;
using KnownFirst.Services.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

/// <summary>
/// Focused contract tests for the first-run onboarding state store and the install-origin
/// classifier. The binding data-integrity requirement is that a pre-existing installation
/// upgrading to an onboarding-capable build must never be classified as a fresh installation
/// merely because the newly introduced onboarding marker is absent.
/// </summary>
[TestClass]
public sealed class OnboardingInstallOriginTests
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

    private const string OnboardingStateKey = "onboarding_state";
    private const string PreparationLimitKey = "preparation_limit";

    /// <summary>
    /// The exact legacy preference evidence set verified during PLAN_ONLY. Every one of these keys
    /// can only exist because a pre-onboarding KnownFirst build already ran on this device.
    /// </summary>
    private static readonly string[] ExpectedLegacyEvidenceKeys =
    [
        "knownfirst.uiLanguage",
        "theme_preference",
        "whats_new_seen_version",
        "preparation_limit",
        "card_direction",
        "learning_mode",
        "online_lookup_consent",
        "enhanced_term_recognition_enabled",
        "learning_timezone_mode",
        "explicit_learning_timezone_id",
        "learning_day_cutoff_minutes"
    ];

    private static (InstallOriginClassifier Classifier, InMemoryPreferences Preferences, MauiOnboardingStateStore Store) CreateClassifier()
    {
        var preferences = new InMemoryPreferences();
        var store = new MauiOnboardingStateStore(preferences);
        var classifier = new InstallOriginClassifier(
            preferences,
            store,
            NullLogger<InstallOriginClassifier>.Instance);

        return (classifier, preferences, store);
    }

    // ---------------------------------------------------------------------------------------
    // A. Onboarding state persistence and normalization
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void OnboardingStatePolicy_NormalizesEverySupportedLifecycleState()
    {
        foreach (var expected in new[] { OnboardingState.Required, OnboardingState.InProgress, OnboardingState.Completed })
        {
            Assert.IsTrue(
                OnboardingStatePolicy.TryNormalize((int)expected, out var normalized),
                $"State {expected} must be a supported persisted value.");
            Assert.AreEqual(expected, normalized);
        }
    }

    [TestMethod]
    public void OnboardingStatePolicy_RejectsUnsupportedValuesInsteadOfSilentlyDefaulting()
    {
        // A silent default here would either re-show onboarding to an existing user or skip it for
        // a genuinely fresh installation. Unsupported values must be re-classified instead.
        foreach (var unsupported in new[] { int.MinValue, -1, 0, 4, 99, int.MaxValue })
        {
            Assert.IsFalse(
                OnboardingStatePolicy.TryNormalize(unsupported, out _),
                $"Value {unsupported} must not resolve to a lifecycle state.");
        }
    }

    [TestMethod]
    public void StateStore_ReturnsNullWhenNoMarkerHasEverBeenWritten()
    {
        var (_, preferences, store) = CreateClassifier();

        Assert.IsNull(store.GetState());
        Assert.IsFalse(preferences.ContainsKey(OnboardingStateKey));
    }

    [TestMethod]
    public void StateStore_RoundTripsEveryLifecycleStateThroughThePreferenceLayer()
    {
        var (_, preferences, store) = CreateClassifier();

        foreach (var expected in new[] { OnboardingState.Required, OnboardingState.InProgress, OnboardingState.Completed })
        {
            store.SetState(expected);

            Assert.IsTrue(preferences.ContainsKey(OnboardingStateKey));
            Assert.AreEqual((int)expected, preferences.Get(OnboardingStateKey, -1));
            Assert.AreEqual(expected, store.GetState());
        }
    }

    [TestMethod]
    public void StateStore_TreatsAnUnreadableStoredValueAsUnclassified()
    {
        var (_, preferences, store) = CreateClassifier();

        preferences.Set(OnboardingStateKey, 4711);

        Assert.IsNull(store.GetState());
    }

    // ---------------------------------------------------------------------------------------
    // B. Fresh installation
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Classify_WithNoMarkerAndNoLegacyEvidence_PersistsRequired()
    {
        var (classifier, preferences, store) = CreateClassifier();

        var result = classifier.EnsureClassified();

        Assert.AreEqual(OnboardingState.Required, result);
        Assert.AreEqual(OnboardingState.Required, store.GetState());
        Assert.AreEqual((int)OnboardingState.Required, preferences.Get(OnboardingStateKey, -1));
    }

    [TestMethod]
    public void Classify_FreshInstallation_DoesNotPinTheLegacyPreparationLimit()
    {
        var (classifier, preferences, _) = CreateClassifier();

        classifier.EnsureClassified();

        Assert.IsFalse(
            preferences.ContainsKey(PreparationLimitKey),
            "A fresh installation must adopt the current default, not a legacy compatibility pin.");
    }

    // ---------------------------------------------------------------------------------------
    // C. Existing installation — every representative legacy-evidence path
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Classify_WithAnyIndividualLegacyEvidenceKey_PersistsCompleted()
    {
        foreach (var evidenceKey in ExpectedLegacyEvidenceKeys)
        {
            var (classifier, preferences, store) = CreateClassifier();
            preferences.Set(evidenceKey, "legacy-evidence");

            var result = classifier.EnsureClassified();

            Assert.AreEqual(
                OnboardingState.Completed,
                result,
                $"Legacy evidence key '{evidenceKey}' must grandfather the installation as existing.");
            Assert.AreEqual(OnboardingState.Completed, store.GetState());
        }
    }

    [TestMethod]
    public void Classifier_UsesExactlyTheVerifiedLegacyPreferenceEvidenceSet()
    {
        CollectionAssert.AreEquivalent(
            ExpectedLegacyEvidenceKeys,
            InstallOriginClassifier.LegacyPreferenceEvidenceKeys.ToArray());
    }

    [TestMethod]
    public void ClassifierEvidenceSet_StaysAlignedWithTheKeysApplicationSettingsActuallyWrites()
    {
        // Drift guard: if AppSettingsService ever renames one of its preference keys, the evidence
        // set would silently stop detecting a real existing installation.
        var preferences = new InMemoryPreferences();
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        appSettings.SetPreparationLimit(20);
        appSettings.SetCardDirection(CardDirectionPreference.MeaningToTerm);
        appSettings.SetLearningMode(LearningMode.Typing);
        appSettings.GrantOnlineLookupConsent();
        appSettings.SetEnhancedTermRecognitionEnabled(false);
        appSettings.SetLearningTimezoneMode(LearningTimezoneMode.Explicit);
        appSettings.SetExplicitLearningTimezoneId("Asia/Tokyo");
        appSettings.SetLearningDayCutoffMinutes(457);

        foreach (var writtenKey in preferences.Keys)
        {
            Assert.Contains(
                writtenKey,
                InstallOriginClassifier.LegacyPreferenceEvidenceKeys.ToArray(),
                $"Preference key '{writtenKey}' is written by AppSettingsService but is not legacy evidence.");
        }
    }

    // ---------------------------------------------------------------------------------------
    // D. Idempotence — an already classified installation is never reclassified
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Classify_WithAnExistingMarker_IsRespectedAndNeverReclassified()
    {
        foreach (var persisted in new[] { OnboardingState.Required, OnboardingState.InProgress, OnboardingState.Completed })
        {
            var (classifier, preferences, store) = CreateClassifier();
            store.SetState(persisted);

            // Legacy evidence that would otherwise force Completed must not override the marker.
            preferences.Set("knownfirst.uiLanguage", "system");

            var result = classifier.EnsureClassified();

            Assert.AreEqual(persisted, result);
            Assert.AreEqual(persisted, store.GetState());
        }
    }

    [TestMethod]
    public void Classify_RepeatedInvocation_DoesNotChangeAnAlreadyClassifiedInstallation()
    {
        var (classifier, preferences, store) = CreateClassifier();

        Assert.AreEqual(OnboardingState.Required, classifier.EnsureClassified());

        // A later start finds the marker it wrote and must leave it exactly as it is.
        Assert.AreEqual(OnboardingState.Required, classifier.EnsureClassified());
        Assert.AreEqual(OnboardingState.Required, store.GetState());
        Assert.IsFalse(preferences.ContainsKey(PreparationLimitKey));
    }

    // ---------------------------------------------------------------------------------------
    // E. Existing-install compatibility pin
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Classify_ExistingInstallationWithoutPreparationLimit_PinsTheLegacyEffectiveDefault()
    {
        var (classifier, preferences, _) = CreateClassifier();
        preferences.Set("knownfirst.uiLanguage", "system");

        classifier.EnsureClassified();

        Assert.IsTrue(preferences.ContainsKey(PreparationLimitKey));
        Assert.AreEqual(10, preferences.Get(PreparationLimitKey, -1));
    }

    [TestMethod]
    public void Classify_ExistingInstallationWithPersistedPreparationLimit_PreservesTheExactValue()
    {
        foreach (var persistedLimit in new[] { 5, 10, 20, 30, 50 })
        {
            var (classifier, preferences, _) = CreateClassifier();
            preferences.Set("knownfirst.uiLanguage", "system");
            preferences.Set(PreparationLimitKey, persistedLimit);

            classifier.EnsureClassified();

            Assert.AreEqual(
                persistedLimit,
                preferences.Get(PreparationLimitKey, -1),
                $"A persisted preparation limit of {persistedLimit} must never be overwritten.");
        }
    }

    [TestMethod]
    public void CompatibilityPin_IsAFrozenLegacyValueIndependentOfTheCurrentPolicyDefault()
    {
        // The pin deliberately freezes the legacy effective default so a future change to
        // PreparationLimitPolicy.DefaultLimit cannot silently move an existing user's budget.
        // Read through a local so the guard survives as a real assertion rather than being folded
        // away as a compile-time constant comparison.
        int pinnedLegacyLimit = InstallOriginClassifier.LegacyEffectivePreparationLimit;

        Assert.AreEqual(10, pinnedLegacyLimit);
    }

    // ---------------------------------------------------------------------------------------
    // F. Startup ordering (source contract)
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Startup_ClassifiesInstallOriginBeforeTheFirstLegacyPreferenceWriter()
    {
        var startup = LoadStartupArtifact("MauiProgram.cs");

        var classifierIndex = startup.IndexOf(
            "GetRequiredService<KnownFirst.Services.Onboarding.IInstallOriginClassifier>().EnsureClassified()",
            StringComparison.Ordinal);
        var languageIndex = startup.IndexOf(
            "GetRequiredService<ILanguageSelectionService>().Initialize()",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(
            0,
            classifierIndex,
            "Startup must classify install origin exactly once.");
        Assert.IsGreaterThanOrEqualTo(0, languageIndex);
        Assert.IsLessThan(
            languageIndex,
            classifierIndex,
            "Install-origin classification must run before ILanguageSelectionService.Initialize() persists the system language marker.");
    }

    [TestMethod]
    public void Startup_RegistersTheOnboardingStateStoreAndInstallOriginClassifier()
    {
        var startup = LoadStartupArtifact("MauiProgram.cs");

        Assert.Contains(
            "AddSingleton<KnownFirst.Services.Onboarding.IOnboardingStateStore, KnownFirst.Services.Onboarding.MauiOnboardingStateStore>()",
            startup);
        Assert.Contains(
            "AddSingleton<KnownFirst.Services.Onboarding.IInstallOriginClassifier, KnownFirst.Services.Onboarding.InstallOriginClassifier>()",
            startup);
    }

    // ---------------------------------------------------------------------------------------
    // I. Schema boundary — this package introduces no database migration
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void OnboardingFoundation_IntroducesNoDatabaseSchemaMigration()
    {
        int currentSchemaVersion = DatabaseSchema.CurrentVersion;

        Assert.AreEqual(
            12,
            currentSchemaVersion,
            "Onboarding state is preference-level application state and must not move the database schema.");
    }

    private static string LoadStartupArtifact(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", fileName));
}
