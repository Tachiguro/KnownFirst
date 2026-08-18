using System.Globalization;
using System.Text;

namespace KnownFirst.Core.Text;

/// <summary>
/// Conservative, deterministic, lexicon-only two-component German compound decomposer.
///
/// Considers only exact two-component splits of the exact source surface form. A decomposition
/// requires exactly one unambiguous, fully lexicon-backed split: zero valid splits or more than one
/// valid split both fail closed and derive nothing. Never performs suffix stripping, stemming,
/// lemmatization guessing, provider/network lookup, inferred morphology, or linking/Fugen-element
/// handling.
/// </summary>
public static class ConservativeGermanCompoundDecomposer
{
    public static bool TryDecompose(
        string compoundWord,
        IGermanLexicon lexicon,
        out GermanCompoundDecomposition? decomposition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compoundWord);
        ArgumentNullException.ThrowIfNull(lexicon);

        var normalized = compoundWord.Normalize(NormalizationForm.FormC);
        var seenLemmaPairs = new HashSet<(string LeftLemma, string RightLemma)>();
        var distinctDecompositions = new List<GermanCompoundDecomposition>();

        for (var splitIndex = 1; splitIndex < normalized.Length; splitIndex++)
        {
            var leftForm = normalized[..splitIndex];
            var rightForm = normalized[splitIndex..];

            if (!TryResolveComponent(leftForm, lexicon, requireNoun: false, out var leftComponent)
                || leftComponent is null)
            {
                continue;
            }

            if (!TryResolveComponent(rightForm, lexicon, requireNoun: true, out var rightComponent)
                || rightComponent is null)
            {
                continue;
            }

            if (seenLemmaPairs.Add((leftComponent.Lemma, rightComponent.Lemma)))
            {
                distinctDecompositions.Add(new GermanCompoundDecomposition(leftComponent, rightComponent));
            }
        }

        if (distinctDecompositions.Count != 1)
        {
            decomposition = null;
            return false;
        }

        decomposition = distinctDecompositions[0];
        return true;
    }

    private static bool TryResolveComponent(
        string componentForm,
        IGermanLexicon lexicon,
        bool requireNoun,
        out GermanCompoundComponent? component)
    {
        if (TryResolveExact(componentForm, componentForm, lexicon, out component)
            && component is not null
            && (!requireNoun || component.Category == GermanLexemeCategory.Noun))
        {
            return true;
        }

        if (requireNoun)
        {
            var uppercased = UppercaseFirstLetter(componentForm);
            if (!string.Equals(uppercased, componentForm, StringComparison.Ordinal)
                && TryResolveExact(uppercased, componentForm, lexicon, out component)
                && component is not null
                && component.Category == GermanLexemeCategory.Noun)
            {
                return true;
            }
        }

        component = null;
        return false;
    }

    private static bool TryResolveExact(
        string lookupForm,
        string literalComponentForm,
        IGermanLexicon lexicon,
        out GermanCompoundComponent? component)
    {
        if (lexicon.TryLookupStem(lookupForm, out var stemEntry) && stemEntry is not null)
        {
            component = new GermanCompoundComponent(literalComponentForm, stemEntry.Lemma, stemEntry.Category);
            return true;
        }

        if (lexicon.TryLookupLemma(lookupForm, out var lexemeEntry) && lexemeEntry is not null)
        {
            component = new GermanCompoundComponent(literalComponentForm, lexemeEntry.Lemma, lexemeEntry.Category);
            return true;
        }

        component = null;
        return false;
    }

    private static string UppercaseFirstLetter(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var firstRune = value.EnumerateRunes().First();
        var upper = Rune.ToUpper(firstRune, CultureInfo.InvariantCulture);
        return upper + value[firstRune.Utf16SequenceLength..];
    }
}
