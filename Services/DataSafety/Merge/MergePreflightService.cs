using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
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

        if (validated.V1 is { } v1)
        {
            sourceAppVersion = v1.Manifest.SourceAppVersion;
            sourceDatabaseSchemaVersion = v1.Manifest.SourceDatabaseSchemaVersion;
            createdAtUtc = v1.Manifest.CreatedAtUtc;
            sourcePlatform = v1.Manifest.SourcePlatform;
        }
        else if (validated.V2 is { } v2)
        {
            sourceAppVersion = v2.Manifest.SourceAppVersion;
            sourceDatabaseSchemaVersion = v2.Manifest.SourceDatabaseSchemaVersion;
            createdAtUtc = v2.Manifest.CreatedAtUtc;
            sourcePlatform = v2.Manifest.SourcePlatform;
        }
        else
        {
            var v3 = validated.V3!;
            sourceAppVersion = v3.Manifest.SourceAppVersion;
            sourceDatabaseSchemaVersion = v3.Manifest.SourceDatabaseSchemaVersion;
            createdAtUtc = v3.Manifest.CreatedAtUtc;
            sourcePlatform = v3.Manifest.SourcePlatform;
        }

        var manifestInfo = new MergeManifestInfo(
            validated.FormatVersion,
            sourceAppVersion,
            sourceDatabaseSchemaVersion,
            createdAtUtc,
            sourcePlatform);

        var archiveLearningSessions = validated.V3?.Payload.Workflows.LearningSessions
            ?? validated.V2?.Payload.Workflows.LearningSessions
            ?? [];
        var archiveContainsActiveLearning = archiveLearningSessions
            .Any(session => session.Status == BackupLearningSessionStatus.Active);

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

        if (targetCapability is Schema13CapabilityResult)
        {
            var sourceBasePayload = validated.V2?.Payload
                ?? (validated.V1 is { } legacyV1 ? BackupArchiveV1UpgradePolicy.Upgrade(legacyV1.Payload) : null);
            if (sourceBasePayload is not null)
            {
                try
                {
                    ArchiveLearningReviewCausalOrderPolicy.ThrowIfAmbiguous(sourceBasePayload.Learning.ReviewEvents);
                }
                catch (BackupFormatException exception)
                {
                    return MergePreflightPlan.ForEarlyExit(
                        MergePreflightStatus.ValidationFailed,
                        manifestInfo,
                        true,
                        exception.Code);
                }
            }

            Schema13PortableSnapshotCaptureResult schema13Capture;
            try
            {
                schema13Capture = await database.ExecuteSnapshotAsync(
                    Schema13BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy);
            }
            catch (OperationCanceledException)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Cancelled, manifestInfo, true, BackupErrorCodes.OperationCancelled);
            }
            catch (BackupSchemaCapabilityException exception)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.ValidationFailed, manifestInfo, true, exception.ErrorCode);
            }
            catch (Exception)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, MergePreflightErrorCodes.UnexpectedFailure);
            }

            if (schema13Capture.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.BlockedByActiveWorkflow, manifestInfo, true, BackupErrorCodes.ActiveWorkflowUnsupported);
            }

            var targetPayload = BackupModelMapperV3.MapToExternal(
                schema13Capture.Snapshot
                ?? throw new InvalidOperationException("Schema-13 capture reported success without a snapshot."));
            BackupPayloadV3 sourcePayload;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourcePayload = validated.V3?.Payload
                    ?? await Schema13LegacySourceProjector.ProjectAsync(sourceBasePayload!, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Cancelled, manifestInfo, true, BackupErrorCodes.OperationCancelled);
            }
            catch (Schema13MigrationException exception)
                when (exception.ErrorCode == "schema13-migration-missing-review-history")
            {
                var legacyConflict = Schema13MergePreflightPlan.ForLegacyProjectionConflict(
                    Schema13MergePreflightErrorCodes.LegacyHistoryInsufficient);
                var basePlan = MergePreflightPlannerV2.CreatePlan(
                    Schema13MergePreflightPlanner.ToV2(targetPayload),
                    sourceBasePayload!,
                    manifestInfo);
                return basePlan with
                {
                    Status = MergePreflightStatus.NonExecutableConflict,
                    IsExecutable = false,
                    ErrorCode = Schema13MergePreflightErrorCodes.LegacyHistoryInsufficient,
                    Schema13Plan = legacyConflict
                };
            }
            catch (MergePlanningException exception)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, exception.Code);
            }
            catch (Exception)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, MergePreflightErrorCodes.UnexpectedFailure);
            }

            try
            {
                return Schema13MergePreflightPlanner.CreateCombinedPlan(targetPayload, sourcePayload, manifestInfo);
            }
            catch (MergePlanningException exception)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, exception.Code);
            }
            catch (Exception)
            {
                return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.Failed, manifestInfo, true, MergePreflightErrorCodes.UnexpectedFailure);
            }
        }

        if (targetCapability is Schema8CapabilityResult or Schema9CapabilityResult or Schema10CapabilityResult or Schema11CapabilityResult or Schema12CapabilityResult)
        {
            var useSchema10ActiveLearningPreflightCapture =
                archiveContainsActiveLearning && targetCapability is Schema10CapabilityResult or Schema11CapabilityResult or Schema12CapabilityResult;
            KnownFirst.Data.Schema8.Schema8PortableSnapshotCaptureResult captureResultV2;
            try
            {
                // The Schema-10 identities are read in the same snapshot callback as the capture itself,
                // so the target payload the planner sees carries the same persistent identities the
                // writer will later match against.
                captureResultV2 = await database.ExecuteSnapshotAsync(connection =>
                {
                    var captured = useSchema10ActiveLearningPreflightCapture
                        ? Data.Schema8.Schema8BackupSnapshotRepository.CapturePortableSnapshotForSchema10ActiveLearningMergePreflight(connection)
                        : Data.Schema8.Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection);
                    if (captured.Snapshot is null)
                    {
                        return captured;
                    }

                    var resolvedTargetCapability = BackupSchemaCapability.Resolve(connection);
                    var enrichedSnapshot = captured.Snapshot;
                    if (resolvedTargetCapability is Schema10CapabilityResult or Schema11CapabilityResult or Schema12CapabilityResult)
                    {
                        enrichedSnapshot = Data.Schema8.Schema8BackupSnapshotRepository.WithSchema10LearningIdentities(
                            connection, enrichedSnapshot);
                    }

                    // German Enhanced Term Recognition Package 5A-2: the target payload the planner
                    // classifies against must see the same transported evidence a Schema-11 target already
                    // holds, or every archive evidence row would misclassify as New on every re-import.
                    if (resolvedTargetCapability is Schema11CapabilityResult or Schema12CapabilityResult)
                    {
                        enrichedSnapshot = Data.Schema8.Schema8BackupSnapshotRepository.WithSchema11DerivedEvidenceOwningCandidateIds(
                            connection, enrichedSnapshot);
                    }

                    return captured with { Snapshot = enrichedSnapshot };
                });
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

                if (archiveContainsActiveLearning && !useSchema10ActiveLearningPreflightCapture)
                {
                    return MergePreflightPlan.ForEarlyExit(MergePreflightStatus.BlockedByActiveWorkflow, manifestInfo, true, BackupErrorCodes.ActiveWorkflowUnsupported);
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
