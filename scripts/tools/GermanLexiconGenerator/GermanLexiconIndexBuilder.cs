using System.Globalization;
using System.Text;
using KnownFirst.Core.Text;

namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Deterministically reduces parsed upstream analyses to the compact runtime index consumed by
/// <c>KnownFirst.Core.Text.German.GeneratedGermanLexicon</c>.
///
/// Two independent, fail-closed reductions:
///
/// <b>Lemma table (base-form lookup):</b> for each distinct word form, every distinct
/// (lemma, raw-tag-category) pair the upstream data records is collected. A word form is
/// included only when it has exactly one such pair, that category maps to a supported
/// <see cref="GermanLexemeCategory"/>, and the word form is textually identical to its own
/// lemma (i.e. it genuinely is a base/citation form: infinitive, nominative singular, or
/// positive-uninflected adjective). Any other outcome — multiple distinct analyses, an
/// unsupported category, or a form that is itself an inflected non-base form — fails closed and
/// is excluded rather than guessed.
///
/// <b>Stem table (compound-initial verb-stem lookup):</b> the upstream dictionary carries no
/// literal compound-first-constituent ("TRUNC") tag comparable to RFTagger compound splitting —
/// its only "(TRUNC)" usage marks unrelated numeral/adjective hyphen elision. Genuine,
/// deterministic compound-initial verb-stem evidence is instead derived from each verb's
/// <c>V,imp,sing</c> (imperative singular) analyses. For regular ("weak") German verbs this
/// imperative-singular form is textually identical to the stem conventionally used as the first
/// constituent of a nominal compound (capitalized, since a compound noun's first letter is
/// always capitalized) — e.g. <c>schreiben</c> → <c>schreib</c> → <c>Schreibmaschine</c>. This
/// is useful evidence for many verbs, but it does not give complete coverage: strong/irregular
/// verbs with imperative ablaut (e.g. <c>nehmen</c> → <c>nimm</c>, <c>geben</c> → <c>gib</c>)
/// conventionally form compounds from their infinitive stem instead, so their derived
/// imperative-based candidate simply will not match real compounds using that verb — a silent
/// coverage gap for those verbs, not a source of incorrect matches (the two-sided, mutually
/// confirming split requirement in <c>ConservativeGermanCompoundDecomposer</c> means an
/// off-target derived stem can only ever fail to match, never falsely match). A derived stem is
/// accepted only when its imperative-singular surface form maps to exactly one lemma, and its
/// capitalized key does not collide with any other stem entry or with an existing lemma-table
/// entry of a different lemma. Noun compound stems are not stored separately: a noun lemma is
/// always its own compound stem, and <c>GeneratedGermanLexicon.TryLookupStem</c> falls back to
/// the lemma table for those.
/// </summary>
public static class GermanLexiconIndexBuilder
{
    private const string ImperativeSingularTagFragment = ",imp,sing";

    public static GermanLexiconIndexBuildResult Build(IEnumerable<GermanMorphDictionaryAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);

