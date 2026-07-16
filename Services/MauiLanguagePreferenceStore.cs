using KnownFirst.Core.Language;

namespace KnownFirst.Services;

public sealed class MauiLanguagePreferenceStore : ILanguagePreferenceStore
{
    public const string PreferenceKey = "knownfirst.uiLanguage";

    public bool HasSavedLanguage => Preferences.Default.ContainsKey(PreferenceKey);

    public string? GetSavedLanguage() =>
        HasSavedLanguage
            ? Preferences.Default.Get(PreferenceKey, string.Empty)
            : null;

    public void SetSavedLanguage(string languageCode)
    {
        Preferences.Default.Set(PreferenceKey, languageCode);
    }
}
