using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Decides exactly once per installation whether it is a genuinely fresh installation or a
/// pre-existing installation upgrading to an onboarding-capable build.
/// </summary>
public interface IInstallOriginClassifier
{
    /// <summary>
    /// Returns the onboarding state, classifying and persisting it first when no readable marker
    /// exists. Idempotent: an already classified installation is never reclassified.
    /// </summary>
    OnboardingState EnsureClassified();
}
