using KnownFirst.Data;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public interface IMergeWriterService
{
    /// <summary>
    /// Applies <paramref name="plan"/> (previously produced by <see cref="IMergePreflightService"/> against
    /// <paramref name="archive"/> and some earlier snapshot of the target) to the current populated Schema-8
    /// target, inside one transaction. Refuses — with zero mutation — a plan that is not executable or that
    /// no longer corresponds to the archive and the target's current state.
    /// </summary>
    Task<MergeWriteResult> ApplyAsync(BackupPayloadV2 archive, MergePreflightPlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// Transactional Schema-8 populated-target merge writer (KF-MEANING-001 Slice 8). The first slice
/// permitted to mutate a populated target: every insert/reuse/enrich decision is driven by an already-
/// computed, already-executable <see cref="MergePreflightPlan"/> (Slice 7), never invented here. Opens
/// exactly one write transaction via <see cref="IKnownFirstDatabase.RunInTransactionAsync{T}"/> — the same
/// primitive <see cref="Services.DataSafety.BackupService.ImportPortableArchiveAsync"/> already uses for
/// restore-into-empty — so any exception or cancellation rolls back every change SQLite-net's own
/// transaction handling already guarantees for that primitive.
///
/// <para>Before writing anything, the plan supplied is re-validated against a fresh capture of the
/// target's current state: <see cref="MergePreflightPlannerV2.CreatePlan"/> is re-run against the same
/// archive payload and the freshly-captured target, and the result must match the supplied plan exactly
/// (see <see cref="MergeWritePlanComparer"/>). A mismatch — the target changed since the plan was computed,
/// or the plan does not correspond to this archive — is refused with zero mutation, never guessed at or
/// partially honored.</para>
///
/// <para>Creates no safety copy and invokes no Import routing/UI layer — both remain a separate package's
/// responsibility (see <see cref="MergeSafetyCopyService"/> for the former, already-shipped in Slice 2).</para>
/// </summary>
public sealed class MergeWriterService(IKnownFirstDatabase database, IBackupImportFailureInjector? failureInjector = null) : IMergeWriterService
{
    public async Task<MergeWriteResult> ApplyAsync(BackupPayloadV2 archive, MergePreflightPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsExecutable)
        {
            return new MergeWriteResult(MergeWriteStatus.NotExecutable, MergeWriterErrorCodes.PlanNotExecutable);
        }

        try
        {
            return await database.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var capability = BackupSchemaCapability.Resolve(connection);
                if (capability is not (Schema8CapabilityResult or Schema9CapabilityResult or Schema10CapabilityResult))
                {
                    return new MergeWriteResult(MergeWriteStatus.Failed, MergeWriterErrorCodes.TargetNotSchema8);
                }

                var captureResult = Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection);
                if (captureResult.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow)
                {
                    return new MergeWriteResult(MergeWriteStatus.BlockedByActiveWorkflow, BackupErrorCodes.ActiveWorkflowUnsupported);
                }

                var targetSnapshot = captureResult.Snapshot
                    ?? throw new InvalidOperationException("Snapshot capture reported success without a snapshot.");

                // Same enrichment the preflight capture performs, so the plan recomputed below is built
                // over identical target identities and a Schema-10 target's rows match by StableId.
                if (capability is Schema10CapabilityResult)
                {
                    targetSnapshot = Schema8BackupSnapshotRepository.WithSchema10LearningIdentities(connection, targetSnapshot);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var targetPayload = BackupModelMapperV2.MapToExternal(targetSnapshot);

                // plan.IsExecutable is already true here (checked above), so plan.Manifest is always
                // non-null: ForEarlyExit — the only path that can leave Manifest null — always sets
                // IsExecutable false.
                var recomputedPlan = MergePreflightPlannerV2.CreatePlan(targetPayload, archive, plan.Manifest!);

                if (!MergeWritePlanComparer.Matches(plan, recomputedPlan))
                {
                    return new MergeWriteResult(MergeWriteStatus.StalePlan, MergeWriterErrorCodes.StalePlan);
                }

                var targetIndex = MergeWriterTargetIndex.Build(targetSnapshot);
                MergeWriterExecutor.Execute(connection, targetSnapshot, targetIndex, archive, recomputedPlan, cancellationToken, failureInjector);

                return MergeWriteResult.SuccessResult;
            });
        }
        catch (OperationCanceledException)
        {
            return new MergeWriteResult(MergeWriteStatus.Cancelled, BackupErrorCodes.OperationCancelled);
        }
        catch (BackupFormatException exception)
        {
            return new MergeWriteResult(MergeWriteStatus.Failed, exception.Code);
        }
        catch (BackupSchemaCapabilityException exception)
        {
            return new MergeWriteResult(MergeWriteStatus.Failed, exception.ErrorCode);
        }
        catch (Exception)
        {
            return new MergeWriteResult(MergeWriteStatus.Failed, MergeWriterErrorCodes.UnexpectedFailure);
        }
    }
}
