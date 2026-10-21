namespace KnownFirst.Models;

/// <summary>
/// Compact immutable projection representing a recent vocabulary identity for the vocabulary overview.
/// </summary>
public sealed record VocabularyOverviewItem(
    int WordId,
    string CanonicalTerm,
    string SourceLanguage,
    DateTime CreatedAt)
{
    public int Id => WordId;

    public string Language => SourceLanguage;
}
