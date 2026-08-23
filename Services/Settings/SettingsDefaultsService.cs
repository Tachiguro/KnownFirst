using KnownFirst.Core.Language;
using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Settings;

/// <summary>
/// Single owner of the settings-default restoration policy.
/// <para>
/// The service restores only preference-level state: application settings, the theme preference,
/// and the language preference. It deliberately depends on nothing that can reach user content, so
/// it can never delete vocabulary, known words, learning cards, review history, imported documents,
/// Schema-12 learning-day state or grants, portable user data, onboarding state, or What's New seen
/// state. It also never clears the whole preference store.
/// </para>
/// <para>
/// Online-dictionary lookup consent is the one value the two flows treat differently:
/// <see cref="RestoreDefaults"/> preserves whatever the user currently has, while
/// <see cref="RestoreDefaultsForFullReset"/> always ends with consent revoked. Keeping both in this
/// one service means the shared defaults cannot drift apart while the consent contracts stay
/// explicit and separately testable.
/// </para>
/// </summary>
public sealed class SettingsDefaultsService(
    IAppSettingsService appSettings,
    IThemePreferenceReset themePreferenceReset,
    ILanguageSelectionService languageSelection,
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
        RestoreSharedDefaults();

        // Deliberately unconditional and never derived from the in-memory consent value. The full
        // reset clears the preference store first, which leaves the previously loaded in-memory
        // consent stale; reading it here could re-grant a consent the destructive reset removed.
        appSettings.RevokeOnlineLookupConsent();

        logger.LogInformation(
            "Default settings were restored as part of the destructive full application reset. Online dictionary lookup consent is revoked.");
    }

    private void RestoreSharedDefaults()
    {
        appSettings.Reset();
        themePreferenceReset.ResetPreference();
        languageSelection.ResetToDeviceLanguage();
    }
}
