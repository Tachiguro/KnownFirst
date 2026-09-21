using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests.AnalysisEvidence;

public static class AnalysisEvidenceCollector
{
    public static AnalysisEvidenceDocument Collect(
        string content,
        string? sourceLanguage = null,
        bool enableGermanCompoundDecomposition = false,
        IGermanLexicon? germanLexicon = null,
        TextAnalyzer? analyzer = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        analyzer ??= new TextAnalyzer();
        var result = analyzer.Analyze(content, sourceLanguage, enableGermanCompoundDecomposition, germanLexicon);
        return Collect(content, result, sourceLanguage);
    }

    public static AnalysisEvidenceDocument Collect(
        string content,
        TextAnalysisResult result,
        string? sourceLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(result);

        var contentSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var input = new AnalysisInputEvidence(content, sourceLanguage, content.Length, contentSha256);

        var sentences = result.Sentences
            .OrderBy(s => s.Order)
            .Select(s =>
            {
                var inRange = s.StartPosition >= 0 && s.Length >= 0 && s.StartPosition + s.Length <= content.Length;
                var text = inRange ? content.Substring(s.StartPosition, s.Length) : string.Empty;
                var exactMatch = inRange && string.Equals(text, content.Substring(s.StartPosition, s.Length), StringComparison.Ordinal);
                return new AnalysisSentenceEvidence(
                    s.Order,
                    s.StartPosition,
                    s.Length,
                    text,
                    s.BoundaryReasonCode,
                    s.BoundaryExplanation,
                    exactMatch);
            })
            .ToArray();

#if DEBUG
        var tokenDecisions = (result.Diagnostics?.TokenDecisions ?? Array.Empty<TokenAnalysisDecision>())
            .Select(d =>
            {
                var inRange = d.StartPosition >= 0 && d.Length >= 0 && d.StartPosition + d.Length <= content.Length;
                var exactMatch = inRange && string.Equals(content.Substring(d.StartPosition, d.Length), d.RawValue, StringComparison.Ordinal);
                return new AnalysisTokenDecisionEvidence(
                    d.RawValue,
                    d.StartPosition,
                    d.Length,
                    d.NormalizedValue,
                    d.Kind,
                    d.IsIncluded,
                    d.ReasonCode,
                    d.Explanation,
                    d.SentenceOrder,
                    exactMatch);
            })
            .ToArray();

        var candidateGroups = (result.Diagnostics?.CandidateGroups ?? Array.Empty<CandidateGroupingAnalysis>())
            .Select(g => new AnalysisCandidateGroupEvidence(
                g.Identity,
                g.CanonicalTerm,
                g.Kind,
                g.FormsBeforeDeduplication,
                g.FormsAfterDeduplication,
                g.OccurrenceCount,
                g.ReasonCode,
                g.Explanation))
            .ToArray();

        var contextDecisions = (result.Diagnostics?.ContextDecisions ?? Array.Empty<ContextSelectionDecision>())
            .Select(c => new AnalysisContextDecisionEvidence(
                c.CandidateIdentity,
                c.OccurrenceOrder,
                c.SentenceOrder,
                c.SentenceStartPosition,
                c.SentenceLength,
                c.OccurrenceStartPosition,
                c.OccurrenceLength,
                c.SentenceText,
                c.Target,
                c.Fingerprint,
                c.IsSelected,
                c.ReasonCode,
                c.Explanation,
                c.TargetStartInSentence))
            .ToArray();

        var invariantFailures = (result.Diagnostics?.InvariantFailures ?? Array.Empty<AnalysisInvariantFailure>())
            .Select(f => new AnalysisInvariantFailureEvidence(f.Code, f.Explanation))
            .ToArray();
#else
        var tokenDecisions = Array.Empty<AnalysisTokenDecisionEvidence>();
        var candidateGroups = Array.Empty<AnalysisCandidateGroupEvidence>();
        var contextDecisions = Array.Empty<AnalysisContextDecisionEvidence>();
        var invariantFailures = Array.Empty<AnalysisInvariantFailureEvidence>();
#endif

        var candidates = result.Candidates
            .Select(c =>
            {
                var surfaceForms = c.SurfaceForms
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new AnalysisSurfaceFormEvidence(kv.Key, kv.Value))
                    .ToArray();

                var occurrences = c.Occurrences
                    .OrderBy(o => o.Order)
                    .Select(o =>
                    {
                        var inRange = o.StartPosition >= 0 && o.Length >= 0 && o.StartPosition + o.Length <= content.Length;
                        var exactMatch = inRange && string.Equals(content.Substring(o.StartPosition, o.Length), o.SurfaceForm, StringComparison.Ordinal);
                        return new AnalysisOccurrenceEvidence(
                            o.Order,
                            o.SurfaceForm,
                            o.Identity,
                            o.Kind,
                            o.StartPosition,
                            o.Length,
                            o.SentenceOrder,
                            o.CanonicalTerm,
                            o.TechnicalFamily,
                            o.TechnicalInstanceYear,
                            o.TechnicalInstanceIdentifier,
                            o.TechnicalVariant,
                            exactMatch);
                    })
                    .ToArray();

                var derivedEvidence = c.DerivedEvidence
                    .OrderBy(d => d.SourceSentenceOrder)
                    .ThenBy(d => d.SourceStartPosition)
                    .ThenBy(d => d.ComponentForm, StringComparer.Ordinal)
                    .Select(d =>
                    {
                        var inRange = d.SourceStartPosition >= 0 && d.SourceLength >= 0 && d.SourceStartPosition + d.SourceLength <= content.Length;
                        var exactMatch = inRange && string.Equals(content.Substring(d.SourceStartPosition, d.SourceLength), d.SourceSurfaceForm, StringComparison.Ordinal);
                        return new AnalysisDerivedTermEvidence(
                            d.SourceIdentity,
                            d.SourceSurfaceForm,
                            d.SourceStartPosition,
                            d.SourceLength,
                            d.SourceSentenceOrder,
                            d.ComponentForm,
                            exactMatch);
                    })
                    .ToArray();

                return new AnalysisCandidateEvidence(
                    c.Identity,
                    c.CanonicalTerm,
                    c.Kind,
                    c.Provenance,
                    surfaceForms,
                    c.Occurrences.Count,
                    occurrences,
                    derivedEvidence);
            })
            .ToArray();

        return new AnalysisEvidenceDocument(
            input,
            sentences,
            tokenDecisions,
            candidates,
            candidateGroups,
            contextDecisions,
            invariantFailures);
    }
}
