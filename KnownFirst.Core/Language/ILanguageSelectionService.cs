namespace KnownFirst.Core.Language;

public interface ILanguageSelectionService
{
    event EventHandler? UiLanguageChanged;

    string CurrentUiLanguage { get; }

    bool IsSystemPreferenceActive { get; }

    string? PreviewUiLanguage => null;

    bool IsSystemPreviewActive => false;

    IReadOnlyList<string> SupportedUiLanguages { get; }

    void Initialize();

    void SetUiLanguage(string languageCode);

    void ResetToDeviceLanguage();

    void ReapplyCurrentCulture();

    void ApplyPreviewLanguage(string languageCode) { }

    void ClearPreview() { }
}
