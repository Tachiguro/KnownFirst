using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Outcome of a read-only merge preflight (design §9, KF-BACKUP-002 Slice 3). <see cref="Ready"/> and
/// <see cref="NoChanges"/> are the only statuses where <see cref="MergePreflightPlan.IsExecutable"/> is
/// true. <see cref="RequiresUserDecision"/> means at least one required, blocking decision exists
/// (<see cref="MergePreflightPlan.KnowledgeStateConflictDecisions"/>,
/// <see cref="MergePreflightPlan.WorkflowStatusConflictDecisions"/>,
/// <see cref="MergePreflightPlan.SemanticMeaningGroupingDecisions"/>, or
/// <see cref="MergePreflightPlan.PreferredVariantSelectionDecisions"/> — all four are blocking). <see
/// cref="BlockedByPrerequisite"/> means no decision is outstanding but at least one entry exists in
/// <see cref="MergePreflightPlan.BlockingPrerequisites"/> (current storage — live schema or archive
/// format — cannot represent the planned result at all, so there is nothing to decide, only to migrate).
/// Exactly one of these two blocking conditions determines the status when either is present; if both
/// are present, <see cref="RequiresUserDecision"/> takes priority for user-facing framing, but
/// <see cref="MergePreflightPlan.IsExecutable"/> is false either way. The remaining statuses are
/// early-exit outcomes produced before the pure planner ever runs.
/// </summary>
public enum MergePreflightStatus
{
    Ready,
    NoChanges,
    RequiresUserDecision,
    BlockedByPrerequisite,
    BlockedByActiveWorkflow,
    ValidationFailed,
    Cancelled,
    Failed
}

/// <summary>
/// Every physical portable entity graph this slice plans, mirroring <see cref="BackupRecordCounts"/>
/// one-for-one. Answer variants (PrimaryAnswer/AcceptedAlias text decomposed out of a
/// <see cref="PreparedMeaning"/> row) are <b>not</b> a physical entity kind — the archive format has no
/// independent answer-variant row — so they are exposed separately as
/// <see cref="MergePreflightPlan.DerivedAnswerVariantPlans"/> and never counted here or given their own
/// primary <see cref="MergePlanAction"/>.
/// </summary>
public enum MergeEntityKind
{
    SourceMaterial,
    SentenceRange,
    Vocabulary,
    EncounteredForm,
    Occurrence,
    PreparedMeaning,
    ContextSnapshot,
    LegacyReviewSummary,
    VocabularyReviewWorkflow,
    VocabularyReviewItem,
    PreparationWorkflow,
    PreparationItem,
    LearningCard,
    LearningReview,
    LearningWorkflow,
    LearningQueueItem
}

/// <summary>
/// One of exactly six mutually-exclusive classifications a physical archive-side entity can receive.
/// Every entity kind uses whichever subset applies to it; e.g. only <see cref="PreparedMeaning"/> ever
/// produces <see cref="PreservedVariant"/> (design rule: "same SemanticMeaning plus different
/// note/example/provenance: preserve exact content variant without creating a second semantic card"),
/// and only <see cref="MergeEntityKind.LearningReview"/> ever produces <see cref="DeduplicatedEvent"/>
/// (its analog of <see cref="ExactDuplicateSkipped"/>). A <see cref="MergeEntityKind.LearningCard"/>
/// whose SemanticMeaning genuinely differs from a matched physical slot's occupant classifies
/// <see cref="New"/> (a distinct planned future card), never <see cref="PreservedVariant"/> — see
/// <see cref="MergePreflightPlan.BlockingPrerequisites"/> for how that is represented instead.
/// </summary>
public enum MergeEntityClassification
{
    New,
    ExactDuplicateSkipped,
    Enriched,
    PreservedVariant,
    UnresolvedConflict,
    DeduplicatedEvent
}

