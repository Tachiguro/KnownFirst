namespace KnownFirst.Core.Language;

public sealed class LanguageSelectionService : ILanguageSelectionService
{
    private readonly ILanguagePreferenceStore _preferenceStore;
    private readonly IDeviceCultureProvider _deviceCultureProvider;
    private readonly IUiCultureContext _cultureContext;
    private readonly ILanguageDiagnostics _diagnostics;

    public LanguageSelectionService(
        ILanguagePreferenceStore preferenceStore,
        IDeviceCultureProvider deviceCultureProvider,
        IUiCultureContext cultureContext,
        ILanguageDiagnostics? diagnostics = null)
    {
        _preferenceStore = preferenceStore;
        _deviceCultureProvider = deviceCultureProvider;
        _cultureContext = cultureContext;
        _diagnostics = diagnostics ?? NullLanguageDiagnostics.Instance;
    }

    public const string EnglishLanguageCode = LanguagePreferencePolicy.EnglishLanguageCode;
    public const string GermanLanguageCode = LanguagePreferencePolicy.GermanLanguageCode;
    public const string RussianLanguageCode = LanguagePreferencePolicy.RussianLanguageCode;
    public const string SystemPreferenceCode = LanguagePreferencePolicy.SystemPreferenceCode;

    public event EventHandler? UiLanguageChanged;

    public string CurrentUiLanguage { get; private set; } = EnglishLanguageCode;

    public bool IsSystemPreferenceActive { get; private set; }

    public string? PreviewUiLanguage { get; private set; }

    public bool IsSystemPreviewActive { get; private set; }

    public IReadOnlyList<string> SupportedUiLanguages => LanguagePreferencePolicy.SupportedLanguageCodes;

    public void Initialize()
    {
        PreviewUiLanguage = null;
        IsSystemPreviewActive = false;

        var hasSavedPreference = _preferenceStore.HasSavedLanguage;
        var storedPreference = hasSavedPreference ? _preferenceStore.GetSavedLanguage() : null;
        _diagnostics.LogInitialization(hasSavedPreference, storedPreference);

        if (LanguagePreferencePolicy.IsSystemPreference(storedPreference))
        {
            var deviceCulture = _deviceCultureProvider.GetDeviceCultureName();
            _diagnostics.LogDeviceCultureDetected(deviceCulture);
            var selectedLanguage = LanguagePreferencePolicy.Normalize(deviceCulture);

            if (!hasSavedPreference)
            {
                PersistSystemMarker();
            }

            ApplyCulture(selectedLanguage);
            CurrentUiLanguage = selectedLanguage;
            IsSystemPreferenceActive = true;
            _diagnostics.LogStartupLanguageResolved(selectedLanguage);
            return;
        }

        LanguagePreferencePolicy.TryNormalizeSupportedLanguage(storedPreference, out var manualLanguage);
        ApplyCulture(manualLanguage);
        CurrentUiLanguage = manualLanguage;
        IsSystemPreferenceActive = false;
        _diagnostics.LogStartupLanguageResolved(manualLanguage);
    }

