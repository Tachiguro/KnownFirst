using System.Text;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

/// <summary>
/// Deterministic, offline, test-only German lexicon fixture.
///
/// This fixture exists solely to exercise the <see cref="IGermanLexicon"/> contract and to
/// support later conservative compound-decomposition tests. It is a handful of hand-written
/// entries and is explicitly NOT production lexical coverage of German.
/// </summary>
internal sealed class FixtureGermanLexicon : IGermanLexicon
{
    private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
        new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
        {
            ["schreiben"] = new("schreiben", GermanLexemeCategory.Verb),
            ["waschen"] = new("waschen", GermanLexemeCategory.Verb),
            ["Maschine"] = new("Maschine", GermanLexemeCategory.Noun),
            ["Arbeit"] = new("Arbeit", GermanLexemeCategory.Noun),
            ["Zimmer"] = new("Zimmer", GermanLexemeCategory.Noun),
            ["Sicherheit"] = new("Sicherheit", GermanLexemeCategory.Noun),
            ["Management"] = new("Management", GermanLexemeCategory.Noun)
        };

    private static readonly IReadOnlyDictionary<string, GermanCompoundStemEntry> Stems =
        new Dictionary<string, GermanCompoundStemEntry>(StringComparer.Ordinal)
        {
            ["Schreib"] = new("Schreib", "schreiben", GermanLexemeCategory.Verb),
            ["Wasch"] = new("Wasch", "waschen", GermanLexemeCategory.Verb),
            ["Maschine"] = new("Maschine", "Maschine", GermanLexemeCategory.Noun),
            ["Arbeit"] = new("Arbeit", "Arbeit", GermanLexemeCategory.Noun),
            ["Zimmer"] = new("Zimmer", "Zimmer", GermanLexemeCategory.Noun),
            ["Sicherheit"] = new("Sicherheit", "Sicherheit", GermanLexemeCategory.Noun),
            ["Management"] = new("Management", "Management", GermanLexemeCategory.Noun)
        };

    public bool TryLookupLemma(string form, out GermanLexemeEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(form);

        return Lexemes.TryGetValue(form.Normalize(NormalizationForm.FormC), out entry);
    }

    public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(componentForm);

        return Stems.TryGetValue(componentForm.Normalize(NormalizationForm.FormC), out entry);
    }
}
