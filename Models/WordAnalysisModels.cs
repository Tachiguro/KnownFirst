#if DEBUG
using KnownFirst.Core.Text;
using System.Text;

namespace KnownFirst.Models;

public sealed record AnalysisDocumentSummary(
    int DocumentId,
    string Title,
    string SourceLanguage,
    string ExplanationLanguage,
    int CharacterCount,
    string ContentFingerprint,
    int SentenceCount,
    int IncludedTokenCount,
    int ExcludedTokenCount,
    int CandidateCount,
    int OccurrenceCount);

public sealed record AnalysisSentenceDetails(
    TextSpan Span,
    string Text,
    bool MatchesOriginalSubstring);

public sealed record DocumentAnalysisReport(
    AnalysisDocumentSummary Summary,
    IReadOnlyList<AnalysisSentenceDetails> Sentences,
    TextAnalysisDiagnostics Diagnostics);

public static class WordAnalysisReportFormatter
{
    public static string Format(DocumentAnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("KnownFirst analysis report");
        builder.AppendLine("Document summary");
        builder.AppendLine($"Title: {report.Summary.Title}");
        builder.AppendLine($"Languages: {report.Summary.SourceLanguage} -> {report.Summary.ExplanationLanguage}");
        builder.AppendLine($"Characters: {report.Summary.CharacterCount}");
        builder.AppendLine($"Fingerprint: {report.Summary.ContentFingerprint}");
        builder.AppendLine($"Sentences: {report.Summary.SentenceCount}");
        builder.AppendLine($"Included tokens: {report.Summary.IncludedTokenCount}");
        builder.AppendLine($"Excluded decisions: {report.Summary.ExcludedTokenCount}");
        builder.AppendLine($"Candidates: {report.Summary.CandidateCount}");
        builder.AppendLine($"Occurrences: {report.Summary.OccurrenceCount}");

        builder.AppendLine();
        builder.AppendLine("Sentence spans");
        foreach (var sentence in report.Sentences)
        {
            builder.AppendLine($"[{sentence.Span.Order}] start={sentence.Span.StartPosition}, length={sentence.Span.Length}, reason={sentence.Span.BoundaryReasonCode}, substring={sentence.MatchesOriginalSubstring}");
            builder.AppendLine($"Text: {sentence.Text}");
            builder.AppendLine($"Explanation: {sentence.Span.BoundaryExplanation}");
        }

        builder.AppendLine();
        builder.AppendLine("Token decisions");
        foreach (var token in report.Diagnostics.TokenDecisions)
        {
            builder.AppendLine($"start={token.StartPosition}, length={token.Length}, included={token.IsIncluded}, kind={token.Kind?.ToString() ?? "none"}, reason={token.ReasonCode}");
            builder.AppendLine($"Raw: {token.RawValue}");
            builder.AppendLine($"Normalized: {token.NormalizedValue}");
            builder.AppendLine($"Explanation: {token.Explanation}");
        }

        builder.AppendLine();
        builder.AppendLine("Candidate grouping");
        foreach (var candidate in report.Diagnostics.CandidateGroups)
        {
            builder.AppendLine($"{candidate.CanonicalTerm} ({candidate.Identity}, {candidate.Kind}), occurrences={candidate.OccurrenceCount}, reason={candidate.ReasonCode}");
            builder.AppendLine($"Forms before: {string.Join(", ", candidate.FormsBeforeDeduplication)}");
            builder.AppendLine($"Forms after: {string.Join(", ", candidate.FormsAfterDeduplication)}");
            builder.AppendLine($"Explanation: {candidate.Explanation}");
        }

        builder.AppendLine();
        builder.AppendLine("Context selection");
        foreach (var context in report.Diagnostics.ContextDecisions)
        {
            builder.AppendLine($"candidate={context.CandidateIdentity}, occurrence={context.OccurrenceOrder}, selected={context.IsSelected}, reason={context.ReasonCode}, fingerprint={context.Fingerprint}");
            builder.AppendLine($"sentenceStart={context.SentenceStartPosition}, sentenceLength={context.SentenceLength}, targetStart={context.OccurrenceStartPosition}, targetLength={context.OccurrenceLength}, targetRelativeStart={context.TargetStartInSentence}");
            builder.AppendLine($"Sentence: {context.SentenceText}");
            builder.AppendLine($"Target: {context.Target}");
            builder.AppendLine($"Explanation: {context.Explanation}");
        }

        builder.AppendLine();
        builder.AppendLine("Coordinate invariants");
        if (report.Diagnostics.InvariantFailures.Count == 0)
        {
            builder.AppendLine("All coordinate invariants passed.");
        }
        else
        {
            foreach (var failure in report.Diagnostics.InvariantFailures)
            {
                builder.AppendLine($"{failure.Code}: {failure.Explanation}");
            }
        }

        return builder.ToString();
    }
}
#endif
