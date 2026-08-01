namespace KnownFirst.Services.Study;

/// <summary>
/// Stable, semantically fixed failure codes for the Schema-8 learning paths (KF-MEANING-001 Slice 4).
/// Every value names a fail-closed rejection that happens <em>before</em> any mutation, except
/// <see cref="ReplayDivergence"/>, which aborts and rolls back an in-flight rating transaction.
/// </summary>
public enum Schema8LearningDataErrorCode
{
    /// <summary>The referenced learning-queue row does not exist.</summary>
    QueueItemNotFound,

    /// <summary>The queue row's owning learning session does not exist.</summary>
    SessionNotFound,

    /// <summary>The owning session is not active, or the queue row belongs to a different session.</summary>
    SessionNotActive,

    /// <summary>The queue row's card does not exist.</summary>
    CardNotFound,

    /// <summary>The card's Sense/direction graph is malformed (null SenseId, missing Sense, undefined direction, or more than one card per (SenseId, Direction)).</summary>
    InvalidCardGraph,

    /// <summary>The card's Sense does not exist.</summary>
    SenseNotFound,

    /// <summary>The queue row carries no <c>TargetAnswerVariantId</c>.</summary>
    MissingTarget,

    /// <summary>The target variant does not exist, belongs to another Sense, or its assignment is not currently Required.</summary>
    InvalidTarget,

    /// <summary>The assignment graph for the card's (SenseId, Direction) is missing, duplicated, or ambiguous.</summary>
    InvalidAssignmentGraph,

    /// <summary>An assignment violates "Requirement = Required if and only if RequiredSinceUtc is not null".</summary>
    RequirementBoundaryViolation,

    /// <summary>The queue row was already submitted.</summary>
    DuplicateSubmission,

    /// <summary>The queue row's flags contradict the interaction being submitted (unrevealed reading, unchecked typing, Again after a correct typed answer, or a checked-but-incorrect row that was never completed).</summary>
    InvalidQueueState,

    /// <summary>A correct typed submission has no valid same-queue pending match handoff.</summary>
    MissingMatchEvidence,

    /// <summary>Matched-variant evidence is present where none may exist, resolves differently against fresh data, or is otherwise not attributable.</summary>
    InvalidMatchEvidence,

    /// <summary>A persisted progress row is structurally invalid for its card/variant pair.</summary>
    ProgressRowInvalid,

    /// <summary>A persisted progress row declares a newer replay algorithm than this build implements.</summary>
    ReplayVersionUnsupported,

    /// <summary>The pre-write event calculation disagrees with the full replay including the inserted review.</summary>
    ReplayDivergence,

    /// <summary>The rated queue row's owning session vanished or became invalid inside the rating transaction.</summary>
    SessionMissingForRatedQueueItem
}

/// <summary>
/// Thrown by every Schema-8 learning rejection (KF-MEANING-001 Slice 4). Carries a stable
/// <see cref="Schema8LearningDataErrorCode"/> so tests and diagnostics never depend on message text.
/// </summary>
public sealed class Schema8LearningDataException : Exception
{
    public Schema8LearningDataException(Schema8LearningDataErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public Schema8LearningDataErrorCode Code { get; }

    /// <summary>
    /// <see langword="true"/> when the failure is a permanent data/state rejection rather than a transient
    /// or injected fault. A non-retryable failure clears the pending match handoff for that queue row; a
    /// transient failure or an injected rollback preserves it so the identical call can be retried.
    /// </summary>
    public bool IsNonRetryable => true;

    public static Schema8LearningDataException Create(Schema8LearningDataErrorCode code, string detail) =>
        new(code, $"Schema-8 learning rejection ({code}): {detail}");
}
