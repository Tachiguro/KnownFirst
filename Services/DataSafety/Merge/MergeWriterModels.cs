namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Outcome of <see cref="IMergeWriterService.ApplyAsync"/> (KF-MEANING-001 Slice 8). Exactly one non-
/// <see cref="Success"/> value applies whenever no mutation occurred; the writer never partially applies a
/// plan — every failure path (including <see cref="Failed"/>) is preceded by a transaction rollback.
/// </summary>
public enum MergeWriteStatus
{
    Success,
    NotExecutable,
    StalePlan,
    BlockedByActiveWorkflow,
    Cancelled,
    Failed
}

/// <summary>Result of applying (or refusing to apply) a <see cref="MergePreflightPlan"/> to a populated
/// Schema-8 target. <see cref="ErrorCode"/> is non-null whenever <see cref="Status"/> is not
/// <see cref="MergeWriteStatus.Success"/>.</summary>
public sealed record MergeWriteResult(MergeWriteStatus Status, string? ErrorCode)
{
    public static readonly MergeWriteResult SuccessResult = new(MergeWriteStatus.Success, null);
}

/// <summary>Stable error codes specific to the Slice 8 merge writer. Reuses <see cref="BackupErrorCodes"/>
/// wherever an existing stable code already applies (active workflow, cancellation, missing reference,
/// invariant violation); this class only fills the writer-specific gaps.</summary>
public static class MergeWriterErrorCodes
{
    /// <summary>The supplied plan's <see cref="MergePreflightPlan.IsExecutable"/> is false. The writer never
    /// opens a transaction for this case.</summary>
    public const string PlanNotExecutable = "merge-writer-plan-not-executable";

    /// <summary>Re-running the same preflight computation against the current target state and the
    /// supplied source archive produced a plan that disagrees with the one supplied — the target changed
    /// since the plan was computed, or the plan does not correspond to this archive. Zero mutation occurs.</summary>
    public const string StalePlan = "merge-writer-stale-plan";

    /// <summary>The resolved target capability is not Schema-8. This writer only ever applies a plan to an
    /// already-Schema-8 target — Schema-7 targets and schema migration are out of scope.</summary>
    public const string TargetNotSchema8 = "merge-writer-target-not-schema8";

    /// <summary>An unexpected internal failure that must never surface raw exception text.</summary>
    public const string UnexpectedFailure = "merge-writer-unexpected-failure";
}
