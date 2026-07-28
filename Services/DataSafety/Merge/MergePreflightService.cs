using KnownFirst.Data;

namespace KnownFirst.Services.DataSafety.Merge;

public interface IMergePreflightService
{
    Task<MergePreflightPlan> CreatePreflightPlanAsync(Stream archiveStream, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only orchestration for KF-BACKUP-002 Slice 3: validates the incoming archive stream, captures
/// the target database's current state with the same race-free, fail-closed active-workflow check
/// Slice 2's safety copy already uses, maps that snapshot through the existing
/// <see cref="BackupModelMapper"/>, and hands both payloads to the pure
/// <see cref="MergePreflightPlanner"/>. Opens no write transaction and creates no safety copy — this is
/// strictly the preview engine; a future slice reruns the same matcher again, immediately before
/// mutation, after the validated safety copy has actually been created.
/// </summary>
public sealed class MergePreflightService(IKnownFirstDatabase database) : IMergePreflightService
{
    public async Task<MergePreflightPlan> CreatePreflightPlanAsync(Stream archiveStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        ValidatedBackupArchive validated;
        try
        {
            validated = await BackupArchiveReader.ValidateAsync(archiveStream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Cancelled, null, false, BackupErrorCodes.OperationCancelled);
        }
        catch (BackupFormatException exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.ValidationFailed, null, false, exception.Code);
        }
        catch (Exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, null, false, MergePreflightErrorCodes.UnexpectedFailure);
        }

        var manifestInfo = new MergeManifestInfo(
            validated.Manifest.FormatVersion,
            validated.Manifest.SourceAppVersion,
            validated.Manifest.SourceDatabaseSchemaVersion,
            validated.Manifest.CreatedAtUtc,
            validated.Manifest.SourcePlatform);

        PortableSnapshotCaptureResult captureResult;
        try
        {
            captureResult = await database.ExecuteSnapshotAsync(
                connection => BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection));
        }
        catch (OperationCanceledException)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Cancelled, manifestInfo, true, BackupErrorCodes.OperationCancelled);
        }
        catch (BackupFormatException exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, exception.Code);
        }
        catch (Exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, MergePreflightErrorCodes.UnexpectedFailure);
        }

        if (captureResult.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.BlockedByActiveWorkflow, manifestInfo, true, BackupErrorCodes.ActiveWorkflowUnsupported);
        }

        var snapshot = captureResult.Snapshot
            ?? throw new InvalidOperationException("Snapshot capture reported success without a snapshot.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPayload = BackupModelMapper.MapToExternal(snapshot);
            return MergePreflightPlanner.CreatePlan(targetPayload, validated.Payload, validated.Manifest);
        }
        catch (OperationCanceledException)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Cancelled, manifestInfo, true, BackupErrorCodes.OperationCancelled);
        }
        catch (MergePlanningException exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, exception.Code);
        }
        catch (KeyNotFoundException)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, BackupErrorCodes.MissingReference);
        }
        catch (Exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, MergePreflightErrorCodes.UnexpectedFailure);
        }
    }
}
