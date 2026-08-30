namespace KnownFirst.Models.Backup;

// Archive format v3 (KF-BACKUP-006 Slice 1).
// Dedicated DTO root for Schema-13 clean learning controls and FSRS-6 card states and review history.
// Reuses unchanged V1/V2 nested records by reference without modifying V1/V2 DTO definitions.

public enum BackupFsrsCardStateKind
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3
}

public sealed record BackupWordLearningControl(
    string VocabularyId,
    DateTime DecidedAtUtc);

public sealed record BackupSenseLearningControl(
    string SenseId,
    DateTime DecidedAtUtc);

public sealed record BackupFsrsReviewHistoryEntry(
    string StableId,
    string CardId,
    int SequenceNumber,
    BackupReviewRating Rating,
    DateTime ReviewedAtUtc);

public sealed record BackupFsrsCardState(
    string CardId,
    BackupFsrsCardStateKind State,
    double? Stability,
    double? Difficulty,
    DateTime? LastReviewedAtUtc,
    int? StepIndex,
    DateTime? DueAtUtc);

public sealed record BackupRecordCountsV3(
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
    int LearningCards,
    int LearningReviews,
    int LearningWorkflows,
    int LearningQueueItems,
    int Senses,
    int AnswerVariants,
    int SenseAnswerVariantAssignments,
    int AnswerVariantProgress,
    int DerivedTermEvidence,
    int WordLearningControls,
    int SenseLearningControls,
    int FsrsReviewHistoryEntries,
    int FsrsCardStates);

public sealed record BackupManifestV3(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform,
    BackupRecordCountsV3 RecordCounts,
    string DataChecksum,
    IReadOnlyList<string> OptionalFeatures,
    IReadOnlyList<string> RequiredFeatures);

public sealed record BackupPayloadV3(
    IReadOnlyList<BackupSourceMaterial> SourceMaterials,
    IReadOnlyList<BackupVocabularyItem> Vocabulary,
    IReadOnlyList<BackupSense> Senses,
    IReadOnlyList<BackupPreparedItemV2> PreparedLearning,
    IReadOnlyList<BackupAnswerVariant> AnswerVariants,
    IReadOnlyList<BackupSenseAnswerVariantAssignment> SenseAnswerVariantAssignments,
    IReadOnlyList<BackupAnswerVariantProgress> AnswerVariantProgress,
    BackupLearningDataV2 Learning,
    BackupWorkflowDataV2 Workflows,
    IReadOnlyList<BackupDerivedTermEvidenceV2> DerivedTermEvidence,
    IReadOnlyList<BackupWordLearningControl> WordLearningControls,
    IReadOnlyList<BackupSenseLearningControl> SenseLearningControls,
    IReadOnlyList<BackupFsrsReviewHistoryEntry> FsrsReviewHistoryEntries,
    IReadOnlyList<BackupFsrsCardState> FsrsCardStates,
    BackupExtensions Extensions);
