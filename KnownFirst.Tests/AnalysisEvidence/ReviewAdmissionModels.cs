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

    public ReviewAdmissionArtifactEvidence ToArtifactEvidence()
    {
        var preVocab = PreImportVocabulary
            .Select(v => new PreImportVocabularyEntryArtifact(
                v.Identity,
                v.CanonicalTerm,
                v.Status,
                v.TotalOccurrenceCount,
                v.DocumentCount))
            .ToArray();

        var admissions = CandidateAdmissions
            .Select(a => new CandidateAdmissionEntryArtifact(
                a.Identity,
                a.CanonicalTerm,
                a.PreImportStatus,
                a.Disposition,
                a.PersistedReviewOrder,
                a.WasWordCreatedForSession,
                a.TotalOccurrenceCountBefore,
                a.TotalOccurrenceCountAfter,
                a.DocumentCountBefore,
                a.DocumentCountAfter,
                a.OccurrenceContributionCount))
            .ToArray();

        var persisted = PersistedReviewCandidates
            .Select(p => new PersistedReviewCandidateEntryArtifact(
                p.Order,
                p.Identity,
                p.CanonicalTerm,
                p.Status,
                p.PreviousWordStatus,
                p.WasWordCreatedForSession,
                p.OccurrenceCount))
            .ToArray();

        return new ReviewAdmissionArtifactEvidence(
            AdmissionExecuted: true,
            Outcome: ImportResult.Outcome,
            CandidateCount: ImportResult.CandidateCount,
            PreImportVocabulary: preVocab,
            CandidateAdmissions: admissions,
            PersistedReviewCandidates: persisted);
    }
}
