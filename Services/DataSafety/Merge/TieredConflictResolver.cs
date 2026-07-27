namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Shared implementation for the recurring "ranked tier" conflict shape used by KnowledgeState,
/// PreparationState, and workflow-session status: equal values dedupe; different tiers resolve
/// deterministically to the higher tier; same-tier, different values are an unresolved conflict that
/// keeps the target unchanged. Intentionally internal — each public policy exposes its own tier
/// definition and reason codes rather than a generic tier function, so callers cannot construct an
/// arbitrary, undocumented tiering.
/// </summary>
internal static class TieredConflictResolver
{
    public static MergeConflictResult<TEnum> Resolve<TEnum>(
        TEnum target,
        TEnum archive,
        Func<TEnum, int> tierOf,
        string equalReasonCode,
        string monotonicAdvanceReasonCode,
        string unresolvedSameTierReasonCode)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(tierOf);

        if (target.Equals(archive))
        {
            return new MergeConflictResult<TEnum>(
                MergeConflictClassification.DeterministicMonotonic, target, false, equalReasonCode);
        }

        var targetTier = tierOf(target);
        var archiveTier = tierOf(archive);

        if (targetTier != archiveTier)
        {
            var winner = targetTier > archiveTier ? target : archive;
            return new MergeConflictResult<TEnum>(
                MergeConflictClassification.DeterministicMonotonic, winner, false, monotonicAdvanceReasonCode);
        }

        return new MergeConflictResult<TEnum>(
            MergeConflictClassification.UnresolvedKeepTargetWithWarning, target, true, unresolvedSameTierReasonCode);
    }
}
