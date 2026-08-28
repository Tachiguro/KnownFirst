using System.Reflection;
using KnownFirst.Core.Language;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Onboarding;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingCompletionRecoveryTests
{
    [TestMethod]
    public void RecoveryContract_IsAvailableForVerifiedJournalRollForward()
    {
        var recoveryContract = Assembly.GetExecutingAssembly().GetType(
            "KnownFirst.Services.Onboarding.IOnboardingRecoveryService");

        Assert.IsNotNull(recoveryContract, "The B3 recovery service contract must be available.");
        Assert.IsNotNull(recoveryContract.GetMethod("Recover"));
    }

    [TestMethod]
    public void TransactionalCompletion_UsesVerifiedJournalBarrierAndExactRollForwardOrder()
    {
        var fixture = new B3Fixture();
        var result = fixture.Service.CompleteOnboarding(fixture.Draft with { OnlineLookupConsent = false });

        Assert.IsTrue(result);
        Assert.IsNotNull(fixture.Journals.LastSaved);
        Assert.AreEqual(OnboardingDraftPolicy.ComputeFingerprint(fixture.Draft with { OnlineLookupConsent = false }), fixture.Journals.LastSaved.TargetFingerprint);
        Assert.AreEqual("1.2.3", fixture.Journals.LastSaved.AppVersion);
        Assert.AreEqual(fixture.Journals.LastSaved.TargetFingerprint, fixture.Drafts.LastSaved!.LastCompletionAttemptFingerprint);
        CollectionAssert.AreEqual(new[] { "Draft.Save", "Journal.SaveVerified", "Language.Set:de", "Theme.Set:Dark", "Name.Set:Ada", "Preparation.Set:20", "Direction.Set:MeaningToTerm", "Mode.Set:Typing", "Enhanced.Set:False", "TimezoneMode.Set:Explicit", "TimezoneId.Set:Europe/Berlin", "Cutoff.Set:180", "Consent.Revoke", "Release.MarkSeen:1.2.3", "State.Set:Completed", "Progress.Clear", "Draft.Clear", "Journal.Clear" }, fixture.Events);
    }

    [TestMethod]
    public void TransactionalCompletion_FailsClosedWhenJournalVerificationFailsOrConsentIsNull()
    {
        var fixture = new B3Fixture { JournalSaveResult = false };
        Assert.IsFalse(fixture.Service.CompleteOnboarding(fixture.Draft));
        CollectionAssert.AreEqual(new[] { "Draft.Save", "Journal.SaveVerified" }, fixture.Events);
        Assert.AreEqual(OnboardingState.InProgress, fixture.States.State);

        var nullConsent = new B3Fixture();
        Assert.IsFalse(nullConsent.Service.CompleteOnboarding(nullConsent.Draft with { OnlineLookupConsent = null }));
        Assert.IsEmpty(nullConsent.Events);
    }

    [TestMethod]
    public void TransactionalCompletion_FaultKeepsJournalForReplay()
    {
        var fixture = new B3Fixture();
        fixture.App.ThrowOn = "Mode.Set";
        Assert.IsFalse(fixture.Service.CompleteOnboarding(fixture.Draft));
        Assert.IsNotNull(fixture.Journals.Saved);
        Assert.AreEqual(OnboardingState.InProgress, fixture.States.State);
        Assert.IsFalse(fixture.Journals.Cleared);

        fixture.App.ThrowOn = null;
        fixture.Service.RollForward(fixture.Journals.Saved!);
        Assert.AreEqual(OnboardingState.Completed, fixture.States.State);
        Assert.IsTrue(fixture.Journals.Cleared);
    }

    [TestMethod]
    public void Recovery_ValidJournalRollsForwardAndCorruptJournalFailsClosed()
    {
        var valid = new RecoveryFixture { State = OnboardingState.InProgress };
        valid.Journals.Result = OnboardingCompletionJournalReadResult.Valid(valid.Journal);
        valid.Completion.OnRollForward = _ => valid.Events.Add("RollForward");
        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, valid.Recovery.Recover());
        CollectionAssert.AreEqual(new[] { "RollForward" }, valid.Events);

        var corrupt = new RecoveryFixture { State = OnboardingState.InProgress };
        corrupt.Journals.Result = OnboardingCompletionJournalReadResult.Malformed();
        corrupt.Drafts.Result = OnboardingDraftReadResult.Valid(corrupt.Draft);
        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, corrupt.Recovery.Recover());
        Assert.IsTrue(corrupt.App.ResetCalled);
        Assert.IsTrue(corrupt.App.Revoked);
        Assert.AreEqual(OnboardingStep.Summary, corrupt.Progress.Step);
        Assert.AreEqual("de", corrupt.Language.Preview);
        Assert.AreEqual(ThemePreference.Dark, corrupt.Theme.Preview);
        Assert.IsFalse(corrupt.Language.SetCalled);
        Assert.IsFalse(corrupt.Theme.SetCalled);
    }

    [TestMethod]
    public void Recovery_CompletedCleanupAndFutureDataAreNonDestructive()
    {
        var completed = new RecoveryFixture { State = OnboardingState.Completed };
        completed.Journals.Result = OnboardingCompletionJournalReadResult.Invalid("bad");
        completed.Drafts.Result = OnboardingDraftReadResult.Valid(completed.Draft);
        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, completed.Recovery.Recover());
        Assert.IsTrue(completed.Journals.Cleared);
        Assert.IsTrue(completed.Drafts.Cleared);
        Assert.IsFalse(completed.App.ResetCalled);

        var future = new RecoveryFixture { State = OnboardingState.InProgress };
        future.Journals.Result = OnboardingCompletionJournalReadResult.UnsupportedVersion(99);
        Assert.AreEqual(OnboardingRecoveryOutcome.UnsupportedFutureData, future.Recovery.Recover());
        Assert.IsFalse(future.Journals.Cleared);
        Assert.IsFalse(future.App.ResetCalled);
    }
}

