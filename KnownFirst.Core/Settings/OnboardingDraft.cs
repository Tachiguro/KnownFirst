using KnownFirst.Core.Language;

namespace KnownFirst.Core.Settings;

public enum OnboardingDraftStatus
{
    Missing = 0,
    Valid = 1,
    Malformed = 2,
    UnsupportedVersion = 3,
    Invalid = 4
}

public sealed record OnboardingDraftReadResult(
    OnboardingDraftStatus Status,
    OnboardingDraft? Draft = null,
    string? ErrorMessage = null)
{
    public static OnboardingDraftReadResult Valid(OnboardingDraft draft) =>
        new(OnboardingDraftStatus.Valid, draft);

    public static OnboardingDraftReadResult Missing() =>
        new(OnboardingDraftStatus.Missing);

    public static OnboardingDraftReadResult Malformed(string? error = null) =>
        new(OnboardingDraftStatus.Malformed, null, error);

    public static OnboardingDraftReadResult UnsupportedVersion(int version) =>
        new(OnboardingDraftStatus.UnsupportedVersion, null, $"Unsupported draft version: {version}");

    public static OnboardingDraftReadResult Invalid(string reason) =>
        new(OnboardingDraftStatus.Invalid, null, reason);
}

public sealed record OnboardingDraft(
    int Version,
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
    string? LastCompletionAttemptFingerprint);
