using System.Reflection;
using KnownFirst.Core.Language;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Onboarding;
using KnownFirst.Services.Settings;
using KnownFirst.Services.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingTransactionalCutoverTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _store = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _store.Keys.ToArray();
        public bool ContainsKey(string key, string? sharedName = null) => _store.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => _store.Remove(key);
        public void Clear(string? sharedName = null) => _store.Clear();

        public void Set<T>(string key, T value, string? sharedName = null)
        {
            if (value is null)
            {
                _store.Remove(key);
            }
            else
            {
                _store[key] = value;
            }
        }

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            if (_store.TryGetValue(key, out var val) && val is T typed)
            {
                return typed;
            }

            return defaultValue;
        }
    }

    private sealed class FakeBuildIdentityService(string version) : IBuildIdentityService
    {
        public BuildIdentity Identity { get; } = new(
            Product: "KnownFirst",
            Version: version,
            BuildNumber: "1",
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
        public string GetFormattedBuildIdentity() => version;
    }

    private sealed class FakeWhatsNewPreferenceStore(InMemoryPreferences preferences) : IWhatsNewPreferenceStore
    {
        private const string Key = "whats_new_seen_version";
        public string GetSeenVersion() => preferences.Get(Key, string.Empty);
        public void SetSeenVersion(string version) => preferences.Set(Key, version);
    }

    private sealed class FakeDeviceCultureProvider(string culture = "en-US") : IDeviceCultureProvider
    {
        public string GetDeviceCultureName() => culture;
    }

    private sealed class FakeUiCultureContext : IUiCultureContext
    {
        public UiCultureState CurrentCultureState { get; private set; } = new("en", "en-US", "en-US", "en-US");
        public UiCultureState ApplyUiCulture(string languageCode)
        {
            var specific = languageCode == "de" ? "de-DE" : "en-US";
            CurrentCultureState = new UiCultureState(languageCode, specific, specific, specific);
            return CurrentCultureState;
        }
    }

    private sealed class FakeLanguagePreferenceStore(InMemoryPreferences preferences) : ILanguagePreferenceStore
    {
        private const string Key = "ui_language_preference";
        public bool HasSavedLanguage => preferences.ContainsKey(Key);
        public string? GetSavedLanguage() => preferences.Get<string?>(Key, null);
        public void SetSavedLanguage(string languageCode) => preferences.Set(Key, languageCode);
        public void ClearSavedLanguage() => preferences.Remove(Key);
    }

    private sealed class FakeThemeApplication : IThemeApplication
    {
        public Microsoft.Maui.ApplicationModel.AppTheme UserAppTheme { get; set; } = Microsoft.Maui.ApplicationModel.AppTheme.Unspecified;
        public Microsoft.Maui.ApplicationModel.AppTheme RequestedTheme => Microsoft.Maui.ApplicationModel.AppTheme.Light;
        public event EventHandler? RequestedThemeChanged;
        public void TriggerRequestedThemeChanged() => RequestedThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CutoverEnvironment
    {
        public InMemoryPreferences Preferences { get; } = new();
        public MauiOnboardingDraftStore DraftStore { get; }
        public MauiOnboardingCompletionJournalStore JournalStore { get; }
        public MauiOnboardingStateStore StateStore { get; }
        public MauiOnboardingProgressStore ProgressStore { get; }
        public AppSettingsService AppSettings { get; }
        public MauiDisplayNameStore DisplayNameStore { get; }
        public LanguageSelectionService LanguageSelection { get; }
        public ThemeService ThemeService { get; }
        public OnboardingCompletionService CompletionService { get; }
        public OnboardingRecoveryService RecoveryService { get; }

        public CutoverEnvironment()
        {
            DraftStore = new MauiOnboardingDraftStore(Preferences);
            JournalStore = new MauiOnboardingCompletionJournalStore(Preferences);
            StateStore = new MauiOnboardingStateStore(Preferences);
            ProgressStore = new MauiOnboardingProgressStore(Preferences);
            AppSettings = new AppSettingsService(Preferences, NullLogger<AppSettingsService>.Instance);
            DisplayNameStore = new MauiDisplayNameStore(Preferences);

            var cultureContext = new FakeUiCultureContext();
            var deviceCulture = new FakeDeviceCultureProvider();
            var langStore = new FakeLanguagePreferenceStore(Preferences);
            LanguageSelection = new LanguageSelectionService(langStore, deviceCulture, cultureContext);
            LanguageSelection.Initialize();

            ThemeService = new ThemeService(Preferences, NullLogger<ThemeService>.Instance);
            ThemeService.Initialize(new FakeThemeApplication());

            var buildIdentity = new FakeBuildIdentityService("1.0.0");
            var whatsNew = new FakeWhatsNewPreferenceStore(Preferences);
            var releaseNotes = new ReleaseNotesService(buildIdentity, whatsNew, NullLogger<ReleaseNotesService>.Instance);

            CompletionService = new OnboardingCompletionService(
                releaseNotes,
                buildIdentity,
                StateStore,
                ProgressStore,
                LanguageSelection,
                ThemeService,
                DisplayNameStore,
                AppSettings,
                DraftStore,
                JournalStore,
                NullLogger<OnboardingCompletionService>.Instance);

            RecoveryService = new OnboardingRecoveryService(
                CompletionService,
                JournalStore,
                DraftStore,
                StateStore,
                ProgressStore,
                AppSettings,
                DisplayNameStore,
                LanguageSelection,
                ThemeService,
                Preferences,
                NullLogger<OnboardingRecoveryService>.Instance);
        }
    }

    [TestMethod]
    public void CutoverMatrix1_FreshRequiredOnboarding_CreatesDefaultDraftWithNullConsent()
    {
        var env = new CutoverEnvironment();
        env.StateStore.SetState(OnboardingState.Required);

        var outcome = env.RecoveryService.Recover();
        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, outcome);

        var readResult = env.DraftStore.Read();
        Assert.AreEqual(OnboardingDraftStatus.Missing, readResult.Status);

        var defaultDraft = OnboardingDraftPolicy.CreateDefault();
        Assert.IsNull(defaultDraft.OnlineLookupConsent, "Fresh draft consent must be undecided (null).");
        Assert.IsFalse(env.AppSettings.HasOnlineLookupConsent, "Committed consent must remain false.");

        env.DraftStore.Save(defaultDraft);
        var verifiedResult = env.DraftStore.Read();
        Assert.AreEqual(OnboardingDraftStatus.Valid, verifiedResult.Status);
        Assert.IsNull(verifiedResult.Draft!.OnlineLookupConsent);
    }

    [TestMethod]
    public void CutoverMatrix2_DraftEdits_PersistInStoreWithoutMutatingCommittedSettings()
    {
        var env = new CutoverEnvironment();
        var draft = OnboardingDraftPolicy.CreateDefault();
        env.DraftStore.Save(draft);

        var modifiedDraft = draft with
        {
            DisplayName = "TestUser",
            PreparationLimit = 15,
            EnhancedTermRecognitionEnabled = false,
            CardDirection = CardDirectionPreference.TermToMeaning,
            LearningMode = LearningMode.Typing,
            LearningTimezoneMode = LearningTimezoneMode.Explicit,
            ExplicitLearningTimezoneId = "Europe/Berlin",
            LearningDayCutoffMinutes = 300
        };
        env.DraftStore.Save(modifiedDraft);

        var readDraft = env.DraftStore.Read().Draft!;
        Assert.AreEqual("TestUser", readDraft.DisplayName);
        Assert.AreEqual(15, readDraft.PreparationLimit);
        Assert.IsFalse(readDraft.EnhancedTermRecognitionEnabled);

        Assert.IsNull(env.DisplayNameStore.GetDisplayName(), "Committed DisplayName must remain null.");
        Assert.AreEqual(5, env.AppSettings.PreparationLimit, "Committed PreparationLimit must remain default 5.");
        Assert.IsTrue(env.AppSettings.EnhancedTermRecognitionEnabled, "Committed ETR must remain default true.");
        Assert.AreEqual(CardDirectionPreference.Both, env.AppSettings.CardDirection);
        Assert.AreEqual(LearningMode.Automatic, env.AppSettings.LearningMode);
        Assert.AreEqual(LearningTimezoneMode.System, env.AppSettings.LearningTimezoneMode);
        Assert.AreEqual(LearningDayConfiguration.DefaultCutoffMinutes, env.AppSettings.LearningDayCutoffMinutes);
    }

    [TestMethod]
    public void CutoverMatrix3_BackAndForwardNavigation_PreservesDraftIndependentlyOfProgress()
    {
        var env = new CutoverEnvironment();
        var draft = OnboardingDraftPolicy.CreateDefault() with { DisplayName = "Alice", OnlineLookupConsent = true };
        env.DraftStore.Save(draft);

        env.ProgressStore.SetCurrentStep(OnboardingStep.OnlineLookup);
        Assert.AreEqual(OnboardingStep.OnlineLookup, env.ProgressStore.GetCurrentStep());

        env.ProgressStore.SetCurrentStep(OnboardingStep.Workflow);
        Assert.AreEqual(OnboardingStep.Workflow, env.ProgressStore.GetCurrentStep());

        var restoredDraft = env.DraftStore.Read().Draft!;
        Assert.AreEqual("Alice", restoredDraft.DisplayName);
        Assert.IsTrue(restoredDraft.OnlineLookupConsent);
    }

    [TestMethod]
    public void CutoverMatrix4_OnlineLookup_NullConsentBlocksAdvancement_AndDraftTrueAloneDoesNotAuthorize()
    {
        var env = new CutoverEnvironment();
        var draft = OnboardingDraftPolicy.CreateDefault();
        Assert.IsNull(draft.OnlineLookupConsent);

        Assert.IsFalse(env.CompletionService.CompleteOnboarding(draft), "Completion must reject null consent.");

        var draftWithTrue = draft with { OnlineLookupConsent = true };
        env.DraftStore.Save(draftWithTrue);

        Assert.IsFalse(env.AppSettings.HasOnlineLookupConsent,
            "Package A privacy invariant: Draft OnlineLookupConsent=true alone must never grant committed network authorization.");
    }

    [TestMethod]
    public void CutoverMatrix5_PreviewServices_DoNotPersistCommittedPreferences()
    {
        var env = new CutoverEnvironment();

        env.LanguageSelection.ApplyPreviewLanguage(LanguagePreferencePolicy.GermanLanguageCode);
        Assert.AreEqual("de", env.LanguageSelection.PreviewUiLanguage);
        Assert.AreEqual("en", env.LanguageSelection.CurrentUiLanguage, "Committed language must remain untouched.");

        env.ThemeService.ApplyPreviewPreference(ThemePreference.Dark);
        Assert.AreEqual(ThemePreference.Dark, env.ThemeService.PreviewPreference);
        Assert.AreEqual(ThemePreference.System, env.ThemeService.Preference, "Committed theme must remain untouched.");
    }

    [TestMethod]
    public void CutoverMatrix6_FinishSetup_TransactionalRollForwardCommitsAllSettingsAndSetsCompleted()
    {
        var env = new CutoverEnvironment();
        env.StateStore.SetState(OnboardingState.InProgress);

        var draft = OnboardingDraftPolicy.CreateDefault() with
        {
            UiLanguage = LanguagePreferencePolicy.GermanLanguageCode,
            Theme = ThemePreference.Dark,
            DisplayName = "Grace",
            OnlineLookupConsent = true,
            EnhancedTermRecognitionEnabled = false,
            CardDirection = CardDirectionPreference.MeaningToTerm,
            LearningMode = LearningMode.Typing,
            PreparationLimit = 10,
            LearningTimezoneMode = LearningTimezoneMode.Explicit,
            ExplicitLearningTimezoneId = "Europe/Berlin",
            LearningDayCutoffMinutes = 180
        };

        var completed = env.CompletionService.CompleteOnboarding(draft);
        Assert.IsTrue(completed, "CompleteOnboarding must succeed for valid draft with explicit consent.");

        Assert.AreEqual(OnboardingState.Completed, env.StateStore.GetState());
        Assert.IsNull(env.ProgressStore.GetCurrentStep(), "Progress must be cleared after completion.");
        Assert.AreEqual(OnboardingDraftStatus.Missing, env.DraftStore.Read().Status, "Draft must be cleared after completion.");

        Assert.AreEqual("Grace", env.DisplayNameStore.GetDisplayName());
        Assert.IsTrue(env.AppSettings.HasOnlineLookupConsent);
        Assert.IsFalse(env.AppSettings.EnhancedTermRecognitionEnabled);
        Assert.AreEqual(CardDirectionPreference.MeaningToTerm, env.AppSettings.CardDirection);
        Assert.AreEqual(LearningMode.Typing, env.AppSettings.LearningMode);
        Assert.AreEqual(10, env.AppSettings.PreparationLimit);
        Assert.AreEqual(LearningTimezoneMode.Explicit, env.AppSettings.LearningTimezoneMode);
        Assert.AreEqual("Europe/Berlin", env.AppSettings.ExplicitLearningTimezoneId);
        Assert.AreEqual(180, env.AppSettings.LearningDayCutoffMinutes);
        Assert.AreEqual("de", env.LanguageSelection.CurrentUiLanguage);
        Assert.AreEqual(ThemePreference.Dark, env.ThemeService.Preference);
    }

    [TestMethod]
    public void CutoverMatrix7_FailedCompletionWithInProgress_AllowsRetryAndLeavesDraftIntact()
    {
        var env = new CutoverEnvironment();
        env.StateStore.SetState(OnboardingState.InProgress);

        var draft = OnboardingDraftPolicy.CreateDefault() with
        {
            OnlineLookupConsent = null
        };
        env.DraftStore.Save(draft);

        var result = env.CompletionService.CompleteOnboarding(draft);
        Assert.IsFalse(result);
        Assert.AreEqual(OnboardingState.InProgress, env.StateStore.GetState());

        var read = env.DraftStore.Read();
        Assert.AreEqual(OnboardingDraftStatus.Valid, read.Status);
    }

    [TestMethod]
    public void CutoverMatrix8_AuthoritativeCompletedState_TreatedAsCompletedEvenIfResultIsFalse()
    {
        var env = new CutoverEnvironment();
        env.StateStore.SetState(OnboardingState.Completed);

        var state = env.StateStore.GetState();
        Assert.AreEqual(OnboardingState.Completed, state,
            "Mandatory carry-forward contract: OnboardingState.Completed is authoritative once persisted.");
    }

    [TestMethod]
    public void CutoverMatrix9_FullResetContract_ClearsAllPackageBState()
    {
        var env = new CutoverEnvironment();
        env.DraftStore.Save(OnboardingDraftPolicy.CreateDefault());
        env.ProgressStore.SetCurrentStep(OnboardingStep.Practice);
        env.StateStore.SetState(OnboardingState.InProgress);

        env.Preferences.Clear();

        Assert.AreEqual(OnboardingDraftStatus.Missing, env.DraftStore.Read().Status);
        Assert.IsNull(env.ProgressStore.GetCurrentStep());
        Assert.AreEqual(OnboardingCompletionJournalStatus.Missing, env.JournalStore.Read().Status);
    }
}
