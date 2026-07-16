namespace KnownFirst.Services;

public interface ISettingsFeedbackService
{
    void SetPendingLanguageConfirmation(string languageCode);

    string? ConsumePendingLanguageConfirmation();

    void ClearPendingLanguageConfirmation();
}
