namespace KnownFirst.Core.Text;

/// <summary>
/// One lexicon-resolved component of a conservative two-component German compound decomposition.
/// </summary>
/// <param name="ComponentForm">
/// The literal substring of the source compound this component was resolved from. Carries no
/// independent source-coordinate semantics of its own.
/// </param>
/// <param name="Lemma">Canonical lemma the component resolves to, per <see cref="IGermanLexicon"/> evidence.</param>
/// <param name="Category">Word-class evidence backing the resolution.</param>
public sealed record GermanCompoundComponent(string ComponentForm, string Lemma, GermanLexemeCategory Category);