internal static class B3TestData
{
    public static OnboardingDraft Draft => new(1, "de", ThemePreference.Dark, "Ada", true, false, CardDirectionPreference.MeaningToTerm, LearningMode.Typing, 20, LearningTimezoneMode.Explicit, "Europe/Berlin", 180, null);
    public static OnboardingCompletionJournal Journal => new(1, "attempt", "fingerprint", "de", ThemePreference.Dark, "Ada", true, false, CardDirectionPreference.MeaningToTerm, LearningMode.Typing, 20, LearningTimezoneMode.Explicit, "Europe/Berlin", 180, "1.2.3");
}

internal sealed class B3Fixture
{
    public List<string> Events { get; } = [];
    public RecordingDrafts Drafts { get; }
    public RecordingJournals Journals { get; }
    public RecordingState States { get; }
    public RecordingAppSettings App { get; }
    public OnboardingCompletionService Service { get; }
    public bool JournalSaveResult { get => Journals.SaveResult; init => Journals.SaveResult = value; }
    public OnboardingDraft Draft { get; } = B3TestData.Draft;
    public B3Fixture()
    {
        Drafts = new RecordingDrafts(Events); Journals = new RecordingJournals(Events); States = new RecordingState(Events); App = new RecordingAppSettings(Events);
        Service = new OnboardingCompletionService(new RecordingReleaseNotes(Events), new TestBuildIdentity(), States, new RecordingProgress(Events), new RecordingLanguage(Events), new RecordingTheme(Events), new RecordingDisplay(Events), App, Drafts, Journals, NullLogger<OnboardingCompletionService>.Instance);
    }
}

internal sealed class RecoveryFixture
{
    public List<string> Events { get; } = [];
    public RecordingCompletion Completion { get; } = new();
    public RecordingJournals Journals { get; }
    public RecordingDrafts Drafts { get; }
    public RecordingState States { get; } = new([]);
    public RecordingProgress Progress { get; } = new([]);
    public RecordingAppSettings App { get; } = new([]);
    public RecordingDisplay Display { get; } = new([]);
    public RecordingLanguage Language { get; } = new([]);
    public RecordingTheme Theme { get; } = new([]);
    public InMemoryPreferences Preferences { get; } = new();
    public OnboardingRecoveryService Recovery { get; }
    public OnboardingDraft Draft { get; } = B3TestData.Draft;
    public OnboardingCompletionJournal Journal { get; } = B3TestData.Journal;
    public OnboardingState State { get => States.State!.Value; init => States.State = value; }
    public RecoveryFixture()
    {
        Journals = new RecordingJournals(Events); Drafts = new RecordingDrafts(Events);
        Recovery = new OnboardingRecoveryService(Completion, Journals, Drafts, States, Progress, App, Display, Language, Theme, Preferences, NullLogger<OnboardingRecoveryService>.Instance);
    }
}

