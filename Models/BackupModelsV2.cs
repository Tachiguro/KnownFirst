namespace KnownFirst.Models.Backup;

// Archive format v2 (KF-MEANING-001 Slice 2). These types are a fully separate DTO root from the v1
// types in BackupModels.cs — nothing here is deserialized into a v1 positional-record constructor,
// and nothing in BackupModels.cs is modified. Only genuinely-unchanged v1 nested types
// (BackupSourceMaterial, BackupVocabularyItem, BackupSourceReference, BackupExtensions,
// BackupVocabularyReviewWorkflow, BackupPreparationWorkflow) are reused by reference.

/// <summary>Sense-level learning progression tier (architecture doc §2.2). Mirrors the ordinal values of
/// <see cref="KnownFirst.Data.Migrations.Schema8.SenseStatus"/> exactly.</summary>
public enum BackupSenseStatus
{
    Prepared = 0,
    Learning = 1,
    Mastered = 2,
    Suspended = 3
}

/// <summary>Direction-specific mastery requirement for a Sense/AnswerVariant assignment (architecture doc §2.6).
/// Mirrors the ordinal values of <see cref="KnownFirst.Data.Migrations.Schema8.AnswerVariantRequirement"/> exactly.</summary>
public enum BackupAnswerVariantRequirement
{
    Required = 0,
    AcceptedOnly = 1
}

