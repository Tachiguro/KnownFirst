using KnownFirst.Core.Language;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services;

public sealed class MauiLanguagePreferenceStore(IPreferences preferences) : ILanguagePreferenceStore
{
    public const string PreferenceKey = "knownfirst.uiLanguage";

    public bool HasSavedLanguage => preferences.ContainsKey(PreferenceKey);

    public string? GetSavedLanguage() =>
        HasSavedLanguage
            ? preferences.Get(PreferenceKey, string.Empty)
            : null;

    public void SetSavedLanguage(string languageCode)
    {
        preferences.Set(PreferenceKey, languageCode);
    }
}
