namespace KnownFirst.Core.Text;

public sealed record VocabularyCandidate(
    string Identity,
    string CanonicalTerm,
    TokenKind Kind,
    IReadOnlyDictionary<string, int> SurfaceForms,
    IReadOnlyList<TokenOccurrence> Occurrences);
