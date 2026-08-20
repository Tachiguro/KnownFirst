using System.Globalization;
using System.Text;

namespace KnownFirst.Core.Text;

/// <summary>
/// Conservative, deterministic, lexicon-only German compound decomposer.
///
/// Considers every way to split the exact source surface form into an ordered sequence of
/// <see cref="MinComponents"/> to <see cref="MaxComponents"/> components, each at least
/// <see cref="MinComponentSpanLength"/> characters long. A decomposition requires exactly one
/// unambiguous, fully lexicon-backed full partition of the complete word: zero valid partitions or
/// more than one valid partition both fail closed and derive nothing, regardless of whether the
/// competing partitions differ in split position, component count, or fallback interpretation.
///
/// For every component span, a literal lexicon match (the exact substring, or its first-letter
/// upper-cased form) always wins outright when it succeeds. Only when no literal match exists may a
/// bounded, lexicon-confirmed fallback be considered: stripping one candidate linking/Fugen or
/// nominal de-inflection suffix from the end of the span and re-resolving the remainder. If more
/// than one candidate suffix independently produces a valid remainder match for the same span, that
/// span is locally ambiguous and contributes no component. Never performs broad stemming,
/// lemmatization guessing, or provider/network lookup.
/// </summary>
public static class ConservativeGermanCompoundDecomposer
{
    /// <summary>Minimum number of components a decomposition may have.</summary>
    public const int MinComponents = 2;

    /// <summary>Maximum number of components a decomposition may have; bounds the search depth.</summary>
    public const int MaxComponents = 4;

    /// <summary>Minimum length, in characters, of any literal component span before any fallback stripping.</summary>
    public const int MinComponentSpanLength = 2;

    /// <summary>
    /// Bounded set of candidate suffixes considered as a fallback interpretation once literal
    /// resolution has failed for a span. Each member is shipped only because a concrete,
    /// lexicon-confirmed example justifies it: "s" and "es" are established German linking
    /// (Fugen) elements (e.g. "Arbeit" + s + "Zimmer", "Bund" + es + "Land"); "e" is a
    /// de-inflection suffix that recovers a lexicon-confirmed singular noun lemma from a plural
    /// surface form (e.g. "Griffe" -&gt; "Griff"). Other candidates from the broader linguistic
    /// inventory ("n", "en", "er") were deliberately not shipped in this package for lack of an
    /// equally concrete, justified example; omitting them is intentional, not an oversight.
    /// </summary>
    private static readonly string[] FallbackSuffixes = { "es", "s", "e" };

    public static bool TryDecompose(
        string compoundWord,
        IGermanLexicon lexicon,
        out GermanCompoundDecomposition? decomposition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compoundWord);
        ArgumentNullException.ThrowIfNull(lexicon);

        var normalized = compoundWord.Normalize(NormalizationForm.FormC);
        var spanCache = new Dictionary<(int Start, int End, bool RequireNoun), GermanCompoundComponent?>();
        var seenPartitionKeys = new HashSet<string>(StringComparer.Ordinal);
        var distinctDecompositions = new List<GermanCompoundDecomposition>();

        Search(
            normalized,
            start: 0,
            componentsSoFar: new List<GermanCompoundComponent>(),
            lexicon,
            spanCache,
            seenPartitionKeys,
            distinctDecompositions);

        if (distinctDecompositions.Count != 1)
        {
            decomposition = null;
            return false;
        }