public sealed record MergeEntityPlanCounts(
    int NewCount,
    int ExactDuplicateSkippedCount,
    int EnrichedCount,
    int PreservedVariantCount,
    int UnresolvedConflictCount,
    int DeduplicatedEventCount)
{
    public static readonly MergeEntityPlanCounts Zero = new(0, 0, 0, 0, 0, 0);

    public int TotalInsertableCount => NewCount + EnrichedCount + PreservedVariantCount;

    public int Total => NewCount + ExactDuplicateSkippedCount + EnrichedCount + PreservedVariantCount + UnresolvedConflictCount + DeduplicatedEventCount;

    public MergeEntityPlanCounts Increment(MergeEntityClassification classification) => classification switch
    {
        MergeEntityClassification.New => this with { NewCount = NewCount + 1 },
        MergeEntityClassification.ExactDuplicateSkipped => this with { ExactDuplicateSkippedCount = ExactDuplicateSkippedCount + 1 },
        MergeEntityClassification.Enriched => this with { EnrichedCount = EnrichedCount + 1 },
        MergeEntityClassification.PreservedVariant => this with { PreservedVariantCount = PreservedVariantCount + 1 },
        MergeEntityClassification.UnresolvedConflict => this with { UnresolvedConflictCount = UnresolvedConflictCount + 1 },
        MergeEntityClassification.DeduplicatedEvent => this with { DeduplicatedEventCount = DeduplicatedEventCount + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, null)
    };
}

/// <summary>
/// One exact, itemized plan action for a single physical archive-side entity — every archive row
/// receives exactly one of these as its primary action, and every primary action corresponds to exactly
/// one physical archive row (verified by test against <see cref="BackupRecordCounts"/>).
/// <see cref="StableIdentity"/> is the content-derived merge identity (never a target-local numeric id);
/// <see cref="ArchiveLocalId"/> is the entity's own archive-local string id (or, for entities with no own
/// id such as a LearningReview, a synthesized positional label), retained only so a future writer can
/// look the content back up inside the same archive <see cref="BackupPayload"/> it already holds — it is
/// never a target-local id. <see cref="DecisionId"/> is non-null if and only if <see cref="Classification"/>
/// is <see cref="MergeEntityClassification.UnresolvedConflict"/>, and matches exactly one decision
/// object's own <see cref="DecisionId"/> in the plan.
/// </summary>
public sealed record MergePlanAction(
    MergeEntityKind EntityKind,
    string StableIdentity,
    string ArchiveLocalId,
    MergeEntityClassification Classification,
    string ReasonCode,
    DecisionId? DecisionId = null);

/// <summary>
/// A required decision for a same-tier <see cref="BackupKnowledgeState"/> conflict (design §5.1 tier 1:
/// Known/UnknownBacklog/Ignored). No choice set is defined yet (design §13 item 1 leaves the UX open),
/// so this only exposes both sides' values; the target's value is kept unchanged until a future release
/// resolves §13 item 1.
/// </summary>
public sealed record KnowledgeStateConflictDecision(
    DecisionId DecisionId,
    VocabularyIdentity VocabularyIdentity,
    string ArchiveVocabularyId,
    BackupKnowledgeState TargetState,
    BackupKnowledgeState ArchiveState,
    string ReasonCode);

/// <summary>
/// Choices for resolving a <see cref="PreferredVariantSelectionDecision"/>. Never applied automatically;
/// exactly two choices exist because the decision is purely about which side's exact Meaning variant the
/// future card references/displays as preferred — it never discards data, so there is no "keep both"
/// choice the way an actual conflict might need.
/// </summary>
public enum PreferredVariantChoice
{
    SelectTargetVariant,
    SelectArchiveVariant
}

/// <summary>
/// Required, blocking decision surfaced when target and archive have a matched card — the same
/// <see cref="FutureCardIdentity"/> (the same learnable sense, already confirmed to be the same by
/// identity) — but the two cards' referenced PreparedItem rows resolve to different
/// <see cref="ExactMeaningVariantIdentity"/> values. <b>Correction (final focused review):</b> the
/// comparison key is the referenced <see cref="ExactMeaningVariantIdentity"/> of each side's card, never
/// <c>DisplayTerm</c> text — <c>DisplayTerm</c> is presentation content, not stable variant identity, and
/// two cards can reference different exact variants while happening to share the same <c>DisplayTerm</c>
/// (that divergence would otherwise be silently missed). <c>TargetPreferredAnswerText</c>/
/// <c>ArchivePreferredAnswerText</c> below are human-readable summaries only, never the comparison key.
/// Both exact variants are always preserved regardless of this decision — the decision changes only which
/// one the future card references/displays as preferred; it never deletes a Meaning variant. Unresolved,
/// this decision makes <see cref="MergePreflightPlan.Status"/> <see cref="MergePreflightStatus.RequiresUserDecision"/>
/// and <see cref="MergePreflightPlan.IsExecutable"/> false — it is not advisory. Never created for two
/// genuinely distinct <see cref="SemanticMeaningIdentity"/> values (no matched <see cref="FutureCardIdentity"/>
/// exists in that case) — that scenario has no natural preferred/non-preferred pairing to select between.
/// </summary>
public sealed record PreferredVariantSelectionDecision(
    DecisionId DecisionId,
    FutureCardIdentity FutureCardIdentity,
    SemanticMeaningIdentity SemanticMeaningIdentity,
    ExactMeaningVariantIdentity TargetExactMeaningVariantIdentity,
    ExactMeaningVariantIdentity ArchiveExactMeaningVariantIdentity,
    string TargetPreferredAnswerText,
    string ArchivePreferredAnswerText,
    IReadOnlyList<PreferredVariantChoice> AvailableChoices)
{
    public static readonly IReadOnlyList<PreferredVariantChoice> StandardChoices =
    [
        PreferredVariantChoice.SelectTargetVariant,
        PreferredVariantChoice.SelectArchiveVariant
    ];
}

