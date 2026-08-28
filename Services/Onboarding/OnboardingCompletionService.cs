using KnownFirst.Core.Settings;
using KnownFirst.Core.Language;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Settings;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Default implementation of <see cref="IOnboardingCompletionService"/> coordinating the terminal
/// persistence sequence across What'\''s New seen version, onboarding state, and progress clearing.
/// </summary>
public sealed class OnboardingCompletionService : IOnboardingCompletionService
{
    private readonly IReleaseNotesService _releaseNotes;
    private readonly IBuildIdentityService _buildIdentity;
    private readonly IOnboardingStateStore _stateStore;
    private readonly IOnboardingProgressStore _progressStore;
    private readonly ILogger<OnboardingCompletionService> _logger;
    private readonly ILanguageSelectionService? _languageSelection;
    private readonly IThemeService? _themeService;
    private readonly IDisplayNameStore? _displayNameStore;
    private readonly IAppSettingsService? _appSettings;
    private readonly IOnboardingDraftStore? _draftStore;
    private readonly IOnboardingCompletionJournalStore? _journalStore;

    // Retained for the production B3 caller and its legacy tests. DI selects the richer constructor.
    public OnboardingCompletionService(
        IReleaseNotesService releaseNotes,
        IBuildIdentityService buildIdentity,
        IOnboardingStateStore stateStore,
        IOnboardingProgressStore progressStore,
        ILogger<OnboardingCompletionService> logger)
        : this(releaseNotes, buildIdentity, stateStore, progressStore, null!, null!, null!, null!, null!, null!, logger)
    {
    }

    public OnboardingCompletionService(
        IReleaseNotesService releaseNotes,
        IBuildIdentityService buildIdentity,
        IOnboardingStateStore stateStore,
        IOnboardingProgressStore progressStore,
        ILanguageSelectionService languageSelection,
        IThemeService themeService,
        IDisplayNameStore displayNameStore,
        IAppSettingsService appSettings,
        IOnboardingDraftStore draftStore,
        IOnboardingCompletionJournalStore journalStore,
        ILogger<OnboardingCompletionService> logger)
    {
        _releaseNotes = releaseNotes;
        _buildIdentity = buildIdentity;
        _stateStore = stateStore;
        _progressStore = progressStore;
        _languageSelection = languageSelection;
        _themeService = themeService;
        _displayNameStore = displayNameStore;
        _appSettings = appSettings;
        _draftStore = draftStore;
        _journalStore = journalStore;
        _logger = logger;
    }

    public void CompleteOnboarding()
    {
        var version = _buildIdentity.Identity.Version;
        _releaseNotes.MarkSeen(version);
        _stateStore.SetState(OnboardingState.Completed);
        _progressStore.ClearProgress();

        _logger.LogInformation(
            "Onboarding completion sequence executed successfully for version {Version}.",
            version);
    }

    public bool CompleteOnboarding(OnboardingDraft draft)
    {
        if (!OnboardingDraftPolicy.IsValid(draft, out _) || !draft.OnlineLookupConsent.HasValue)
        {
            return false;
        }

        EnsureTransactionalDependencies();

        var fingerprint = OnboardingDraftPolicy.ComputeFingerprint(draft);
        var persistedDraft = draft with { LastCompletionAttemptFingerprint = fingerprint };
        var journal = new OnboardingCompletionJournal(
            Version: OnboardingCompletionJournalPolicy.CurrentVersion,
            AttemptId: Guid.NewGuid().ToString("N"),
            TargetFingerprint: fingerprint,
            UiLanguage: draft.UiLanguage,
            Theme: draft.Theme,
            DisplayName: draft.DisplayName,
            OnlineLookupConsent: draft.OnlineLookupConsent.Value,
            EnhancedTermRecognitionEnabled: draft.EnhancedTermRecognitionEnabled,
            CardDirection: draft.CardDirection,
            LearningMode: draft.LearningMode,
            PreparationLimit: draft.PreparationLimit,
            LearningTimezoneMode: draft.LearningTimezoneMode,
            ExplicitLearningTimezoneId: draft.ExplicitLearningTimezoneId,
            LearningDayCutoffMinutes: draft.LearningDayCutoffMinutes,
            AppVersion: _buildIdentity.Identity.Version);

        if (!OnboardingCompletionJournalPolicy.IsValid(journal, out _))
        {
            return false;
        }

        try
        {
            _draftStore!.Save(persistedDraft);
            if (!_journalStore!.SaveVerified(journal))
            {
                return false;
            }

            RollForward(journal);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Transactional onboarding completion requires journal replay.");
            return false;
        }
    }

    public void RollForward(OnboardingCompletionJournal journal)
    {
        if (!OnboardingCompletionJournalPolicy.IsValid(journal, out var reason))
        {
            throw new ArgumentException(reason ?? "The completion journal is invalid.", nameof(journal));
        }

        EnsureTransactionalDependencies();

        _languageSelection!.SetUiLanguage(journal.UiLanguage);
        _themeService!.SetPreference(journal.Theme);
        _displayNameStore!.SetDisplayName(journal.DisplayName);
        _appSettings!.SetPreparationLimit(journal.PreparationLimit);
        _appSettings.SetCardDirection(journal.CardDirection);
        _appSettings.SetLearningMode(journal.LearningMode);
        _appSettings.SetEnhancedTermRecognitionEnabled(journal.EnhancedTermRecognitionEnabled);
        _appSettings.SetLearningTimezoneMode(journal.LearningTimezoneMode);
        _appSettings.SetExplicitLearningTimezoneId(journal.ExplicitLearningTimezoneId);
        _appSettings.SetLearningDayCutoffMinutes(journal.LearningDayCutoffMinutes);
        if (journal.OnlineLookupConsent)
        {
            _appSettings.GrantOnlineLookupConsent();
        }
        else
        {
            _appSettings.RevokeOnlineLookupConsent();
        }

        _releaseNotes.MarkSeen(journal.AppVersion);
        _stateStore.SetState(OnboardingState.Completed);
        _progressStore.ClearProgress();
        _draftStore!.Clear();
        _journalStore!.Clear();
    }

    private void EnsureTransactionalDependencies()
    {
        if (_languageSelection is null || _themeService is null || _displayNameStore is null ||
            _appSettings is null || _draftStore is null || _journalStore is null)
        {
            throw new InvalidOperationException("Transactional onboarding completion dependencies are unavailable.");
        }
    }
}
