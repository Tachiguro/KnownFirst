using KnownFirst.Data;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Services.DataSafety;

public sealed class BackupService(
    IKnownFirstDatabase database,
    IBackupPlatformInfo platformInfo,
    IBackupImportFailureInjector? failureInjector = null,
    IMergePreflightService? mergePreflightService = null,
    IMergeSafetyCopyService? mergeSafetyCopyService = null,
    IMergeWriterService? mergeWriterService = null) : IBackupService
{
    private readonly IMergePreflightService _mergePreflightService =
        mergePreflightService ?? new MergePreflightService(database);
    private readonly IMergeSafetyCopyService _mergeSafetyCopyService =
        mergeSafetyCopyService ?? new MergeSafetyCopyService(database, platformInfo);
    private readonly IMergeWriterService _mergeWriterService =
        mergeWriterService ?? new MergeWriterService(database, failureInjector);

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
    /// Read-only preview (KF-MEANING-001 Slice 9): mirrors <see cref="ImportPortableArchiveAsync"/>'s
    /// routing decision without ever mutating the target, creating a safety copy, or invoking the merge
    /// writer. A populated Schema-8 target reuses <see cref="_mergePreflightService"/> — the exact same
    /// read-only planner the real import call runs — to distinguish an executable merge with mutations
    /// from a no-op duplicate import from a blocked/non-executable plan. The subsequent
    /// <see cref="ImportPortableArchiveAsync"/> call re-reads the caller's freshly reopened stream and
    /// re-resolves the plan from the target's state at that later moment; this preview is informational
    /// only and can never bypass or weaken that independent re-evaluation.
    /// </summary>
    public async Task<PortableImportPreview> PreviewPortableImportAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        ValidatedBackupArchiveEnvelope validated;
        try
        {
            validated = await BackupArchiveReader.ValidateVersionedAsync(sourceStream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.Cancelled, BackupErrorCodes.OperationCancelled);
        }
        catch (BackupFormatException exception)
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.ValidationFailed, exception.Code);
        }
        catch (BackupSchemaCapabilityException exception)
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.ValidationFailed, exception.ErrorCode);
        }
        catch
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.Failed, BackupErrorCodes.RestoreFailed);
        }

        var archiveSummary = BuildSummary(validated);

        try
        {
            var (capability, targetHasDurableData) = await database.ExecuteSnapshotAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolvedCapability = BackupSchemaCapability.Resolve(connection);
                var hasDurableData = resolvedCapability switch
                {
                    Schema7CapabilityResult => BackupImportRepository.HasDurableUserData(connection),
                    Schema8CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    Schema9CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    Schema10CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    Schema11CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    _ => throw new InvalidOperationException("Unrecognized backup schema capability result.")
                };
                return (resolvedCapability, hasDurableData);
            });

            if (capability is Schema7CapabilityResult)
            {
                if (validated.V2 is not null)
                {
                    return PortableImportPreview.ForBlocked(
                        PortableImportPreviewDisposition.ValidationFailed,
                        BackupErrorCodes.Schema8ArchiveIncompatibleWithSchema7Target,
                        archiveSummary);
                }

                if (targetHasDurableData)
                {
                    return PortableImportPreview.ForBlocked(
                        PortableImportPreviewDisposition.Blocked,
                        BackupErrorCodes.TargetNotEmpty,
                        archiveSummary);
                }

                return PortableImportPreview.ForRestoreIntoEmpty(archiveSummary);
            }

            if (!targetHasDurableData)
            {
                return PortableImportPreview.ForRestoreIntoEmpty(archiveSummary);
            }

            var plan = await _mergePreflightService.CreatePreflightPlanAsync(validated, cancellationToken);
            if (!plan.IsExecutable)
            {
                var (disposition, errorCode) = MapNonExecutablePreflightPreview(plan);
                return PortableImportPreview.ForBlocked(disposition, errorCode, archiveSummary, plan.WarningCodes);
            }

            var (inserted, enriched, preserved, skipped) = AggregateMergeCounts(plan);
            return RequiresWriterExecution(plan)
                ? PortableImportPreview.ForMergeChanges(archiveSummary, inserted, enriched, preserved, skipped, plan.WarningCodes)
                : PortableImportPreview.ForMergeNoChange(archiveSummary, skipped, plan.WarningCodes);
        }
        catch (OperationCanceledException)
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.Cancelled, BackupErrorCodes.OperationCancelled, archiveSummary);
        }
        catch (BackupSchemaCapabilityException exception)
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.ValidationFailed, exception.ErrorCode, archiveSummary);
        }
        catch
        {
            return PortableImportPreview.ForBlocked(PortableImportPreviewDisposition.Failed, BackupErrorCodes.RestoreFailed, archiveSummary);
        }
    }

    private static (PortableImportPreviewDisposition Disposition, string? ErrorCode) MapNonExecutablePreflightPreview(MergePreflightPlan plan) =>
        plan.Status switch
        {
            MergePreflightStatus.Cancelled =>
                (PortableImportPreviewDisposition.Cancelled, plan.ErrorCode ?? BackupErrorCodes.OperationCancelled),
            MergePreflightStatus.ValidationFailed =>
                (PortableImportPreviewDisposition.ValidationFailed, plan.ErrorCode ?? MergePreflightErrorCodes.UnexpectedFailure),
            MergePreflightStatus.BlockedByActiveWorkflow =>
                (PortableImportPreviewDisposition.Blocked, plan.ErrorCode ?? BackupErrorCodes.ActiveWorkflowUnsupported),
            MergePreflightStatus.RequiresUserDecision =>
                (PortableImportPreviewDisposition.Blocked, plan.ErrorCode ?? PortableImportPreviewErrorCodes.MergeRequiresUserDecision),
            MergePreflightStatus.BlockedByPrerequisite =>
                (PortableImportPreviewDisposition.Blocked, plan.ErrorCode ?? PortableImportPreviewErrorCodes.MergeBlockedByPrerequisite),
            _ => (PortableImportPreviewDisposition.Failed, plan.ErrorCode ?? MergePreflightErrorCodes.UnexpectedFailure)
        };

    /// <summary>
    /// Import routing (KF-MEANING-001 Slice 8): the caller's <paramref name="sourceStream"/> is validated
    /// exactly once, regardless of which branch below runs — it need not be seekable, and it is never
    /// rewound or re-validated. An empty-or-Schema-7 target restores exactly as before (unchanged
    /// dual-schema restore-into-empty orchestration, KF-MEANING-001 Slice 2); a populated, active Schema-8
    /// target routes through <see cref="ImportIntoPopulatedSchema8Async"/> instead — preflight, safety
    /// copy, and the transactional merge writer, in that order. The routing read below (capability +
    /// emptiness) is a best-effort hint only: the restore branch re-resolves capability and re-checks
    /// emptiness itself inside its own transaction exactly as before (no weakened TOCTOU guarantee there),
    /// and the merge branch's writer independently re-validates the plan against the target's current
    /// state immediately before mutating (see <see cref="MergeWriterService"/>) — this routing check can
    /// never bypass either guard, only choose which one runs.
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

            var (capability, targetHasDurableData) = await database.ExecuteSnapshotAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolvedCapability = BackupSchemaCapability.Resolve(connection);
                var hasDurableData = resolvedCapability switch
                {
                    Schema7CapabilityResult => BackupImportRepository.HasDurableUserData(connection),
                    Schema8CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    Schema9CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    Schema10CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    Schema11CapabilityResult => Schema8BackupImportRepository.HasDurableUserData(connection),
                    _ => throw new InvalidOperationException("Unrecognized backup schema capability result.")
                };
                return (resolvedCapability, hasDurableData);
            });

            if ((capability is Schema8CapabilityResult or Schema9CapabilityResult or Schema10CapabilityResult or Schema11CapabilityResult) && targetHasDurableData)
            {
                return await ImportIntoPopulatedSchema8Async(validated, cancellationToken);
            }

            return await database.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolvedCapability = BackupSchemaCapability.Resolve(connection);

                switch (resolvedCapability)
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
                        return new PortableImportResult(
                            PortableImportStatus.Success,
                            null,
                            new PortableImportSummary(PortableImportDisposition.RestoredIntoEmpty, false, 0, 0, 0, 0));

                    case Schema8CapabilityResult:
                    case Schema9CapabilityResult:
                    case Schema10CapabilityResult:
                    case Schema11CapabilityResult:
                        if (Schema8BackupImportRepository.HasDurableUserData(connection))
                        {
                            return new PortableImportResult(PortableImportStatus.TargetNotEmpty, BackupErrorCodes.TargetNotEmpty);
                        }

                        var payloadV2 = validated.V2 is not null
                            ? validated.V2.Payload
                            : BackupArchiveV1UpgradePolicy.Upgrade(validated.V1!.Payload);

                        // Schema 9, 10, and 11 share Schema 8's meaning-centric data model (additive
                        // stable-id / derivation-evidence activations), so a fresh proof object satisfies
                        // the repository's Schema-8 capability requirement identically to a resolved one.
                        var schema8ImportCapability = resolvedCapability is Schema8CapabilityResult schema8
                            ? schema8.Capability
                            : new ValidatedSchema8Capability();
                        Schema8BackupImportRepository.ImportIntoEmptySchema8Database(
                            connection, schema8ImportCapability, payloadV2, cancellationToken, failureInjector);
                        return new PortableImportResult(
                            PortableImportStatus.Success,
                            null,
                            new PortableImportSummary(PortableImportDisposition.RestoredIntoEmpty, false, 0, 0, 0, 0));

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

    /// <summary>
    /// Populated-Schema-8-target routing (KF-MEANING-001 Slice 8): compute a current read-only preflight
    /// plan, reject a non-executable/blocked plan before any safety-copy attempt, return a no-change
    /// success with no safety copy and no writer invocation when the plan requires no mutation and no
    /// scheduler replay, otherwise create and validate a safety copy and only then invoke the transactional
    /// merge writer. The writer's own in-transaction plan recomputation remains the final stale-plan and
    /// active-workflow guard — never bypassed or duplicated here.
    /// </summary>
    private async Task<PortableImportResult> ImportIntoPopulatedSchema8Async(
        ValidatedBackupArchiveEnvelope validated,
        CancellationToken cancellationToken)
    {
        var plan = await _mergePreflightService.CreatePreflightPlanAsync(validated, cancellationToken);

        if (!plan.IsExecutable)
        {
            return new PortableImportResult(
                MapNonExecutablePreflightStatus(plan.Status),
                plan.ErrorCode ?? MergeWriterErrorCodes.PlanNotExecutable);
        }

        if (!RequiresWriterExecution(plan))
        {
            return new PortableImportResult(
                PortableImportStatus.Success,
                null,
                BuildMergeSummary(plan, PortableImportDisposition.MergeNoChange, safetyCopyCreated: false));
        }

        var sourceDescription =
            $"Merge import ({plan.Manifest!.SourcePlatform}, app {plan.Manifest.SourceAppVersion}, archived {plan.Manifest.CreatedAtUtc:O})";
        var safetyCopyResult = await _mergeSafetyCopyService.CreateSafetyCopyAsync(sourceDescription, cancellationToken);
        if (safetyCopyResult.Status != MergeSafetyCopyStatus.Success)
        {
            return new PortableImportResult(
                MapSafetyCopyFailureStatus(safetyCopyResult.Status),
                safetyCopyResult.ErrorCode);
        }

        var archivePayloadV2 = validated.V2?.Payload ?? BackupArchiveV1UpgradePolicy.Upgrade(validated.V1!.Payload);
        var writeResult = await _mergeWriterService.ApplyAsync(archivePayloadV2, plan, cancellationToken);
        if (writeResult.Status != MergeWriteStatus.Success)
        {
            // The safety copy already created above is deliberately retained — never deleted here,
            // regardless of why the writer refused or rolled back.
            return new PortableImportResult(
                MapWriterFailureStatus(writeResult.Status),
                writeResult.ErrorCode);
        }

        return new PortableImportResult(
            PortableImportStatus.Success,
            null,
            BuildMergeSummary(plan, PortableImportDisposition.MergeApplied, safetyCopyCreated: true));
    }

    /// <summary>
    /// A plan requires the writer only when it contains at least one action requiring insertion,
    /// enrichment, or preserved-variant handling, or requires scheduler replay — never determined by
    /// comparing database row counts before/after.
    /// </summary>
    private static bool RequiresWriterExecution(MergePreflightPlan plan) =>
        plan.PerEntity.Values.Any(counts => counts.TotalInsertableCount > 0) || plan.RequiresSchedulerReplay;

    private static PortableImportStatus MapNonExecutablePreflightStatus(MergePreflightStatus status) => status switch
    {
        MergePreflightStatus.Cancelled => PortableImportStatus.Cancelled,
        MergePreflightStatus.ValidationFailed => PortableImportStatus.ValidationFailed,
        _ => PortableImportStatus.Failed
    };

    private static PortableImportStatus MapSafetyCopyFailureStatus(MergeSafetyCopyStatus status) => status switch
    {
        MergeSafetyCopyStatus.Cancelled => PortableImportStatus.Cancelled,
        _ => PortableImportStatus.Failed
    };

    private static PortableImportStatus MapWriterFailureStatus(MergeWriteStatus status) => status switch
    {
        MergeWriteStatus.Cancelled => PortableImportStatus.Cancelled,
        _ => PortableImportStatus.Failed
    };

    private static PortableImportSummary BuildMergeSummary(
        MergePreflightPlan plan, PortableImportDisposition disposition, bool safetyCopyCreated)
    {
        var (inserted, enriched, preserved, skipped) = AggregateMergeCounts(plan);
        return new PortableImportSummary(disposition, safetyCopyCreated, inserted, enriched, preserved, skipped);
    }

    private static (int Inserted, int Enriched, int Preserved, int Skipped) AggregateMergeCounts(MergePreflightPlan plan)
    {
        var inserted = 0;
        var enriched = 0;
        var preserved = 0;
        var skipped = 0;
        foreach (var counts in plan.PerEntity.Values)
        {
            inserted += counts.NewCount;
            enriched += counts.EnrichedCount;
            preserved += counts.PreservedVariantCount;
            skipped += counts.ExactDuplicateSkippedCount + counts.DeduplicatedEventCount;
        }

        return (inserted, enriched, preserved, skipped);
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

            case CapturedSchema9SnapshotEnvelope schema9:
                var payloadV2FromSchema9 = BackupModelMapperV2.MapToExternal(schema9.Snapshot);
                await BackupArchiveWriterV2.WriteArchiveAsync(
                    payloadV2FromSchema9, platformInfo, schema9.Capability, DateTime.UtcNow, destinationStream, cancellationToken);
                break;

            case CapturedSchema10SnapshotEnvelope schema10:
                var payloadV2FromSchema10 = BackupModelMapperV2.MapToExternal(schema10.Snapshot);
                await BackupArchiveWriterV2.WriteArchiveAsync(
                    payloadV2FromSchema10, platformInfo, schema10.Capability, DateTime.UtcNow, destinationStream, cancellationToken);
                break;

            case CapturedSchema11SnapshotEnvelope schema11:
                var payloadV2FromSchema11 = BackupModelMapperV2.MapToExternal(schema11.Snapshot);
                await BackupArchiveWriterV2.WriteArchiveAsync(
                    payloadV2FromSchema11, platformInfo, schema11.Capability, DateTime.UtcNow, destinationStream, cancellationToken);
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
