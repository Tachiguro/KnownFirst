namespace KnownFirst.Core.Language;

public interface ILanguageSelectionService
{
    event EventHandler? UiLanguageChanged;

    string CurrentUiLanguage { get; }

    IReadOnlyList<string> SupportedUiLanguages { get; }

    void Initialize();

    void SetUiLanguage(string languageCode);

    void ResetToDeviceLanguage();

    void ReapplyCurrentCulture();
}