        var perWord = new Dictionary<string, HashSet<(string Lemma, string RawCategory)>>(StringComparer.Ordinal);
        var imperativeSingularLemmasByForm = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var analysis in analyses)
        {
            var word = Normalize(analysis.Word);
            var lemma = Normalize(analysis.Lemma);

            if (!perWord.TryGetValue(word, out var pairs))
            {
                pairs = [];
                perWord[word] = pairs;
            }

            pairs.Add((lemma, analysis.RawCategory));

            if (analysis.RawCategory == "V"
                && analysis.Tags.Contains(ImperativeSingularTagFragment, StringComparison.Ordinal))
            {
                if (!imperativeSingularLemmasByForm.TryGetValue(word, out var lemmas))
                {
                    lemmas = new HashSet<string>(StringComparer.Ordinal);
                    imperativeSingularLemmasByForm[word] = lemmas;
                }

                lemmas.Add(lemma);
            }
        }

        var (lemmaEntries, ambiguousWordForms, unsupportedCategoryWordForms, inflectedFormWordFormsExcluded) =
            ReduceLemmaEntries(perWord);

        var sortedLemmas = lemmaEntries
            .OrderBy(e => e.Lemma, StringComparer.Ordinal)
            .ToList();

        var lemmaFormKeys = new HashSet<string>(sortedLemmas.Select(e => e.Lemma), StringComparer.Ordinal);

        var (stemEntries, ambiguousImperativeSingularForms, stemCollisionExclusions) =
            ReduceStemEntries(imperativeSingularLemmasByForm, lemmaFormKeys);

        var sortedStems = stemEntries
            .OrderBy(e => e.ComponentForm, StringComparer.Ordinal)
            .ToList();

        var statistics = new GermanLexiconIndexBuildStatistics(
            TotalDistinctWordForms: perWord.Count,
            AmbiguousWordForms: ambiguousWordForms,
            UnsupportedCategoryWordForms: unsupportedCategoryWordForms,
            InflectedFormWordFormsExcluded: inflectedFormWordFormsExcluded,
            UnambiguousBaseFormLemmaEntries: sortedLemmas.Count,
            ImperativeSingularCandidateForms: imperativeSingularLemmasByForm.Count,
            AmbiguousImperativeSingularForms: ambiguousImperativeSingularForms,
            DerivedVerbStemCollisionExclusions: stemCollisionExclusions,
            DerivedVerbStemEntries: sortedStems.Count);

        return new GermanLexiconIndexBuildResult(sortedLemmas, sortedStems, statistics);
    }

    private static (List<GermanLexemeEntry> Entries, int Ambiguous, int UnsupportedCategory, int InflectedExcluded)
        ReduceLemmaEntries(Dictionary<string, HashSet<(string Lemma, string RawCategory)>> perWord)
    {
        var entries = new List<GermanLexemeEntry>();
        var ambiguous = 0;
        var unsupportedCategory = 0;
        var inflectedExcluded = 0;

        foreach (var (word, pairs) in perWord)
        {
            if (pairs.Count != 1)
            {
                ambiguous++;
                continue;
            }

            var (lemma, rawCategory) = pairs.Single();
            var category = GermanLexemeCategoryMapper.Map(rawCategory);
            if (category is null)
            {
                unsupportedCategory++;
                continue;
            }

            if (!string.Equals(word, lemma, StringComparison.Ordinal))
            {
                inflectedExcluded++;
                continue;
            }

            entries.Add(new GermanLexemeEntry(lemma, category.Value));
        }

        return (entries, ambiguous, unsupportedCategory, inflectedExcluded);
    }

    private static (List<GermanCompoundStemEntry> Entries, int Ambiguous, int CollisionExclusions) ReduceStemEntries(
        Dictionary<string, HashSet<string>> imperativeSingularLemmasByForm,
        HashSet<string> lemmaFormKeys)
    {
        var ambiguous = 0;
        var collisionExclusions = 0;

        // componentForm -> lemma for keys accepted so far. A key here is provisional until the
        // whole input has been scanned: a later-arriving distinct raw form that capitalizes to
        // the same key with a different lemma invalidates it retroactively (see below), so
        // "accepted" never means "final" until after the loop.
        var acceptedByKey = new Dictionary<string, string>(StringComparer.Ordinal);

        // Keys permanently excluded because two distinct raw forms produced genuinely
        // conflicting lemmas under the same capitalized component key. Once a key lands here it
        // stays fail-closed for the rest of the scan: no later candidate for that key is ever
        // accepted, even if it would otherwise agree with one of the earlier candidates.
        var rejectedKeys = new HashSet<string>(StringComparer.Ordinal);

        // Iterate in explicit ordinal order rather than relying on Dictionary<string, ...>
        // enumeration order, which .NET does not guarantee stable across process runs
        // (randomized string hashing). This keeps collision/tie-break resolution below
        // deterministic even though, for the German alphabet this data uses, capitalizing only
        // the first Rune is injective over distinct surface forms and no such collision is
        // currently reachable in the pinned upstream data.
        foreach (var (surfaceForm, lemmas) in imperativeSingularLemmasByForm.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (lemmas.Count != 1)
            {
                ambiguous++;
                continue;
            }

            var lemma = lemmas.Single();
            var componentForm = CapitalizeFirstLetter(surfaceForm);

            if (lemmaFormKeys.Contains(componentForm))
            {
                collisionExclusions++;
                continue;
            }

            if (rejectedKeys.Contains(componentForm))
            {
                collisionExclusions++;
                continue;
            }

            if (acceptedByKey.TryGetValue(componentForm, out var existingLemma))
            {
                if (string.Equals(existingLemma, lemma, StringComparison.Ordinal))
                {
                    // Same key, same lemma again: a harmless repeat, not a genuine ambiguity.
                    collisionExclusions++;
                    continue;
                }

                // Genuine ambiguity between two distinct raw forms: neither lemma may win.
                // Retroactively exclude the previously accepted candidate too, and permanently
                // reject the key so every later candidate for it is excluded as well.
                acceptedByKey.Remove(componentForm);
                rejectedKeys.Add(componentForm);
                collisionExclusions += 2;
                continue;
            }

            acceptedByKey[componentForm] = lemma;
        }

        var entries = acceptedByKey
            .Select(kv => new GermanCompoundStemEntry(kv.Key, kv.Value, GermanLexemeCategory.Verb))
            .ToList();

        return (entries, ambiguous, collisionExclusions);
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormC);

    private static string CapitalizeFirstLetter(string value)
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