        decomposition = distinctDecompositions[0];
        return true;
    }

    private static void Search(
        string normalized,
        int start,
        List<GermanCompoundComponent> componentsSoFar,
        IGermanLexicon lexicon,
        Dictionary<(int Start, int End, bool RequireNoun), GermanCompoundComponent?> spanCache,
        HashSet<string> seenPartitionKeys,
        List<GermanCompoundDecomposition> distinctDecompositions)
    {
        var length = normalized.Length;

        if (start == length)
        {
            if (componentsSoFar.Count >= MinComponents)
            {
                RecordPartition(componentsSoFar, seenPartitionKeys, distinctDecompositions);
            }

            return;
        }

        if (componentsSoFar.Count >= MaxComponents)
        {
            return;
        }

        for (var end = start + MinComponentSpanLength; end <= length; end++)
        {
            var isFinal = end == length;
            if (!isFinal && length - end < MinComponentSpanLength)
            {
                continue;
            }

            var component = ResolveSpan(normalized, start, end, lexicon, requireNoun: isFinal, spanCache);
            if (component is null)
            {
                continue;
            }

            componentsSoFar.Add(component);
            Search(normalized, end, componentsSoFar, lexicon, spanCache, seenPartitionKeys, distinctDecompositions);
            componentsSoFar.RemoveAt(componentsSoFar.Count - 1);
        }
    }

    private static void RecordPartition(
        List<GermanCompoundComponent> components,
        HashSet<string> seenPartitionKeys,
        List<GermanCompoundDecomposition> distinctDecompositions)
    {
        const char ComponentSeparator = '␞';
        const char FieldSeparator = '␟';

        var key = string.Join(
            ComponentSeparator,
            components.Select(component =>
                component.ComponentForm + FieldSeparator + component.Lemma + FieldSeparator + (int)component.Category));

        if (seenPartitionKeys.Add(key))
        {
            distinctDecompositions.Add(new GermanCompoundDecomposition(components.ToArray()));
        }
    }

    private static GermanCompoundComponent? ResolveSpan(
        string normalized,
        int start,
        int end,
        IGermanLexicon lexicon,
        bool requireNoun,
        Dictionary<(int Start, int End, bool RequireNoun), GermanCompoundComponent?> spanCache)
    {
        var cacheKey = (start, end, requireNoun);
        if (spanCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var span = normalized[start..end];

        var literal = ResolveCandidateText(span, span, lexicon, requireNoun);
        if (literal is not null)
        {
            spanCache[cacheKey] = literal;
            return literal;
        }

        GermanCompoundComponent? fallback = null;
        var fallbackMatchCount = 0;

        foreach (var suffix in FallbackSuffixes)
        {
            if (span.Length - suffix.Length < MinComponentSpanLength
                || !span.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = span[..^suffix.Length];
            var candidate = ResolveCandidateText(remainder, remainder, lexicon, requireNoun);
            if (candidate is null)
            {
                continue;
            }

            fallbackMatchCount++;
            fallback = candidate;

            if (fallbackMatchCount > 1)
            {
                break;
            }
        }

        var result = fallbackMatchCount == 1 ? fallback : null;
        spanCache[cacheKey] = result;
        return result;
    }

    private static GermanCompoundComponent? ResolveCandidateText(
        string lookupCandidate,
        string literalComponentForm,
        IGermanLexicon lexicon,
        bool requireNoun)
    {
        if (TryResolveExact(lookupCandidate, literalComponentForm, lexicon, requireNoun, out var component))
        {
            return component;
        }

        var uppercased = UppercaseFirstLetter(lookupCandidate);
        if (!string.Equals(uppercased, lookupCandidate, StringComparison.Ordinal)
            && TryResolveExact(uppercased, literalComponentForm, lexicon, requireNoun, out component))
        {
            return component;
        }

        return null;
    }

    private static bool TryResolveExact(
        string lookupForm,
        string literalComponentForm,
        IGermanLexicon lexicon,
        bool requireNoun,
        out GermanCompoundComponent? component)
    {
        if (lexicon.TryLookupStem(lookupForm, out var stemEntry)
            && stemEntry is not null
            && (!requireNoun || stemEntry.Category == GermanLexemeCategory.Noun))
        {
            component = new GermanCompoundComponent(literalComponentForm, stemEntry.Lemma, stemEntry.Category);
            return true;
        }

        if (lexicon.TryLookupLemma(lookupForm, out var lexemeEntry)
            && lexemeEntry is not null
            && (!requireNoun || lexemeEntry.Category == GermanLexemeCategory.Noun))
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
