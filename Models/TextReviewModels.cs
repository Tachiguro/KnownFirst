using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
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
    LexicalLookupMode LookupMode,
    string? TargetLanguage)
{
    public ImportTextRequest(
        string title,
        string content,
        string textLanguage,
        string explanationLanguage)
        : this(
            title,
            content,
            textLanguage,
            string.Equals(textLanguage, explanationLanguage, StringComparison.OrdinalIgnoreCase)
                ? LexicalLookupMode.Definition
                : LexicalLookupMode.DefinitionAndTranslation,
            string.Equals(textLanguage, explanationLanguage, StringComparison.OrdinalIgnoreCase)
                ? null
                : explanationLanguage)
    {
    }

    public string ExplanationLanguage => TargetLanguage ?? TextLanguage;
}

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
    int DocumentId,
    int WordId,
    string Identity,
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

public sealed record DiagnosticsLexicalCache(
    int Id,
    string NormalizedLemma,
    string SourceLanguage,
    string ExplanationLanguage,
    TokenKind TokenKind,
    string Provider,
    string SourceProject,
    string PageTitle,
    long? RevisionId,
    DateTime FetchedAtUtc);

public sealed record DiagnosticsPreparationSession(
    int Id,
    PreparationSessionStatus Status,
    PreparationMethod Method,
    int CompletedItems,
    int TotalItems,
    DateTime UpdatedAtUtc);

public sealed record DiagnosticsPreparationCandidate(
    int Id,
    int SessionId,
    int WordId,
    string Term,
    int Order,
    PreparationCandidateStatus Status,
    int SelectedMeaningIndex,
    IReadOnlyList<string> AvailableMeanings,
    int LookupAttemptCount,
    string LastErrorCode);

public sealed record DiagnosticsPreparedMeaning(
    int Id,
    int WordId,
    string DisplayTerm,
    string SelectedMeaningId,
    string? AcronymExpansion,
    string? Translation,
    string Definition,
    string Source,
    string SourceProject,
    string SourcePageTitle,
    bool ConfirmedByUser,
    DateTime PreparedAt,
    string? EncounteredSurfaceForm = null,
    string? GrammaticalRelationship = null);

public sealed record DiagnosticsLearningCard(
    int Id,
    int WordId,
    int MeaningId,
    CardDirection Direction,
    CardState State,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor,
    ReviewRating? LastRating);

public sealed record DiagnosticsLearningReview(
    int Id,
    int CardId,
    int SessionId,
    ReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor);

public sealed record DiagnosticsLearningSession(
    int Id,
    LearningSessionStatus Status,
    int CompletedCards,
    int TotalCards,
    int AgainCount,
    int HardCount,
    int GoodCount,
    int EasyCount,
    DateTime UpdatedAtUtc);

public sealed record DiagnosticsCleanupEligibility(
    int DocumentId,
    string DocumentTitle,
    bool HasActiveReview,
    bool HasOccurrences,
    bool HasActiveContextSnapshots,
    bool IsEligible);

public sealed record ReviewDiagnosticsSnapshot(
    string DatabasePath,
    IReadOnlyList<DiagnosticsDocument> Documents,
    IReadOnlyList<DiagnosticsSentence> Sentences,
    IReadOnlyList<DiagnosticsCandidate> Candidates,
    IReadOnlyList<DiagnosticsOccurrence> Occurrences,
    IReadOnlyList<DiagnosticsSession> Sessions,
    IReadOnlyList<DiagnosticsLexicalCache> LexicalCache,
    IReadOnlyList<DiagnosticsPreparationSession> PreparationSessions,
    IReadOnlyList<DiagnosticsPreparationCandidate> PreparationCandidates,
    IReadOnlyList<DiagnosticsPreparedMeaning> PreparedMeanings,
    IReadOnlyList<DiagnosticsLearningCard> LearningCards,
    IReadOnlyList<DiagnosticsLearningReview> LearningReviews,
    IReadOnlyList<DiagnosticsLearningSession> LearningSessions,
    IReadOnlyList<DiagnosticsCleanupEligibility> CleanupEligibility,
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
