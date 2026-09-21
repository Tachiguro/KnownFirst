using KnownFirst.Core.Text;
using KnownFirst.Models;

namespace KnownFirst.Tests.AnalysisEvidence;

public sealed record AnalysisInputEvidence(
    string Text,
    string? SourceLanguage,
    int Length,
    string ContentSha256);

public sealed record AnalysisSentenceEvidence(
    int Order,
    int StartPosition,
    int Length,
    string Text,
    string BoundaryReasonCode,
    string BoundaryExplanation,
    bool ExactSubstringMatch);

public sealed record AnalysisTokenDecisionEvidence(
    string RawValue,
    int StartPosition,
    int Length,
    string NormalizedValue,
    TokenKind? Kind,
    bool IsIncluded,
    string ReasonCode,
    string Explanation,
    int? SentenceOrder,
    bool ExactSubstringMatch);

public sealed record AnalysisSurfaceFormEvidence(
    string Form,
    int Count);

public sealed record AnalysisOccurrenceEvidence(
    int Order,
    string SurfaceForm,
    string Identity,
    TokenKind Kind,
    int StartPosition,
    int Length,
    int SentenceOrder,
    string? CanonicalTerm,
    TechnicalTokenFamily TechnicalFamily,
    int? TechnicalInstanceYear,
    string? TechnicalInstanceIdentifier,
    string? TechnicalVariant,
    bool ExactSubstringMatch);

public sealed record AnalysisDerivedTermEvidence(
    string SourceIdentity,
    string SourceSurfaceForm,
    int SourceStartPosition,
    int SourceLength,
    int SourceSentenceOrder,
    string ComponentForm,
    bool ExactSubstringMatch);

public sealed record AnalysisCandidateEvidence(
    string Identity,
    string CanonicalTerm,
    TokenKind Kind,
    CandidateProvenanceKind Provenance,
    IReadOnlyList<AnalysisSurfaceFormEvidence> SurfaceForms,
    int OccurrenceCount,
    IReadOnlyList<AnalysisOccurrenceEvidence> Occurrences,
    IReadOnlyList<AnalysisDerivedTermEvidence> DerivedEvidence);

public sealed record AnalysisCandidateGroupEvidence(
    string Identity,
    string CanonicalTerm,
    TokenKind Kind,
    IReadOnlyList<string> FormsBeforeDeduplication,
    IReadOnlyList<string> FormsAfterDeduplication,
    int OccurrenceCount,
    string ReasonCode,
    string Explanation);

public sealed record AnalysisContextDecisionEvidence(
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
    string Explanation,
    int TargetStartInSentence);

public sealed record AnalysisInvariantFailureEvidence(
    string Code,
    string Explanation);

public sealed record AnalysisEvidenceDocument(
    AnalysisInputEvidence Input,
    IReadOnlyList<AnalysisSentenceEvidence> Sentences,
    IReadOnlyList<AnalysisTokenDecisionEvidence> TokenDecisions,
    IReadOnlyList<AnalysisCandidateEvidence> Candidates,
    IReadOnlyList<AnalysisCandidateGroupEvidence> CandidateGroups,
    IReadOnlyList<AnalysisContextDecisionEvidence> ContextDecisions,
    IReadOnlyList<AnalysisInvariantFailureEvidence> InvariantFailures)
{
    public bool InvariantsPassed => InvariantFailures.Count == 0;
}

public enum CorpusExpectationAuthority
{
    BindingContract,
    VerifiedCurrentBehavior,
    CharacterizationExpectation,
    UnspecifiedRequiresDecision
}

public sealed record PreImportVocabularyEntryArtifact(
    string Identity,
    string? CanonicalTerm,
    WordStatus Status,
    int TotalOccurrenceCount,
    int DocumentCount);

public sealed record CandidateAdmissionEntryArtifact(
    string Identity,
    string CanonicalTerm,
    WordStatus? PreImportStatus,
    ReviewAdmissionDisposition Disposition,
    int? PersistedReviewOrder,
    bool WasWordCreatedForSession,
    int TotalOccurrenceCountBefore,
    int TotalOccurrenceCountAfter,
    int DocumentCountBefore,
    int DocumentCountAfter,
    int OccurrenceContributionCount);

public sealed record PersistedReviewCandidateEntryArtifact(
    int Order,
    string Identity,
    string CanonicalTerm,
    WordStatus Status,
    WordStatus PreviousWordStatus,
    bool WasWordCreatedForSession,
    int OccurrenceCount);

public sealed record ReviewAdmissionArtifactEvidence(
    bool AdmissionExecuted,
    ImportAnalysisOutcome? Outcome,
    int? CandidateCount,
    IReadOnlyList<PreImportVocabularyEntryArtifact>? PreImportVocabulary,
    IReadOnlyList<CandidateAdmissionEntryArtifact>? CandidateAdmissions,
    IReadOnlyList<PersistedReviewCandidateEntryArtifact>? PersistedReviewCandidates,
    string? Notes = null)
{
    public static ReviewAdmissionArtifactEvidence None() =>
        new(
            AdmissionExecuted: false,
            Outcome: null,
            CandidateCount: null,
            PreImportVocabulary: null,
            CandidateAdmissions: null,
            PersistedReviewCandidates: null,
            Notes: "Review admission execution was not requested for this case.");
}

public sealed record GermanGoldCorpusEvidenceArtifact(
    string CaseId,
    string Description,
    CorpusExpectationAuthority Authority,
    AnalysisEvidenceDocument AnalyzerEvidence,
    ReviewAdmissionArtifactEvidence ReviewAdmission);
