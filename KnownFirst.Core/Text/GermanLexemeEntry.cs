namespace KnownFirst.Core.Text;

/// <summary>A single German lexeme known to a lexicon, identified by its canonical lemma.</summary>
public sealed record GermanLexemeEntry(string Lemma, GermanLexemeCategory Category);
