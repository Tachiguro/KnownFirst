using KnownFirst.Core.Settings;

namespace KnownFirst.Services;

public interface IThemeService
{
    event EventHandler? ThemeChanged;

    ThemePreference Preference { get; }

    ThemePreference EffectiveTheme { get; }

    string EffectiveThemeCssName { get; }

    void Initialize(Microsoft.Maui.Controls.Application application);

    bool SetPreference(ThemePreference preference);

    void ResetPreference();
}
