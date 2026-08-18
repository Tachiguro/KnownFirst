namespace KnownFirst.Core.Text;

public sealed record VocabularyCandidate(
    string Identity,
    string CanonicalTerm,
    TokenKind Kind,
    IReadOnlyDictionary<string, int> SurfaceForms,
    IReadOnlyList<TokenOccurrence> Occurrences)
{
    /// <summary>
    /// Distinguishes a candidate grouping literal occurrences from one derived from a source
    /// compound. Candidates are <see cref="CandidateProvenanceKind.Direct"/> unless stated otherwise.
    /// </summary>
    public CandidateProvenanceKind Provenance { get; init; } = CandidateProvenanceKind.Direct;

    /// <summary>
    /// Derivation evidence pointing back at literal source compound occurrences. Always empty for
    /// direct candidates.
    /// </summary>
    public IReadOnlyList<DerivedTermEvidence> DerivedEvidence { get; init; } =
        Array.Empty<DerivedTermEvidence>();

    public bool IsDerived => Provenance == CandidateProvenanceKind.DerivedFromCompound;

    /// <summary>
    /// Forms literally encountered through <see cref="Occurrences"/>. Derived stems, canonical
    /// lemmas, <see cref="SurfaceForms"/> keys, and derivation evidence are never substituted here,
    /// so a derived candidate without literal occurrences has no encountered forms.
    /// </summary>
    public IReadOnlyList<string> EncounteredForms => EncounteredFormPolicy.Deduplicate(
        Kind,
        Occurrences.OrderBy(occurrence => occurrence.Order).Select(occurrence => occurrence.SurfaceForm));
}
