using KnownFirst.Core.Language;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services;

public sealed class LanguageDiagnostics(ILogger<LanguageDiagnostics> logger) : ILanguageDiagnostics
{
    public void LogInitialization(bool savedPreferenceExists, string? storedLanguage)
    {
        logger.LogDebug(
            "Language initialization: saved preference exists = {SavedPreferenceExists}, value = {StoredLanguage}",
            savedPreferenceExists,
            storedLanguage ?? "<none>");
    }

    public void LogDeviceCultureDetected(string deviceCulture)
    {
        logger.LogDebug("Device culture detected for first launch: {DeviceCulture}", deviceCulture);
    }

    public void LogStartupLanguageResolved(string languageCode)
    {
        logger.LogInformation("Startup UI language resolved: {LanguageCode}", languageCode);
    }

    public void LogManualLanguageRequested(string requestedLanguage)
    {
        logger.LogInformation("Language change requested: {RequestedLanguage}", requestedLanguage);
    }

    public void LogPreferencePersisted(string languageCode)
    {
        logger.LogInformation("Language preference persisted and verified: {LanguageCode}", languageCode);
    }

    public void LogCultureApplied(UiCultureState cultureState)
    {
        logger.LogInformation(
            "Culture applied: CurrentCulture = {CurrentCulture}, CurrentUICulture = {CurrentUiCulture}, DefaultThreadCurrentCulture = {DefaultThreadCurrentCulture}, DefaultThreadCurrentUICulture = {DefaultThreadCurrentUiCulture}",
            cultureState.CurrentCulture,
            cultureState.CurrentUiCulture,
            cultureState.DefaultThreadCurrentCulture,
            cultureState.DefaultThreadCurrentUiCulture);
    }
}