/// <summary>
/// Required decision for a matched workflow-session/row whose full historical content diverges in a way
/// the current schema cannot represent as two preserved rows (e.g. <c>ReviewSessionEntity</c>'s unique
/// index on <c>DocumentId</c> permits only one session per document, so two devices' independently
/// completed review sessions for the same document cannot both be kept — see
/// <see cref="MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired"/>). Also covers
/// <c>PreparationWorkflow</c>'s two same-tier terminal statuses (Completed vs. Cancelled — design §5's
/// <c>WorkflowSessionStateConflictPolicy</c>). Kept minimal (no bespoke choice set) because no product
/// decision has been made about how a user should choose; the target's existing content is kept
/// unchanged until one is.
/// </summary>
public sealed record WorkflowStatusConflictDecision(
    DecisionId DecisionId,
    MergeEntityKind EntityKind,
    string ArchiveLocalId,
    string ReasonCode);

/// <summary>
/// Bounded, non-identifying summary of a semantic meaning's available strong-discriminator fields, for
/// display inside a <see cref="SemanticMeaningGroupingDecision"/>. Fields that are absent (never
/// persisted, or not supplied by this particular meaning) are null — never a guessed or default value.
/// </summary>
public sealed record SemanticMeaningVariantSummary(
    string? Definition,
    string? Translation,
    string? ProviderSenseId,
    string? GrammaticalRelationship,
    string? TopicOrDomain,
    string ExplanationLanguage);

/// <summary>Choices for resolving a <see cref="SemanticMeaningGroupingDecision"/>. Never applied automatically.</summary>
public enum SemanticMeaningGroupingChoice
{
    TreatAsSameSemanticMeaning,
    TreatAsDistinctSemanticMeanings
}

/// <summary>
/// Required, blocking decision surfaced when a target and an archive PreparedItem share the same stable
/// Word identity and the same source/explanation languages, and <b>neither</b> side carries any reliable
/// sense discriminator (<see cref="SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator"/> is false
/// for both) — so their <see cref="SemanticMeaningIdentity"/> values collapsing to equal is not
/// trustworthy evidence they are really the same sense, yet their Translation/answer text differs. The
/// planner never guesses in this situation. Both exact meaning variants remain preserved regardless of
/// which choice is later made — this decision only determines whether they are grouped under one
/// SemanticMeaning (sharing a future card) or planned as two. Unresolved, this decision makes
/// <see cref="MergePreflightPlan.Status"/> <see cref="MergePreflightStatus.RequiresUserDecision"/> and
/// <see cref="MergePreflightPlan.IsExecutable"/> false.
/// </summary>
public sealed record SemanticMeaningGroupingDecision(
    DecisionId DecisionId,
    VocabularyIdentity VocabularyIdentity,
    SemanticMeaningVariantSummary TargetSummary,
    SemanticMeaningVariantSummary ArchiveSummary,
    IReadOnlyList<SemanticMeaningGroupingChoice> AvailableChoices)
{
    public static readonly IReadOnlyList<SemanticMeaningGroupingChoice> StandardChoices =
    [
        SemanticMeaningGroupingChoice.TreatAsSameSemanticMeaning,
        SemanticMeaningGroupingChoice.TreatAsDistinctSemanticMeanings
    ];
}

