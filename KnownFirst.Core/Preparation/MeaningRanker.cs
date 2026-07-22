using System.Text.RegularExpressions;
using KnownFirst.Core.Text;

namespace KnownFirst.Core.Preparation;

public sealed partial class MeaningRanker
{
    public IReadOnlyList<LexicalMeaning> Rank(
        IEnumerable<LexicalMeaning> meanings,
        TokenKind tokenKind,
        string? context)
    {
        ArgumentNullException.ThrowIfNull(meanings);

        var contextWords = NormalizeWords(context ?? string.Empty);
        return meanings
            .Select((meaning, index) => new
            {
                Meaning = meaning,
                Index = index,
                TokenKindMatch = GetTokenKindMatch(meaning, tokenKind),
                Overlap = NormalizeWords($"{meaning.Definition} {meaning.Example}")
                    .Count(contextWords.Contains)
            })
            .OrderByDescending(item => item.TokenKindMatch)
            .ThenByDescending(item => item.Overlap)
            .ThenBy(item => item.Index)
            .Select(item => item.Meaning)
            .ToArray();
    }

    private static int GetTokenKindMatch(LexicalMeaning meaning, TokenKind tokenKind)
    {
        var descriptor = $"{meaning.PartOfSpeech} {string.Join(' ', meaning.UsageLabels)}"
            .ToLowerInvariant();
        var isAcronym = descriptor.Contains("acronym", StringComparison.Ordinal)
            || descriptor.Contains("initialism", StringComparison.Ordinal)
            || descriptor.Contains("akronym", StringComparison.Ordinal);
        var isAbbreviation = descriptor.Contains("abbreviation", StringComparison.Ordinal)
            || descriptor.Contains("abkürzung", StringComparison.Ordinal);
        return tokenKind switch
        {
            TokenKind.Acronym => isAcronym ? 1 : 0,
            TokenKind.Abbreviation => isAbbreviation ? 1 : 0,
            TokenKind.TechnicalTerm => descriptor.Contains("comput", StringComparison.Ordinal)
                || descriptor.Contains("technical", StringComparison.Ordinal)
                || descriptor.Contains("fach", StringComparison.Ordinal)
                    ? 1
                    : 0,
            _ => isAcronym || isAbbreviation ? 0 : 1
        };
    }

    private static HashSet<string> NormalizeWords(string value) => WordRegex()
        .Matches(value.Normalize())
        .Select(match => match.Value.ToLowerInvariant())
        .Where(word => word.Length > 2)
        .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
