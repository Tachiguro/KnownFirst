using KnownFirst.Core.Settings;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Preference-backed onboarding state, following the same minimal single-key shape as
/// <see cref="MauiWhatsNewPreferenceStore"/>. The concrete preference key stays in this
/// application-layer store; <c>KnownFirst.Core</c> owns only the lifecycle enum and its
/// value interpretation.
/// </summary>
public sealed class MauiOnboardingStateStore(IPreferences preferences) : IOnboardingStateStore
{
    internal const string StatePreferenceKey = "onboarding_state";

    public OnboardingState? GetState()
    {
        if (!preferences.ContainsKey(StatePreferenceKey))
        {
            return null;
        }

        var saved = preferences.Get(StatePreferenceKey, 0);
        return OnboardingStatePolicy.TryNormalize(saved, out var state) ? state : null;
    }

    public void SetState(OnboardingState state) =>
        preferences.Set(StatePreferenceKey, (int)state);
}
