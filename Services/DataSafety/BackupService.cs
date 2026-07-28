using KnownFirst.Data;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public sealed class BackupService(
    IKnownFirstDatabase database,
    IBackupPlatformInfo platformInfo,
    IBackupImportFailureInjector? failureInjector = null) : IBackupService
{
    public async Task CreateBackupAsync(Stream destinationStream, CancellationToken cancellationToken)
    {
        var captured = await database.ExecuteSnapshotAsync(connection =>
            BackupSnapshotCapture.CaptureFullForBackup(connection));

        await WriteArchiveAsync(captured, destinationStream, cancellationToken);
    }

    public async Task CreatePortableArchiveAsync(
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        var captured = await database.ExecuteSnapshotAsync(connection =>
            BackupSnapshotCapture.CaptureForExport(connection));

        await WriteValidatedPortableArchiveAsync(
            captured,
            destinationStream,
            cancellationToken);
    }

    public async Task<BackupPortableArchiveSummary> ValidatePortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        var validated = await BackupArchiveReader.ValidateVersionedAsync(
            sourceStream,
            cancellationToken);
        return BuildSummary(validated);
    }

    internal static BackupPortableArchiveSummary BuildSummary(ValidatedBackupArchiveEnvelope validated)
    {
        if (validated.V1 is { } v1)
        {
            var counts = v1.Manifest.RecordCounts;
            return new BackupPortableArchiveSummary(
                v1.Manifest.FormatVersion,
                v1.Manifest.SourceAppVersion,
                v1.Manifest.SourceDatabaseSchemaVersion,
                v1.Manifest.CreatedAtUtc,
                v1.Manifest.SourcePlatform,
                v1.Manifest.OptionalFeatures,
                v1.Manifest.RequiredFeatures,
                new BackupPortableArchiveCounts(
                    counts.SourceMaterials, counts.SentenceRanges, counts.VocabularyItems, counts.EncounteredForms,
                    counts.Occurrences, counts.PreparedItems, counts.ContextSnapshots, counts.LegacyReviewSummaries,
                    counts.VocabularyReviewWorkflows, counts.VocabularyReviewItems, counts.PreparationWorkflows,
                    counts.PreparationItems, counts.LearningCards, counts.LearningReviews, counts.LearningWorkflows,
                    counts.LearningQueueItems, null, null, null, null));
        }

        var v2 = validated.V2!;
        var v2Counts = v2.Manifest.RecordCounts;
        return new BackupPortableArchiveSummary(
            v2.Manifest.FormatVersion,
            v2.Manifest.SourceAppVersion,
            v2.Manifest.SourceDatabaseSchemaVersion,
            v2.Manifest.CreatedAtUtc,
            v2.Manifest.SourcePlatform,
            v2.Manifest.OptionalFeatures,
            v2.Manifest.RequiredFeatures,
            new BackupPortableArchiveCounts(
                v2Counts.SourceMaterials, v2Counts.SentenceRanges, v2Counts.VocabularyItems, v2Counts.EncounteredForms,
                v2Counts.Occurrences, v2Counts.PreparedItems, v2Counts.ContextSnapshots, v2Counts.LegacyReviewSummaries,
                v2Counts.VocabularyReviewWorkflows, v2Counts.VocabularyReviewItems, v2Counts.PreparationWorkflows,
                v2Counts.PreparationItems, v2Counts.LearningCards, v2Counts.LearningReviews, v2Counts.LearningWorkflows,
                v2Counts.LearningQueueItems, v2Counts.Senses, v2Counts.AnswerVariants,
                v2Counts.SenseAnswerVariantAssignments, v2Counts.AnswerVariantProgress));
    }

    /// <summary>
    /// Dual-schema restore-into-empty orchestration (KF-MEANING-001 Slice 2). Resolves
    /// <see cref="BackupSchemaCapability"/> inside the same <c>RunInTransactionAsync</c> callback that
    /// already checks emptiness — no separate connection, no TOCTOU gap — then dispatches:
    /// v1 archive + Schema-7 target is exactly today's unchanged call; v1 archive + Schema-8 target
    /// upgrades in memory first (<see cref="BackupArchiveV1UpgradePolicy"/>); v2 archive + Schema-8 target
    /// restores directly; v2 archive + Schema-7 target is rejected with a stable error code and zero
    /// mutation — never a downgrade, never an implicit migrate-then-restore.
    /// </summary>
    public async Task<PortableImportResult> ImportPortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        try
        {
            var validated = await BackupArchiveReader.ValidateVersionedAsync(
                sourceStream,
                cancellationToken);

            return await database.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capability = BackupSchemaCapability.Resolve(connection);

                switch (capability)
                {
                    case Schema7CapabilityResult:
                        if (validated.V2 is not null)
                        {
                            // Zero mutation: no read beyond the capability/version check above has
                            // happened yet, and nothing is written below this point.
                            return new PortableImportResult(
                                PortableImportStatus.ValidationFailed,
                                BackupErrorCodes.Schema8ArchiveIncompatibleWithSchema7Target);
                        }

                        if (BackupImportRepository.HasDurableUserData(connection))
                        {
                            return new PortableImportResult(PortableImportStatus.TargetNotEmpty, BackupErrorCodes.TargetNotEmpty);
                        }

                        BackupImportRepository.ImportIntoEmptyDatabase(
                            connection, validated.V1!.Payload, cancellationToken, failureInjector);
                        return new PortableImportResult(PortableImportStatus.Success, null);

                    case Schema8CapabilityResult schema8:
                        if (Schema8BackupImportRepository.HasDurableUserData(connection))
                        {
                            return new PortableImportResult(PortableImportStatus.TargetNotEmpty, BackupErrorCodes.TargetNotEmpty);
                        }

                        var payloadV2 = validated.V2 is not null
                            ? validated.V2.Payload
                            : BackupArchiveV1UpgradePolicy.Upgrade(validated.V1!.Payload);

                        Schema8BackupImportRepository.ImportIntoEmptySchema8Database(
                            connection, schema8.Capability, payloadV2, cancellationToken, failureInjector);
                        return new PortableImportResult(PortableImportStatus.Success, null);

                    default:
                        throw new InvalidOperationException("Unrecognized backup schema capability result.");
                }
            });
        }
        catch (OperationCanceledException)
        {
            return new PortableImportResult(
                PortableImportStatus.Cancelled,
                BackupErrorCodes.OperationCancelled);
        }
        catch (BackupFormatException exception)
        {
            return new PortableImportResult(
                PortableImportStatus.ValidationFailed,
                exception.Code);
        }
        catch (BackupSchemaCapabilityException exception)
        {
            return new PortableImportResult(
                PortableImportStatus.ValidationFailed,
                exception.ErrorCode);
        }
        catch
        {
            return new PortableImportResult(
                PortableImportStatus.Failed,
                BackupErrorCodes.RestoreFailed);
        }
    }

    private async Task WriteArchiveAsync(
        CapturedBackupSnapshotEnvelope captured,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        switch (captured)
        {
            case CapturedSchema7SnapshotEnvelope schema7:
                var payloadV1 = BackupModelMapper.MapToExternal(schema7.Snapshot);
                await BackupArchiveWriter.WriteArchiveAsync(
                    payloadV1, platformInfo, schema7.Capability, DateTime.UtcNow, destinationStream, cancellationToken);
                break;

            case CapturedSchema8SnapshotEnvelope schema8:
                var payloadV2 = BackupModelMapperV2.MapToExternal(schema8.Snapshot);
                await BackupArchiveWriterV2.WriteArchiveAsync(
                    payloadV2, platformInfo, schema8.Capability, DateTime.UtcNow, destinationStream, cancellationToken);
                break;

            default:
                throw new InvalidOperationException("Unrecognized captured backup snapshot envelope.");
        }
    }

    private async Task WriteValidatedPortableArchiveAsync(
        CapturedBackupSnapshotEnvelope captured,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.GetTempFileName();
        try
        {
            await using (var staging = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await WriteArchiveAsync(captured, staging, cancellationToken);
                await staging.FlushAsync(cancellationToken);
                if (staging.Length > BackupFormatLimits.MaxArchiveBytes)
                {
                    throw new BackupFormatException(BackupErrorCodes.ArchiveTooLarge);
                }

                staging.Position = 0;
                await BackupArchiveReader.ValidateVersionedAsync(staging, cancellationToken);
                staging.Position = 0;
                await staging.CopyToAsync(destinationStream, cancellationToken);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the private staging file.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of the private staging file.
            }
        }
    }
}
