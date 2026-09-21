using KnownFirst.Models;

namespace KnownFirst.Tests.AnalysisEvidence;

public enum ReviewAdmissionDisposition
{
    AdmittedNewWord,
    AdmittedExistingUnreviewed,
    NotAdmittedExistingUnknownBacklog,
    NotAdmittedExistingKnown,
    NotAdmittedExistingIgnored,
    NotAdmittedExistingEstablishedStatus
}

public sealed record PreImportVocabularyEntry(
    string Identity,
    string? CanonicalTerm,
    WordStatus Status,
    int TotalOccurrenceCount,
    int DocumentCount,
    int WordId);

public sealed record PersistedReviewCandidateEvidence(
    int Order,
    int WordId,
    string Identity,
    string CanonicalTerm,
    WordStatus Status,
    WordStatus PreviousWordStatus,
    bool WasWordCreatedForSession,
    int OccurrenceCount);

public sealed record CandidateAdmissionEvidence(
    string Identity,
    string CanonicalTerm,
    WordStatus? PreImportStatus,
    ReviewAdmissionDisposition Disposition,
    int? PersistedReviewOrder,
    int? PersistedWordId,
    bool WasWordCreatedForSession,
    int TotalOccurrenceCountBefore,
    int TotalOccurrenceCountAfter,
    int DocumentCountBefore,
    int DocumentCountAfter,
    int OccurrenceContributionCount);

public sealed record ReviewAdmissionEvidence(
    AnalysisEvidenceDocument AnalyzerEvidence,
    ImportAnalysisResult ImportResult,
    IReadOnlyList<PreImportVocabularyEntry> PreImportVocabulary,
    IReadOnlyList<CandidateAdmissionEvidence> CandidateAdmissions,
    IReadOnlyList<PersistedReviewCandidateEvidence> PersistedReviewCandidates)
{
    public bool HasAcceptedReviewSession =>
        ImportResult.Outcome == ImportAnalysisOutcome.Accepted && ImportResult.SessionId > 0;
}