/// <summary>
/// One derived answer-variant plan entry decomposed out of a physical <see cref="MergeEntityKind.PreparedMeaning"/>
/// row's <c>DisplayTerm</c> (<see cref="AnswerVariantRole.PrimaryAnswer"/>) or accepted-alias list
/// (<see cref="AnswerVariantRole.AcceptedAlias"/>). This is not a physical archive entity — the archive
/// format has no independent answer-variant row — so instances here never increment any
/// <see cref="MergeEntityPlanCounts"/> and are not counted toward "every archive row receives exactly one
/// primary action." <see cref="Role"/> is presentational metadata only (see
/// <see cref="AnswerVariantIdentity"/>'s doc comment) and never itself splits identity.
/// <see cref="AnswerVariantRole.RequiredAnswerVariant"/> is never produced by Slice 3: current archives
/// have no field recording which alias a user selected as individually required.
/// </summary>
public sealed record DerivedAnswerVariantPlan(
    AnswerVariantIdentity StableIdentity,
    string SourcePreparedItemArchiveLocalId,
    AnswerVariantRole Role,
    MergeEntityClassification Classification,
    string ReasonCode);

/// <summary>Manifest fields surfaced verbatim from the validated archive; never includes wall-clock "now".</summary>
public sealed record MergeManifestInfo(
    int FormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    DateTime CreatedAtUtc,
    BackupSourcePlatform SourcePlatform);

/// <summary>
/// The complete, deterministic read-only merge preflight result (design §9, KF-BACKUP-002 Slice 3 /
/// KF-MEANING-001 identity revision). Contains no target-local numeric ids and no wall-clock timestamp
/// beyond the archive's own manifest <see cref="MergeManifestInfo.CreatedAtUtc"/>. Never opens a write
/// transaction and never creates a safety copy — both remain later slices' responsibility.
/// <see cref="IsExecutable"/> is true if and only if <see cref="Status"/> is <see cref="MergePreflightStatus.Ready"/>
/// or <see cref="MergePreflightStatus.NoChanges"/>; it is false whenever any blocking decision or any
/// entry in <see cref="BlockingPrerequisites"/> exists, even if <see cref="WarningCodes"/> also contains
/// purely informational notices that do not by themselves prevent execution.
/// </summary>
public sealed record MergePreflightPlan(
    MergePreflightStatus Status,
    bool IsExecutable,
    MergeManifestInfo? Manifest,
    bool ChecksumVerified,
    IReadOnlyDictionary<MergeEntityKind, MergeEntityPlanCounts> PerEntity,
    IReadOnlyList<MergePlanAction> Actions,
    IReadOnlyList<DerivedAnswerVariantPlan> DerivedAnswerVariantPlans,
    IReadOnlyList<KnowledgeStateConflictDecision> KnowledgeStateConflictDecisions,
    IReadOnlyList<WorkflowStatusConflictDecision> WorkflowStatusConflictDecisions,
    IReadOnlyList<SemanticMeaningGroupingDecision> SemanticMeaningGroupingDecisions,
    IReadOnlyList<PreferredVariantSelectionDecision> PreferredVariantSelectionDecisions,
    IReadOnlyList<string> BlockingPrerequisites,
    IReadOnlyDictionary<MergeEntityClassification, IReadOnlyList<string>> SampleDetails,
    IReadOnlyList<string> WarningCodes,
    bool RequiresSchedulerReplay,
    string? ErrorCode)
{
    public const int MaxSampleDetailsPerCategory = 20;

    public static MergePreflightPlan ForEarlyExit(MergePreflightStatus status, MergeManifestInfo? manifest, bool checksumVerified, string? errorCode) =>
        new(
            status,
            false,
            manifest,
            checksumVerified,
            new Dictionary<MergeEntityKind, MergeEntityPlanCounts>(),
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<MergeEntityClassification, IReadOnlyList<string>>(),
            [],
            false,
            errorCode);
}

/// <summary>
/// Stable error codes specific to Slice 3's read-only preflight orchestration. Reuses
/// <see cref="BackupErrorCodes"/> wherever an existing stable code already applies (archive validation
/// failure, active-workflow, cancellation); this class only fills the one genuine gap — an unexpected
/// internal failure that must never surface raw exception text.
/// </summary>
public static class MergePreflightErrorCodes
{
    public const string UnexpectedFailure = "merge-preflight-unexpected-failure";

    /// <summary>
    /// Schema 8 is active, but the read-only target projection and planner adaptation intentionally remain
    /// owned by Meaning Slice 7. Slice 6 returns this stable fail-closed result before legacy capture runs.
    /// </summary>
    public const string Schema8AdaptationRequired = "merge-preflight-schema8-adaptation-required";
}

