using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// PreparationState conflict resolution per design §5.2. Rank order: Unprepared(0) &lt; Preparing(1)
/// &lt; PreparationFailed(2) &lt; Prepared(3) — every pair has a defined monotonic answer: any progress
/// beats none, a completed outcome (success or failure) always outranks an in-progress marker, and
/// Prepared outranks PreparationFailed because a recorded failure carries no user decision to preserve
/// while a successful preparation directly produces confirmed content. Unlike KnowledgeState, no pair
/// in this enum is genuinely ambiguous, so no same-tier unresolved case is ever reached.
/// </summary>
public static class PreparationStateConflictPolicy
{
    public static MergeConflictResult<BackupPreparationState> Resolve(BackupPreparationState target, BackupPreparationState archive) =>
        TieredConflictResolver.Resolve(
            target,
            archive,
            Tier,
            equalReasonCode: "preparation-state-equal",
            monotonicAdvanceReasonCode: "preparation-state-monotonic-advance",
            unresolvedSameTierReasonCode: "preparation-state-unexpected-same-tier-conflict");

    private static int Tier(BackupPreparationState state) => state switch
    {
        BackupPreparationState.Unprepared => 0,
        BackupPreparationState.Preparing => 1,
        BackupPreparationState.PreparationFailed => 2,
        BackupPreparationState.Prepared => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };
}
