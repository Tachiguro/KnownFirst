using KnownFirst.Core.Settings;
using Microsoft.Maui.ApplicationModel;

namespace KnownFirst.Services;

public interface IThemeApplication
{
    AppTheme UserAppTheme { get; set; }
    AppTheme RequestedTheme { get; }
    event EventHandler? RequestedThemeChanged;
}

public interface IThemeService : IThemePreferenceReset
{
    event EventHandler? ThemeChanged;

    ThemePreference Preference { get; }

    ThemePreference? PreviewPreference { get; }

    ThemePreference EffectiveTheme { get; }

    string EffectiveThemeCssName { get; }

#if ANDROID || WINDOWS
    void Initialize(Microsoft.Maui.Controls.Application application);
#endif

    void Initialize(IThemeApplication application);

    bool SetPreference(ThemePreference preference);

    void ApplyPreviewPreference(ThemePreference preference);

    void ClearPreview();
}
