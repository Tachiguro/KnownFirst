namespace KnownFirst.Core.Text;

/// <summary>
/// Minimal deterministic, offline German lexicon seam used as recognition evidence for later
/// conservative compound decomposition.
///
/// A lexicon is never a whitelist: absent evidence never removes, filters, or alters source text,
/// tokens, occurrences, or candidates. Implementations must be deterministic, free of platform or
/// reflection dependencies, and safe under AOT and trimming. Lookups compare Unicode NFC-normalized
/// forms.
/// </summary>
public interface IGermanLexicon
{
    /// <summary>Looks up an exact base lexeme form.</summary>
    bool TryLookupLemma(string form, out GermanLexemeEntry? entry);

    /// <summary>Looks up a compound component form and returns the lemma it maps to.</summary>
    bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry);
}
