namespace KnownFirst.Services;

public sealed class SettingsFeedbackService : ISettingsFeedbackService
{
    private string? _pendingLanguageConfirmation;

    public void SetPendingLanguageConfirmation(string languageCode)
    {
        _pendingLanguageConfirmation = languageCode;
    }

    public string? ConsumePendingLanguageConfirmation()
    {
        var languageCode = _pendingLanguageConfirmation;
        _pendingLanguageConfirmation = null;
        return languageCode;
    }

    public void ClearPendingLanguageConfirmation()
    {
        _pendingLanguageConfirmation = null;
    }
}
