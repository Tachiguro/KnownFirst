namespace KnownFirst.Core.Text;

/// <summary>
/// Maps a compound component form (e.g. <c>Schreib</c>) to the canonical lemma it belongs to
/// (e.g. <c>schreiben</c>).
/// </summary>
public sealed record GermanCompoundStemEntry(
    string ComponentForm,
    string Lemma,
    GermanLexemeCategory Category);
