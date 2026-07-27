using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public enum MergeSafetyCopyStatus
{
    Success,
    BlockedByActiveWorkflow,
    Cancelled,
    SafetyCopyFailed
}

/// <summary>
/// Outcome of one <see cref="IMergeSafetyCopyService.CreateSafetyCopyAsync"/> attempt. A
/// <see cref="MergeSafetyCopyStatus.Success"/> result is only ever returned for a
/// <see cref="ArchivePath"/> that was reopened and validated from its final, post-move path (and a
/// <see cref="MetadataPath"/> sidecar that was re-read and validated); <see cref="ErrorCode"/> is
/// null exactly when <see cref="Status"/> is <see cref="MergeSafetyCopyStatus.Success"/>.
/// </summary>
public sealed record MergeSafetyCopyResult(
    MergeSafetyCopyStatus Status,
    string? ArchivePath,
    string? MetadataPath,
    DateTime? CreatedAtUtc,
    long? ArchiveSizeBytes,
    BackupRecordCounts? RecordCounts,
    BackupManifest? ValidatedManifest,
    string? ErrorCode)
{
    internal static MergeSafetyCopyResult ForSuccess(
        string archivePath,
        string metadataPath,
        DateTime createdAtUtc,
        long archiveSizeBytes,
        BackupRecordCounts recordCounts,
        BackupManifest validatedManifest) =>
        new(
            MergeSafetyCopyStatus.Success,
            archivePath,
            metadataPath,
            createdAtUtc,
            archiveSizeBytes,
            recordCounts,
            validatedManifest,
            null);

    internal static readonly MergeSafetyCopyResult BlockedByActiveWorkflow = new(
        MergeSafetyCopyStatus.BlockedByActiveWorkflow,
        null, null, null, null, null, null,
        BackupErrorCodes.ActiveWorkflowUnsupported);

    internal static readonly MergeSafetyCopyResult Cancelled = new(
        MergeSafetyCopyStatus.Cancelled,
        null, null, null, null, null, null,
        BackupErrorCodes.OperationCancelled);

    internal static readonly MergeSafetyCopyResult Failed = new(
        MergeSafetyCopyStatus.SafetyCopyFailed,
        null, null, null, null, null, null,
        BackupErrorCodes.SafetyBackupFailed);
}
