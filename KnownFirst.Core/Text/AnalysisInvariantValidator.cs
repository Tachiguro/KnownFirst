namespace KnownFirst.Core.Text;

public static class AnalysisInvariantValidator
{
    public static IReadOnlyList<AnalysisInvariantFailure> Validate(
        string content,
        TextAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(analysis);

        var failures = new List<AnalysisInvariantFailure>();
        var occurrences = analysis.Candidates.SelectMany(candidate => candidate.Occurrences).ToArray();

        foreach (var sentence in analysis.Sentences)
        {
            if (!IsRangeValid(content.Length, sentence.StartPosition, sentence.Length))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "SentenceRangeOutsideDocument",
                    $"Sentence {sentence.Order} lies outside the original document."));
            }
        }

        foreach (var occurrence in occurrences)
        {
            if (!IsRangeValid(content.Length, occurrence.StartPosition, occurrence.Length))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "OccurrenceRangeOutsideDocument",
                    $"Occurrence {occurrence.Order} lies outside the original document."));
                continue;
            }

            var containingSentences = analysis.Sentences.Count(sentence =>
                occurrence.StartPosition >= sentence.StartPosition
                && occurrence.StartPosition + occurrence.Length <= sentence.EndPosition);
            if (containingSentences != 1)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "OccurrenceSentenceMembershipInvalid",
                    $"Occurrence {occurrence.Order} belongs to {containingSentences} sentence spans instead of exactly one."));
            }

            var exactSurface = content.Substring(occurrence.StartPosition, occurrence.Length);
            if (!string.Equals(exactSurface, occurrence.SurfaceForm, StringComparison.Ordinal))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "OccurrenceSubstringMismatch",
                    $"Occurrence {occurrence.Order} does not equal its exact original substring."));
            }
        }

        if (occurrences.Length != analysis.OccurrenceCount)
        {
            failures.Add(new AnalysisInvariantFailure(
                "OccurrenceCountMismatch",
                $"The analysis reports {analysis.OccurrenceCount} occurrences but contains {occurrences.Length} occurrence records."));
        }

        foreach (var candidate in analysis.Candidates)
        {
            var encounteredForms = EncounteredFormPolicy.Deduplicate(
                candidate.Kind,
                candidate.Occurrences.OrderBy(item => item.Order).Select(item => item.SurfaceForm));
            var encounteredKeys = encounteredForms
                .Select(form => EncounteredFormPolicy.CreateComparisonKey(candidate.Kind, form))
                .ToArray();
            if (encounteredKeys.Distinct(StringComparer.Ordinal).Count() != encounteredKeys.Length)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "EncounteredFormComparisonDuplicate",
                    $"Candidate {candidate.Identity} contains duplicate encountered-form comparison values."));
            }

            var contexts = ContextSelectionPolicy.Select(
                content,
                analysis.Sentences,
                candidate.Occurrences,
                candidate.Identity);
            var selected = contexts.Where(context => context.IsSelected).ToArray();
            if (selected.Select(context => context.Fingerprint).Distinct(StringComparer.Ordinal).Count() != selected.Length)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "SelectedContextFingerprintDuplicate",
                    $"Candidate {candidate.Identity} contains duplicate selected context fingerprints."));
            }

            foreach (var context in selected)
            {
                var matchingSentences = analysis.Sentences.Count(sentence =>
                    sentence.StartPosition == context.SentenceStartPosition
                    && sentence.Length == context.SentenceLength);
                if (matchingSentences != 1)
                {
                    failures.Add(new AnalysisInvariantFailure(
                        "SelectedContextSentenceMismatch",
                        $"Selected context for occurrence {context.OccurrenceOrder} does not equal exactly one sentence span."));
                }

                var relativeStart = context.OccurrenceStartPosition - context.SentenceStartPosition;
                if (relativeStart < 0
                    || context.OccurrenceLength < 0
                    || relativeStart > context.SentenceLength - context.OccurrenceLength)
                {
                    failures.Add(new AnalysisInvariantFailure(
                        "ContextTargetOutsideSentence",
                        $"Target for occurrence {context.OccurrenceOrder} lies outside its selected context."));
                    continue;
                }

                var target = context.SentenceText.Substring(relativeStart, context.OccurrenceLength);
                if (!string.Equals(target, context.Target, StringComparison.Ordinal))
                {
                    failures.Add(new AnalysisInvariantFailure(
                        "ContextTargetSubstringMismatch",
                        $"Target for occurrence {context.OccurrenceOrder} does not equal the displayed occurrence."));
                }
            }
        }

        return failures;
    }

    private static bool IsRangeValid(int contentLength, int start, int length) =>
        start >= 0 && length >= 0 && start <= contentLength - length;
}
