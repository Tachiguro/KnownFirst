namespace KnownFirst.Services.Settings;

/// <summary>
/// Shared default-restoration policy for the two distinct Settings reset flows. Both flows use the
/// same defaults so they cannot drift apart, but they deliberately differ in how they treat the
/// online-dictionary lookup consent.
/// </summary>
public interface ISettingsDefaultsService
{
    /// <summary>
    /// Non-destructive "Restore default settings" action. Restores the ordinary settings, theme,
    /// and language defaults and <b>preserves the user's current online-dictionary lookup consent
    /// exactly as it is</b>: granted stays granted, not granted stays not granted.
    /// </summary>
    void RestoreDefaults();

    /// <summary>
    /// Default restoration for the destructive full application reset. Restores the same defaults
    /// but always ends with online-dictionary lookup consent revoked, independently of whatever the
    /// consent state was before the reset.
    /// </summary>
    void RestoreDefaultsForFullReset();
}
