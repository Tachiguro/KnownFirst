namespace KnownFirst.Models.Backup;

// Archive format v4 (KF-LEARN-011 Slice 5).
// Dedicated DTO root for Schema-14 LearningTarget runtime data and target-centric FSRS scheduling.
// Reuses unchanged core lexical and workflow DTOs by reference.

public enum BackupLearningTargetKind
{
    Definition = 0,
    Translation = 1
}

public sealed record BackupLearningTarget(
    string Id,
    string StableId,
    string SenseId,
    BackupLearningTargetKind TargetKind,
    string SourceLanguage,
    string TargetLanguage,
    bool TypingOptOut,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BackupTargetAnswerVariant(
    string Id,
    string StableId,
    string TargetId,
    string AnswerLanguage,
    string DisplayText,
    string NormalizedText,
    BackupAnswerVariantRequirement Requirement,
    bool IsPreferred,
    DateTime? RequiredSinceUtc,
    string? SourceMeaningId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BackupTargetFsrsState(
    string TargetId,
    BackupFsrsCardStateKind State,
    double? Stability,
    double? Difficulty,
    DateTime? LastReviewedAtUtc,
    int? StepIndex,
    DateTime? DueAtUtc);

public sealed record BackupTargetFsrsReviewHistoryEntry(
    string Id,
    string StableId,
    string TargetId,
    int SequenceNumber,
    BackupReviewRating Rating,
    DateTime ReviewedAtUtc);

public sealed record BackupTargetReview(
    string Id,
    string StableId,
    string TargetId,
    string SessionId,
    BackupReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    bool IsSessionRepeat,
    string? TargetAnswerVariantId,
    string? MatchedAnswerVariantId,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc);

public sealed record BackupRecordCountsV4(
    int SourceMaterials,
    int SentenceRanges,
    int VocabularyItems,
    int EncounteredForms,
    int Occurrences,
    int PreparedItems,
    int ContextSnapshots,
    int LegacyReviewSummaries,
    int VocabularyReviewWorkflows,
    int VocabularyReviewItems,
    int PreparationWorkflows,
    int PreparationItems,
    int Senses,
    int DerivedTermEvidence,
    int WordLearningControls,
    int SenseLearningControls,
    int LearningWorkflows,
    int LearningQueueItems,
    int LearningTargets,
    int TargetAnswerVariants,
    int TargetFsrsStates,
    int TargetFsrsReviewHistoryEntries,
    int TargetReviews);

public sealed record BackupManifestV4(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform,
    BackupRecordCountsV4 RecordCounts,
    string DataChecksum,
    IReadOnlyList<string> OptionalFeatures,
    IReadOnlyList<string> RequiredFeatures);

public sealed record BackupPayloadV4(
    IReadOnlyList<BackupSourceMaterial> SourceMaterials,
    IReadOnlyList<BackupVocabularyItem> Vocabulary,
    IReadOnlyList<BackupSense> Senses,
    IReadOnlyList<BackupPreparedItemV2> PreparedLearning,
    BackupWorkflowDataV2 Workflows,
    IReadOnlyList<BackupDerivedTermEvidenceV2> DerivedTermEvidence,
    IReadOnlyList<BackupWordLearningControl> WordLearningControls,
    IReadOnlyList<BackupSenseLearningControl> SenseLearningControls,
    IReadOnlyList<BackupLearningTarget> LearningTargets,
    IReadOnlyList<BackupTargetAnswerVariant> TargetAnswerVariants,
    IReadOnlyList<BackupTargetFsrsState> TargetFsrsStates,
    IReadOnlyList<BackupTargetFsrsReviewHistoryEntry> TargetFsrsReviewHistoryEntries,
    IReadOnlyList<BackupTargetReview> TargetReviews,
    BackupExtensions Extensions);
