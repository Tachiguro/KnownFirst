using KnownFirst.Core.Text;
using System.Globalization;
using System.Text;

namespace KnownFirst.Core.Learning;

public sealed class SpellingAnswerComparer
{
    public SpellingComparisonResult Compare(
        string? enteredAnswer,
        string canonicalAnswer,
        IEnumerable<string>? acceptedAliases,
        TokenKind tokenKind,
        string sourceLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAnswer);

        var enteredRaw = enteredAnswer ?? string.Empty;
        var entered = Normalize(enteredRaw);
        var canonical = Normalize(canonicalAnswer);
        var aliases = (acceptedAliases ?? [])
            .Select(Normalize)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var caseSensitive = tokenKind == TokenKind.Acronym
            || IsGermanNoun(canonical, sourceLanguage);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(entered, canonical, comparison))
        {
            return new SpellingComparisonResult(true, enteredRaw, canonical, string.Empty, null);
        }

        var matchedAlias = aliases.FirstOrDefault(alias => string.Equals(entered, alias, comparison));
        if (matchedAlias is not null)
        {
            return new SpellingComparisonResult(true, enteredRaw, canonical, string.Empty, matchedAlias);
        }

        return new SpellingComparisonResult(
            false,
            enteredRaw,
            canonical,
            CreateReadableDifference(entered, canonical),
            null);
    }

    private static string Normalize(string value) =>
        value.Trim().Normalize(NormalizationForm.FormC);

    private static bool IsGermanNoun(string canonicalAnswer, string sourceLanguage) =>
        string.Equals(sourceLanguage, "de", StringComparison.OrdinalIgnoreCase)
        && canonicalAnswer.Length > 0
        && char.IsLetter(canonicalAnswer[0])
        && char.GetUnicodeCategory(canonicalAnswer[0]) == UnicodeCategory.UppercaseLetter;

    private static string CreateReadableDifference(string entered, string expected)
    {
        var builder = new StringBuilder();
        var length = Math.Max(entered.Length, expected.Length);
        for (var index = 0; index < length; index++)
        {
            var actual = index < entered.Length ? entered[index].ToString() : "\u2205";
            var wanted = index < expected.Length ? expected[index].ToString() : "\u2205";
            if (actual == wanted)
            {
                builder.Append(actual);
            }
            else
            {
                builder.Append('[').Append(actual).Append(" \u2192 ").Append(wanted).Append(']');
            }
        }

        return builder.ToString();
    }
}
