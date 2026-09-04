namespace KnownFirst.Core.Learning;

/// <summary>
/// Immutable semantic identity of a LearningTarget.
/// Translation targets distinguish source and target language (e.g. German -> English != English -> German).
/// Definition targets explicitly identify the word's lexical language and definition language.
/// UI locale is never semantic language truth.
/// </summary>
public sealed record LearningTargetIdentity
{
    public LearningTargetKind Kind { get; }
    public string SourceLanguage { get; }
    public string TargetLanguage { get; }

    public LearningTargetIdentity(
        LearningTargetKind kind,
        string sourceLanguage,
        string targetLanguage)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Invalid learning target kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        Kind = kind;
        SourceLanguage = sourceLanguage.Trim().ToLowerInvariant();
        TargetLanguage = targetLanguage.Trim().ToLowerInvariant();
    }

    public static LearningTargetIdentity Definition(string sourceLanguage, string definitionLanguage) =>
        new(LearningTargetKind.Definition, sourceLanguage, definitionLanguage);

    public static LearningTargetIdentity Translation(string sourceLanguage, string targetLanguage) =>
        new(LearningTargetKind.Translation, sourceLanguage, targetLanguage);
}
