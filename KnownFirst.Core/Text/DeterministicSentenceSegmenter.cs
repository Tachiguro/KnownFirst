namespace KnownFirst.Core.Text;

public sealed class DeterministicSentenceSegmenter : ISentenceSegmenter
{
    public IReadOnlyList<TextSpan> Segment(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var spans = new List<TextSpan>();
        var sentenceStart = 0;
        var index = 0;

        while (index < content.Length)
        {
            var terminator = content[index];
            if (!IsSentenceTerminator(terminator)
                || (terminator == '.' && ShouldSuppressPeriodBoundary(content, index)))
            {
                index++;
                continue;
            }

            var sentenceEnd = index + 1;
            var firstCitationStart = -1;
            var lastCitationEnd = -1;
            var consumedTrailingValue = true;

            while (consumedTrailingValue)
            {
                consumedTrailingValue = false;
                while (sentenceEnd < content.Length && IsClosingPunctuation(content[sentenceEnd]))
                {
                    sentenceEnd++;
                    consumedTrailingValue = true;
                }

                if (TryConsumeCitationGroup(content, sentenceEnd, out var citationEnd))
                {
                    firstCitationStart = firstCitationStart < 0 ? sentenceEnd : firstCitationStart;
                    lastCitationEnd = citationEnd;
                    sentenceEnd = citationEnd;
                    consumedTrailingValue = true;
                }
            }

            if (sentenceEnd < content.Length && !char.IsWhiteSpace(content[sentenceEnd]))
            {
                index++;
                continue;
            }

            var (reasonCode, explanation) = CreateBoundaryReason(
                content,
                terminator,
                sentenceEnd,
                firstCitationStart,
                lastCitationEnd);
            AddTrimmedSpan(content, sentenceStart, sentenceEnd, spans, reasonCode, explanation);
            sentenceStart = sentenceEnd;
            index = sentenceEnd;
        }

        AddTrimmedSpan(
            content,
            sentenceStart,
            content.Length,
            spans,
            AnalysisReasonCodes.SentenceBoundaryFinalRemainder,
            "Final non-empty remainder retained as one sentence.");
        return spans;
    }

    private static bool ShouldSuppressPeriodBoundary(string content, int periodIndex) =>
        IsDecimalPoint(content, periodIndex) || IsKnownAbbreviationAt(content, periodIndex);

    private static bool IsDecimalPoint(string content, int periodIndex) =>
        periodIndex > 0
        && periodIndex + 1 < content.Length
        && char.IsDigit(content[periodIndex - 1])
        && char.IsDigit(content[periodIndex + 1]);

    private static bool IsKnownAbbreviationAt(string content, int periodIndex)
    {
        var start = periodIndex - 1;
        while (start >= 0 && (char.IsLetter(content[start]) || content[start] == '.'))
        {
            start--;
        }

        return AbbreviationPolicy.IsKnown(content[(start + 1)..(periodIndex + 1)]);
    }

    private static bool TryConsumeCitationGroup(string content, int start, out int end)
    {
        end = start;
        if (start >= content.Length || content[start] != '[')
        {
            return false;
        }

        var index = start + 1;
        var hasDigit = false;
        while (index < content.Length && content[index] != ']')
        {
            var value = content[index];
            if (char.IsDigit(value))
            {
                hasDigit = true;
            }
            else if (value is not (',' or '-' or '\u2013') && !char.IsWhiteSpace(value))
            {
                return false;
            }

            index++;
        }

        if (!hasDigit || index >= content.Length || content[index] != ']')
        {
            return false;
        }

        end = index + 1;
        return true;
    }

    private static (string ReasonCode, string Explanation) CreateBoundaryReason(
        string content,
        char terminator,
        int sentenceEnd,
        int firstCitationStart,
        int lastCitationEnd)
    {
        var terminatorName = terminator switch
        {
            '.' => "period",
            '!' => "exclamation mark",
            '?' => "question mark",
            _ => "sentence terminator"
        };

        if (firstCitationStart >= 0)
        {
            var citations = content[firstCitationStart..lastCitationEnd];
            return (
                AnalysisReasonCodes.SentenceBoundaryTerminatorCitation,
                $"Boundary created after a {terminatorName} followed by citation `{citations}`.");
        }

        return sentenceEnd >= content.Length
            ? (
                AnalysisReasonCodes.SentenceBoundaryTerminatorEnd,
                $"Boundary created after a {terminatorName} at the end of the document.")
            : (
                AnalysisReasonCodes.SentenceBoundaryTerminatorWhitespace,
                $"Boundary created after a {terminatorName} followed by whitespace.");
    }

    private static void AddTrimmedSpan(
        string content,
        int start,
        int end,
        ICollection<TextSpan> spans,
        string reasonCode,
        string explanation)
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
            spans.Add(new TextSpan(start, end - start, spans.Count, reasonCode, explanation));
        }
    }

    private static bool IsSentenceTerminator(char value) => value is '.' or '!' or '?';

    private static bool IsClosingPunctuation(char value) => value is
        '"' or
        '\'' or
        '\u00BB' or
        '\u2019' or
        '\u201D' or
        ')' or
        ']' or
        '}';
}
