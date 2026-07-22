using System.Globalization;
using System.Text;

namespace KnownFirst.Core.Text;

public static class GermanVocabularyNormalizer
{
    private const string ProcessPluralSuffix = "prozesse";
    private const string ProcessSingularSuffix = "prozess";

    private static readonly IReadOnlyDictionary<string, string> ExplicitBaseForms =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["definierte"] = "definiert",
            ["definierten"] = "definiert",
            ["neuer"] = "neu",
            ["neue"] = "neu",
            ["neuen"] = "neu"
        };

    public static IReadOnlyList<TokenOccurrence> Normalize(
        string content,
        string? sourceLanguage,
        IReadOnlyList<TokenOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(occurrences);

        if (!IsGerman(sourceLanguage) || occurrences.Count == 0)
        {
            return occurrences;
        }

        var normalized = occurrences.Select(NormalizeReliableBaseForm).ToArray();
        for (var index = 0; index < occurrences.Count; index++)
        {
            var occurrence = occurrences[index];
            if (!EndsWithEllipsisHyphen(content, occurrence)
                || !HasCoordinatedProcessCompletion(content, occurrences, index))
            {
                continue;
            }

            var hyphen = content[occurrence.StartPosition + occurrence.Length];
            normalized[index] = WithCanonicalTerm(
                occurrence,
                EnsureGermanNounCapitalization(occurrence.SurfaceForm + ProcessSingularSuffix))
                with
                {
                    Length = occurrence.Length + 1,
                    SurfaceForm = occurrence.SurfaceForm + hyphen
                };
        }

        return normalized;
    }

    private static TokenOccurrence NormalizeReliableBaseForm(TokenOccurrence occurrence)
    {
        if (occurrence.Kind != TokenKind.Word)
        {
            return occurrence;
        }

        var normalizedSurface = occurrence.SurfaceForm
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();
        if (ExplicitBaseForms.TryGetValue(normalizedSurface, out var explicitBaseForm))
        {
            return WithCanonicalTerm(occurrence, explicitBaseForm);
        }

        if (normalizedSurface.EndsWith(ProcessPluralSuffix, StringComparison.Ordinal))
        {
            var singular = occurrence.SurfaceForm.Normalize(NormalizationForm.FormC)[..^1];
            return WithCanonicalTerm(occurrence, EnsureGermanNounCapitalization(singular));
        }

        return occurrence;
    }

    private static bool HasCoordinatedProcessCompletion(
        string content,
        IReadOnlyList<TokenOccurrence> occurrences,
        int prefixIndex)
    {
        var prefix = occurrences[prefixIndex];
        var maximumIndex = Math.Min(occurrences.Count - 1, prefixIndex + 12);
        for (var index = prefixIndex + 1; index <= maximumIndex; index++)
        {
            var candidate = occurrences[index];
            if (candidate.SentenceOrder != prefix.SentenceOrder)
            {
                return false;
            }

            if (IsProcessWord(candidate.SurfaceForm))
            {
                return true;
            }

            if (IsCoordinationWord(candidate.SurfaceForm)
                || EndsWithEllipsisHyphen(content, candidate))
            {
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool IsProcessWord(string surfaceForm)
    {
        var normalized = surfaceForm.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return normalized.EndsWith(ProcessPluralSuffix, StringComparison.Ordinal)
            || normalized.EndsWith(ProcessSingularSuffix, StringComparison.Ordinal);
    }

    private static bool IsCoordinationWord(string surfaceForm) =>
        surfaceForm.Normalize(NormalizationForm.FormC).ToLowerInvariant() is "und" or "oder" or "sowie";

    private static bool EndsWithEllipsisHyphen(string content, TokenOccurrence occurrence)
    {
        var index = occurrence.StartPosition + occurrence.Length;
        return occurrence.Kind == TokenKind.Word
            && index < content.Length
            && content[index] is '-' or '\u2010' or '\u2011';
    }

    private static TokenOccurrence WithCanonicalTerm(
        TokenOccurrence occurrence,
        string canonicalTerm)
    {
        var resolution = VocabularyIdentityPolicy.Resolve(canonicalTerm, TokenKind.Word, "de");
        return occurrence with
        {
            Identity = resolution.Identity,
            CanonicalTerm = resolution.CanonicalTerm,
            Kind = TokenKind.Word
        };
    }

    private static string EnsureGermanNounCapitalization(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var firstRune = normalized.EnumerateRunes().First();
        var upper = Rune.ToUpper(firstRune, CultureInfo.GetCultureInfo("de-DE"));
        return upper + normalized[firstRune.Utf16SequenceLength..];
    }

    private static bool IsGerman(string? sourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(sourceLanguage))
        {
            return false;
        }

        var languageFamily = sourceLanguage.Trim()
            .Replace('_', '-')
            .Split('-', 2, StringSplitOptions.TrimEntries)[0];
        return string.Equals(languageFamily, "de", StringComparison.OrdinalIgnoreCase);
    }
}
