using KnownFirst.Core.Settings;

namespace KnownFirst.Services;

public interface IAppSettingsService
{
    int PreparationLimit { get; }

    IReadOnlyList<int> SupportedPreparationLimits { get; }

    CardDirectionPreference CardDirection { get; }

    LearningMode LearningMode { get; }

    bool HasOnlineLookupConsent { get; }

    void SetPreparationLimit(int preparationLimit);

    void SetCardDirection(CardDirectionPreference preference);

    void SetLearningMode(LearningMode mode);

    void GrantOnlineLookupConsent();

    void RevokeOnlineLookupConsent();

    void Reset();
}
