using KnownFirst.Core.Settings;
using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Default implementation of <see cref="IOnboardingCompletionService"/> coordinating the terminal
/// persistence sequence across What'\''s New seen version, onboarding state, and progress clearing.
/// </summary>
public sealed class OnboardingCompletionService(
    IReleaseNotesService releaseNotes,
    IBuildIdentityService buildIdentity,
    IOnboardingStateStore stateStore,
    IOnboardingProgressStore progressStore,
    ILogger<OnboardingCompletionService> logger) : IOnboardingCompletionService
{
    public void CompleteOnboarding()
    {
        var version = buildIdentity.Identity.Version;
        releaseNotes.MarkSeen(version);
        stateStore.SetState(OnboardingState.Completed);
        progressStore.ClearProgress();

        logger.LogInformation(
            "Onboarding completion sequence executed successfully for version {Version}.",
            version);
    }
}
