namespace KnownFirst.Services.Lexical.Wikipedia;

public sealed record WikipediaArticleRequest
{
    public WikipediaArticleRequest(
        string sourceLanguage,
        string requestedTitle,
        string? targetLanguage = null)
    {
        if (sourceLanguage != "en" && sourceLanguage != "de")
        {
            throw new ArgumentException("SourceLanguage must be 'en' or 'de'.", nameof(sourceLanguage));
        }

        if (string.IsNullOrWhiteSpace(requestedTitle))
        {
            throw new ArgumentException("RequestedTitle must not be empty.", nameof(requestedTitle));
        }

        if (targetLanguage != null && targetLanguage != "en" && targetLanguage != "de")
        {
            throw new ArgumentException("TargetLanguage must be 'en' or 'de'.", nameof(targetLanguage));
        }

        if (targetLanguage != null && string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("TargetLanguage must not be identical to SourceLanguage.", nameof(targetLanguage));
        }

        SourceLanguage = sourceLanguage;
        RequestedTitle = requestedTitle.Trim().Normalize(System.Text.NormalizationForm.FormC);
        TargetLanguage = targetLanguage;
    }

    public string SourceLanguage { get; }
    public string RequestedTitle { get; }
    public string? TargetLanguage { get; }
}
