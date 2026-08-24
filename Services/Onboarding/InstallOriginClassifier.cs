using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Onboarding;

/// <summary>
/// Classifies the installation exactly once, the first time an onboarding-capable build starts.
/// <para>
/// The binding data-integrity rule is that an existing installation must never be treated as a
/// fresh one merely because the newly introduced onboarding marker is absent. Evidence is taken
/// exclusively from the application preference layer: every pre-onboarding build persists at
/// least the language marker on its very first start, so a real existing installation always
/// leaves preference evidence behind.
/// </para>
/// <para>
/// Database-file existence is deliberately <b>not</b> evidence. The destructive full reset
/// recreates an empty database immediately, so a crash between
/// <c>Preferences.Clear()</c> and the reset's onboarding write would otherwise be misread as an
/// existing installation and silently skip onboarding. Preference-only evidence makes that window
/// safe: a cleared store simply classifies as fresh, which is the correct outcome after a reset.
/// </para>
/// </summary>
public sealed class InstallOriginClassifier(
    IPreferences preferences,
    IOnboardingStateStore stateStore,
    ILogger<InstallOriginClassifier> logger) : IInstallOriginClassifier
{
    /// <summary>
    /// The daily new-word budget an existing installation effectively ran with before onboarding
    /// existed. Deliberately a frozen literal rather than
    /// <c>PreparationLimitPolicy.DefaultLimit</c>: the whole purpose of the pin is to keep an
    /// existing user's budget stable across a later change of that policy default.
    /// </summary>
    public const int LegacyEffectivePreparationLimit = 10;

    internal const string PreparationLimitPreferenceKey = "preparation_limit";

    /// <summary>
    /// Preference keys that can only exist because a pre-onboarding KnownFirst build already ran
    /// on this device. Owned here rather than in <c>KnownFirst.Core</c> because these are concrete
    /// application preference keys, not domain policy.
    /// </summary>
    internal static IReadOnlyList<string> LegacyPreferenceEvidenceKeys { get; } = Array.AsReadOnly<string>(
    [
        "knownfirst.uiLanguage",
        "theme_preference",
        "whats_new_seen_version",
        PreparationLimitPreferenceKey,
        "card_direction",
        "learning_mode",
        "online_lookup_consent",
        "enhanced_term_recognition_enabled",
        "learning_timezone_mode",
        "explicit_learning_timezone_id",
        "learning_day_cutoff_minutes"
    ]);

    public OnboardingState EnsureClassified()
    {
        if (stateStore.GetState() is { } alreadyClassified)
        {
            logger.LogDebug(
                "Install origin was already classified. OnboardingState = {OnboardingState}",
                alreadyClassified);
            return alreadyClassified;
        }

        var evidenceKey = LegacyPreferenceEvidenceKeys
            .FirstOrDefault(key => preferences.ContainsKey(key));

        if (evidenceKey is null)
        {
            stateStore.SetState(OnboardingState.Required);
            logger.LogInformation(
                "No legacy KnownFirst preference evidence was found. The installation is classified as genuinely fresh and onboarding is required.");
            return OnboardingState.Required;
        }

        stateStore.SetState(OnboardingState.Completed);
        PinLegacyPreparationLimit();
        logger.LogInformation(
            "Legacy KnownFirst preference evidence was found ('{EvidenceKey}'). The existing installation is grandfathered and onboarding is not shown.",
            evidenceKey);
        return OnboardingState.Completed;
    }

    /// <summary>
    /// An existing installation that never opened Settings has no persisted daily new-word budget
    /// and silently inherits whatever the policy default happens to be. Pinning the legacy value
    /// here — before application settings are first read — keeps that user's budget unchanged
    /// across the upgrade. An already persisted value is never touched.
    /// </summary>
    private void PinLegacyPreparationLimit()
    {
        if (preferences.ContainsKey(PreparationLimitPreferenceKey))
        {
            return;
        }

        preferences.Set(PreparationLimitPreferenceKey, LegacyEffectivePreparationLimit);
        logger.LogInformation(
            "The legacy daily new-word budget {PreparationLimit} was pinned for the grandfathered installation.",
            LegacyEffectivePreparationLimit);
    }
}
