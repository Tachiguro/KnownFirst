using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Onboarding;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingCompletionTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _store = new(StringComparer.Ordinal);

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
            if (_store.TryGetValue(key, out var val))
            {
                return (T)val;
            }

            return defaultValue;
        }
    }

    private sealed class FakeBuildIdentityService(string version) : IBuildIdentityService
    {
        public BuildIdentity Identity { get; } = new(
            Product: "KnownFirst",
            Version: version,
            BuildNumber: "9",
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

    private sealed class FakeThemePreferenceReset : IThemePreferenceReset
    {
        public void ResetPreference() { }
    }

    private sealed class FakeLanguageSelection : KnownFirst.Core.Language.ILanguageSelectionService
    {
        public event EventHandler? UiLanguageChanged;
        public string CurrentUiLanguage => "en";
        public bool IsSystemPreferenceActive => true;
        public string? PreviewUiLanguage => null;
        public bool IsSystemPreviewActive => false;
        public IReadOnlyList<string> SupportedUiLanguages => ["en", "de", "ru"];
        public void Initialize() { }
        public void SetUiLanguage(string languageCode) { }
        public void ResetToDeviceLanguage() { }
        public void ReapplyCurrentCulture() { }
        public void ApplyPreviewLanguage(string languageCode) => throw new NotSupportedException();
        public void ClearPreview() => throw new NotSupportedException();
    }

    private sealed class RecordingReleaseNotesService(List<string> eventLog, Action? onMarkSeen = null) : IReleaseNotesService
    {
        public ReleaseNoteEntry? GetUnseenReleaseNotes() => null;
        public IReadOnlyList<ReleaseNoteEntry> GetReleaseNoteHistory() => [];
        public void MarkSeen(string version)
        {
            eventLog.Add($"MarkSeen:{version}");
            onMarkSeen?.Invoke();
        }
    }

    private sealed class RecordingOnboardingStateStore(List<string> eventLog, Action? onSetState = null) : IOnboardingStateStore
    {
        private OnboardingState? _state = OnboardingState.InProgress;
        public OnboardingState? GetState() => _state;
        public void SetState(OnboardingState state)
        {
            eventLog.Add($"SetState:{state}");
            _state = state;
            onSetState?.Invoke();
        }
    }

    private sealed class RecordingOnboardingProgressStore(List<string> eventLog) : IOnboardingProgressStore
    {
        private OnboardingStep? _step = OnboardingStep.Summary;
        public OnboardingStep? GetCurrentStep() => _step;
        public void SetCurrentStep(OnboardingStep step) => _step = step;
        public void ClearProgress()
        {
            eventLog.Add("ClearProgress");
            _step = null;
        }
    }

    [TestMethod]
    public void AllNineOnboardingSteps_ResumeCorrectlyFromPersistedState()
    {
        var preferences = new InMemoryPreferences();
        var progressStore = new MauiOnboardingProgressStore(preferences);

        foreach (var step in Enum.GetValues<OnboardingStep>())
        {
            progressStore.SetCurrentStep(step);
            var retrieved = progressStore.GetCurrentStep();

            Assert.IsNotNull(retrieved);
            Assert.AreEqual(step, retrieved.Value);
            Assert.IsTrue(OnboardingStepPolicy.TryNormalize((int)retrieved.Value, out var normalized));
            Assert.AreEqual(step, normalized);
        }
    }

    [TestMethod]
    public void InvalidStoredProgress_NormalizesToWelcomeLanguage()
    {
        var preferences = new InMemoryPreferences();
        var progressStore = new MauiOnboardingProgressStore(preferences);

        int[] invalidValues = [-99, 0, 10, 999];
        foreach (var val in invalidValues)
        {
            preferences.Set("onboarding_step", val);
            var step = progressStore.GetCurrentStep();
            var normalized = OnboardingStepPolicy.Normalize(val);
            Assert.AreEqual(OnboardingStep.WelcomeLanguage, normalized);
        }
    }

    [TestMethod]
    public void CompletionService_ExecutesOperationsInExactRequiredOrder()
    {
        var eventLog = new List<string>();
        var releaseNotes = new RecordingReleaseNotesService(eventLog);
        var buildIdentity = new FakeBuildIdentityService("1.0.0-beta.14");
        var stateStore = new RecordingOnboardingStateStore(eventLog);
        var progressStore = new RecordingOnboardingProgressStore(eventLog);
        var completionService = new OnboardingCompletionService(
            releaseNotes,
            buildIdentity,
            stateStore,
            progressStore,
            NullLogger<OnboardingCompletionService>.Instance);

        completionService.CompleteOnboarding();

        Assert.AreEqual(3, eventLog.Count);
        Assert.AreEqual("MarkSeen:1.0.0-beta.14", eventLog[0]);
        Assert.AreEqual("SetState:Completed", eventLog[1]);
        Assert.AreEqual("ClearProgress", eventLog[2]);
    }

    [TestMethod]
    public void CompletionService_PersistsSeenVersion_SetsStateCompleted_AndClearsProgress()
    {
        var preferences = new InMemoryPreferences();
        var stateStore = new MauiOnboardingStateStore(preferences);
        var progressStore = new MauiOnboardingProgressStore(preferences);
        var whatsNewStore = new FakeWhatsNewPreferenceStore(preferences);
        var buildIdentity = new FakeBuildIdentityService("1.0.0-beta.14");
        var releaseNotes = new ReleaseNotesService(
            buildIdentity,
            whatsNewStore,
            NullLogger<ReleaseNotesService>.Instance);
        var completionService = new OnboardingCompletionService(
            releaseNotes,
            buildIdentity,
            stateStore,
            progressStore,
            NullLogger<OnboardingCompletionService>.Instance);

        // Pre-condition: InProgress at Summary step
        stateStore.SetState(OnboardingState.InProgress);
        progressStore.SetCurrentStep(OnboardingStep.Summary);
        Assert.AreEqual(string.Empty, whatsNewStore.GetSeenVersion());

        // Invoke production completion service
        completionService.CompleteOnboarding();

        // Verification
        Assert.AreEqual("1.0.0-beta.14", whatsNewStore.GetSeenVersion());
        Assert.AreEqual(OnboardingState.Completed, stateStore.GetState());
        Assert.IsNull(progressStore.GetCurrentStep());
    }

    [TestMethod]
    public void CompletionService_WhenReleaseNotesThrows_FailsClosedWithoutCompletingStateOrClearingProgress()
    {
        var eventLog = new List<string>();
        var releaseNotes = new RecordingReleaseNotesService(eventLog, onMarkSeen: () => throw new InvalidOperationException("ReleaseNotes disk error"));
        var buildIdentity = new FakeBuildIdentityService("1.0.0-beta.14");
        var stateStore = new RecordingOnboardingStateStore(eventLog);
        var progressStore = new RecordingOnboardingProgressStore(eventLog);
        var completionService = new OnboardingCompletionService(
            releaseNotes,
            buildIdentity,
            stateStore,
            progressStore,
            NullLogger<OnboardingCompletionService>.Instance);

        Assert.ThrowsExactly<InvalidOperationException>(() => completionService.CompleteOnboarding());

        Assert.AreEqual(1, eventLog.Count);
        Assert.AreEqual("MarkSeen:1.0.0-beta.14", eventLog[0]);
        Assert.AreEqual(OnboardingState.InProgress, stateStore.GetState());
        Assert.AreEqual(OnboardingStep.Summary, progressStore.GetCurrentStep());
    }

    [TestMethod]
    public void CompletionService_WhenStateStoreThrows_FailsClosedWithoutClearingProgress()
    {
        var eventLog = new List<string>();
        var releaseNotes = new RecordingReleaseNotesService(eventLog);
        var buildIdentity = new FakeBuildIdentityService("1.0.0-beta.14");
        var stateStore = new RecordingOnboardingStateStore(eventLog, onSetState: () => throw new InvalidOperationException("StateStore write error"));
        var progressStore = new RecordingOnboardingProgressStore(eventLog);
        var completionService = new OnboardingCompletionService(
            releaseNotes,
            buildIdentity,
            stateStore,
            progressStore,
            NullLogger<OnboardingCompletionService>.Instance);

        Assert.ThrowsExactly<InvalidOperationException>(() => completionService.CompleteOnboarding());

        Assert.AreEqual(2, eventLog.Count);
        Assert.AreEqual("MarkSeen:1.0.0-beta.14", eventLog[0]);
        Assert.AreEqual("SetState:Completed", eventLog[1]);
        Assert.AreEqual(OnboardingStep.Summary, progressStore.GetCurrentStep());
    }

    [TestMethod]
    public void FullDestructiveReset_ReestablishesRequiredStateAndClearsProgress()
    {
        var preferences = new InMemoryPreferences();
        var stateStore = new MauiOnboardingStateStore(preferences);
        var progressStore = new MauiOnboardingProgressStore(preferences);
        var displayNameStore = new MauiDisplayNameStore(preferences);
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var settingsDefaults = new SettingsDefaultsService(
            appSettings,
            new FakeThemePreferenceReset(),
            new FakeLanguageSelection(),
            stateStore,
            NullLogger<SettingsDefaultsService>.Instance);

        // Seed completed state and settings
        stateStore.SetState(OnboardingState.Completed);
        progressStore.SetCurrentStep(OnboardingStep.Summary);
        displayNameStore.SetDisplayName("Tachi");
        appSettings.GrantOnlineLookupConsent();
        appSettings.SetPreparationLimit(20);

        // Execute full reset
        preferences.Clear();
        settingsDefaults.RestoreDefaultsForFullReset();

        Assert.AreEqual(OnboardingState.Required, stateStore.GetState());
        Assert.IsNull(progressStore.GetCurrentStep());
        Assert.IsNull(displayNameStore.GetDisplayName());
        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
        Assert.AreEqual(5, appSettings.PreparationLimit);
    }

    [TestMethod]
    public void NonDestructiveRestoreDefaults_PreservesOnboardingCompletionAndProgress()
    {
        var preferences = new InMemoryPreferences();
        var stateStore = new MauiOnboardingStateStore(preferences);
        var progressStore = new MauiOnboardingProgressStore(preferences);
        var displayNameStore = new MauiDisplayNameStore(preferences);
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var settingsDefaults = new SettingsDefaultsService(
            appSettings,
            new FakeThemePreferenceReset(),
            new FakeLanguageSelection(),
            stateStore,
            NullLogger<SettingsDefaultsService>.Instance);

        stateStore.SetState(OnboardingState.Completed);
        displayNameStore.SetDisplayName("Tachi");
        appSettings.GrantOnlineLookupConsent();
        appSettings.SetPreparationLimit(20);

        settingsDefaults.RestoreDefaults();

        Assert.AreEqual(OnboardingState.Completed, stateStore.GetState());
        Assert.AreEqual("Tachi", displayNameStore.GetDisplayName());
        Assert.IsTrue(appSettings.HasOnlineLookupConsent);
        Assert.AreEqual(5, appSettings.PreparationLimit);
    }
}