public sealed record BackupSense(
    string Id,
    string StableId,
    string VocabularyId,
    string SourceLanguage,
    string ExplanationLanguage,
    string ProviderSenseId,
    string TopicOrDomain,
    string PartOfSpeech,
    string GrammaticalRelationship,
    string AcronymExpansion,
    string? DefaultMeaningId,
    BackupSenseStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>v1 <see cref="BackupContextSnapshot"/> plus an explicit <see cref="SenseId"/> (never derived
/// implicitly from the containing Meaning) so capture, validation, and restore can all prove the context's
/// Sense agrees with its parent Meaning's Sense.</summary>
public sealed record BackupContextSnapshotV2(
    string SourceMaterialId,
    string SourceTitle,
    string Text,
    int TargetStart,
    int TargetLength,
    string NormalizedFingerprint,
    DateTime CreatedAtUtc,
    string SenseId);

public sealed record BackupPreparedItemV2(
    string Id,
    string SenseId,
    string StableId,
    string VocabularyId,
    string SourceLanguage,
    string ExplanationLanguage,
    string DisplayTerm,
    string? EncounteredSurfaceForm,
    string? GrammaticalRelationship,
    BackupTokenKind TokenKind,
    string? ProviderMeaningId,
    string? AcronymExpansion,
    string? Translation,
    string? Definition,
    string? DictionaryExample,
    string? AdditionalNote,
    string? LegacyAnswerText,
    IReadOnlyList<string> AcceptedAliases,
    bool ConfirmedByUser,
    BackupSourceReference Source,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime PreparedAtUtc,
    IReadOnlyList<BackupContextSnapshotV2> Contexts);

/// <summary>Pure normalized answer-text expression. No Requirement or preferred field — those are
/// per-direction facts owned by <see cref="BackupSenseAnswerVariantAssignment"/>.</summary>
public sealed record BackupAnswerVariant(
    string Id,
    string StableId,
    string SenseId,
    string AnswerLanguage,
    string DisplayText,
    string NormalizedText,
    string? SourceMeaningId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Direction-specific requirement/preference for one answer variant. <paramref name="RequiredSinceUtc"/>
/// (KF-MEANING-001 Slice 4) is the durable Requirement-effective boundary and Required-epoch identity:
/// non-null exactly when <paramref name="Requirement"/> is
/// <see cref="BackupAnswerVariantRequirement.Required"/>. Declared last and defaulted to
/// <see langword="null"/> so an archive written before this field existed deserializes it as null (the v2
/// codec sets <c>RespectRequiredConstructorParameters</c>) and is then accepted or rejected by
/// <c>BackupModelContractV2</c> rather than failing during deserialization.
/// </summary>
public sealed record BackupSenseAnswerVariantAssignment(
    string Id,
    string StableId,
    string SenseId,
    BackupCardDirection CardDirection,
    string AnswerVariantId,
    BackupAnswerVariantRequirement Requirement,
    bool IsPreferred,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? RequiredSinceUtc = null);

/// <summary>Unique by <c>(CardId, AnswerVariantId)</c>. Not a StableId-bearing entity.</summary>
public sealed record BackupAnswerVariantProgress(
    string CardId,
    string AnswerVariantId,
    BackupLearningInteractionMode InteractionMode,
    int ConsecutiveReadingSuccessCount,
    int ConsecutiveTypingSuccessCount,
    int ConsecutiveTypingFailureCount,
    DateTime? LastAssessedAtUtc,
    bool MasteryReviewExtensionScheduled,
    bool IsMastered,
    int ReplayVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>v2 learning card. Deliberately has no <c>PreparedItemId</c> field — <see cref="PreferredMeaningId"/>
/// is the authoritative v2 contract; <see cref="VocabularyId"/> is retained only as a legacy denormalization.</summary>
public sealed record BackupLearningCardV2(
    string Id,
    string VocabularyId,
    string SenseId,
    string PreferredMeaningId,
    BackupCardDirection Direction,
    BackupCardState State,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor,
    int SuccessfulReviewCount,
    int LapseCount,
    DateTime? LastReviewedAtUtc,
    BackupReviewRating? LastRating,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BackupLearningReviewV2(
    string CardId,
    string LearningSessionId,
    BackupReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor,
    string? TargetAnswerVariantId,
    string? MatchedAnswerVariantId);

/// <summary><paramref name="StableId"/> is the KF-BACKUP-005A persistent queue-row identity. It is a
/// trailing nullable field so an archive written before Schema 10 stays byte-compatible with archive
/// formatVersion 2: a manifest reporting source schema &lt;= 9 legitimately omits it (those workflows are
/// Completed, so the reader reconstructs the identity through the shared deterministic bootstrap), while
/// a manifest reporting source schema &gt;= 10 must supply it or the archive fails closed.</summary>
public sealed record BackupLearningQueueItemV2(
    string Id,
    string CardId,
    int QueueOrder,
    bool IsDueCard,
    bool IsAgainRepeat,
    bool AnswerRevealed,
    bool SpellingChecked,
    bool SpellingCorrect,
    bool IsCompleted,
    BackupReviewRating? Rating,
    DateTime? CompletedAtUtc,
    string? TargetAnswerVariantId,
    string? StableId = null);

/// <summary><paramref name="StableId"/> is the KF-BACKUP-005A persistent workflow identity; see
/// <see cref="BackupLearningQueueItemV2"/> for why it is trailing and nullable.</summary>
public sealed record BackupLearningWorkflowV2(
    string Id,
    BackupLearningSessionStatus Status,
    int TotalCards,
    int CompletedCards,
    int AgainCount,
    int HardCount,
    int GoodCount,
    int EasyCount,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<BackupLearningQueueItemV2> QueueItems,
    string? StableId = null);

public sealed record BackupLearningDataV2(
    IReadOnlyList<BackupLearningCardV2> Cards,
    IReadOnlyList<BackupLearningReviewV2> ReviewEvents);

public sealed record BackupWorkflowDataV2(
    IReadOnlyList<BackupVocabularyReviewWorkflow> VocabularyReviews,
    IReadOnlyList<BackupPreparationWorkflow> PreparationBatches,
    IReadOnlyList<BackupLearningWorkflowV2> LearningSessions);

/// <summary>
/// German Enhanced Term Recognition Package 5A-2: transports one Schema-11 <c>DerivedTermEvidenceEntries</c>
/// row. <paramref name="ReviewItemId"/> is the owning <see cref="BackupVocabularyReviewItem.Id"/> archive
/// reference — never a target-local id, and never itself an identity component (restore/merge resolve it
/// to whichever local <c>ReviewCandidate</c> row the owning item's stable identity matches, so multiple
/// evidence rows referencing the same owning item can never multiply the candidate). This row carries no
/// archive-local id of its own: nothing else references an evidence row by id, and the physical Schema-11
/// uniqueness (<c>ReviewCandidateId, SourceIdentity, SourceStartPosition, SourceLength, ComponentForm</c>)
/// already gives every row a stable position within its owning item, so a dedicated id would be unused
/// bookkeeping (mirrors the existing <c>BackupSenseAnswerVariantAssignment</c>/<c>BackupLearningReview</c>
/// precedent of "no independent archive id for a row addressed by content"). <paramref name="SourceIdentity"/>,
/// <paramref name="SourceSurfaceForm"/>, <paramref name="SourceStartPosition"/>, <paramref name="SourceLength"/>,
/// <paramref name="SourceSentenceOrder"/>, and <paramref name="ComponentForm"/> are the exact transported
/// fields of <see cref="Data.Entities.DerivedTermEvidenceEntity"/> / <see cref="Core.Text.DerivedTermEvidence"/>.
/// </summary>
public sealed record BackupDerivedTermEvidenceV2(
    string ReviewItemId,
    string SourceIdentity,
    string SourceSurfaceForm,
    int SourceStartPosition,
    int SourceLength,
    int SourceSentenceOrder,
    string ComponentForm);

public sealed record BackupRecordCountsV2(
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
    int DerivedTermEvidence);

public sealed record BackupManifestV2(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform,
    BackupRecordCountsV2 RecordCounts,
    string DataChecksum,
    IReadOnlyList<string> OptionalFeatures,
    IReadOnlyList<string> RequiredFeatures);

public sealed record BackupPayloadV2(
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
    BackupExtensions Extensions);

/// <summary>Minimal envelope used only to peek a manifest's <c>formatVersion</c> field before deciding
/// which version-specific codec to deserialize the rest with. Never used as the authoritative manifest.</summary>
public sealed record BackupFormatVersionEnvelope(int FormatVersion);

/// <summary>Version-neutral record counts: every v1 counter is always populated; the four v2-only
/// counters are <see langword="null"/> for a v1 archive ("not applicable to this format version",
/// distinct from a v2 archive genuinely reporting <c>0</c>).</summary>
public sealed record BackupPortableArchiveCounts(
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
    int? Senses,
    int? AnswerVariants,
    int? SenseAnswerVariantAssignments,
    int? AnswerVariantProgress);

/// <summary>Version-aware replacement for the old bare <c>BackupManifest</c> return value of
/// <c>IBackupService.ValidatePortableArchiveAsync</c> — succeeds for both a v1 and a v2 archive.</summary>
public sealed record BackupPortableArchiveSummary(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform,
    IReadOnlyList<string> OptionalFeatures,
    IReadOnlyList<string> RequiredFeatures,
    BackupPortableArchiveCounts Counts);
