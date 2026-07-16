using System.Globalization;

namespace KnownFirst.Core.Language;

public sealed class SystemUiCultureContext : IUiCultureContext
{
    public UiCultureState ApplyUiCulture(string languageCode)
    {
        var cultureName = languageCode == LanguagePreferencePolicy.GermanLanguageCode
            ? "de-DE"
            : "en-US";
        var culture = CultureInfo.GetCultureInfo(cultureName);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        return new UiCultureState(
            CultureInfo.CurrentCulture.Name,
            CultureInfo.CurrentUICulture.Name,
            CultureInfo.DefaultThreadCurrentCulture?.Name ?? string.Empty,
            CultureInfo.DefaultThreadCurrentUICulture?.Name ?? string.Empty);
    }
}
