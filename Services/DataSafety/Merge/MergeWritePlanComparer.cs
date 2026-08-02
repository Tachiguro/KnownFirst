namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Structural equality between two <see cref="MergePreflightPlan"/> values, restricted to the fields that
/// determine what the writer would actually do (KF-MEANING-001 Slice 8). Used to detect a stale or
/// mismatched plan: the writer recomputes the plan against the current target and the supplied archive,
/// then requires the result to match the plan it was handed, byte-for-byte on every substantive field.
/// <see cref="MergePreflightPlan.Manifest"/> is never compared — it is an echo of whatever the caller
/// passes to <see cref="MergePreflightPlannerV2.CreatePlan"/> and carries no bearing on what gets written.
/// </summary>
internal static class MergeWritePlanComparer
{
    public static bool Matches(MergePreflightPlan supplied, MergePreflightPlan recomputed)
    {
        if (supplied.Status != recomputed.Status
            || supplied.IsExecutable != recomputed.IsExecutable
            || supplied.RequiresSchedulerReplay != recomputed.RequiresSchedulerReplay)
        {
            return false;
        }

        if (!ActionsMatch(supplied.Actions, recomputed.Actions))
        {
            return false;
        }

        if (!PerEntityMatches(supplied.PerEntity, recomputed.PerEntity))
        {
            return false;
        }

        if (!supplied.BlockingPrerequisites.SequenceEqual(recomputed.BlockingPrerequisites, StringComparer.Ordinal))
        {
            return false;
        }

        if (supplied.KnowledgeStateConflictDecisions.Count != recomputed.KnowledgeStateConflictDecisions.Count
            || supplied.WorkflowStatusConflictDecisions.Count != recomputed.WorkflowStatusConflictDecisions.Count
            || supplied.SemanticMeaningGroupingDecisions.Count != recomputed.SemanticMeaningGroupingDecisions.Count
            || supplied.PreferredVariantSelectionDecisions.Count != recomputed.PreferredVariantSelectionDecisions.Count)
        {
            return false;
        }

        return true;
    }

    private static bool ActionsMatch(IReadOnlyList<MergePlanAction> supplied, IReadOnlyList<MergePlanAction> recomputed)
    {
        if (supplied.Count != recomputed.Count)
        {
            return false;
        }

        for (var i = 0; i < supplied.Count; i++)
        {
            var a = supplied[i];
            var b = recomputed[i];
            if (a.EntityKind != b.EntityKind
                || !string.Equals(a.StableIdentity, b.StableIdentity, StringComparison.Ordinal)
                || !string.Equals(a.ArchiveLocalId, b.ArchiveLocalId, StringComparison.Ordinal)
                || a.Classification != b.Classification
                || !string.Equals(a.ReasonCode, b.ReasonCode, StringComparison.Ordinal)
                || a.DecisionId != b.DecisionId)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PerEntityMatches(
        IReadOnlyDictionary<MergeEntityKind, MergeEntityPlanCounts> supplied,
        IReadOnlyDictionary<MergeEntityKind, MergeEntityPlanCounts> recomputed)
    {
        foreach (var kind in Enum.GetValues<MergeEntityKind>())
        {
            var suppliedCounts = supplied.TryGetValue(kind, out var s) ? s : MergeEntityPlanCounts.Zero;
            var recomputedCounts = recomputed.TryGetValue(kind, out var r) ? r : MergeEntityPlanCounts.Zero;
            if (suppliedCounts != recomputedCounts)
            {
                return false;
            }
        }

        return true;
    }
}
