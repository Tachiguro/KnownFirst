using System.Reflection;
using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services;

public sealed class ThemeService(IPreferences preferences, ILogger<ThemeService> logger) : IThemeService, IDisposable
{
    private const string ThemePreferenceKey = "theme_preference";
    private IThemeApplication? _application;
    private bool _initialized;

    public event EventHandler? ThemeChanged;

    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    public ThemePreference? PreviewPreference { get; private set; }

    public ThemePreference EffectiveTheme { get; private set; } = ThemePreference.Light;

    public string EffectiveThemeCssName =>
        EffectiveTheme == ThemePreference.Dark ? "dark" : "light";

    public void Initialize(Microsoft.Maui.Controls.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        Initialize(new MauiApplicationAdapter(application));
    }

    public void Initialize(object application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application is Microsoft.Maui.Controls.Application mauiApp)
        {
            Initialize(new MauiApplicationAdapter(mauiApp));
            return;
        }

        if (application is IThemeApplication themeApp)
        {
            Initialize(themeApp);
            return;
        }

        Initialize(new DuckTypedApplicationAdapter(application));
    }

    public void Initialize(IThemeApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (_initialized)
        {
            return;
        }

        _application = application;
        Preference = ReadPreference();
        PreviewPreference = null;
        _application.RequestedThemeChanged += OnRequestedThemeChanged;
        _initialized = true;

        ApplyNativeTheme();
        EffectiveTheme = ResolveEffectiveTheme(_application.RequestedTheme);
        logger.LogInformation(
            "Theme initialized. Preference = {ThemePreference}, effective theme = {EffectiveTheme}",
            Preference,
            EffectiveTheme);
    }

    public void ApplyPreviewPreference(ThemePreference preference)
    {
        EnsureInitialized();

        var normalizedPreference = ThemePreferencePolicy.Normalize((int)preference);
        if (normalizedPreference != preference)
        {
            logger.LogWarning(
                "The requested preview theme preference '{ThemePreference}' is unsupported. Falling back to System.",
                preference);
        }

        var previewChanged = PreviewPreference != normalizedPreference;
        PreviewPreference = normalizedPreference;
        ApplyNativeTheme();

        var effectiveTheme = ResolveEffectiveTheme(_application!.RequestedTheme);
        UpdateEffectiveTheme(effectiveTheme, notify: true);

        logger.LogInformation(
            "Preview theme preference applied. Preview = {PreviewPreference}, effective theme = {EffectiveTheme}",
            PreviewPreference,
            EffectiveTheme);
    }

    public void ClearPreview()
    {
        EnsureInitialized();

        if (!PreviewPreference.HasValue)
        {
            return;
        }

        PreviewPreference = null;
        ApplyNativeTheme();

        var effectiveTheme = ResolveEffectiveTheme(_application!.RequestedTheme);
        UpdateEffectiveTheme(effectiveTheme, notify: true);

        logger.LogInformation(
            "Preview theme preference cleared. Preference = {ThemePreference}, effective theme = {EffectiveTheme}",
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

        var hasPersistedPreference = preferences.ContainsKey(ThemePreferenceKey);
        var persistedValue = preferences.Get(ThemePreferenceKey, (int)ThemePreference.System);
        var storeAlreadyMatches = hasPersistedPreference && persistedValue == (int)normalizedPreference;
        var hadPreview = PreviewPreference.HasValue;

        if (!hadPreview && Preference == normalizedPreference && storeAlreadyMatches)
        {
            return false;
        }

        preferences.Set(ThemePreferenceKey, (int)normalizedPreference);
        Preference = normalizedPreference;
        PreviewPreference = null;
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

        var hadPreview = PreviewPreference.HasValue;
        var preferenceChanged = Preference != ThemePreference.System;
        PreviewPreference = null;
        preferences.Remove(ThemePreferenceKey);
        Preference = ThemePreference.System;
        ApplyNativeTheme();

        var effectiveTheme = ResolveEffectiveTheme(_application!.RequestedTheme);
        var effectiveThemeChanged = EffectiveTheme != effectiveTheme;
        EffectiveTheme = effectiveTheme;

        if (preferenceChanged || effectiveThemeChanged || hadPreview)
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
        var savedValue = preferences.Get(ThemePreferenceKey, (int)ThemePreference.System);
        var normalizedPreference = ThemePreferencePolicy.Normalize(savedValue);
        if ((int)normalizedPreference == savedValue)
        {
            return normalizedPreference;
        }

        logger.LogWarning(
            "The saved theme preference value '{ThemePreference}' is unsupported. Falling back to System.",
            savedValue);
        preferences.Set(ThemePreferenceKey, (int)ThemePreference.System);
        return normalizedPreference;
    }

    private void ApplyNativeTheme()
    {
        var activeSelector = PreviewPreference ?? Preference;
        _application!.UserAppTheme = activeSelector switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    private ThemePreference ResolveEffectiveTheme(AppTheme requestedTheme)
    {
        var activeSelector = PreviewPreference ?? Preference;
        return activeSelector switch
        {
            ThemePreference.Light => ThemePreference.Light,
            ThemePreference.Dark => ThemePreference.Dark,
            _ => requestedTheme == AppTheme.Dark ? ThemePreference.Dark : ThemePreference.Light
        };
    }

    private void OnRequestedThemeChanged(object? sender, EventArgs eventArgs)
    {
        var activeSelector = PreviewPreference ?? Preference;
        if (!_initialized || activeSelector != ThemePreference.System)
        {
            return;
        }

        UpdateEffectiveTheme(ResolveEffectiveTheme(_application!.RequestedTheme), notify: true);
        logger.LogInformation(
            "System theme changed. EffectiveTheme = {EffectiveTheme}",
            EffectiveTheme);
    }

    private void UpdateEffectiveTheme(ThemePreference effectiveTheme, bool notify)
    {
        var changed = EffectiveTheme != effectiveTheme;
        EffectiveTheme = effectiveTheme;

        var activeSelector = PreviewPreference ?? Preference;
        if (notify && (changed || activeSelector != ThemePreference.System))
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

    private sealed class MauiApplicationAdapter : IThemeApplication
    {
        private readonly Microsoft.Maui.Controls.Application _app;

        public MauiApplicationAdapter(Microsoft.Maui.Controls.Application app)
        {
            _app = app;
            _app.RequestedThemeChanged += HandleRequestedThemeChanged;
        }

        public AppTheme UserAppTheme
        {
            get => _app.UserAppTheme;
            set => _app.UserAppTheme = value;
        }

        public AppTheme RequestedTheme => _app.RequestedTheme;

        public event EventHandler? RequestedThemeChanged;

        private void HandleRequestedThemeChanged(object? sender, Microsoft.Maui.Controls.AppThemeChangedEventArgs e)
        {
            RequestedThemeChanged?.Invoke(sender, EventArgs.Empty);
        }
    }

    private sealed class DuckTypedApplicationAdapter : IThemeApplication
    {
        private readonly object _target;
        private readonly PropertyInfo? _userAppThemeProp;
        private readonly PropertyInfo? _requestedThemeProp;

        public DuckTypedApplicationAdapter(object target)
        {
            _target = target;
            var type = target.GetType();
            _userAppThemeProp = type.GetProperty("UserAppTheme");
            _requestedThemeProp = type.GetProperty("RequestedTheme");
            var requestedThemeChangedEvent = type.GetEvent("RequestedThemeChanged");

            if (requestedThemeChangedEvent is not null)
            {
                var handlerType = requestedThemeChangedEvent.EventHandlerType;
                if (handlerType == typeof(EventHandler))
                {
                    EventHandler handler = (s, e) => RequestedThemeChanged?.Invoke(s, e);
                    requestedThemeChangedEvent.AddEventHandler(_target, handler);
                }
            }
        }

        public AppTheme UserAppTheme
        {
            get => (AppTheme)(_userAppThemeProp?.GetValue(_target) ?? AppTheme.Unspecified);
            set => _userAppThemeProp?.SetValue(_target, value);
        }

        public AppTheme RequestedTheme =>
            (AppTheme)(_requestedThemeProp?.GetValue(_target) ?? AppTheme.Light);

        public event EventHandler? RequestedThemeChanged;
    }
}
