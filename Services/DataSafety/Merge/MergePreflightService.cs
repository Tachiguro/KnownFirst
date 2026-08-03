using KnownFirst.Data;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public interface IMergePreflightService
{
    Task<MergePreflightPlan> CreatePreflightPlanAsync(Stream archiveStream, CancellationToken cancellationToken);

    /// <summary>
    /// Computes the plan directly from an already-validated archive envelope. Used by orchestration (Import
    /// routing) that has already consumed the caller's source stream exactly once and must never rewind or
    /// re-validate it — the caller's stream may not even be seekable.
    /// </summary>
    Task<MergePreflightPlan> CreatePreflightPlanAsync(ValidatedBackupArchiveEnvelope validated, CancellationToken cancellationToken);
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

        ValidatedBackupArchiveEnvelope validated;
        try
        {
            validated = await BackupArchiveReader.ValidateVersionedAsync(archiveStream, cancellationToken);
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

        return await CreatePreflightPlanAsync(validated, cancellationToken);
    }

    public async Task<MergePreflightPlan> CreatePreflightPlanAsync(ValidatedBackupArchiveEnvelope validated, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validated);

        string sourceAppVersion;
        int sourceDatabaseSchemaVersion;
        DateTime createdAtUtc;
        BackupSourcePlatform sourcePlatform;

        if (validated.FormatVersion == BackupFormatLimits.FormatVersion)
        {
            sourceAppVersion = validated.V1!.Manifest.SourceAppVersion;
            sourceDatabaseSchemaVersion = validated.V1!.Manifest.SourceDatabaseSchemaVersion;
            createdAtUtc = validated.V1!.Manifest.CreatedAtUtc;
            sourcePlatform = validated.V1!.Manifest.SourcePlatform;
        }
        else
        {
            sourceAppVersion = validated.V2!.Manifest.SourceAppVersion;
            sourceDatabaseSchemaVersion = validated.V2!.Manifest.SourceDatabaseSchemaVersion;
            createdAtUtc = validated.V2!.Manifest.CreatedAtUtc;
            sourcePlatform = validated.V2!.Manifest.SourcePlatform;
        }

        var manifestInfo = new MergeManifestInfo(
            validated.FormatVersion,
            sourceAppVersion,
            sourceDatabaseSchemaVersion,
            createdAtUtc,
            sourcePlatform);

        BackupSchemaCapabilityResult targetCapability;
        try
        {
            targetCapability = await database.ExecuteSnapshotAsync(BackupSchemaCapability.Resolve);
        }
        catch (OperationCanceledException)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Cancelled, manifestInfo, true, BackupErrorCodes.OperationCancelled);
        }
        catch (BackupSchemaCapabilityException exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, exception.ErrorCode);
        }
        catch (Exception)
        {
            return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, MergePreflightErrorCodes.UnexpectedFailure);
        }

        if (targetCapability is Schema8CapabilityResult or Schema9CapabilityResult)
        {
            KnownFirst.Data.Schema8.Schema8PortableSnapshotCaptureResult captureResultV2;
            try
            {
                captureResultV2 = await database.ExecuteSnapshotAsync(
                    connection => Data.Schema8.Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection));
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

            if (captureResultV2.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.BlockedByActiveWorkflow, manifestInfo, true, BackupErrorCodes.ActiveWorkflowUnsupported);
            }

            var snapshotV2 = captureResultV2.Snapshot
                ?? throw new InvalidOperationException("Snapshot capture reported success without a snapshot.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPayloadV2 = BackupModelMapperV2.MapToExternal(snapshotV2);

                BackupPayloadV2 archivePayloadV2;
                if (validated.FormatVersion == BackupFormatLimits.FormatVersion)
                {
                    archivePayloadV2 = BackupArchiveV1UpgradePolicy.Upgrade(validated.V1!.Payload);
                }
                else
                {
                    archivePayloadV2 = validated.V2!.Payload;
                }

                return MergePreflightPlannerV2.CreatePlan(targetPayloadV2, archivePayloadV2, manifestInfo);
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

        if (validated.FormatVersion != BackupFormatLimits.FormatVersion)
        {
            return MergePreflightPlan.ForEarlyExit(
                MergePreflightStatus.BlockedByPrerequisite,
                manifestInfo,
                true,
                BackupErrorCodes.UnsupportedFormat);
        }

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
            return MergePreflightPlanner.CreatePlan(targetPayload, validated.V1!.Payload, validated.V1!.Manifest);
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
