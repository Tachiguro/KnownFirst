using KnownFirst.Core.Settings;
using KnownFirst.Services;

namespace KnownFirst.Tests;

/// <summary>
/// Test-only <see cref="IAppSettingsService"/> double with enhanced German term recognition
/// permanently off. For tests unrelated to German enhanced term recognition that only need a
/// working <see cref="KnownFirst.Services.TextReviewService"/> to satisfy its required
/// constructor dependency.
/// </summary>
internal sealed class DisabledEnhancedRecognitionSettings : IAppSettingsService
{
    public int PreparationLimit => 20;
    public IReadOnlyList<int> SupportedPreparationLimits => [20];
    public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
    public LearningMode LearningMode => LearningMode.Automatic;
    public bool HasOnlineLookupConsent => false;
    public bool EnhancedTermRecognitionEnabled => false;

    public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
    public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
    public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
    public void GrantOnlineLookupConsent() => throw new NotSupportedException();
    public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
    public void SetEnhancedTermRecognitionEnabled(bool value) => throw new NotSupportedException();
    public void Reset() => throw new NotSupportedException();
}
