using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Per design §5.3 (Rule R1), LearningCard scheduling state — State, DueAtUtc, IntervalDays,
/// EaseFactor, SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, including
/// CardState.Retired — is never resolved by direct field-by-field comparison. It is derived by
/// merging/deduping the card's LearningReview history (§4.4/§6) and replaying it through
/// KnownFirst.Core.Learning.SimpleSpacedRepetitionScheduler. This classification therefore only
/// distinguishes the trivial already-equal case (no replay needed) from the general case, which always
/// requires the caller to merge events and replay rather than pick a "winning" state value here.
/// </summary>
public static class LearningCardStateConflictPolicy
{
    public static MergeConflictResult<BackupCardState> Classify(BackupCardState target, BackupCardState archive)
    {
        if (target == archive)
        {
            return new MergeConflictResult<BackupCardState>(
                MergeConflictClassification.DeterministicMonotonic, target, false, "learning-card-state-equal-no-replay-needed");
        }

        return new MergeConflictResult<BackupCardState>(
            MergeConflictClassification.PreserveBothAndDerive, null, false, "learning-card-state-derived-from-review-replay");
    }
}
