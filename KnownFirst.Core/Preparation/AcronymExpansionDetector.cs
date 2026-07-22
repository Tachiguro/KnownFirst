using KnownFirst.Core.Text;
using System.Text.RegularExpressions;

namespace KnownFirst.Core.Preparation;

public sealed partial class AcronymExpansionDetector
{
    public static bool IsAcronymCandidate(string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length is < 2 or > 20)
        {
            return false;
        }

        var letterCount = 0;
        foreach (var character in term)
        {
            if (char.IsLetter(character))
            {
                letterCount++;
                if (!char.IsUpper(character))
                {
                    return false;
                }
            }
            else if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return letterCount >= 2;
    }

    public string? FindExpansion(string documentContent, string term, TokenKind tokenKind)
    {
        if (tokenKind != TokenKind.Acronym
            || string.IsNullOrWhiteSpace(documentContent)
            || string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        var escapedTerm = Regex.Escape(term);
        var longFormPattern =
            $@"(?<long>[\p{{L}}\p{{N}}][\p{{L}}\p{{N}}\p{{Pd}}'\u2019]*(?:[ \t]+[\p{{L}}\p{{N}}][\p{{L}}\p{{N}}\p{{Pd}}'\u2019]*){{1,11}})[ \t]*\([ \t]*{escapedTerm}[ \t]*\)";
        foreach (Match match in Regex.Matches(documentContent, longFormPattern, RegexOptions.CultureInvariant))
        {
            var expansion = SelectMatchingSuffix(match.Groups["long"].Value, term);
            if (expansion is not null)
            {
                return expansion;
            }
        }

        var reversePattern =
            $@"(?<![\p{{L}}\p{{N}}]){escapedTerm}[ \t]*\([ \t]*(?<long>[^()\r\n.!?]{{2,160}}?)[ \t]*\)";
        foreach (Match match in Regex.Matches(documentContent, reversePattern, RegexOptions.CultureInvariant))
        {
            var expansion = SelectMatchingSuffix(match.Groups["long"].Value, term);
            if (expansion is not null)
            {
                return expansion;
            }
        }

        return null;
    }

    private static string? SelectMatchingSuffix(string longForm, string acronym)
    {
        var tokenMatches = LongFormTokenRegex().Matches(longForm).Cast<Match>().ToArray();
        for (var start = 0; start < tokenMatches.Length; start++)
        {
            var initials = string.Concat(tokenMatches[start..].SelectMany(GetInitials));
            if (!string.Equals(initials, acronym, StringComparison.Ordinal))
            {
                continue;
            }

            var first = tokenMatches[start];
            var last = tokenMatches[^1];
            return longForm.Substring(first.Index, last.Index + last.Length - first.Index);
        }

        return null;
    }

    private static IEnumerable<char> GetInitials(Match token) => token.Value
        .Split(['-', '\u2010', '\u2011', '\u2013', '\u2014'], StringSplitOptions.RemoveEmptyEntries)
        .Where(part => part.Length > 0)
        .Select(part => char.ToUpperInvariant(part[0]));

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:[\p{Pd}'\u2019][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex LongFormTokenRegex();
}
