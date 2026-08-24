using KnownFirst.Core.Settings;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Onboarding;

public sealed class MauiOnboardingProgressStore(IPreferences preferences) : IOnboardingProgressStore
{
    internal const string StepPreferenceKey = "onboarding_step";

    public OnboardingStep? GetCurrentStep()
    {
        if (!preferences.ContainsKey(StepPreferenceKey))
        {
            return null;
        }

        var saved = preferences.Get(StepPreferenceKey, 0);
        return OnboardingStepPolicy.TryNormalize(saved, out var step) ? step : null;
    }

    public void SetCurrentStep(OnboardingStep step) =>
        preferences.Set(StepPreferenceKey, (int)step);

    public void ClearProgress() =>
        preferences.Remove(StepPreferenceKey);
}
