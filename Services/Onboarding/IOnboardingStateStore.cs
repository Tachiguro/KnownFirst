using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Durable storage for the first-run onboarding lifecycle state. Onboarding state is device-local
/// application state and lives in the application preference layer next to the theme, language, and
/// What's New seen-version markers — never in the SQLite user database, which would make it part of
/// the portable archive and backup contracts.
/// </summary>
public interface IOnboardingStateStore
{
    /// <summary>
    /// The persisted state, or <see langword="null"/> when no readable onboarding marker exists.
    /// An unreadable or unsupported stored value is reported as <see langword="null"/> so it is
    /// re-classified rather than silently resolved to an arbitrary lifecycle state.
    /// </summary>
    OnboardingState? GetState();

    void SetState(OnboardingState state);
}
