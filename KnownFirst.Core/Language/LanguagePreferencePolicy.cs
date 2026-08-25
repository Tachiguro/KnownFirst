using System.Globalization;

namespace KnownFirst.Core.Language;

public readonly record struct DeviceLanguageClassification(
    bool IsSupported,
    string EffectiveLanguage);

public readonly record struct UiLanguageOption(
    string PreferenceCode,
    string LocalizationKey);

public static class LanguagePreferencePolicy
{
    public const string EnglishLanguageCode = "en";
    public const string GermanLanguageCode = "de";
    public const string RussianLanguageCode = "ru";
    public const string SystemPreferenceCode = "system";

    public static IReadOnlyList<UiLanguageOption> UiLanguageOptions { get; } =
        Array.AsReadOnly(new UiLanguageOption[]
        {
            new(SystemPreferenceCode, "Settings_UILanguageSystem"),
            new(EnglishLanguageCode, "Settings_English"),
            new(GermanLanguageCode, "Settings_German"),
            new(RussianLanguageCode, "Settings_Russian")
        });

    private static readonly IReadOnlyList<string> LanguageCodes =
        Array.AsReadOnly([EnglishLanguageCode, GermanLanguageCode, RussianLanguageCode]);

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

    /// <summary>
    /// A stored preference represents System when it is missing (legacy/fresh install),
    /// explicitly "system", or malformed/unsupported. Malformed values fail safely to System
    /// rather than crashing or silently freezing on an arbitrary default.
    /// </summary>
    public static bool IsSystemPreference(string? storedPreference) =>
        storedPreference is null
        || IsExplicitSystemSelector(storedPreference)
        || !TryNormalizeSupportedLanguage(storedPreference, out _);

    /// <summary>
    /// True only for the literal "system" selector value. Used to validate explicit user
    /// input (e.g. SetUiLanguage), which must still reject genuinely invalid codes rather
    /// than silently treating them as System.
    /// </summary>
    public static bool IsExplicitSystemSelector(string? value) =>
        value is not null
        && string.Equals(value.Trim(), SystemPreferenceCode, StringComparison.OrdinalIgnoreCase);

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
            RussianLanguageCode => RussianLanguageCode,
            EnglishLanguageCode => EnglishLanguageCode,
            _ => EnglishLanguageCode
        };
    }

    public static DeviceLanguageClassification ClassifyDeviceCulture(string? deviceCulture)
    {
        if (string.IsNullOrWhiteSpace(deviceCulture))
        {
            return new DeviceLanguageClassification(false, EnglishLanguageCode);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(deviceCulture.Trim().Replace('_', '-'));
            var languageFamily = culture.TwoLetterISOLanguageName.ToLowerInvariant();

            return TryNormalizeSupportedLanguage(languageFamily, out var effectiveLanguage)
                ? new DeviceLanguageClassification(true, effectiveLanguage)
                : new DeviceLanguageClassification(false, EnglishLanguageCode);
        }
        catch (CultureNotFoundException)
        {
            return new DeviceLanguageClassification(false, EnglishLanguageCode);
        }
    }

    public static bool TryNormalizeSupportedLanguage(string? value, out string normalizedLanguage)
    {
        var candidate = value?.Trim().ToLowerInvariant();

        if (candidate is EnglishLanguageCode or GermanLanguageCode or RussianLanguageCode)
        {
            normalizedLanguage = candidate;
            return true;
        }

        normalizedLanguage = EnglishLanguageCode;
        return false;
    }
}
