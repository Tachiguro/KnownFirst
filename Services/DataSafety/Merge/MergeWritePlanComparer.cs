namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Structural equality between two <see cref="MergePreflightPlan"/> values, restricted to the fields that
/// determine what the writer would actually do (KF-MEANING-001 Slice 8). Used to detect a stale or
/// mismatched plan: the writer recomputes the plan against the current target and the supplied archive,
/// then requires the result to match the plan it was handed, byte-for-byte on every substantive field.
/// <see cref="MergePreflightPlan.Manifest"/> is not compared separately: its causal-order feature flag is
/// already consumed while recomputing the substantive actions from the same supplied manifest.
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

        if (!Schema13PlansMatch(supplied.Schema13Plan, recomputed.Schema13Plan))
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

    private static bool Schema13PlansMatch(
        Schema13MergePreflightPlan? supplied,
        Schema13MergePreflightPlan? recomputed)
    {
        if (supplied is null || recomputed is null)
        {
            return supplied is null && recomputed is null;
        }

        if (!string.Equals(
                supplied.ExpectedTargetFingerprint,
                recomputed.ExpectedTargetFingerprint,
                StringComparison.Ordinal)
            || !supplied.Actions.SequenceEqual(recomputed.Actions)
            || !supplied.Conflicts.SequenceEqual(recomputed.Conflicts)
            || supplied.TargetExpectations.Count != recomputed.TargetExpectations.Count)
        {
            return false;
        }

        for (var index = 0; index < supplied.TargetExpectations.Count; index++)
        {
            var left = supplied.TargetExpectations[index];
            var right = recomputed.TargetExpectations[index];
            if (left.Kind != right.Kind
                || !string.Equals(left.SemanticIdentity, right.SemanticIdentity, StringComparison.Ordinal)
                || left.SemanticEntityPresent != right.SemanticEntityPresent
                || left.ControlPresent != right.ControlPresent
                || left.ControlDecidedAtUtc != right.ControlDecidedAtUtc
                || left.FsrsCardState != right.FsrsCardState
                || !left.FsrsHistory.SequenceEqual(right.FsrsHistory))
            {
                return false;
            }
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
