using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// WordEntity.Status (KnowledgeState) conflict resolution per design §5.1. Tiers: 0 = Unreviewed;
/// 1 = Known/UnknownBacklog/Ignored (three mutually exclusive review judgments, no natural ordering);
/// 2 = Prepared; 3 = Learning; 4 = Mastered. Cross-tier conflicts always adopt the higher tier
/// (deterministic monotonic). Same-tier conflicts within tier 1 are the one genuinely ambiguous case:
/// the target's value is kept unchanged and the archive's alternative must be surfaced as a preflight
/// warning — never guessed at via timestamp or enum ordinal.
/// </summary>
public static class KnowledgeStateConflictPolicy
{
    public static MergeConflictResult<BackupKnowledgeState> Resolve(BackupKnowledgeState target, BackupKnowledgeState archive) =>
        TieredConflictResolver.Resolve(
            target,
            archive,
            Tier,
            equalReasonCode: "knowledge-state-equal",
            monotonicAdvanceReasonCode: "knowledge-state-monotonic-tier-advance",
            unresolvedSameTierReasonCode: "knowledge-state-tier1-conflict");

    private static int Tier(BackupKnowledgeState state) => state switch
    {
        BackupKnowledgeState.Unreviewed => 0,
        BackupKnowledgeState.Known => 1,
        BackupKnowledgeState.UnknownBacklog => 1,
        BackupKnowledgeState.Ignored => 1,
        BackupKnowledgeState.Prepared => 2,
        BackupKnowledgeState.Learning => 3,
        BackupKnowledgeState.Mastered => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };
}
