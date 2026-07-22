namespace KnownFirst.Models.Backup;

public enum BackupSourcePlatform
{
    Windows,
    Android
}

public enum BackupLexicalLookupMode
{
    Definition,
    Translation,
    DefinitionAndTranslation
}

public enum BackupKnowledgeState
{
    Unreviewed,
    Known,
    UnknownBacklog,
    Prepared,
    Learning,
    Mastered,
    Ignored
}

public enum BackupTokenKind
{
    Word,
    Acronym,
    Abbreviation,
    TechnicalTerm
}

public enum BackupPreparationState
{
    Unprepared,
    Preparing,
    Prepared,
    PreparationFailed
}

public enum BackupLearningInteractionMode
{
    Reading,
    Typing
}

public enum BackupTechnicalTokenFamily
{
    None,
    Cve,
    Sha
}

public enum BackupCardDirection
{
    TermToMeaning,
    MeaningToTerm
}

public enum BackupCardState
{
    New,
    Learning,
    Review,
    Relearning,
    Suspended,
    Retired
}

public enum BackupReviewRating
{
    Again,
    Hard,
    Good,
    Easy
}

public enum BackupReviewSessionStatus
{
    Active,
    Completed
}

public enum BackupPreparationMethod
{
    AutomaticOnline,
    Manual
}

public enum BackupPreparationSessionStatus
{
    Active,
    Completed,
    Cancelled
}

public enum BackupPreparationCandidateStatus
{
    Pending,
    ResultReady,
    Prepared,
    Skipped,
    Failed,
    MarkedKnown,
    Excluded,
    Cancelled
}

public enum BackupLearningSessionStatus
{
    Active,
    Completed
}

public enum BackupLexicalLookupStatus
{
    Success,
    NotFound,
    TransientFailure,
    PermanentFailure,
    ParseFailure
}

public enum BackupGrammaticalRelationKind
{
    Plural,
    Singular,
    ThirdPersonSingular,
    PastTense,
    PastParticiple,
    PresentParticiple,
    Comparative,
    Superlative
}

public sealed record BackupManifest(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform,
    BackupRecordCounts RecordCounts,
    string DataChecksum,
    IReadOnlyList<string> OptionalFeatures,
    IReadOnlyList<string> RequiredFeatures);

public sealed record BackupRecordCounts(
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
    int LearningQueueItems);

public sealed record BackupPayload(
    IReadOnlyList<BackupSourceMaterial> SourceMaterials,
    IReadOnlyList<BackupVocabularyItem> Vocabulary,
    IReadOnlyList<BackupPreparedItem> PreparedLearning,
    BackupLearningData Learning,
    BackupWorkflowData Workflows,
    BackupExtensions Extensions);

public sealed record BackupSourceMaterial(
    string Id,
    string Title,
    string TextLanguage,
    string ExplanationLanguage,
    BackupLexicalLookupMode LookupMode,
    string? TargetLanguage,
    string OriginalText,
    string ContentSha256,
    DateTime ImportedAtUtc,
    int StoredWordCount,
    IReadOnlyList<BackupSentenceRange> Sentences,
    IReadOnlyList<BackupOccurrence> Occurrences);

public sealed record BackupSentenceRange(
    string Id,
    int Order,
    int Start,
    int Length);

public sealed record BackupOccurrence(
    string VocabularyId,
    string SentenceId,
    int Start,
    int Length,
    string SurfaceForm,
    int Order,
    BackupTechnicalTokenFamily TechnicalFamily,
    int? TechnicalInstanceYear,
    string? TechnicalInstanceIdentifier,
    string? TechnicalVariant);

public sealed record BackupVocabularyItem(
    string Id,
    string Language,
    string CanonicalTerm,
    string IdentityKey,
    BackupTokenKind TokenKind,
    BackupKnowledgeState KnowledgeState,
    BackupPreparationState PreparationState,
    int TotalOccurrenceCount,
    int DocumentCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<BackupEncounteredForm> EncounteredForms,
    BackupAutomaticLearningState AutomaticLearning,
    IReadOnlyList<BackupLegacyReviewSummary> LegacyReviewSummaries);

public sealed record BackupEncounteredForm(
    string SurfaceForm,
    int OccurrenceCount);

public sealed record BackupAutomaticLearningState(
    BackupLearningInteractionMode InteractionMode,
    int ConsecutiveRecallSuccessCount,
    int ConsecutiveTypingSuccessCount,
    int ConsecutiveTypingFailureCount,
    bool MasteryReviewExtensionScheduled);