    public void ApplyPreviewLanguage(string languageCode)
    {
        _diagnostics.LogManualLanguageRequested(languageCode);

        if (LanguagePreferencePolicy.IsExplicitSystemSelector(languageCode))
        {
            var deviceCulture = _deviceCultureProvider.GetDeviceCultureName();
            _diagnostics.LogDeviceCultureDetected(deviceCulture);
            var selectedLanguage = LanguagePreferencePolicy.Normalize(deviceCulture);

            var previewChanged = !IsSystemPreviewActive || !string.Equals(PreviewUiLanguage, selectedLanguage, StringComparison.Ordinal);
            PreviewUiLanguage = selectedLanguage;
            IsSystemPreviewActive = true;
            ApplyCulture(selectedLanguage);

            if (previewChanged)
            {
                UiLanguageChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (!LanguagePreferencePolicy.TryNormalizeSupportedLanguage(languageCode, out var normalizedLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(languageCode));
        }

        var manualPreviewChanged = IsSystemPreviewActive || !string.Equals(PreviewUiLanguage, normalizedLanguage, StringComparison.Ordinal);
        PreviewUiLanguage = normalizedLanguage;
        IsSystemPreviewActive = false;
        ApplyCulture(normalizedLanguage);

        if (manualPreviewChanged)
        {
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearPreview()
    {
        if (PreviewUiLanguage is null && !IsSystemPreviewActive)
        {
            return;
        }

        PreviewUiLanguage = null;
        IsSystemPreviewActive = false;

        if (IsSystemPreferenceActive)
        {
            var deviceCulture = _deviceCultureProvider.GetDeviceCultureName();
            _diagnostics.LogDeviceCultureDetected(deviceCulture);
            var selectedLanguage = LanguagePreferencePolicy.Normalize(deviceCulture);
            ApplyCulture(selectedLanguage);
            CurrentUiLanguage = selectedLanguage;
        }
        else
        {
            ApplyCulture(CurrentUiLanguage);
        }

        UiLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetUiLanguage(string languageCode)
    {
        _diagnostics.LogManualLanguageRequested(languageCode);

        if (LanguagePreferencePolicy.IsExplicitSystemSelector(languageCode))
        {
            var noPreview = PreviewUiLanguage is null && !IsSystemPreviewActive;
            var storeHasSystem = _preferenceStore.HasSavedLanguage && LanguagePreferencePolicy.IsExplicitSystemSelector(_preferenceStore.GetSavedLanguage());
            if (noPreview && IsSystemPreferenceActive && storeHasSystem)
            {
                return;
            }

            PreviewUiLanguage = null;
            IsSystemPreviewActive = false;
            ApplySystemSelection(persistExplicitMarker: true);
            return;
        }

        if (!LanguagePreferencePolicy.TryNormalizeSupportedLanguage(languageCode, out var normalizedLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(languageCode));
        }

        var noPreviewActive = PreviewUiLanguage is null && !IsSystemPreviewActive;
        var storeHasManual = _preferenceStore.HasSavedLanguage && string.Equals(_preferenceStore.GetSavedLanguage(), normalizedLanguage, StringComparison.Ordinal);

        if (noPreviewActive
            && !IsSystemPreferenceActive
            && string.Equals(CurrentUiLanguage, normalizedLanguage, StringComparison.Ordinal)
            && storeHasManual)
        {
            return;
        }

        PreviewUiLanguage = null;
        IsSystemPreviewActive = false;
        PersistAndVerify(normalizedLanguage);
        ApplyCulture(normalizedLanguage);
        CurrentUiLanguage = normalizedLanguage;
        IsSystemPreferenceActive = false;
        UiLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDeviceLanguage()
    {
        PreviewUiLanguage = null;
        IsSystemPreviewActive = false;
        ApplySystemSelection(persistExplicitMarker: true);
    }

    public void ReapplyCurrentCulture()
    {
        var effectiveLanguage = PreviewUiLanguage ?? CurrentUiLanguage;
        ApplyCulture(effectiveLanguage);
    }

    private void ApplySystemSelection(bool persistExplicitMarker)
    {
        var deviceCulture = _deviceCultureProvider.GetDeviceCultureName();
        _diagnostics.LogDeviceCultureDetected(deviceCulture);
        var selectedLanguage = LanguagePreferencePolicy.Normalize(deviceCulture);

        if (persistExplicitMarker)
        {
            PersistSystemMarker();
        }

        ApplyCulture(selectedLanguage);
        CurrentUiLanguage = selectedLanguage;
        IsSystemPreferenceActive = true;
        _diagnostics.LogStartupLanguageResolved(selectedLanguage);
        UiLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PersistSystemMarker()
    {
        _preferenceStore.SetSavedLanguage(LanguagePreferencePolicy.SystemPreferenceCode);
        var storedPreference = _preferenceStore.GetSavedLanguage();

        if (!LanguagePreferencePolicy.IsExplicitSystemSelector(storedPreference))
        {
            throw new InvalidOperationException("The System language preference could not be verified after persistence.");
        }

        _diagnostics.LogPreferencePersisted(LanguagePreferencePolicy.SystemPreferenceCode);
    }

    private void PersistAndVerify(string languageCode)
    {
        _preferenceStore.SetSavedLanguage(languageCode);
        var storedLanguage = _preferenceStore.GetSavedLanguage();

        if (!LanguagePreferencePolicy.TryNormalizeSupportedLanguage(storedLanguage, out var verifiedLanguage)
            || !string.Equals(verifiedLanguage, languageCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The language preference could not be verified after persistence.");
        }

        _diagnostics.LogPreferencePersisted(languageCode);
    }

    private void ApplyCulture(string languageCode)
    {
        var cultureState = _cultureContext.ApplyUiCulture(languageCode);
        _diagnostics.LogCultureApplied(cultureState);
    }
}
