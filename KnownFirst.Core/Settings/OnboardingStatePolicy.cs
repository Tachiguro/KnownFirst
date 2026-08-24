namespace KnownFirst.Core.Settings;

/// <summary>
/// Single source of truth for interpreting a persisted onboarding-state value.
/// <para>
/// Unlike the other settings policies this one deliberately exposes no silent default fallback.
/// A missing or unreadable value must be treated exactly like "never classified" so the
/// install-origin classifier can re-derive the correct answer from legacy preference evidence.
/// Falling back to a fixed state here would either re-show onboarding to an existing user or
/// silently skip it for a genuinely fresh installation.
/// </para>
/// </summary>
public static class OnboardingStatePolicy
{
    public static bool TryNormalize(int value, out OnboardingState state)
    {
        switch (value)
        {
            case (int)OnboardingState.Required:
                state = OnboardingState.Required;
                return true;
            case (int)OnboardingState.InProgress:
                state = OnboardingState.InProgress;
                return true;
            case (int)OnboardingState.Completed:
                state = OnboardingState.Completed;
                return true;
            default:
                state = default;
                return false;
        }
    }
}
