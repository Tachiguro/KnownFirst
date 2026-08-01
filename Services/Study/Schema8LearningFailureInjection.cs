namespace KnownFirst.Services.Study;

/// <summary>
/// The five stable destructive boundaries of the Schema-8 rating transaction (KF-MEANING-001 Slice 4). Each
/// value names an exact position relative to the first mutation of that step, so a test can prove complete
/// rollback and retry safety at that boundary.
/// </summary>
public enum Schema8LearningMutationCheckpoint
{
    /// <summary>Immediately after the review insert and physical-Id acquisition, before the target/matched update.</summary>
    AfterReviewInsert,

    /// <summary>Immediately after the review target/matched update, before queue completion.</summary>
    AfterReviewTargetMatchedUpdate,

    /// <summary>After the complete expected Required replacement set and all mutations are calculated, before the first progress delete, insert, or update.</summary>
    DuringProgressReplacement,

    /// <summary>After progress replacement, immediately before changing the reviewed card to Retired or pruning any queue row.</summary>
    BeforeCardRetirement,

    /// <summary>After retirement and queue cleanup, immediately before updating the affected Sense status.</summary>
    BeforeSenseRollup
}

/// <summary>
/// Test-only fault-injection hook for the Schema-8 rating transaction, mirroring
/// <see cref="IPreparationFaultInjector"/>. Never referenced by production callers — the default
/// (no injector supplied to <see cref="LearningService"/>) is exactly ordinary production behaviour. A test
/// throws from <see cref="AtCheckpoint"/> to prove that the whole <c>RunInTransactionAsync</c> transaction
/// rolls back at that exact boundary and that the identical call stays retryable.
/// </summary>
public interface ISchema8LearningFailureInjector
{
    void AtCheckpoint(Schema8LearningMutationCheckpoint checkpoint);
}
