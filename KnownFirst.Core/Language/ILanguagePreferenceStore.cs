namespace KnownFirst.Core.Language;

public interface ILanguagePreferenceStore
{
    bool HasSavedLanguage { get; }

    string? GetSavedLanguage();

    void SetSavedLanguage(string languageCode);
}
