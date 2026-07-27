namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Exactly one of the classifications the design's conflict matrices are allowed to produce (§2
/// invariant 14: "no enum or state conflict is resolved by silent destructive fallback"). Never a
/// plain boolean, and never inferred from enum ordinal comparison.
/// </summary>
public enum MergeConflictClassification
{
    /// <summary>The target's existing value is kept; the archive's value is discarded (used only where explicitly documented as safe).</summary>
    KeepTarget,

    /// <summary>The archive's value replaces the target's value.</summary>
    AdoptArchive,

    /// <summary>A single winner is computed by a deterministic, order-independent rule (e.g. tier/rank comparison).</summary>
    DeterministicMonotonic,

    /// <summary>Both variants are preserved; no single resolved value exists here — the caller derives one downstream (e.g. scheduler replay).</summary>
    PreserveBothAndDerive,

    /// <summary>Two same-tier, mutually exclusive values with no natural ordering: the target's value is kept unchanged and the conflict must be surfaced as a preflight warning.</summary>
    UnresolvedKeepTargetWithWarning
}

/// <summary>
/// The explicit outcome of a conflict-resolution policy. <typeparamref name="TState"/> is normally the
/// domain enum being resolved (e.g. BackupKnowledgeState); <see cref="ResolvedState"/> is populated
/// whenever the classification implies a single resolved value, and left null only for
/// <see cref="MergeConflictClassification.PreserveBothAndDerive"/>, where no single value exists yet.
/// </summary>
public readonly record struct MergeConflictResult<TState>(
    MergeConflictClassification Classification,
    TState? ResolvedState,
    bool RequiresPreflightWarning,
    string ReasonCode)
    where TState : struct, Enum;
