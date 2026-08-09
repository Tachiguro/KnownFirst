using KnownFirst.Data.Schema10;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Decides which persistent <c>StableId</c> each learning workflow and queue row of an incoming archive
/// must be stored with on a Schema-10 target (KF-BACKUP-005A).
///
/// <para>Two cases, and the archive itself says which one applies:</para>
/// <list type="bullet">
/// <item><description>The archive <b>carries</b> an identity (written from a Schema-10 source). It is
/// preserved exactly. Regenerating it would make the restored copy disagree with the database that
/// produced the archive — the precise failure the identity exists to prevent.</description></item>
/// <item><description>The archive <b>omits</b> it (written from a Schema-8/9 source). Ordinary portable
/// export only ever emitted Completed workflows, so the identity is reconstructed through
/// <see cref="LearningWorkflowStableIdBootstrapPolicy"/> — the exact same computation the Schema-10
/// migration performs on a local database. That shared derivation is what makes "migrate locally" and
/// "receive the same history through an archive" converge on one identity instead of two.</description></item>
/// </list>
///
/// <para>An Active workflow without an identity (only reachable through a full/internal backup restore,
/// never through ordinary portable export) receives a fresh GUID-form id, matching what the migration
/// would have given the same row locally.</para>
/// </summary>
public static class LearningWorkflowStableIdArchiveResolver
{
    /// <summary>Archive-local id → persistent StableId, for workflows and for queue rows.</summary>
    public sealed record ResolvedArchiveStableIds(
        IReadOnlyDictionary<string, string> WorkflowStableIdsByArchiveId,
        IReadOnlyDictionary<string, string> QueueStableIdsByArchiveId);

    public static ResolvedArchiveStableIds Resolve(BackupPayloadV2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var workflowStableIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var queueStableIds = new Dictionary<string, string>(StringComparer.Ordinal);
        if (payload.Workflows.LearningSessions.Count == 0)
        {
            return new ResolvedArchiveStableIds(workflowStableIds, queueStableIds);
        }

        var cardIdentities = BuildArchiveFutureCardIdentities(payload);

        foreach (var workflow in payload.Workflows.LearningSessions)
        {
            var isCompleted = workflow.Status == BackupLearningSessionStatus.Completed;
            var orderedItems = workflow.QueueItems.OrderBy(item => item.QueueOrder).ToList();

            var workflowStableId = LearningWorkflowStableId.IsValid(workflow.StableId)
                ? workflow.StableId!
                : isCompleted
                    ? LearningWorkflowStableIdBootstrapPolicy.ComputeCompletedSessionStableId(
                        workflow.StartedAtUtc,
                        workflow.CompletedAtUtc,
                        [.. orderedItems.Select(item => (ResolveCardIdentity(cardIdentities, item.CardId), item.Rating))])
                    : LearningWorkflowStableId.NewGuidForm();

            workflowStableIds[workflow.Id] = workflowStableId;

            foreach (var item in orderedItems)
            {
                queueStableIds[item.Id] = LearningWorkflowStableId.IsValid(item.StableId)
                    ? item.StableId!
                    : isCompleted
                        ? LearningWorkflowStableIdBootstrapPolicy.ComputeCompletedQueueItemStableId(
                            workflowStableId,
                            ResolveCardIdentity(cardIdentities, item.CardId),
                            item.QueueOrder,
                            item.IsAgainRepeat)
                        : LearningWorkflowStableId.NewGuidForm();
            }
        }

        return new ResolvedArchiveStableIds(workflowStableIds, queueStableIds);
    }

    /// <summary>
    /// The archive's own <see cref="FutureCardIdentity"/> per archive-local card id, derived through the
    /// same policy chain <see cref="MergePreflightPlannerV2"/> uses, so a bootstrap computed here matches
    /// one computed from the equivalent local rows.
    /// </summary>
    private static Dictionary<string, FutureCardIdentity> BuildArchiveFutureCardIdentities(BackupPayloadV2 payload)
    {
        var vocabularyIdentities = new Dictionary<string, VocabularyIdentity>(StringComparer.Ordinal);
        foreach (var item in payload.Vocabulary)
        {
            vocabularyIdentities[item.Id] = VocabularyMergeIdentityPolicy.Compute(item);
        }

        var senseIdentities = new Dictionary<string, SemanticMeaningIdentity>(StringComparer.Ordinal);
        foreach (var sense in payload.Senses)
        {
            if (vocabularyIdentities.TryGetValue(sense.VocabularyId, out var vocabularyIdentity))
            {
                senseIdentities[sense.Id] = SemanticMeaningIdentityPolicy.Compute(sense, vocabularyIdentity);
            }
        }

        var cardIdentities = new Dictionary<string, FutureCardIdentity>(StringComparer.Ordinal);
        foreach (var card in payload.Learning.Cards)
        {
            if (!senseIdentities.TryGetValue(card.SenseId, out var senseIdentity))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            cardIdentities[card.Id] = FutureCardIdentityPolicy.Compute(senseIdentity, card.Direction);
        }

        return cardIdentities;
    }

    private static FutureCardIdentity ResolveCardIdentity(
        IReadOnlyDictionary<string, FutureCardIdentity> cardIdentities, string archiveCardId) =>
        cardIdentities.TryGetValue(archiveCardId, out var identity)
            ? identity
            : throw new BackupFormatException(BackupErrorCodes.MissingReference);
}
