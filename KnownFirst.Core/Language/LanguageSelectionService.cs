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

    public event EventHandler? UiLanguageChanged;

    public string CurrentUiLanguage { get; private set; } = EnglishLanguageCode;

    public IReadOnlyList<string> SupportedUiLanguages => LanguagePreferencePolicy.SupportedLanguageCodes;

    public void Initialize()
    {
        var savedPreferenceExists = _preferenceStore.HasSavedLanguage;
        var savedLanguage = savedPreferenceExists ? _preferenceStore.GetSavedLanguage() : null;
        _diagnostics.LogInitialization(savedPreferenceExists, savedLanguage);

        string selectedLanguage;
        if (savedPreferenceExists)
        {
            selectedLanguage = LanguagePreferencePolicy.Resolve(savedLanguage, deviceCulture: null);
        }
        else
        {
            var deviceCulture = _deviceCultureProvider.GetDeviceCultureName();
            _diagnostics.LogDeviceCultureDetected(deviceCulture);
            selectedLanguage = LanguagePreferencePolicy.Resolve(savedLanguage: null, deviceCulture);
            PersistAndVerify(selectedLanguage);
        }

        ApplyCulture(selectedLanguage);
        CurrentUiLanguage = selectedLanguage;
        _diagnostics.LogStartupLanguageResolved(selectedLanguage);
    }

    public void SetUiLanguage(string languageCode)
    {
        _diagnostics.LogManualLanguageRequested(languageCode);

        if (!LanguagePreferencePolicy.TryNormalizeSupportedLanguage(languageCode, out var normalizedLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(languageCode));
        }

        if (string.Equals(CurrentUiLanguage, normalizedLanguage, StringComparison.Ordinal))
        {
            return;
        }

        PersistAndVerify(normalizedLanguage);
        ApplyCulture(normalizedLanguage);
        CurrentUiLanguage = normalizedLanguage;
        UiLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDeviceLanguage()
    {
        var deviceCulture = _deviceCultureProvider.GetDeviceCultureName();
        _diagnostics.LogDeviceCultureDetected(deviceCulture);
        var selectedLanguage = LanguagePreferencePolicy.Resolve(savedLanguage: null, deviceCulture);

        PersistAndVerify(selectedLanguage);
        ApplyCulture(selectedLanguage);
        CurrentUiLanguage = selectedLanguage;
        _diagnostics.LogStartupLanguageResolved(selectedLanguage);
        UiLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReapplyCurrentCulture()
    {
        ApplyCulture(CurrentUiLanguage);
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
