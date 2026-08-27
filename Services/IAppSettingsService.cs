using KnownFirst.Core.Settings;

namespace KnownFirst.Services;

public interface IAppSettingsService
{
    int PreparationLimit { get; }

    IReadOnlyList<int> SupportedPreparationLimits { get; }

    CardDirectionPreference CardDirection { get; }

    LearningMode LearningMode { get; }

    bool HasOnlineLookupConsent { get; }

    event Action<bool>? OnlineLookupConsentChanged
    {
        add { }
        remove { }
    }

    bool EnhancedTermRecognitionEnabled { get; }

    LearningTimezoneMode LearningTimezoneMode { get; }

    string? ExplicitLearningTimezoneId { get; }

    int LearningDayCutoffMinutes { get; }

    void SetPreparationLimit(int preparationLimit);

    void SetCardDirection(CardDirectionPreference preference);

    void SetLearningMode(LearningMode mode);

    void GrantOnlineLookupConsent();

    void RevokeOnlineLookupConsent();

    void SetEnhancedTermRecognitionEnabled(bool enabled);

    void SetLearningTimezoneMode(LearningTimezoneMode mode);

    void SetExplicitLearningTimezoneId(string? timezoneId);

    void SetLearningDayCutoffMinutes(int minutes);

    void Reset();
}