/// <summary>
/// Deterministic, stable codes recorded whenever a planned outcome cannot be fully represented by the
/// current live schema or archive format (KF-MEANING-001 acceptance work). <see cref="MeaningCardSchemaMigrationRequired"/>
/// and <see cref="ArchiveFormatMigrationRequired"/> and <see cref="WorkflowHistorySchemaMigrationRequired"/>
/// are <b>blocking</b> — they are recorded in <see cref="MergePreflightPlan.BlockingPrerequisites"/> and
/// force <see cref="MergePreflightPlan.IsExecutable"/> false. <see cref="AnswerVariantProgressMigrationRequired"/>
/// and <see cref="TopicPersistenceRequired"/> are informational — recorded only in
/// <see cref="MergePreflightPlan.WarningCodes"/> — because, verified against current code, neither ever
/// corresponds to a concrete piece of content the plan cannot represent today (see each constant's own
/// doc comment). Nothing any of these codes describes is ever silently collapsed: every distinct
/// SemanticMeaning and every distinct AnswerVariant is still fully counted and itemized.
/// </summary>
public static class MergePreflightSchemaGapCodes
{
    /// <summary>
    /// <b>Blocking.</b> Two distinct <see cref="FutureCardIdentity"/> values (i.e. two distinct
    /// SemanticMeanings, same Word and Direction) both require the one physical <c>LearningCardEntity</c>
    /// slot the live schema's <c>IX_LearningCards_Word_Direction</c> unique index allows
    /// (<c>(WordId, Direction)</c>, verified in code — see architecture document §16.4/§17). Both senses
    /// are preserved as planned content (each classifies <see cref="MergeEntityClassification.New"/>, a
    /// distinct planned future card), but neither can be scheduled today. Always accompanies
    /// <see cref="ArchiveFormatMigrationRequired"/>.
    /// </summary>
    public const string MeaningCardSchemaMigrationRequired = "meaning-card-schema-migration-required";

    /// <summary>
    /// <b>Blocking.</b> Verified against <c>BackupArchiveWriter.ValidatePayloadGraph</c>: a single v1
    /// archive payload cannot itself contain two <c>BackupLearningCard</c> entries sharing the same
    /// <c>(VocabularyId, Direction)</c> pair — its <c>cardKeys</c> uniqueness check throws
    /// <c>InvariantViolation</c> for both writer (export) and reader (validate/import) — see architecture
    /// document §17 for the exact code citation. Emitted alongside <see cref="MeaningCardSchemaMigrationRequired"/>:
    /// even once the live schema is migrated to hold two SemanticMeaning cards for one Word/Direction, the
    /// archive format itself cannot yet round-trip that state as a single v1 export.
    /// </summary>
    public const string ArchiveFormatMigrationRequired = "archive-format-migration-required";

    /// <summary>
    /// <b>Blocking.</b> A matched workflow row's full historical content diverges between target and
    /// archive in a way the live schema's own uniqueness constraints prevent from being preserved as two
    /// rows (verified case: <c>ReviewSessionEntity.DocumentId</c> is uniquely indexed, so two independently
    /// completed review sessions for the same document cannot both be kept).
    /// </summary>
    public const string WorkflowHistorySchemaMigrationRequired = "workflow-history-schema-migration-required";

    /// <summary>
    /// <b>Informational only.</b> An existing, already-matched SemanticMeaning gained a new distinct
    /// AnswerVariant. The current schema tracks automatic-learning progress only at the Word level
    /// (<c>WordEntity</c>'s consecutive-success counters) and has no per-review record of which answer
    /// variant a review event exercised, so no concrete per-variant progress can ever be identified as
    /// "requiring" per-variant persistence from current archive data — there is nothing today this code
    /// could describe as blocking. Never emitted merely because optional aliases exist without any
    /// associated progress signal.
    /// </summary>
    public const string AnswerVariantProgressMigrationRequired = "answer-variant-progress-migration-required";

    /// <summary>
    /// <b>Informational only.</b> Topic/domain/disambiguation data is not persisted anywhere in the
    /// current schema or archive format (<c>MeaningEntity</c>/<c>BackupPreparedItem</c> have no such
    /// field). Emitted whenever the plan processes at least one PreparedMeaning. Every concrete grouping
    /// ambiguity this absence could otherwise cause is instead resolved explicitly by a
    /// <see cref="SemanticMeaningGroupingDecision"/> (never inferred from Definition/Translation text), so
    /// this code never needs to become blocking on its own.
    /// </summary>
    public const string TopicPersistenceRequired = "topic-persistence-required";
}

/// <summary>
/// Thrown by the pure planner when it cannot resolve a required parent reference (missing or ambiguous)
/// from content already present in a payload. Carries a stable <see cref="BackupErrorCodes"/> value
/// only, never raw payload content, so callers can fail closed without leaking archive data through an
/// exception message.
/// </summary>
public sealed class MergePlanningException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
