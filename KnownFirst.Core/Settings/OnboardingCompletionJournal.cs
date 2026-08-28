using KnownFirst.Core.Language;

namespace KnownFirst.Core.Settings;

public enum OnboardingCompletionJournalStatus
{
    Missing = 0,
    Valid = 1,
    Malformed = 2,
    UnsupportedVersion = 3,
    Invalid = 4
}

public sealed record OnboardingCompletionJournalReadResult(
    OnboardingCompletionJournalStatus Status,
    OnboardingCompletionJournal? Journal = null,
    string? ErrorMessage = null)
{
    public static OnboardingCompletionJournalReadResult Valid(OnboardingCompletionJournal journal) =>
        new(OnboardingCompletionJournalStatus.Valid, journal);

    public static OnboardingCompletionJournalReadResult Missing() =>
        new(OnboardingCompletionJournalStatus.Missing);

    public static OnboardingCompletionJournalReadResult Malformed(string? error = null) =>
        new(OnboardingCompletionJournalStatus.Malformed, null, error);

    public static OnboardingCompletionJournalReadResult UnsupportedVersion(int version) =>
        new(OnboardingCompletionJournalStatus.UnsupportedVersion, null, $"Unsupported journal version: {version}");

    public static OnboardingCompletionJournalReadResult Invalid(string reason) =>
        new(OnboardingCompletionJournalStatus.Invalid, null, reason);
}

public sealed record OnboardingCompletionJournal(
    int Version,
    string AttemptId,
    string TargetFingerprint,
    string UiLanguage,
    ThemePreference Theme,
    string? DisplayName,
    bool? OnlineLookupConsent,
    bool EnhancedTermRecognitionEnabled,
    CardDirectionPreference CardDirection,
    LearningMode LearningMode,
    int PreparationLimit,
    LearningTimezoneMode LearningTimezoneMode,
    string? ExplicitLearningTimezoneId,
    int LearningDayCutoffMinutes,
    string AppVersion);

public static class OnboardingCompletionJournalPolicy
{
    public const int CurrentVersion = 1;

    public static bool IsValid(OnboardingCompletionJournal journal, out string? reason)
    {
        if (journal is null)
        {
            reason = "Journal cannot be null.";
            return false;
        }

        if (journal.Version != CurrentVersion)
        {
            reason = $"Unsupported version: {journal.Version}. Expected: {CurrentVersion}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(journal.AttemptId))
        {
            reason = "AttemptId cannot be null or empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(journal.TargetFingerprint))
        {
            reason = "TargetFingerprint cannot be null or empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(journal.AppVersion))
        {
            reason = "AppVersion cannot be null or empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(journal.UiLanguage))
        {
            reason = "UiLanguage cannot be null or empty.";
            return false;
        }

        var normalizedLang = journal.UiLanguage.Trim().ToLowerInvariant();
        if (!string.Equals(normalizedLang, LanguagePreferencePolicy.SystemPreferenceCode, StringComparison.OrdinalIgnoreCase) &&
            !LanguagePreferencePolicy.SupportedLanguageCodes.Contains(normalizedLang))
        {
            reason = $"Unsupported UiLanguage '{journal.UiLanguage}'.";
            return false;
        }

        if (!Enum.IsDefined(typeof(ThemePreference), journal.Theme))
        {
            reason = $"Undefined theme preference: {(int)journal.Theme}.";
            return false;
        }

        if (journal.DisplayName is not null && string.IsNullOrWhiteSpace(journal.DisplayName))
        {
            reason = "DisplayName cannot be whitespace-only.";
            return false;
        }

        if (!Enum.IsDefined(typeof(CardDirectionPreference), journal.CardDirection))
        {
            reason = $"Undefined card direction preference: {(int)journal.CardDirection}.";
            return false;
        }

        if (!Enum.IsDefined(typeof(LearningMode), journal.LearningMode))
        {
            reason = $"Undefined learning mode: {(int)journal.LearningMode}.";
            return false;
        }

        if (!PreparationLimitPolicy.IsValid(journal.PreparationLimit))
        {
            reason = $"Preparation limit {journal.PreparationLimit} is out of supported range [{PreparationLimitPolicy.MinimumLimit}, {PreparationLimitPolicy.MaximumLimit}].";
            return false;
        }

        if (journal.LearningTimezoneMode is not (LearningTimezoneMode.System or LearningTimezoneMode.Explicit))
        {
            reason = $"Undefined learning timezone mode: {(int)journal.LearningTimezoneMode}.";
            return false;
        }

        if (journal.LearningTimezoneMode == LearningTimezoneMode.Explicit)
        {
            if (string.IsNullOrWhiteSpace(journal.ExplicitLearningTimezoneId) ||
                !LearningTimezoneCatalog.ContainsTimezoneId(journal.ExplicitLearningTimezoneId))
            {
                reason = $"Explicit timezone '{journal.ExplicitLearningTimezoneId}' is not in catalog.";
                return false;
            }
        }
        else
        {
            if (journal.ExplicitLearningTimezoneId is not null)
            {
                reason = "ExplicitLearningTimezoneId must be null when LearningTimezoneMode is System.";
                return false;
            }
        }

        if (journal.LearningDayCutoffMinutes is < 0 or >= 1440)
        {
            reason = $"LearningDayCutoffMinutes {journal.LearningDayCutoffMinutes} is out of range [0, 1439].";
            return false;
        }

        reason = null;
        return true;
    }
}
