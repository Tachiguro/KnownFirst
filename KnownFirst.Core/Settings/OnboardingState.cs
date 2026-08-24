namespace KnownFirst.Core.Settings;

/// <summary>
/// The persisted first-run onboarding lifecycle. There is deliberately no "unknown" or "absent"
/// member: the absence of a persisted onboarding marker is classification input for the
/// install-origin classifier, not a lifecycle state an installation can rest in.
/// </summary>
public enum OnboardingState
{
    /// <summary>Onboarding must run: a genuinely fresh installation, or one after a full reset.</summary>
    Required = 1,

    /// <summary>Onboarding was started and not finished; it resumes on the next start.</summary>
    InProgress = 2,

    /// <summary>Onboarding is finished, or the installation was grandfathered as pre-existing.</summary>
    Completed = 3
}
