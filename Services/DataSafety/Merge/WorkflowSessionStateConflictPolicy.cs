using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Workflow-session status conflict classification for the three portable session kinds. Only
/// completed (or, for preparation, completed/cancelled) sessions are ever portable — an active session
/// is rejected before a safety copy is even attempted (design §8) — but this classification is defined
/// generally so it never has to guess: Active is always the lowest tier; ReviewSession/LearningSession
/// have a single terminal outcome (Completed) so any tier change is monotonic; PreparationSession has
/// two mutually exclusive terminal outcomes (Completed, Cancelled) with no natural ordering between
/// them, mirroring KnowledgeState's tier-1 case — kept-target-with-warning, never guessed.
/// </summary>
public static class WorkflowSessionStateConflictPolicy
{
    public static MergeConflictResult<BackupReviewSessionStatus> ResolveReviewSession(
        BackupReviewSessionStatus target, BackupReviewSessionStatus archive) =>
        TieredConflictResolver.Resolve(
            target,
            archive,
            ReviewSessionTier,
            equalReasonCode: "review-session-status-equal",
            monotonicAdvanceReasonCode: "review-session-status-monotonic-advance",
            unresolvedSameTierReasonCode: "review-session-status-unexpected-same-tier-conflict");

    public static MergeConflictResult<BackupPreparationSessionStatus> ResolvePreparationSession(
        BackupPreparationSessionStatus target, BackupPreparationSessionStatus archive) =>
        TieredConflictResolver.Resolve(
            target,
            archive,
            PreparationSessionTier,
            equalReasonCode: "preparation-session-status-equal",
            monotonicAdvanceReasonCode: "preparation-session-status-monotonic-advance",
            unresolvedSameTierReasonCode: "preparation-session-status-terminal-outcome-conflict");

    public static MergeConflictResult<BackupLearningSessionStatus> ResolveLearningSession(
        BackupLearningSessionStatus target, BackupLearningSessionStatus archive) =>
        TieredConflictResolver.Resolve(
            target,
            archive,
            LearningSessionTier,
            equalReasonCode: "learning-session-status-equal",
            monotonicAdvanceReasonCode: "learning-session-status-monotonic-advance",
            unresolvedSameTierReasonCode: "learning-session-status-unexpected-same-tier-conflict");

    private static int ReviewSessionTier(BackupReviewSessionStatus status) => status switch
    {
        BackupReviewSessionStatus.Active => 0,
        BackupReviewSessionStatus.Completed => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static int LearningSessionTier(BackupLearningSessionStatus status) => status switch
    {
        BackupLearningSessionStatus.Active => 0,
        BackupLearningSessionStatus.Completed => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static int PreparationSessionTier(BackupPreparationSessionStatus status) => status switch
    {
        BackupPreparationSessionStatus.Active => 0,
        BackupPreparationSessionStatus.Completed => 1,
        BackupPreparationSessionStatus.Cancelled => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
