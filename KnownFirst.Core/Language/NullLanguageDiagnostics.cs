namespace KnownFirst.Core.Language;

public sealed class NullLanguageDiagnostics : ILanguageDiagnostics
{
    public static NullLanguageDiagnostics Instance { get; } = new();

    private NullLanguageDiagnostics()
    {
    }

    public void LogInitialization(bool savedPreferenceExists, string? storedLanguage)
    {
    }

    public void LogDeviceCultureDetected(string deviceCulture)
    {
    }

    public void LogStartupLanguageResolved(string languageCode)
    {
    }

    public void LogManualLanguageRequested(string requestedLanguage)
    {
    }

    public void LogPreferencePersisted(string languageCode)
    {
    }

    public void LogCultureApplied(UiCultureState cultureState)
    {
    }
}
