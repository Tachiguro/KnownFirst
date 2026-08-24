using KnownFirst.Core.Language;
using KnownFirst.Core.Settings;
using KnownFirst.Services.Onboarding;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Settings;

/// <summary>
/// Single owner of the settings-default restoration policy.
/// <para>
/// The service restores only preference-level state: application settings, the theme preference,
/// and the language preference. It deliberately depends on nothing that can reach user content, so
/// it can never delete vocabulary, known words, learning cards, review history, imported documents,
/// Schema-12 learning-day state or grants, portable user data, or What's New seen state. It also
/// never clears the whole preference store.
/// </para>
/// <para>
/// Online-dictionary lookup consent is one of the two values the flows treat differently:
/// <see cref="RestoreDefaults"/> preserves whatever the user currently has, while
/// <see cref="RestoreDefaultsForFullReset"/> always ends with consent revoked. Keeping both in this
/// one service means the shared defaults cannot drift apart while the consent contracts stay
/// explicit and separately testable.
/// </para>
/// <para>
/// Onboarding state is the other: <see cref="RestoreDefaults"/> never reads or writes it, so a
/// completed onboarding stays completed, while <see cref="RestoreDefaultsForFullReset"/> positively
/// establishes <see cref="OnboardingState.Required"/> so a fully reset installation starts over.
/// </para>
/// </summary>
public sealed class SettingsDefaultsService(
    IAppSettingsService appSettings,
    IThemePreferenceReset themePreferenceReset,
    ILanguageSelectionService languageSelection,
    IOnboardingStateStore onboardingStateStore,
    ILogger<SettingsDefaultsService> logger) : ISettingsDefaultsService
{
    public void RestoreDefaults()
    {
        // Captured before the reset because IAppSettingsService.Reset drops the consent key as part
        // of returning every application setting to its default.
        var consentWasGranted = appSettings.HasOnlineLookupConsent;

        RestoreSharedDefaults();

        if (consentWasGranted)
        {
            appSettings.GrantOnlineLookupConsent();
        }

        logger.LogInformation(
            "Default settings were restored. Application settings, theme preference, and language preference are back to their defaults; online dictionary lookup consent was preserved as {HasOnlineLookupConsent} and no user data was affected.",
            appSettings.HasOnlineLookupConsent);
    }

    public void RestoreDefaultsForFullReset()
    {
        // Written first, and deliberately before RestoreSharedDefaults. The full reset flow has
        // just cleared the whole preference store; RestoreSharedDefaults then recreates legacy
        // preference evidence (ResetToDeviceLanguage persists the "system" language marker). If
        // the onboarding marker were written afterwards, any interruption in between would leave
        // an installation that looks pre-existing to the next start, so a freshly reset user would
        // silently never see onboarding again.
        onboardingStateStore.SetState(OnboardingState.Required);

        RestoreSharedDefaults();

        // Deliberately unconditional and never derived from the in-memory consent value. The full
        // reset clears the preference store first, which leaves the previously loaded in-memory
        // consent stale; reading it here could re-grant a consent the destructive reset removed.
        appSettings.RevokeOnlineLookupConsent();

        logger.LogInformation(
            "Default settings were restored as part of the destructive full application reset. Online dictionary lookup consent is revoked and onboarding is required again.");
    }

    private void RestoreSharedDefaults()
    {
        appSettings.Reset();
        themePreferenceReset.ResetPreference();
        languageSelection.ResetToDeviceLanguage();
    }
}
