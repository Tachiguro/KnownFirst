namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// One parsed analysis line from the upstream German morphological dictionary text format:
/// a word form together with one candidate lemma and its RFTagger-style tag string.
/// </summary>
/// <param name="Word">The word-form header this analysis line belongs to.</param>
/// <param name="Lemma">The candidate canonical lemma for <paramref name="Word"/>.</param>
/// <param name="RawCategory">
/// The first comma-delimited tag token (e.g. <c>NN</c>, <c>V</c>, <c>ADJ</c>, <c>ART</c>).
/// </param>
/// <param name="Tags">The full, unparsed comma-delimited tag string.</param>
public readonly record struct GermanMorphDictionaryAnalysis(string Word, string Lemma, string RawCategory, string Tags);
