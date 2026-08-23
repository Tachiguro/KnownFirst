namespace KnownFirst.Core.Settings;

/// <summary>
/// Minimal reset seam for the theme preference. It exists so non-visual services can restore the
/// theme default without depending on the MAUI application/theme surface.
/// </summary>
public interface IThemePreferenceReset
{
    void ResetPreference();
}
