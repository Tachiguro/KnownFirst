using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Language;
using KnownFirst.Core.Learning;

namespace KnownFirst.Core.Settings;

public static class OnboardingDraftPolicy
{
    public const int CurrentVersion = 1;

    public static OnboardingDraft CreateDefault() =>
        new(
            Version: CurrentVersion,
            UiLanguage: LanguagePreferencePolicy.SystemPreferenceCode,
            Theme: ThemePreference.System,
            DisplayName: null,
            OnlineLookupConsent: null,
            EnhancedTermRecognitionEnabled: EnhancedTermRecognitionPolicy.DefaultEnabled,
            CardDirection: CardDirectionPreferencePolicy.DefaultPreference,
            LearningMode: LearningModePolicy.DefaultMode,
            PreparationLimit: PreparationLimitPolicy.DefaultLimit,
            LearningTimezoneMode: LearningTimezoneMode.System,
            ExplicitLearningTimezoneId: null,
            LearningDayCutoffMinutes: LearningDayConfiguration.DefaultCutoffMinutes,
            LastCompletionAttemptFingerprint: null);

    public static bool IsValid(OnboardingDraft draft, out string? reason)
    {
        if (draft is null)
        {
            reason = "Draft cannot be null.";
            return false;
        }

        if (draft.Version != CurrentVersion)
        {
            reason = $"Unsupported version: {draft.Version}. Expected: {CurrentVersion}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(draft.UiLanguage))
        {
            reason = "UiLanguage cannot be null or empty.";
            return false;
        }

        var normalizedLang = draft.UiLanguage.Trim().ToLowerInvariant();
        if (!string.Equals(normalizedLang, LanguagePreferencePolicy.SystemPreferenceCode, StringComparison.OrdinalIgnoreCase) &&
            !LanguagePreferencePolicy.SupportedLanguageCodes.Contains(normalizedLang))
        {
            reason = $"Unsupported UiLanguage '{draft.UiLanguage}'.";
            return false;
        }

        if (!Enum.IsDefined(typeof(ThemePreference), draft.Theme))
        {
            reason = $"Undefined theme preference: {(int)draft.Theme}.";
            return false;
        }

        if (draft.DisplayName is not null && string.IsNullOrWhiteSpace(draft.DisplayName))
        {
            reason = "DisplayName cannot be whitespace-only.";
            return false;
        }

        if (!Enum.IsDefined(typeof(CardDirectionPreference), draft.CardDirection))
        {
            reason = $"Undefined card direction preference: {(int)draft.CardDirection}.";
            return false;
        }

        if (!Enum.IsDefined(typeof(LearningMode), draft.LearningMode))
        {
            reason = $"Undefined learning mode: {(int)draft.LearningMode}.";
            return false;
        }

        if (!PreparationLimitPolicy.IsValid(draft.PreparationLimit))
        {
            reason = $"Preparation limit {draft.PreparationLimit} is out of supported range [{PreparationLimitPolicy.MinimumLimit}, {PreparationLimitPolicy.MaximumLimit}].";
            return false;
        }

        if (draft.LearningTimezoneMode is not (LearningTimezoneMode.System or LearningTimezoneMode.Explicit))
        {
            reason = $"Undefined learning timezone mode: {(int)draft.LearningTimezoneMode}.";
            return false;
        }

        if (draft.LearningTimezoneMode == LearningTimezoneMode.Explicit)
        {
            if (string.IsNullOrWhiteSpace(draft.ExplicitLearningTimezoneId) ||
                !LearningTimezoneCatalog.ContainsTimezoneId(draft.ExplicitLearningTimezoneId))
            {
                reason = $"Explicit timezone '{draft.ExplicitLearningTimezoneId}' is not in catalog.";
                return false;
            }
        }
        else
        {
            if (draft.ExplicitLearningTimezoneId is not null)
            {
                reason = "ExplicitLearningTimezoneId must be null when LearningTimezoneMode is System.";
                return false;
            }
        }

        if (draft.LearningDayCutoffMinutes is < 0 or >= 1440)
        {
            reason = $"LearningDayCutoffMinutes {draft.LearningDayCutoffMinutes} is out of range [0, 1439].";
            return false;
        }

        if (draft.LastCompletionAttemptFingerprint is not null &&
            string.IsNullOrWhiteSpace(draft.LastCompletionAttemptFingerprint))
        {
            reason = "LastCompletionAttemptFingerprint cannot be whitespace-only.";
            return false;
        }

        reason = null;
        return true;
    }

    public static string ComputeFingerprint(OnboardingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var canonical = string.Join('|',
            draft.Version,
            draft.UiLanguage.Trim().ToLowerInvariant(),
            (int)draft.Theme,
            draft.DisplayName?.Trim() ?? string.Empty,
            draft.OnlineLookupConsent.HasValue ? (draft.OnlineLookupConsent.Value ? "1" : "0") : "null",
            draft.EnhancedTermRecognitionEnabled ? "1" : "0",
            (int)draft.CardDirection,
            (int)draft.LearningMode,
            draft.PreparationLimit,
            (int)draft.LearningTimezoneMode,
            draft.ExplicitLearningTimezoneId?.Trim() ?? string.Empty,
            draft.LearningDayCutoffMinutes);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