internal sealed class TestBuildIdentity : IBuildIdentityService
{
    public BuildIdentity Identity { get; } = new("KnownFirst", "1.2.3", "1", "test", "Debug", "x", "x", "x", "x", "x", "x", "x", "x", false);
    public string FormatHeader() => string.Empty; public string GetFormattedBuildIdentity() => string.Empty;
}
internal sealed class RecordingCompletion : IOnboardingCompletionService { public Action<OnboardingCompletionJournal>? OnRollForward { get; set; } public void CompleteOnboarding() { } public bool CompleteOnboarding(OnboardingDraft d) => false; public void RollForward(OnboardingCompletionJournal j) => OnRollForward?.Invoke(j); }
internal sealed class RecordingDrafts(List<string> e) : IOnboardingDraftStore { public OnboardingDraftReadResult Result { get; set; } = OnboardingDraftReadResult.Missing(); public OnboardingDraft? Saved { get; private set; } public OnboardingDraft? LastSaved { get; private set; } public bool Cleared { get; private set; } public OnboardingDraftReadResult Read() => Result.Status == OnboardingDraftStatus.Missing && Saved is not null ? OnboardingDraftReadResult.Valid(Saved) : Result; public void Save(OnboardingDraft d) { e.Add("Draft.Save"); Saved=d; LastSaved=d; Result=OnboardingDraftReadResult.Valid(d); } public void Clear() { e.Add("Draft.Clear"); Cleared=true; Saved=null; Result=OnboardingDraftReadResult.Missing(); } }
internal sealed class RecordingJournals(List<string> e) : IOnboardingCompletionJournalStore { public OnboardingCompletionJournalReadResult Result { get; set; } = OnboardingCompletionJournalReadResult.Missing(); public OnboardingCompletionJournal? Saved { get; private set; } public OnboardingCompletionJournal? LastSaved { get; private set; } public bool SaveResult { get; set; } = true; public bool Cleared { get; private set; } public OnboardingCompletionJournalReadResult Read() => Result.Status == OnboardingCompletionJournalStatus.Missing && Saved is not null ? OnboardingCompletionJournalReadResult.Valid(Saved) : Result; public void Save(OnboardingCompletionJournal j) { Saved=j; LastSaved=j; } public bool SaveVerified(OnboardingCompletionJournal j) { e.Add("Journal.SaveVerified"); Saved=j; LastSaved=j; return SaveResult; } public void Clear() { e.Add("Journal.Clear"); Cleared=true; Saved=null; Result=OnboardingCompletionJournalReadResult.Missing(); } }
internal sealed class RecordingState(List<string> e) : IOnboardingStateStore { public OnboardingState? State { get; set; } = OnboardingState.InProgress; public OnboardingState? GetState() => State; public void SetState(OnboardingState s) { State=s; e.Add($"State.Set:{s}"); } }
internal sealed class RecordingProgress(List<string> e) : IOnboardingProgressStore { public OnboardingStep? Step { get; set; }=OnboardingStep.Summary; public bool Cleared { get; private set; } public OnboardingStep? GetCurrentStep()=>Step; public void SetCurrentStep(OnboardingStep s)=>Step=s; public void ClearProgress(){e.Add("Progress.Clear");Cleared=true;Step=null;} }
internal sealed class RecordingReleaseNotes(List<string> e) : IReleaseNotesService { public ReleaseNoteEntry? GetUnseenReleaseNotes()=>null; public IReadOnlyList<ReleaseNoteEntry> GetReleaseNoteHistory()=>[]; public void MarkSeen(string v)=>e.Add($"Release.MarkSeen:{v}"); }
internal sealed class RecordingDisplay(List<string> e) : IDisplayNameStore { public string? Value { get; set; }="Ada"; public string? GetDisplayName()=>Value; public void SetDisplayName(string? v){Value=v;e.Add($"Name.Set:{v}");} }
internal sealed class RecordingLanguage(List<string> e) : ILanguageSelectionService { public event EventHandler? UiLanguageChanged; public string CurrentUiLanguage { get; set; }="de"; public bool IsSystemPreferenceActive { get; set; } public string? Preview { get; private set; } public string? PreviewUiLanguage=>Preview; public bool IsSystemPreviewActive=>false; public IReadOnlyList<string> SupportedUiLanguages=>["en","de","ru"]; public bool SetCalled { get; private set; } public void Initialize(){} public void SetUiLanguage(string v){SetCalled=true;CurrentUiLanguage=v;e.Add($"Language.Set:{v}");} public void ResetToDeviceLanguage(){CurrentUiLanguage="en";IsSystemPreferenceActive=true;e.Add("Language.Reset");} public void ReapplyCurrentCulture(){} public void ApplyPreviewLanguage(string v){Preview=v;e.Add($"Language.Preview:{v}");} public void ClearPreview()=>Preview=null; }
internal sealed class RecordingTheme(List<string> e) : IThemeService { public event EventHandler? ThemeChanged; public ThemePreference Preference { get; private set; }=ThemePreference.Dark; public ThemePreference? Preview { get; private set; } public ThemePreference? PreviewPreference=>Preview; public ThemePreference EffectiveTheme=>Preview??Preference; public string EffectiveThemeCssName=>"light"; public bool SetCalled { get; private set; } public void Initialize(IThemeApplication a){} public bool SetPreference(ThemePreference v){SetCalled=true;Preference=v;e.Add($"Theme.Set:{v}");return true;} public void ResetPreference(){Preference=ThemePreference.System;e.Add("Theme.Reset");} public void ApplyPreviewPreference(ThemePreference v){Preview=v;e.Add($"Theme.Preview:{v}");} public void ClearPreview()=>Preview=null; }
internal sealed class RecordingAppSettings(List<string> e) : IAppSettingsService { public int PreparationLimit { get; private set; }=20; public IReadOnlyList<int> SupportedPreparationLimits=>[5,10,20]; public CardDirectionPreference CardDirection { get; private set; }=CardDirectionPreference.MeaningToTerm; public LearningMode LearningMode { get; private set; }=LearningMode.Typing; public bool HasOnlineLookupConsent { get; private set; }=true; public bool EnhancedTermRecognitionEnabled { get; private set; } public LearningTimezoneMode LearningTimezoneMode { get; private set; }=LearningTimezoneMode.Explicit; public string? ExplicitLearningTimezoneId { get; private set; }="Europe/Berlin"; public int LearningDayCutoffMinutes { get; private set; }=180; public bool ResetCalled { get; private set; } public bool Revoked { get; private set; } public string? ThrowOn { get; set; } public event Action<bool>? OnlineLookupConsentChanged; public void SetPreparationLimit(int v){Throw("Preparation.Set");PreparationLimit=v;e.Add($"Preparation.Set:{v}");} public void SetCardDirection(CardDirectionPreference v){Throw("Direction.Set");CardDirection=v;e.Add($"Direction.Set:{v}");} public void SetLearningMode(LearningMode v){Throw("Mode.Set");LearningMode=v;e.Add($"Mode.Set:{v}");} public void GrantOnlineLookupConsent(){Throw("Consent.Grant");HasOnlineLookupConsent=true;e.Add("Consent.Grant");OnlineLookupConsentChanged?.Invoke(true);} public void RevokeOnlineLookupConsent(){Throw("Consent.Revoke");HasOnlineLookupConsent=false;Revoked=true;e.Add("Consent.Revoke");OnlineLookupConsentChanged?.Invoke(false);} public void SetEnhancedTermRecognitionEnabled(bool v){Throw("Enhanced.Set");EnhancedTermRecognitionEnabled=v;e.Add($"Enhanced.Set:{v}");} public void SetLearningTimezoneMode(LearningTimezoneMode v){Throw("TimezoneMode.Set");LearningTimezoneMode=v;e.Add($"TimezoneMode.Set:{v}");} public void SetExplicitLearningTimezoneId(string? v){Throw("TimezoneId.Set");ExplicitLearningTimezoneId=v;e.Add($"TimezoneId.Set:{v}");} public void SetLearningDayCutoffMinutes(int v){Throw("Cutoff.Set");LearningDayCutoffMinutes=v;e.Add($"Cutoff.Set:{v}");} public void Reset(){ResetCalled=true;HasOnlineLookupConsent=false;e.Add("App.Reset");OnlineLookupConsentChanged?.Invoke(false);} private void Throw(string op){if(ThrowOn==op)throw new InvalidOperationException(op);} }
internal sealed class InMemoryPreferences : IPreferences { private readonly Dictionary<string,object> v=new(StringComparer.Ordinal); public bool ContainsKey(string k,string? s=null)=>v.ContainsKey(k); public void Remove(string k,string? s=null)=>v.Remove(k); public void Clear(string? s=null)=>v.Clear(); public void Set<T>(string k,T value,string? s=null){if(value is null)v.Remove(k);else v[k]=value;} public T Get<T>(string k,T d,string? s=null)=>v.TryGetValue(k,out var x)?(T)x:d; }
