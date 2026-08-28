using KnownFirst.Core.Language;
using KnownFirst.Core.Settings;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Onboarding;

public sealed class OnboardingRecoveryService(
    IOnboardingCompletionService completionService,
    IOnboardingCompletionJournalStore journalStore,
    IOnboardingDraftStore draftStore,
    IOnboardingStateStore stateStore,
    IOnboardingProgressStore progressStore,
    IAppSettingsService appSettings,
    IDisplayNameStore displayNameStore,
    ILanguageSelectionService languageSelection,
    IThemeService themeService,
    IPreferences preferences,
    ILogger<OnboardingRecoveryService> logger) : IOnboardingRecoveryService
{
    public const string MigrationStatePreferenceKey = "onboarding_migration_state";
    private const int MigrationNone = 0;
    private const int MigrationCapturing = 1;
    private const int MigrationNormalizing = 2;

    public OnboardingRecoveryOutcome Recover()
    {
        var state = stateStore.GetState();
        var journalResult = journalStore.Read();

        if (journalResult.Status == OnboardingCompletionJournalStatus.Valid)
        {
            completionService.RollForward(journalResult.Journal!);
            return OnboardingRecoveryOutcome.Ready;
        }

        if (journalResult.Status == OnboardingCompletionJournalStatus.UnsupportedVersion)
        {
            return OnboardingRecoveryOutcome.UnsupportedFutureData;
        }

        if (journalResult.Status != OnboardingCompletionJournalStatus.Missing)
        {
            return RecoverUnreadableJournal(state);
        }

        return RecoverWithoutJournal(state);
    }

    private OnboardingRecoveryOutcome RecoverUnreadableJournal(OnboardingState? state)
    {
        if (state == OnboardingState.Completed)
        {
            journalStore.Clear();
            draftStore.Clear();
            progressStore.ClearProgress();
            return OnboardingRecoveryOutcome.Ready;
        }

        if (state == OnboardingState.InProgress)
        {
            NormalizeCommittedBaseline();
            journalStore.Clear();
            return RecoverDraftAfterFailClosedReset();
        }

        journalStore.Clear();
        return OnboardingRecoveryOutcome.Ready;
    }

    private OnboardingRecoveryOutcome RecoverWithoutJournal(OnboardingState? state)
    {
        if (state != OnboardingState.InProgress)
        {
            return OnboardingRecoveryOutcome.Ready;
        }

        if (preferences.Get(MigrationStatePreferenceKey, MigrationNone) != MigrationNone)
        {
            return MigrateLegacyInProgressState();
        }

        var draftResult = draftStore.Read();
        return draftResult.Status switch
        {
            OnboardingDraftStatus.Valid => ClampAndReturnReady(draftResult.Draft!),
            OnboardingDraftStatus.UnsupportedVersion => OnboardingRecoveryOutcome.UnsupportedFutureData,
            OnboardingDraftStatus.Missing => MigrateLegacyInProgressState(),
            _ => ResetInvalidDraftToFirstStep()
        };
    }

    private OnboardingRecoveryOutcome RecoverDraftAfterFailClosedReset()
    {
        var draftResult = draftStore.Read();
        if (draftResult.Status == OnboardingDraftStatus.Valid)
        {
            progressStore.SetCurrentStep(OnboardingStep.Summary);
            ClampProgressForNullConsent(draftResult.Draft!);
            ApplyDraftPreview(draftResult.Draft!);
            return OnboardingRecoveryOutcome.Ready;
        }

        if (draftResult.Status == OnboardingDraftStatus.UnsupportedVersion)
        {
            return OnboardingRecoveryOutcome.UnsupportedFutureData;
        }

        if (draftResult.Status != OnboardingDraftStatus.Missing)
        {
            draftStore.Clear();
        }

        progressStore.SetCurrentStep(OnboardingStep.WelcomeLanguage);
        return OnboardingRecoveryOutcome.Ready;
    }

    private OnboardingRecoveryOutcome ResetInvalidDraftToFirstStep()
    {
        draftStore.Clear();
        progressStore.SetCurrentStep(OnboardingStep.WelcomeLanguage);
        return OnboardingRecoveryOutcome.Ready;
    }

    private OnboardingRecoveryOutcome MigrateLegacyInProgressState()
    {
        var marker = preferences.Get(MigrationStatePreferenceKey, MigrationNone);
        if (marker != MigrationNormalizing)
        {
            preferences.Set(MigrationStatePreferenceKey, MigrationCapturing);
            var captured = CaptureLegacyDraft();
            draftStore.Save(captured);
            var verified = draftStore.Read();
            if (verified.Status != OnboardingDraftStatus.Valid || !Equals(verified.Draft, captured))
            {
                logger.LogWarning("Legacy onboarding capture could not be verified; normalization is deferred.");
                return OnboardingRecoveryOutcome.Ready;
            }

            preferences.Set(MigrationStatePreferenceKey, MigrationNormalizing);
        }

        var draftResult = draftStore.Read();
        if (draftResult.Status == OnboardingDraftStatus.UnsupportedVersion)
        {
            return OnboardingRecoveryOutcome.UnsupportedFutureData;
        }

        if (draftResult.Status == OnboardingDraftStatus.Valid)
        {
            NormalizeCommittedBaseline();
            ApplyDraftPreview(draftResult.Draft!);
            ClampProgressForNullConsent(draftResult.Draft!);
        }
        else
        {
            NormalizeCommittedBaseline();
            if (draftResult.Status != OnboardingDraftStatus.Missing)
            {
                draftStore.Clear();
            }

            progressStore.SetCurrentStep(OnboardingStep.WelcomeLanguage);
        }

        preferences.Remove(MigrationStatePreferenceKey);
        return OnboardingRecoveryOutcome.Ready;
    }

    private OnboardingRecoveryOutcome ClampAndReturnReady(OnboardingDraft draft)
    {
        ClampProgressForNullConsent(draft);
        return OnboardingRecoveryOutcome.Ready;
    }

    private OnboardingDraft CaptureLegacyDraft() =>
        new(
            Version: OnboardingDraftPolicy.CurrentVersion,
            UiLanguage: languageSelection.IsSystemPreferenceActive
                ? LanguagePreferencePolicy.SystemPreferenceCode
                : languageSelection.CurrentUiLanguage,
            Theme: themeService.Preference,
            DisplayName: displayNameStore.GetDisplayName(),
            OnlineLookupConsent: appSettings.HasOnlineLookupConsent ? true : null,
            EnhancedTermRecognitionEnabled: appSettings.EnhancedTermRecognitionEnabled,
            CardDirection: appSettings.CardDirection,
            LearningMode: appSettings.LearningMode,
            PreparationLimit: appSettings.PreparationLimit,
            LearningTimezoneMode: appSettings.LearningTimezoneMode,
            ExplicitLearningTimezoneId: appSettings.ExplicitLearningTimezoneId,
            LearningDayCutoffMinutes: appSettings.LearningDayCutoffMinutes,
            LastCompletionAttemptFingerprint: null);

    private void NormalizeCommittedBaseline()
    {
        appSettings.Reset();
        appSettings.RevokeOnlineLookupConsent();
        displayNameStore.SetDisplayName(null);
        languageSelection.ResetToDeviceLanguage();
        themeService.ResetPreference();
    }

    private void ApplyDraftPreview(OnboardingDraft draft)
    {
        languageSelection.ApplyPreviewLanguage(draft.UiLanguage);
        themeService.ApplyPreviewPreference(draft.Theme);
    }

    private void ClampProgressForNullConsent(OnboardingDraft draft)
    {
        if (draft.OnlineLookupConsent.HasValue)
        {
            return;
        }

        var step = progressStore.GetCurrentStep();
        if (step is > OnboardingStep.OnlineLookup)
        {
            progressStore.SetCurrentStep(OnboardingStep.OnlineLookup);
        }
    }
}
