namespace KnownFirst.Services.Onboarding;

public enum OnboardingRecoveryOutcome
{
    Ready = 0,
    UnsupportedFutureData = 1
}

/// <summary>
/// Dormant startup-recovery seam for Package B4. B3 registers but never invokes it.
/// </summary>
public interface IOnboardingRecoveryService
{
    OnboardingRecoveryOutcome Recover();
}
