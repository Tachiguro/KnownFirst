namespace KnownFirst.Core.Text;

public sealed record VocabularyCandidate(
    string Identity,
    string CanonicalTerm,
    TokenKind Kind,
    IReadOnlyDictionary<string, int> SurfaceForms,
    IReadOnlyList<TokenOccurrence> Occurrences)
{
    public IReadOnlyList<string> EncounteredForms => EncounteredFormPolicy.Deduplicate(
        Kind,
        Occurrences.OrderBy(occurrence => occurrence.Order).Select(occurrence => occurrence.SurfaceForm));
}
