using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services;

public sealed class ThemeService(ILogger<ThemeService> logger) : IThemeService, IDisposable
{
    private const string ThemePreferenceKey = "theme_preference";
    private Microsoft.Maui.Controls.Application? _application;
    private bool _initialized;

    public event EventHandler? ThemeChanged;

    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    public ThemePreference EffectiveTheme { get; private set; } = ThemePreference.Light;

    public string EffectiveThemeCssName =>
        EffectiveTheme == ThemePreference.Dark ? "dark" : "light";

    public void Initialize(Microsoft.Maui.Controls.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (_initialized)
        {
            return;
        }

        _application = application;
        Preference = ReadPreference();
        _application.RequestedThemeChanged += OnRequestedThemeChanged;
        _initialized = true;

        ApplyNativeTheme();
        EffectiveTheme = ResolveEffectiveTheme(_application.RequestedTheme);
        logger.LogInformation(
            "Theme initialized. Preference = {ThemePreference}, effective theme = {EffectiveTheme}",
            Preference,
            EffectiveTheme);
    }

    public bool SetPreference(ThemePreference preference)
    {
        EnsureInitialized();

        var normalizedPreference = ThemePreferencePolicy.Normalize((int)preference);
        if (normalizedPreference != preference)
        {
            logger.LogWarning(
                "The requested theme preference '{ThemePreference}' is unsupported. Falling back to System.",
                preference);
        }

        if (Preference == normalizedPreference)
        {
            return false;
        }

        Preferences.Default.Set(ThemePreferenceKey, (int)normalizedPreference);
        Preference = normalizedPreference;
        ApplyNativeTheme();
        UpdateEffectiveTheme(ResolveEffectiveTheme(_application!.RequestedTheme), notify: true);
        logger.LogInformation(
            "Theme preference changed. Preference = {ThemePreference}, effective theme = {EffectiveTheme}",
            Preference,
            EffectiveTheme);
        return true;
    }

    public void ResetPreference()
    {
        EnsureInitialized();

        var preferenceChanged = Preference != ThemePreference.System;
        Preferences.Default.Remove(ThemePreferenceKey);
        Preference = ThemePreference.System;
        ApplyNativeTheme();

        var effectiveTheme = ResolveEffectiveTheme(_application!.RequestedTheme);
        var effectiveThemeChanged = EffectiveTheme != effectiveTheme;
        EffectiveTheme = effectiveTheme;

        if (preferenceChanged || effectiveThemeChanged)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        logger.LogInformation(
            "Theme preference reset. Preference = {ThemePreference}, effective theme = {EffectiveTheme}",
            Preference,
            EffectiveTheme);
    }

    public void Dispose()
    {
        if (_application is not null)
        {
            _application.RequestedThemeChanged -= OnRequestedThemeChanged;
            _application = null;
        }

        _initialized = false;
    }

    private ThemePreference ReadPreference()
    {
        var savedValue = Preferences.Default.Get(ThemePreferenceKey, (int)ThemePreference.System);
        var normalizedPreference = ThemePreferencePolicy.Normalize(savedValue);
        if ((int)normalizedPreference == savedValue)
        {
            return normalizedPreference;
        }

        logger.LogWarning(
            "The saved theme preference value '{ThemePreference}' is unsupported. Falling back to System.",
            savedValue);
        Preferences.Default.Set(ThemePreferenceKey, (int)ThemePreference.System);
        return normalizedPreference;
    }

    private void ApplyNativeTheme()
    {
        _application!.UserAppTheme = Preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    private ThemePreference ResolveEffectiveTheme(AppTheme requestedTheme) =>
        Preference switch
        {
            ThemePreference.Light => ThemePreference.Light,
            ThemePreference.Dark => ThemePreference.Dark,
            _ => requestedTheme == AppTheme.Dark ? ThemePreference.Dark : ThemePreference.Light
        };

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs eventArgs)
    {
        if (!_initialized || Preference != ThemePreference.System)
        {
            return;
        }

        UpdateEffectiveTheme(ResolveEffectiveTheme(eventArgs.RequestedTheme), notify: true);
        logger.LogInformation(
            "System theme changed. EffectiveTheme = {EffectiveTheme}",
            EffectiveTheme);
    }

    private void UpdateEffectiveTheme(ThemePreference effectiveTheme, bool notify)
    {
        var changed = EffectiveTheme != effectiveTheme;
        EffectiveTheme = effectiveTheme;

        if (notify && (changed || Preference != ThemePreference.System))
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The theme service has not been initialized.");
        }
    }
}
