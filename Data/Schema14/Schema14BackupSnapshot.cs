using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Targets;

namespace KnownFirst.Data.Schema14;

public sealed record CapturedTargetFsrsState(
    int TargetId,
    Fsrs6CardState State,
    double? Stability,
    double? Difficulty,
    DateTime? LastReviewedAtUtc,
    int? StepIndex,
    DateTime? DueAtUtc);

public sealed record CapturedTargetFsrsReviewHistoryEntry(
    int Id,
    string StableId,
    int TargetId,
    int SequenceNumber,
    ReviewRating Rating,
    DateTime ReviewedAtUtc);

public sealed record CapturedTargetReview(
    int Id,
    string StableId,
    int TargetId,
    int SessionId,
    ReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    bool IsSessionRepeat,
    int? TargetAnswerVariantId,
    int? MatchedAnswerVariantId,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc);

/// <summary>
/// Full-fidelity raw capture of a Schema-14 database (KF-LEARN-011 Slice 5).
/// Contains the underlying lexical/workflow model plus the five Schema-14 target collections:
/// LearningTargets, TargetAnswerVariants, TargetFsrsStates, TargetFsrsReviewHistoryEntries, and TargetReviews,
/// as well as WordLearningControls and SenseLearningControls.
/// </summary>
public sealed record Schema14BackupSnapshot(
    Schema8BackupSnapshot BaseSnapshot,
    IReadOnlyList<CapturedWordLearningControl> WordLearningControls,
    IReadOnlyList<CapturedSenseLearningControl> SenseLearningControls,
    IReadOnlyList<PersistedLearningTarget> LearningTargets,
    IReadOnlyList<PersistedTargetAnswerVariant> TargetAnswerVariants,
    IReadOnlyList<CapturedTargetFsrsState> TargetFsrsStates,
    IReadOnlyList<CapturedTargetFsrsReviewHistoryEntry> TargetFsrsReviewHistoryEntries,
    IReadOnlyList<CapturedTargetReview> TargetReviews);

public sealed record Schema14PortableSnapshotCaptureResult(
    KnownFirst.Data.PortableSnapshotCaptureStatus Status,
    Schema14BackupSnapshot? Snapshot);
