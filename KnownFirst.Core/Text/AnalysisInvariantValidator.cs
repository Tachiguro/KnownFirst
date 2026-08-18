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
            ValidateProvenance(content, analysis, candidate, failures);

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

    private static void ValidateProvenance(
        string content,
        TextAnalysisResult analysis,
        VocabularyCandidate candidate,
        List<AnalysisInvariantFailure> failures)
    {
        if (candidate.Provenance != CandidateProvenanceKind.DerivedFromCompound)
        {
            if (candidate.DerivedEvidence.Count != 0)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DirectCandidateCarriesDerivedEvidence",
                    $"Direct candidate {candidate.Identity} carries derivation evidence."));
            }

            return;
        }

        if (candidate.Occurrences.Count != 0)
        {
            failures.Add(new AnalysisInvariantFailure(
                "DerivedCandidateCarriesLiteralOccurrences",
                $"Derived candidate {candidate.Identity} claims {candidate.Occurrences.Count} literal occurrences."));
        }

        if (candidate.DerivedEvidence.Count == 0)
        {
            failures.Add(new AnalysisInvariantFailure(
                "DerivedCandidateMissingEvidence",
                $"Derived candidate {candidate.Identity} carries no derivation evidence."));
            return;
        }

        foreach (var evidence in candidate.DerivedEvidence)
        {
            var hasSemanticFailure = false;

            if (string.IsNullOrWhiteSpace(evidence.SourceSurfaceForm))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceSourceSurfaceMissing",
                    $"Derivation evidence for candidate {candidate.Identity} has no source surface form."));
                hasSemanticFailure = true;
            }

            if (string.IsNullOrWhiteSpace(evidence.SourceIdentity))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceSourceIdentityMissing",
                    $"Derivation evidence for candidate {candidate.Identity} has no source identity."));
                hasSemanticFailure = true;
            }

            if (string.IsNullOrWhiteSpace(evidence.ComponentForm))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceComponentFormMissing",
                    $"Derivation evidence for candidate {candidate.Identity} has no component form."));
                hasSemanticFailure = true;
            }

            if (evidence.SourceLength <= 0)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceSourceLengthInvalid",
                    $"Derivation evidence for candidate {candidate.Identity} has a non-positive source length."));
                hasSemanticFailure = true;
            }

            if (hasSemanticFailure)
            {
                continue;
            }

            if (!IsRangeValid(content.Length, evidence.SourceStartPosition, evidence.SourceLength))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceRangeOutsideDocument",
                    $"Derivation evidence for candidate {candidate.Identity} lies outside the original document."));
                continue;
            }

            var exactSource = content.Substring(evidence.SourceStartPosition, evidence.SourceLength);
            if (!string.Equals(exactSource, evidence.SourceSurfaceForm, StringComparison.Ordinal))
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceSubstringMismatch",
                    $"Derivation evidence for candidate {candidate.Identity} does not equal its exact original substring."));
            }

            var containingSentences = analysis.Sentences.Where(sentence =>
                evidence.SourceStartPosition >= sentence.StartPosition
                && evidence.SourceStartPosition + evidence.SourceLength <= sentence.EndPosition)
                .ToArray();
            if (containingSentences.Length != 1)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceSentenceMembershipInvalid",
                    $"Derivation evidence for candidate {candidate.Identity} belongs to {containingSentences.Length} sentence spans instead of exactly one."));
                continue;
            }

            if (containingSentences[0].Order != evidence.SourceSentenceOrder)
            {
                failures.Add(new AnalysisInvariantFailure(
                    "DerivedEvidenceSentenceOrderMismatch",
                    $"Derivation evidence for candidate {candidate.Identity} reports sentence {evidence.SourceSentenceOrder} but lies in sentence {containingSentences[0].Order}."));
            }
        }
    }

    private static bool IsRangeValid(int contentLength, int start, int length) =>
        start >= 0 && length >= 0 && start <= contentLength - length;
}
