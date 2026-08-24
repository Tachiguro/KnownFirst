namespace KnownFirst.Core.Settings;

public static class OnboardingStepPolicy
{
    public const OnboardingStep FirstStep = OnboardingStep.WelcomeLanguage;
    public const OnboardingStep LastStep = OnboardingStep.Summary;

    public static bool TryNormalize(int rawValue, out OnboardingStep step)
    {
        if (Enum.IsDefined(typeof(OnboardingStep), rawValue))
        {
            step = (OnboardingStep)rawValue;
            return true;
        }

        step = FirstStep;
        return false;
    }

    public static OnboardingStep Normalize(int rawValue) =>
        TryNormalize(rawValue, out var step) ? step : FirstStep;

    public static bool TryGetNext(OnboardingStep current, out OnboardingStep next)
    {
        if (current < LastStep && Enum.IsDefined(typeof(OnboardingStep), (int)current + 1))
        {
            next = (OnboardingStep)((int)current + 1);
            return true;
        }

        next = current;
        return false;
    }

    public static bool TryGetPrevious(OnboardingStep current, out OnboardingStep previous)
    {
        if (current > FirstStep && Enum.IsDefined(typeof(OnboardingStep), (int)current - 1))
        {
            previous = (OnboardingStep)((int)current - 1);
            return true;
        }

        previous = current;
        return false;
    }

    public static bool CanGoBack(OnboardingStep current) => current > FirstStep;
}
