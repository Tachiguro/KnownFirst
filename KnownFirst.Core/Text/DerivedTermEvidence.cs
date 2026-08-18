namespace KnownFirst.Core.Text;

/// <summary>
/// Traces a derived vocabulary candidate back to the literal source compound occurrence it was
/// derived from, without claiming that the derived canonical term itself occurred in the document.
/// </summary>
/// <param name="SourceIdentity">Vocabulary identity of the literal source compound candidate.</param>
/// <param name="SourceSurfaceForm">Exact source compound surface form, e.g. <c>Schreibmaschine</c>.</param>
/// <param name="SourceStartPosition">Start position of the source compound in the original document.</param>
/// <param name="SourceLength">Length of the source compound in the original document.</param>
/// <param name="SourceSentenceOrder">Order of the sentence containing the source compound.</param>
/// <param name="ComponentForm">
/// Inferred component form used for the derivation, e.g. <c>Schreib</c>. This form has no
/// independent source-coordinate semantics: the source range always spans the complete compound.
/// </param>
public sealed record DerivedTermEvidence(
    string SourceIdentity,
    string SourceSurfaceForm,
    int SourceStartPosition,
    int SourceLength,
    int SourceSentenceOrder,
    string ComponentForm);
