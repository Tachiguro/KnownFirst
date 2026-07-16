namespace KnownFirst.Core.Language;

public static class LanguagePreferencePolicy
{
    public const string EnglishLanguageCode = "en";
    public const string GermanLanguageCode = "de";

    private static readonly IReadOnlyList<string> LanguageCodes =
        Array.AsReadOnly([EnglishLanguageCode, GermanLanguageCode]);

    public static IReadOnlyList<string> SupportedLanguageCodes => LanguageCodes;

    public static string Resolve(string? savedLanguage, string? deviceCulture)
    {
        if (savedLanguage is not null)
        {
            return TryNormalizeSupportedLanguage(savedLanguage, out var normalizedSavedLanguage)
                ? normalizedSavedLanguage
                : EnglishLanguageCode;
        }

        return Normalize(deviceCulture);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EnglishLanguageCode;
        }

        var languageFamily = value.Trim()
            .Replace('_', '-')
            .Split('-', 2, StringSplitOptions.TrimEntries)[0]
            .ToLowerInvariant();

        return languageFamily switch
        {
            GermanLanguageCode => GermanLanguageCode,
            EnglishLanguageCode => EnglishLanguageCode,
            _ => EnglishLanguageCode
        };
    }

    public static bool TryNormalizeSupportedLanguage(string? value, out string normalizedLanguage)
    {
        var candidate = value?.Trim().ToLowerInvariant();

        if (candidate is EnglishLanguageCode or GermanLanguageCode)
        {
            normalizedLanguage = candidate;
            return true;
        }

        normalizedLanguage = EnglishLanguageCode;
        return false;
    }
}
