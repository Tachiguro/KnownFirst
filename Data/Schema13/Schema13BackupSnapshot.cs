using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema8;

namespace KnownFirst.Data.Schema13;

public sealed record CapturedWordLearningControl(
    int WordId,
    DateTime DecidedAtUtc);

public sealed record CapturedSenseLearningControl(
    int SenseId,
    DateTime DecidedAtUtc);

public sealed record CapturedFsrsReviewHistoryEntry(
    int Id,
    string StableId,
    int CardId,
    int SequenceNumber,
    ReviewRating Rating,
    DateTime ReviewedAtUtc);

public sealed record CapturedFsrsCardState(
    int CardId,
    Fsrs6CardState State,
    double? Stability,
    double? Difficulty,
    DateTime? LastReviewedAtUtc,
    int? StepIndex,
    DateTime? DueAtUtc);

/// <summary>
/// Full-fidelity raw capture of a Schema-13 database (KF-BACKUP-006 Slice 2).
/// Contains the underlying Schema-8/10/11/12 data model plus the four Schema-13 clean persistence collections:
/// WordLearningControls, SenseLearningControls, FsrsReviewHistoryEntries, and FsrsCardStates.
/// </summary>
public sealed record Schema13BackupSnapshot(
    Schema8BackupSnapshot BaseSnapshot,
    IReadOnlyList<CapturedWordLearningControl> WordLearningControls,
    IReadOnlyList<CapturedSenseLearningControl> SenseLearningControls,
    IReadOnlyList<CapturedFsrsReviewHistoryEntry> FsrsReviewHistoryEntries,
    IReadOnlyList<CapturedFsrsCardState> FsrsCardStates);

public sealed record Schema13PortableSnapshotCaptureResult(
    KnownFirst.Data.PortableSnapshotCaptureStatus Status,
    Schema13BackupSnapshot? Snapshot);
