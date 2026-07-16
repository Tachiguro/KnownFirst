using KnownFirst.Core.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnownFirst.Models;

public enum ImportAnalysisOutcome
{
    Accepted = 0,
    ExactDuplicate = 1,
    NoNewVocabulary = 2
}

public sealed record ImportTextRequest(
    string Title,
    string Content,
    string TextLanguage,
    string ExplanationLanguage);

public sealed record ImportAnalysisResult(
    ImportAnalysisOutcome Outcome,
    int DocumentId,
    int SessionId,
    int CandidateCount)
{
    public bool IsAccepted => Outcome == ImportAnalysisOutcome.Accepted;

    public bool IsComplete => !IsAccepted || CandidateCount == 0;
}

public sealed record ActiveReviewSummary(
    int SessionId,
    int DocumentId,
    string DocumentTitle,
    int ReviewedCount,
    int TotalCandidates);

public sealed record ReviewContext(
    int StartPosition,
    int Length,
    string BeforeTarget,
    string Target,
    string AfterTarget);

public sealed record ReviewCandidateDetails(
    int WordId,
    string Candidate,
    TokenKind TokenKind,
    IReadOnlyList<string> SurfaceForms,
    int OccurrenceCount,
    IReadOnlyList<ReviewContext> Contexts,
    int ReviewedCount,
    int TotalCandidates,
    bool CanUndo);

public sealed record CompletedReviewSummary(
    int SessionId,
    string DocumentTitle,
    int TotalCandidates,
    int KnownCount,
    int UnknownCount,
    int IgnoredCount);

public sealed record ReviewDecisionResult(
    bool IsComplete,
    CompletedReviewSummary? Summary);

public sealed record DiagnosticsDocument(
    int Id,
    string Title,
    string SourceLanguage,
    string ExplanationLanguage,
    int CharacterCount,
    int SentenceCount,
    DateTime ImportedAt,
    ReviewSessionStatus? ReviewStatus);

public sealed record DiagnosticsSentence(
    int Id,
    int DocumentId,
    string DocumentTitle,
    int StartPosition,
    int Length,
    int Order,
    string Preview);

public sealed record DiagnosticsCandidate(
    int Id,
    string Language,
    string CanonicalTerm,
    string Identity,
    TokenKind TokenKind,
    WordStatus Status,
    int OccurrenceCount,
    IReadOnlyList<string> SurfaceForms);

public sealed record DiagnosticsOccurrence(
    int Id,
    int WordId,
    int DocumentId,
    int SentenceSpanId,
    string CandidateText,
    string DocumentTitle,
    string BeforeTarget,
    string SurfaceForm,
    string AfterTarget,
    int StartPosition,
    int Length,
    int Order,
    bool IsTemporary)
{
    public string SentencePreview => BeforeTarget + SurfaceForm + AfterTarget;
}

public sealed record DiagnosticsSession(
    int Id,
    int DocumentId,
    string DocumentTitle,
    ReviewSessionStatus Status,
    int ReviewedCandidates,
    int TotalCandidates,
    int RemainingCandidates,
    DateTime StartedAt,
    DateTime? CompletedAt);

public sealed record ReviewDiagnosticsSnapshot(
    string DatabasePath,
    IReadOnlyList<DiagnosticsDocument> Documents,
    IReadOnlyList<DiagnosticsSentence> Sentences,
    IReadOnlyList<DiagnosticsCandidate> Candidates,
    IReadOnlyList<DiagnosticsOccurrence> Occurrences,
    IReadOnlyList<DiagnosticsSession> Sessions,
    ActiveReviewSummary? ActiveSession);

public static class DiagnosticsReportFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Format(ReviewDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, SerializerOptions);
    }
}
