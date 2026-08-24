using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Durable storage for the active step in the first-run onboarding sequence.
/// Onboarding progress is device-local and stored in preferences — never in SQLite.
/// </summary>
public interface IOnboardingProgressStore
{
    OnboardingStep? GetCurrentStep();

    void SetCurrentStep(OnboardingStep step);

    void ClearProgress();
}
