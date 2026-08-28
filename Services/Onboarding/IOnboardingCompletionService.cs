using KnownFirst.Core.Settings;

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

    /// <summary>
    /// Creates a verified immutable completion target and rolls it forward. A <see langword="false"/>
    /// result leaves any durable recovery evidence in place for a later retry.
    /// </summary>
    bool CompleteOnboarding(OnboardingDraft draft);

    /// <summary>
    /// Applies one verified immutable completion target in deterministic order. Failures are allowed
    /// to propagate so the durable journal remains available for deterministic replay.
    /// </summary>
    void RollForward(OnboardingCompletionJournal journal);
}
