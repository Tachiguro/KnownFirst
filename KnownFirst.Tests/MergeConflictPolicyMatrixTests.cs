using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeConflictPolicyMatrixTests
{
    // --- KnowledgeState (§5.1) ---

    [TestMethod]
    public void KnowledgeState_Equal_IsDeterministicMonotonicNoOp()
    {
        var result = KnowledgeStateConflictPolicy.Resolve(BackupKnowledgeState.Known, BackupKnowledgeState.Known);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupKnowledgeState.Known, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
        Assert.AreEqual("knowledge-state-equal", result.ReasonCode);
    }

    [DataTestMethod]
    [DataRow(BackupKnowledgeState.Known, BackupKnowledgeState.UnknownBacklog)]
    [DataRow(BackupKnowledgeState.Known, BackupKnowledgeState.Ignored)]
    [DataRow(BackupKnowledgeState.UnknownBacklog, BackupKnowledgeState.Ignored)]
    public void KnowledgeState_SameTier1Conflict_KeepsTargetAsUnresolvedWithWarning(
        BackupKnowledgeState target, BackupKnowledgeState archive)
    {
        var result = KnowledgeStateConflictPolicy.Resolve(target, archive);

        Assert.AreEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification);
        Assert.AreEqual(target, result.ResolvedState);
        Assert.IsTrue(result.RequiresPreflightWarning);
        Assert.AreEqual("knowledge-state-tier1-conflict", result.ReasonCode);
    }

    [DataTestMethod]
    [DataRow(BackupKnowledgeState.Known, BackupKnowledgeState.UnknownBacklog)]
    [DataRow(BackupKnowledgeState.Known, BackupKnowledgeState.Ignored)]
    [DataRow(BackupKnowledgeState.UnknownBacklog, BackupKnowledgeState.Ignored)]
    public void KnowledgeState_SameTier1Conflict_IsNonCommutative_ArgumentOrderDeterminesKeptValue(
        BackupKnowledgeState target, BackupKnowledgeState archive)
    {
        var forward = KnowledgeStateConflictPolicy.Resolve(target, archive);
        var swapped = KnowledgeStateConflictPolicy.Resolve(archive, target);

        Assert.AreEqual(target, forward.ResolvedState);
        Assert.AreEqual(archive, swapped.ResolvedState);
        Assert.AreNotEqual(forward.ResolvedState, swapped.ResolvedState);
    }

    [TestMethod]
    public void KnowledgeState_CrossTier_AdoptsHigherTier_AndIsCommutative()
    {
        var forward = KnowledgeStateConflictPolicy.Resolve(BackupKnowledgeState.Unreviewed, BackupKnowledgeState.Mastered);
        var backward = KnowledgeStateConflictPolicy.Resolve(BackupKnowledgeState.Mastered, BackupKnowledgeState.Unreviewed);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, forward.Classification);
        Assert.AreEqual(BackupKnowledgeState.Mastered, forward.ResolvedState);
        Assert.IsFalse(forward.RequiresPreflightWarning);
        Assert.AreEqual("knowledge-state-monotonic-tier-advance", forward.ReasonCode);

        Assert.AreEqual(forward.ResolvedState, backward.ResolvedState);
        Assert.AreEqual(forward.Classification, backward.Classification);
    }

    [TestMethod]
    public void KnowledgeState_UnreviewedVersusAnyTier1_AdoptsTier1()
    {
        var result = KnowledgeStateConflictPolicy.Resolve(BackupKnowledgeState.Unreviewed, BackupKnowledgeState.Ignored);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupKnowledgeState.Ignored, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void KnowledgeState_AllCrossTierPairs_ResolveToHigherTier()
    {
        BackupKnowledgeState[] ascendingByTier =
        [
            BackupKnowledgeState.Unreviewed,
            BackupKnowledgeState.Known, // tier 1 representative
            BackupKnowledgeState.Prepared,
            BackupKnowledgeState.Learning,
            BackupKnowledgeState.Mastered,
        ];

        for (var i = 0; i < ascendingByTier.Length; i++)
        {
            for (var j = i + 1; j < ascendingByTier.Length; j++)
            {
                var result = KnowledgeStateConflictPolicy.Resolve(ascendingByTier[i], ascendingByTier[j]);
                Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
                Assert.AreEqual(ascendingByTier[j], result.ResolvedState);
                Assert.IsFalse(result.RequiresPreflightWarning);
            }
        }
    }

    private static readonly Dictionary<BackupKnowledgeState, int> KnowledgeStateTiers = new()
    {
        [BackupKnowledgeState.Unreviewed] = 0,
        [BackupKnowledgeState.Known] = 1,
        [BackupKnowledgeState.UnknownBacklog] = 1,
        [BackupKnowledgeState.Ignored] = 1,
        [BackupKnowledgeState.Prepared] = 2,
        [BackupKnowledgeState.Learning] = 3,
        [BackupKnowledgeState.Mastered] = 4,
    };

    [TestMethod]
    public void KnowledgeState_EveryEnumValuePair_HasAnExplicitClassification()
    {
        // Exhaustive over Enum.GetValues so a future BackupKnowledgeState member with no entry in
        // KnowledgeStateTiers fails this test (KeyNotFoundException) rather than being silently
        // absorbed into an existing tier — the test itself must be updated before production code's
        // own tier switch (which already throws for unmapped values) is ever reachable from real data.
        var allStates = Enum.GetValues<BackupKnowledgeState>();
        Assert.AreEqual(7, allStates.Length, "This test's expectations were written for exactly 7 BackupKnowledgeState members; update KnowledgeStateTiers and this assertion together if the enum changes.");

        foreach (var target in allStates)
        {
            foreach (var archive in allStates)
            {
                var result = KnowledgeStateConflictPolicy.Resolve(target, archive);
                var targetTier = KnowledgeStateTiers[target];
                var archiveTier = KnowledgeStateTiers[archive];

                if (target == archive)
                {
                    Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification, $"{target} vs {archive}");
                    Assert.AreEqual(target, result.ResolvedState, $"{target} vs {archive}");
                    Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
                }
                else if (targetTier != archiveTier)
                {
                    var expectedWinner = targetTier > archiveTier ? target : archive;
                    Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification, $"{target} vs {archive}");
                    Assert.AreEqual(expectedWinner, result.ResolvedState, $"{target} vs {archive}");
                    Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
                }
                else
                {
                    Assert.AreEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification, $"{target} vs {archive}");
                    Assert.AreEqual(target, result.ResolvedState, $"{target} vs {archive}");
                    Assert.IsTrue(result.RequiresPreflightWarning, $"{target} vs {archive}");
                }
            }
        }
    }

    // --- PreparationState (§5.2) ---

    [TestMethod]
    public void PreparationState_UnpreparedVersusAnyOther_AdoptsOther()
    {
        var result = PreparationStateConflictPolicy.Resolve(BackupPreparationState.Unprepared, BackupPreparationState.Preparing);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupPreparationState.Preparing, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void PreparationState_PreparingVersusPrepared_AdoptsTerminal()
    {
        var result = PreparationStateConflictPolicy.Resolve(BackupPreparationState.Preparing, BackupPreparationState.Prepared);

        Assert.AreEqual(BackupPreparationState.Prepared, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void PreparationState_PreparingVersusPreparationFailed_AdoptsTerminal()
    {
        var result = PreparationStateConflictPolicy.Resolve(BackupPreparationState.Preparing, BackupPreparationState.PreparationFailed);

        Assert.AreEqual(BackupPreparationState.PreparationFailed, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void PreparationState_PreparedVersusPreparationFailed_AdoptsPrepared_AndIsCommutative()
    {
        var forward = PreparationStateConflictPolicy.Resolve(BackupPreparationState.Prepared, BackupPreparationState.PreparationFailed);
        var backward = PreparationStateConflictPolicy.Resolve(BackupPreparationState.PreparationFailed, BackupPreparationState.Prepared);

        Assert.AreEqual(BackupPreparationState.Prepared, forward.ResolvedState);
        Assert.AreEqual(BackupPreparationState.Prepared, backward.ResolvedState);
        Assert.IsFalse(forward.RequiresPreflightWarning);
        Assert.IsFalse(backward.RequiresPreflightWarning);
        Assert.AreNotEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, forward.Classification);
    }

    [TestMethod]
    public void PreparationState_Equal_IsNoOp()
    {
        var result = PreparationStateConflictPolicy.Resolve(BackupPreparationState.Prepared, BackupPreparationState.Prepared);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupPreparationState.Prepared, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void PreparationState_NoPairIsUnresolved()
    {
        var allStates = Enum.GetValues<BackupPreparationState>();

        foreach (var target in allStates)
        {
            foreach (var archive in allStates)
            {
                var result = PreparationStateConflictPolicy.Resolve(target, archive);
                Assert.AreNotEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification);
            }
        }
    }

    private static readonly Dictionary<BackupPreparationState, int> PreparationStateTiers = new()
    {
        [BackupPreparationState.Unprepared] = 0,
        [BackupPreparationState.Preparing] = 1,
        [BackupPreparationState.PreparationFailed] = 2,
        [BackupPreparationState.Prepared] = 3,
    };

    [TestMethod]
    public void PreparationState_EveryEnumValuePair_ResolvesToTheHigherRankedValue()
    {
        var allStates = Enum.GetValues<BackupPreparationState>();
        Assert.AreEqual(4, allStates.Length, "Update this test and PreparationStateTiers together if BackupPreparationState changes.");

        foreach (var target in allStates)
        {
            foreach (var archive in allStates)
            {
                var result = PreparationStateConflictPolicy.Resolve(target, archive);
                var expected = PreparationStateTiers[target] >= PreparationStateTiers[archive] ? target : archive;

                Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification, $"{target} vs {archive}");
                Assert.AreEqual(expected, result.ResolvedState, $"{target} vs {archive}");
                Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
            }
        }
    }

    // --- LearningCard state classification (§5.3) ---

    [TestMethod]
    public void LearningCardState_Equal_NoReplayNeeded()
    {
        var result = LearningCardStateConflictPolicy.Classify(BackupCardState.Review, BackupCardState.Review);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupCardState.Review, result.ResolvedState);
        Assert.AreEqual("learning-card-state-equal-no-replay-needed", result.ReasonCode);
    }

    [TestMethod]
    public void LearningCardState_Different_RequiresReplay_NoSingleResolvedValue()
    {
        var result = LearningCardStateConflictPolicy.Classify(BackupCardState.Learning, BackupCardState.Retired);

        Assert.AreEqual(MergeConflictClassification.PreserveBothAndDerive, result.Classification);
        Assert.IsNull(result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
        Assert.AreEqual("learning-card-state-derived-from-review-replay", result.ReasonCode);
    }

    // --- Workflow-session status classification ---

    [TestMethod]
    public void ReviewSessionStatus_ActiveVersusCompleted_AdoptsCompleted()
    {
        var result = WorkflowSessionStateConflictPolicy.ResolveReviewSession(BackupReviewSessionStatus.Active, BackupReviewSessionStatus.Completed);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupReviewSessionStatus.Completed, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void LearningSessionStatus_ActiveVersusCompleted_AdoptsCompleted()
    {
        var result = WorkflowSessionStateConflictPolicy.ResolveLearningSession(BackupLearningSessionStatus.Active, BackupLearningSessionStatus.Completed);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupLearningSessionStatus.Completed, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void PreparationSessionStatus_CompletedVersusCancelled_IsUnresolvedKeepTargetWithWarning()
    {
        var result = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(
            BackupPreparationSessionStatus.Completed, BackupPreparationSessionStatus.Cancelled);

        Assert.AreEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification);
        Assert.AreEqual(BackupPreparationSessionStatus.Completed, result.ResolvedState);
        Assert.IsTrue(result.RequiresPreflightWarning);
        Assert.AreEqual("preparation-session-status-terminal-outcome-conflict", result.ReasonCode);
    }

    [TestMethod]
    public void PreparationSessionStatus_CompletedVersusCancelled_IsNonCommutative()
    {
        var forward = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(
            BackupPreparationSessionStatus.Completed, BackupPreparationSessionStatus.Cancelled);
        var swapped = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(
            BackupPreparationSessionStatus.Cancelled, BackupPreparationSessionStatus.Completed);

        Assert.AreEqual(BackupPreparationSessionStatus.Completed, forward.ResolvedState);
        Assert.AreEqual(BackupPreparationSessionStatus.Cancelled, swapped.ResolvedState);
    }

    [TestMethod]
    public void PreparationSessionStatus_ActiveVersusCompleted_AdoptsCompleted()
    {
        var result = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(
            BackupPreparationSessionStatus.Active, BackupPreparationSessionStatus.Completed);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupPreparationSessionStatus.Completed, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    [TestMethod]
    public void PreparationSessionStatus_ActiveVersusCancelled_AdoptsCancelled()
    {
        var result = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(
            BackupPreparationSessionStatus.Active, BackupPreparationSessionStatus.Cancelled);

        Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification);
        Assert.AreEqual(BackupPreparationSessionStatus.Cancelled, result.ResolvedState);
        Assert.IsFalse(result.RequiresPreflightWarning);
    }

    // --- Exhaustive workflow-session-status coverage: a future enum member must fail these tests
    //     (via the production tier switch's ArgumentOutOfRangeException) until intentionally handled. ---

    [TestMethod]
    public void ReviewSessionStatus_EveryEnumValuePair_HasAnExplicitClassification()
    {
        var allStatuses = Enum.GetValues<BackupReviewSessionStatus>();
        Assert.AreEqual(2, allStatuses.Length, "Update this test if BackupReviewSessionStatus gains a member.");

        foreach (var target in allStatuses)
        {
            foreach (var archive in allStatuses)
            {
                var result = WorkflowSessionStateConflictPolicy.ResolveReviewSession(target, archive);
                Assert.AreNotEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification, $"{target} vs {archive}");
                Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
                Assert.AreEqual(target == archive ? target : BackupReviewSessionStatus.Completed, result.ResolvedState, $"{target} vs {archive}");
            }
        }
    }

    [TestMethod]
    public void LearningSessionStatus_EveryEnumValuePair_HasAnExplicitClassification()
    {
        var allStatuses = Enum.GetValues<BackupLearningSessionStatus>();
        Assert.AreEqual(2, allStatuses.Length, "Update this test if BackupLearningSessionStatus gains a member.");

        foreach (var target in allStatuses)
        {
            foreach (var archive in allStatuses)
            {
                var result = WorkflowSessionStateConflictPolicy.ResolveLearningSession(target, archive);
                Assert.AreNotEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification, $"{target} vs {archive}");
                Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
                Assert.AreEqual(target == archive ? target : BackupLearningSessionStatus.Completed, result.ResolvedState, $"{target} vs {archive}");
            }
        }
    }

    private static readonly Dictionary<BackupPreparationSessionStatus, int> PreparationSessionStatusTiers = new()
    {
        [BackupPreparationSessionStatus.Active] = 0,
        [BackupPreparationSessionStatus.Completed] = 1,
        [BackupPreparationSessionStatus.Cancelled] = 1,
    };

    [TestMethod]
    public void PreparationSessionStatus_EveryEnumValuePair_HasAnExplicitClassification()
    {
        var allStatuses = Enum.GetValues<BackupPreparationSessionStatus>();
        Assert.AreEqual(3, allStatuses.Length, "Update this test and PreparationSessionStatusTiers together if BackupPreparationSessionStatus changes.");

        foreach (var target in allStatuses)
        {
            foreach (var archive in allStatuses)
            {
                var result = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(target, archive);
                var targetTier = PreparationSessionStatusTiers[target];
                var archiveTier = PreparationSessionStatusTiers[archive];

                if (target == archive)
                {
                    Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification, $"{target} vs {archive}");
                    Assert.AreEqual(target, result.ResolvedState, $"{target} vs {archive}");
                    Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
                }
                else if (targetTier != archiveTier)
                {
                    var expectedWinner = targetTier > archiveTier ? target : archive;
                    Assert.AreEqual(MergeConflictClassification.DeterministicMonotonic, result.Classification, $"{target} vs {archive}");
                    Assert.AreEqual(expectedWinner, result.ResolvedState, $"{target} vs {archive}");
                    Assert.IsFalse(result.RequiresPreflightWarning, $"{target} vs {archive}");
                }
                else
                {
                    Assert.AreEqual(MergeConflictClassification.UnresolvedKeepTargetWithWarning, result.Classification, $"{target} vs {archive}");
                    Assert.AreEqual(target, result.ResolvedState, $"{target} vs {archive}");
                    Assert.IsTrue(result.RequiresPreflightWarning, $"{target} vs {archive}");
                }
            }
        }
    }
}
