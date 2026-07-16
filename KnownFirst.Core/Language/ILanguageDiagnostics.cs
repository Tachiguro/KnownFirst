namespace KnownFirst.Core.Language;

public interface ILanguageDiagnostics
{
    void LogInitialization(bool savedPreferenceExists, string? storedLanguage);

    void LogDeviceCultureDetected(string deviceCulture);

    void LogStartupLanguageResolved(string languageCode);

    void LogManualLanguageRequested(string requestedLanguage);

    void LogPreferencePersisted(string languageCode);

    void LogCultureApplied(UiCultureState cultureState);
}
