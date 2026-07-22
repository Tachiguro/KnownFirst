using System.Text;

namespace KnownFirst.Core.Text;

public sealed record ContextSelectionDecision(
    string CandidateIdentity,
    int OccurrenceOrder,
    int SentenceOrder,
    int SentenceStartPosition,
    int SentenceLength,
    int OccurrenceStartPosition,
    int OccurrenceLength,
    string SentenceText,
    string Target,
    string Fingerprint,
    bool IsSelected,
    string ReasonCode,
    string Explanation)
{
    public int TargetStartInSentence => OccurrenceStartPosition - SentenceStartPosition;
}

public static class ContextSelectionPolicy
{
    public const int MaximumContexts = 3;

    public static IReadOnlyList<ContextSelectionDecision> Select(
        string content,
        IReadOnlyList<TextSpan> sentences,
        IReadOnlyList<TokenOccurrence> occurrences,
        string candidateIdentity,
        int maximumContexts = MaximumContexts)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(candidateIdentity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumContexts);

        var decisions = new List<ContextSelectionDecision>();
        var retainedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var selectedCount = 0;

        foreach (var occurrence in occurrences.OrderBy(item => item.Order))
        {
            var containingSentences = sentences.Where(sentence =>
                    occurrence.StartPosition >= sentence.StartPosition
                    && occurrence.StartPosition + occurrence.Length <= sentence.EndPosition)
                .ToArray();
            if (containingSentences.Length != 1
                || !IsRangeValid(content.Length, occurrence.StartPosition, occurrence.Length)
                || !IsRangeValid(
                    content.Length,
                    containingSentences[0].StartPosition,
                    containingSentences[0].Length))
            {
                decisions.Add(new ContextSelectionDecision(
                    candidateIdentity,
                    occurrence.Order,
                    occurrence.SentenceOrder,
                    -1,
                    0,
                    occurrence.StartPosition,
                    occurrence.Length,
                    string.Empty,
                    occurrence.SurfaceForm,
                    string.Empty,
                    false,
                    AnalysisReasonCodes.RejectedInvalidCoordinates,
                    "Context rejected because the occurrence does not belong to exactly one valid sentence span."));
                continue;
            }

            var sentence = containingSentences[0];
            var sentenceText = content.Substring(sentence.StartPosition, sentence.Length);
            var target = content.Substring(occurrence.StartPosition, occurrence.Length);
            var fingerprint = CreateFingerprint(sentenceText);
            var isDuplicate = retainedFingerprints.Contains(fingerprint);
            var isMaximumReached = !isDuplicate && selectedCount >= maximumContexts;
            var isSelected = !isDuplicate && !isMaximumReached;
            string reasonCode;
            string explanation;

            if (isDuplicate)
            {
                reasonCode = AnalysisReasonCodes.RejectedDuplicateContext;
                explanation = "Context rejected because normalized sentence duplicates the first retained context.";
            }
            else if (isMaximumReached)
            {
                reasonCode = AnalysisReasonCodes.RejectedMaximumContexts;
                explanation = $"Context rejected because the deterministic maximum of {maximumContexts} unique contexts was reached.";
            }
            else
            {
                reasonCode = selectedCount == 0
                    ? AnalysisReasonCodes.SelectedFirstUniqueContext
                    : AnalysisReasonCodes.SelectedUniqueContext;
                explanation = selectedCount == 0
                    ? "Context selected as the first unique sentence in document order."
                    : "Context selected as the next unique sentence in document order.";
                retainedFingerprints.Add(fingerprint);
                selectedCount++;
            }

            decisions.Add(new ContextSelectionDecision(
                candidateIdentity,
                occurrence.Order,
                sentence.Order,
                sentence.StartPosition,
                sentence.Length,
                occurrence.StartPosition,
                occurrence.Length,
                sentenceText,
                target,
                fingerprint,
                isSelected,
                reasonCode,
                explanation));
        }

        return decisions;
    }

    public static string CreateFingerprint(string sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        return string.Join(' ', sentence
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim()
                .Normalize(NormalizationForm.FormC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsRangeValid(int contentLength, int start, int length) =>
        start >= 0 && length >= 0 && start <= contentLength - length;
}
