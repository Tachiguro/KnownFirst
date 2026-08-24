namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Executes the terminal persistence sequence when the user completes first-run onboarding.
/// </summary>
public interface IOnboardingCompletionService
{
    /// <summary>
    /// Persists onboarding completion in strict order:
    /// 1. Marks the current application/build version seen in the What's New store.
    /// 2. Sets durable onboarding state to Completed.
    /// 3. Clears persisted onboarding progress.
    /// </summary>
    void CompleteOnboarding();
}
