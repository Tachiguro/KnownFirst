namespace KnownFirst.Core.Text;

/// <summary>
/// Distinguishes a vocabulary candidate built from literal occurrences in the source text
/// from a candidate derived from a German source compound.
/// </summary>
public enum CandidateProvenanceKind
{
    /// <summary>The candidate groups occurrences that literally appear in the source text.</summary>
    Direct = 0,

    /// <summary>
    /// The candidate was derived from a source compound. Its canonical term never literally
    /// occurred in the document, so it carries derivation evidence instead of occurrences.
    /// </summary>
    DerivedFromCompound = 1
}
