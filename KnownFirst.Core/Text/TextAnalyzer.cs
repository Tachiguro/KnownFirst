using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KnownFirst.Core.Text;

public sealed partial class TextAnalyzer
{
    private static readonly HashSet<string> KnownAcronyms = new(StringComparer.Ordinal)
    {
        "AI",
        "HTML",
        "IP",
        "IT",
        "US"
    };

    private static readonly HashSet<string> KnownAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dr",
        "e.g",
        "i.e",
        "Jr",
        "Mr",
        "Mrs",
        "Ms",
        "Prof",
        "Sr",
        "bzw",
        "ca",
        "usw",
        "z.B"
    };

    public TextAnalysisResult Analyze(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var sentences = ExtractSentenceSpans(content);
        var occurrences = ExtractOccurrences(content, sentences);
        var candidates = occurrences
            .GroupBy(occurrence => occurrence.Identity, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedOccurrences = group.OrderBy(occurrence => occurrence.Order).ToArray();
                var surfaceForms = orderedOccurrences
                    .GroupBy(occurrence => occurrence.SurfaceForm, StringComparer.Ordinal)
                    .ToDictionary(
                        formGroup => formGroup.Key,
                        formGroup => formGroup.Count(),
                        StringComparer.Ordinal);
                var first = orderedOccurrences[0];

                return new VocabularyCandidate(
                    first.Identity,
                    first.SurfaceForm,
                    first.Kind,
                    surfaceForms,
                    orderedOccurrences);
            })
            .OrderBy(candidate => candidate.Occurrences[0].Order)
            .ToArray();

        return new TextAnalysisResult(sentences, candidates, occurrences.Count);
    }

    public IReadOnlyList<TextSpan> ExtractSentenceSpans(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var spans = new List<TextSpan>();
        var sentenceStart = 0;
        var index = 0;

        while (index < content.Length)
        {
            if (content[index] is '\r' or '\n')
            {
                AddTrimmedSpan(content, sentenceStart, index, spans);
                index = SkipLineBreak(content, index);
                sentenceStart = index;
                continue;
            }

            if (IsSentenceTerminal(content[index])
                && !(content[index] == '.' && IsKnownAbbreviationAt(content, index))
                && IsFollowedBySentenceBoundary(content, index + 1))
            {
                var sentenceEnd = index + 1;
                while (sentenceEnd < content.Length && IsClosingPunctuation(content[sentenceEnd]))
                {
                    sentenceEnd++;
                }

                AddTrimmedSpan(content, sentenceStart, sentenceEnd, spans);
                sentenceStart = sentenceEnd;
                index = sentenceEnd;
                continue;
            }

            index++;
        }

        AddTrimmedSpan(content, sentenceStart, content.Length, spans);
        return spans;
    }

    private static List<TokenOccurrence> ExtractOccurrences(
        string content,
        IReadOnlyList<TextSpan> sentences)
    {
        var excludedSpans = ExcludedValueRegex()
            .Matches(content)
            .Select(match => (Start: match.Index, End: match.Index + match.Length))
            .OrderBy(span => span.Start)
            .ToArray();
        var occurrences = new List<TokenOccurrence>();
        var excludedIndex = 0;
        var sentenceIndex = 0;
        var index = 0;

        while (index < content.Length)
        {
            while (excludedIndex < excludedSpans.Length && excludedSpans[excludedIndex].End <= index)
            {
                excludedIndex++;
            }

            if (excludedIndex < excludedSpans.Length
                && index >= excludedSpans[excludedIndex].Start
                && index < excludedSpans[excludedIndex].End)
            {
                index = excludedSpans[excludedIndex].End;
                continue;
            }

            if (!TryReadRune(content, index, out var rune, out var runeLength)
                || !IsLetterOrDigit(rune))
            {
                index += runeLength;
                continue;
            }

            var tokenStart = index;
            var tokenEnd = index;
            var hasLetter = false;
            var hasDigit = false;
            var hasInternalPeriod = false;
            var hasHyphen = false;

            while (tokenEnd < content.Length)
            {
                if (!TryReadRune(content, tokenEnd, out var current, out var currentLength))
                {
                    break;
                }

                if (IsLetter(current))
                {
                    hasLetter = true;
                    tokenEnd += currentLength;
                    continue;
                }

                if (Rune.IsDigit(current))
                {
                    hasDigit = true;
                    tokenEnd += currentLength;
                    continue;
                }

                if (IsMark(current))
                {
                    tokenEnd += currentLength;
                    continue;
                }

                if (IsConnector(current)
                    && TryReadRune(content, tokenEnd + currentLength, out var next, out _)
                    && IsLetterOrDigit(next))
                {
                    hasInternalPeriod |= current.Value == '.';
                    hasHyphen |= IsHyphen(current);
                    tokenEnd += currentLength;
                    continue;
                }

                break;
            }

            var tokenWithoutTrailingPeriod = content.Substring(tokenStart, tokenEnd - tokenStart);
            if (tokenEnd < content.Length
                && content[tokenEnd] == '.'
                && (hasInternalPeriod || KnownAbbreviations.Contains(tokenWithoutTrailingPeriod)))
            {
                tokenEnd++;
            }

            if (!hasLetter)
            {
                index = Math.Max(tokenEnd, index + runeLength);
                continue;
            }

            var surfaceForm = content.Substring(tokenStart, tokenEnd - tokenStart);
            var kind = Classify(surfaceForm, hasDigit, hasHyphen, hasInternalPeriod);
            var identity = CreateIdentity(surfaceForm, kind);

            while (sentenceIndex < sentences.Count && tokenStart >= sentences[sentenceIndex].EndPosition)
            {
                sentenceIndex++;
            }

            if (sentenceIndex < sentences.Count
                && tokenStart >= sentences[sentenceIndex].StartPosition
                && tokenEnd <= sentences[sentenceIndex].EndPosition)
            {
                occurrences.Add(new TokenOccurrence(
                    surfaceForm,
                    identity,
                    kind,
                    tokenStart,
                    tokenEnd - tokenStart,
                    occurrences.Count,
                    sentences[sentenceIndex].Order));
            }

            index = tokenEnd;
        }

        return occurrences;
    }

    private static TokenKind Classify(
        string surfaceForm,
        bool hasDigit,
        bool hasHyphen,
        bool hasInternalPeriod)
    {
        if (hasDigit || hasHyphen)
        {
            return TokenKind.TechnicalTerm;
        }

        if (hasInternalPeriod
            || (surfaceForm.EndsWith(".", StringComparison.Ordinal)
                && KnownAbbreviations.Contains(surfaceForm[..^1])))
        {
            return TokenKind.Abbreviation;
        }

        return KnownAcronyms.Contains(surfaceForm)
            ? TokenKind.Acronym
            : TokenKind.Word;
    }

    private static string CreateIdentity(string surfaceForm, TokenKind kind) => kind switch
    {
        TokenKind.Word => $"W:{surfaceForm.ToLowerInvariant()}",
        TokenKind.Acronym => $"A:{surfaceForm}",
        TokenKind.Abbreviation => $"B:{surfaceForm}",
        TokenKind.TechnicalTerm => $"T:{surfaceForm}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryReadRune(string content, int index, out Rune rune, out int length)
    {
        if (index >= content.Length)
        {
            rune = default;
            length = 0;
            return false;
        }

        var status = Rune.DecodeFromUtf16(content.AsSpan(index), out rune, out length);
        if (status == System.Buffers.OperationStatus.Done)
        {
            return true;
        }

        rune = Rune.ReplacementChar;
        length = 1;
        return true;
    }

    private static bool IsLetterOrDigit(Rune rune) => IsLetter(rune) || Rune.IsDigit(rune);

    private static bool IsLetter(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or
        UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter;

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or
        UnicodeCategory.EnclosingMark;

    private static bool IsConnector(Rune rune) =>
        rune.Value is '.' or '\'' or '\u2019' or '-' or '\u2010' or '\u2011';

    private static bool IsHyphen(Rune rune) => rune.Value is '-' or '\u2010' or '\u2011';

    private static bool IsSentenceTerminal(char value) => value is '.' or '!' or '?';

    private static bool IsKnownAbbreviationAt(string content, int periodIndex)
    {
        var start = periodIndex - 1;
        while (start >= 0 && (char.IsLetter(content[start]) || content[start] == '.'))
        {
            start--;
        }

        var value = content[(start + 1)..periodIndex];
        return KnownAbbreviations.Contains(value);
    }

    private static bool IsClosingPunctuation(char value) => value is '"' or '\'' or '\u2019' or '\u201D' or ')' or ']' or '}';

    private static bool IsFollowedBySentenceBoundary(string content, int index)
    {
        while (index < content.Length && IsClosingPunctuation(content[index]))
        {
            index++;
        }

        return index >= content.Length || char.IsWhiteSpace(content[index]);
    }

    private static int SkipLineBreak(string content, int index)
    {
        if (content[index] == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
        {
            return index + 2;
        }

        return index + 1;
    }

    private static void AddTrimmedSpan(
        string content,
        int start,
        int end,
        ICollection<TextSpan> spans)
    {
        while (start < end && char.IsWhiteSpace(content[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(content[end - 1]))
        {
            end--;
        }

        if (end > start)
        {
            spans.Add(new TextSpan(start, end - start, spans.Count));
        }
    }

    [GeneratedRegex(
        @"(?:https?://|www\.)[^\s<>()]+|[\p{L}\p{M}\p{N}.!#$%&'*+/=?^_`{|}~-]+@[\p{L}\p{M}\p{N}-]+(?:\.[\p{L}\p{M}\p{N}-]+)+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExcludedValueRegex();
}
