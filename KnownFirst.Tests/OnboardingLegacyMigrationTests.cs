using System.Reflection;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingLegacyMigrationTests
{
    [TestMethod]
    public void RecoveryEngine_ExposesTheLegacyMigrationEntryPoint()
    {
        var recoveryEngine = Assembly.GetExecutingAssembly().GetType(
            "KnownFirst.Services.Onboarding.OnboardingRecoveryService");

        Assert.IsNotNull(recoveryEngine, "The B3 recovery engine must exist to migrate legacy InProgress state.");
        Assert.IsNotNull(recoveryEngine.GetMethod("Recover"));
    }

    [TestMethod]
    public void LegacyMigration_CapturesTrueConsentThenRevokesItAndRestoresOnlyPreview()
    {
        var fixture = new RecoveryFixture { State = OnboardingState.InProgress };
        fixture.Progress.Step = OnboardingStep.WelcomeLanguage;
        fixture.Drafts.Result = OnboardingDraftReadResult.Missing();
        fixture.App.GrantOnlineLookupConsent();

        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, fixture.Recovery.Recover());
        Assert.IsNotNull(fixture.Drafts.Saved);
        Assert.IsTrue(fixture.Drafts.Saved.OnlineLookupConsent);
        Assert.IsFalse(fixture.App.HasOnlineLookupConsent);
        Assert.AreEqual("de", fixture.Language.Preview);
        Assert.AreEqual(ThemePreference.Dark, fixture.Theme.Preview);
        Assert.IsFalse(fixture.Preferences.ContainsKey(OnboardingRecoveryService.MigrationStatePreferenceKey));
        Assert.AreEqual(OnboardingStep.WelcomeLanguage, fixture.Progress.Step);
    }

    [TestMethod]
    public void LegacyMigration_NullConsentNeverFabricatesFalseAndClampsOnlyPastOnlineLookup()
    {
        var afterLookup = new RecoveryFixture { State = OnboardingState.InProgress };
        afterLookup.App.RevokeOnlineLookupConsent();
        afterLookup.Progress.Step = OnboardingStep.Summary;
        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, afterLookup.Recovery.Recover());
        Assert.IsNull(afterLookup.Drafts.Saved!.OnlineLookupConsent);
        Assert.AreEqual(OnboardingStep.OnlineLookup, afterLookup.Progress.Step);

        var beforeLookup = new RecoveryFixture { State = OnboardingState.InProgress };
        beforeLookup.App.RevokeOnlineLookupConsent();
        beforeLookup.Progress.Step = OnboardingStep.DisplayName;
        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, beforeLookup.Recovery.Recover());
        Assert.IsNull(beforeLookup.Drafts.Saved!.OnlineLookupConsent);
        Assert.AreEqual(OnboardingStep.DisplayName, beforeLookup.Progress.Step);
    }

    [TestMethod]
    public void LegacyMigration_NormalizingRestartUsesPersistedDraftWithoutRecapturing()
    {
        var fixture = new RecoveryFixture { State = OnboardingState.InProgress };
        fixture.Drafts.Result = OnboardingDraftReadResult.Valid(fixture.Draft);
        fixture.Preferences.Set(OnboardingRecoveryService.MigrationStatePreferenceKey, 2);
        fixture.App.SetPreparationLimit(5);

        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, fixture.Recovery.Recover());
        Assert.AreEqual(20, fixture.Drafts.Result.Draft!.PreparationLimit);
        Assert.IsTrue(fixture.App.ResetCalled);
        Assert.IsFalse(fixture.Preferences.ContainsKey(OnboardingRecoveryService.MigrationStatePreferenceKey));
    }

    [TestMethod]
    public void LegacyMigration_CompletedAndRequiredInstallationsAreUntouched()
    {
        foreach (var state in new[] { OnboardingState.Completed, OnboardingState.Required })
        {
            var fixture = new RecoveryFixture { State = state };
            Assert.AreEqual(OnboardingRecoveryOutcome.Ready, fixture.Recovery.Recover());
            Assert.IsNull(fixture.Drafts.Saved);
            Assert.IsFalse(fixture.App.ResetCalled);
        }
    }

    [TestMethod]
    public void LegacyMigration_RevokesTheRealAuthorizationGateWhileDraftConsentRemainsOnlyData()
    {
        var events = new List<string>();
        var preferences = new InMemoryPreferences();
        var appSettings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        appSettings.GrantOnlineLookupConsent();
        using var gate = new OnlineLookupAuthorizationGate(appSettings);
        Assert.IsTrue(gate.IsAuthorized);

        var drafts = new RecordingDrafts(events);
        var recovery = new OnboardingRecoveryService(
            new RecordingCompletion(),
            new RecordingJournals(events),
            drafts,
            new RecordingState(events),
            new RecordingProgress(events),
            appSettings,
            new RecordingDisplay(events),
            new RecordingLanguage(events),
            new RecordingTheme(events),
            preferences,
            NullLogger<OnboardingRecoveryService>.Instance);

        Assert.AreEqual(OnboardingRecoveryOutcome.Ready, recovery.Recover());
        Assert.IsTrue(drafts.Saved!.OnlineLookupConsent);
        Assert.IsFalse(appSettings.HasOnlineLookupConsent);
        Assert.IsFalse(gate.IsAuthorized);
    }
}