public sealed record BackupLegacyReviewSummary(
    int ReviewCount,
    int ForgotCount,
    int PartialCount,
    int KnownCount,
    DateTime? LastReviewedAtUtc);

public sealed record BackupPreparedItem(
    string Id,
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
    IReadOnlyList<BackupContextSnapshot> Contexts);

public sealed record BackupSourceReference(
    string ProviderName,
    string SourceProject,
    string PageTitle,
    long? RevisionId,
    string Attribution);

public sealed record BackupContextSnapshot(
    string SourceMaterialId,
    string SourceTitle,
    string Text,
    int TargetStart,
    int TargetLength,
    string NormalizedFingerprint,
    DateTime CreatedAtUtc);

public sealed record BackupLearningData(
    IReadOnlyList<BackupLearningCard> Cards,
    IReadOnlyList<BackupLearningReview> ReviewEvents);

public sealed record BackupLearningCard(
    string Id,
    string VocabularyId,
    string PreparedItemId,
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

public sealed record BackupLearningReview(
    string CardId,
    string LearningSessionId,
    BackupReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor);

public sealed record BackupWorkflowData(
    IReadOnlyList<BackupVocabularyReviewWorkflow> VocabularyReviews,
    IReadOnlyList<BackupPreparationWorkflow> PreparationBatches,
    IReadOnlyList<BackupLearningWorkflow> LearningSessions);

public sealed record BackupVocabularyReviewWorkflow(
    string Id,
    string SourceMaterialId,
    BackupReviewSessionStatus Status,
    int TotalCandidates,
    int ReviewedCount,
    int KnownCount,
    int UnknownCount,
    int IgnoredCount,
    int DecisionSequence,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<BackupVocabularyReviewItem> Items);

public sealed record BackupVocabularyReviewItem(
    string Id,
    string VocabularyId,
    int Order,
    BackupKnowledgeState Status,
    BackupKnowledgeState PreviousKnowledgeState,
    int PreviousTotalOccurrenceCount,
    int PreviousDocumentCount,
    DateTime PreviousUpdatedAtUtc,
    int DecisionSequence,
    bool WasVocabularyCreatedForSession,
    DateTime? DecidedAtUtc);

public sealed record BackupPreparationWorkflow(
    string Id,
    BackupPreparationSessionStatus Status,
    BackupPreparationMethod Method,
    int TotalItems,
    int CompletedItems,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<BackupPreparationItem> Items);

public sealed record BackupPreparationItem(
    string Id,
    string VocabularyId,
    int Order,
    BackupPreparationCandidateStatus Status,
    int SelectedMeaningIndex,
    string? LastErrorCode,
    int LookupAttemptCount,
    DateTime UpdatedAtUtc,
    BackupLookupDraft? LookupDraft);

public sealed record BackupLookupDraft(
    BackupLexicalLookupStatus Status,
    BackupLexicalLookupMode LookupMode,
    string SourceLanguage,
    string ExplanationLanguage,
    string? TargetLanguage,
    string QueriedLemma,
    string DisplayTerm,
    BackupTokenKind TokenKind,
    string? AcronymExpansion,
    string? EncounteredSurfaceForm,
    string? GrammaticalRelationship,
    IReadOnlyList<BackupLookupMeaning> Meanings,
    BackupSourceReference Source,
    DateTime LookupAtUtc,
    int RedirectDepth,
    IReadOnlyList<BackupFormRelation> FormRelations);

public sealed record BackupLookupMeaning(
    string MeaningId,
    string? PartOfSpeech,
    string Definition,
    string? Translation,
    string? Example,
    IReadOnlyList<string> UsageLabels);

public sealed record BackupFormRelation(
    BackupGrammaticalRelationKind Kind,
    string BaseLemma,
    string Relationship);

public sealed record BackupLearningWorkflow(
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
    IReadOnlyList<BackupLearningQueueItem> QueueItems);

public sealed record BackupLearningQueueItem(
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
    DateTime? CompletedAtUtc);

public sealed record BackupExtensions(
    IReadOnlyDictionary<string, BackupExtensionPayload> Features);

public sealed record BackupExtensionPayload(string Json);

public sealed record BackupRestorePreview(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform,
    BackupRecordCounts RecordCounts,
    IReadOnlyList<string> OptionalFeatures,
    IReadOnlyList<string> WarningCodes,
    bool ChecksumVerified,
    BackupReplaceAllScope ReplaceAllScope);

public sealed record BackupReplaceAllScope(
    bool ReplacesPersonalDatabaseData,
    bool ClearsLexicalCache,
    bool PreservesPreferences,
    bool PreservesOnlineLookupConsent,
    bool PreservesLogs);
