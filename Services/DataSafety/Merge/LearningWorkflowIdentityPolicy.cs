using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// LearningSession/LearningSessionCard identities per design §4.4. A LearningSession has no natural
/// foreign key, so its identity is a content fingerprint of (StartedAtUtc, CompletedAtUtc, SHA-256
/// digest over the ordered list of (stable LearningCardMatchIdentity, Rating) among its queue items).
/// LearningSessionCard identity cascades from the parent session's identity, one level down.
///
/// <para>The <c>ComputeSchema8*</c> overloads below implement the exact same semantic contract for the
/// Schema-8/FutureCard merge path (KF-MEANING-001 Slice 9): a meaning-aware sibling keyed by
/// <see cref="FutureCardIdentity"/> instead of the physical <see cref="LearningCardMatchIdentity"/>, used
/// by <see cref="MergePreflightPlannerV2"/>, <see cref="MergeWriterTargetIndex"/>, and
/// <see cref="MergeWriterExecutor"/> so no Schema-8 learning-workflow identity hash is ever duplicated.</para>
/// </summary>
public static class LearningWorkflowIdentityPolicy
{
    private const string SessionDomain = "KnownFirst.Merge.LearningSession.v1";
    private const string ItemsDigestDomain = "KnownFirst.Merge.LearningSession.ItemsDigest.v1";
    private const string SessionCardDomain = "KnownFirst.Merge.LearningSessionCard.v1";

    private const string Schema8SessionDomain = "KnownFirst.Merge.Schema8.LearningSession.v1";
    private const string Schema8ItemsDigestDomain = "KnownFirst.Merge.Schema8.LearningSession.ItemsDigest.v1";
    private const string Schema8QueueItemDomain = "KnownFirst.Merge.Schema8.LearningQueueItem.v1";

    public static LearningSessionIdentity ComputeSessionIdentity(
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        IReadOnlyList<(LearningCardMatchIdentity CardIdentity, BackupReviewRating? Rating)> orderedQueueItems)
    {
        ArgumentNullException.ThrowIfNull(orderedQueueItems);

        var digestBuilder = new CanonicalFingerprintBuilder(ItemsDigestDomain);
        foreach (var (cardIdentity, rating) in orderedQueueItems)
        {
            digestBuilder.WriteString(cardIdentity.Value);
            digestBuilder.WriteNullableEnum(rating);
        }

        var itemsDigest = digestBuilder.ComputeSha256Hex();

        var builder = new CanonicalFingerprintBuilder(SessionDomain)
            .WriteUtcTimestamp(startedAtUtc)
            .WriteNullableUtcTimestamp(completedAtUtc)
            .WriteString(itemsDigest);

        return new LearningSessionIdentity(builder.ComputeSha256Hex());
    }

    public static LearningSessionIdentity ComputeSessionIdentity(
        BackupLearningWorkflow workflow,
        IReadOnlyDictionary<string, LearningCardMatchIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(cardIdentitiesByArchiveId);

        var orderedItems = workflow.QueueItems
            .OrderBy(item => item.QueueOrder)
            .Select(item => (ResolveCardIdentity(item.CardId, cardIdentitiesByArchiveId), item.Rating))
            .ToList();

        return ComputeSessionIdentity(workflow.StartedAtUtc, workflow.CompletedAtUtc, orderedItems);
    }

    public static LearningSessionCardIdentity ComputeSessionCardIdentity(
        LearningSessionIdentity sessionIdentity, LearningCardMatchIdentity cardIdentity, int queueOrder)
    {
        var builder = new CanonicalFingerprintBuilder(SessionCardDomain)
            .WriteString(sessionIdentity.Value)
            .WriteString(cardIdentity.Value)
            .WriteInt32(queueOrder);

        return new LearningSessionCardIdentity(builder.ComputeSha256Hex());
    }

    public static LearningSessionCardIdentity ComputeSessionCardIdentity(
        BackupLearningQueueItem item,
        LearningSessionIdentity sessionIdentity,
        IReadOnlyDictionary<string, LearningCardMatchIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(item);

        var cardIdentity = ResolveCardIdentity(item.CardId, cardIdentitiesByArchiveId);
        return ComputeSessionCardIdentity(sessionIdentity, cardIdentity, item.QueueOrder);
    }

    private static LearningCardMatchIdentity ResolveCardIdentity(
        string archiveCardId, IReadOnlyDictionary<string, LearningCardMatchIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(cardIdentitiesByArchiveId);
        if (!cardIdentitiesByArchiveId.TryGetValue(archiveCardId, out var identity))
        {
            throw new KeyNotFoundException(
                $"No stable learning-card identity supplied for archive card id '{archiveCardId}'.");
        }

        return identity;
    }

    /// <summary>Schema-8/FutureCard sibling of <see cref="ComputeSessionIdentity(DateTime, DateTime?, IReadOnlyList{ValueTuple{LearningCardMatchIdentity, BackupReviewRating?}})"/>.</summary>
    public static string ComputeSchema8SessionIdentity(
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        IReadOnlyList<(FutureCardIdentity CardIdentity, BackupReviewRating? Rating)> orderedQueueItems)
    {
        ArgumentNullException.ThrowIfNull(orderedQueueItems);

        var digestBuilder = new CanonicalFingerprintBuilder(Schema8ItemsDigestDomain);
        foreach (var (cardIdentity, rating) in orderedQueueItems)
        {
            digestBuilder.WriteString(cardIdentity.Value);
            digestBuilder.WriteNullableEnum(rating);
        }

        var itemsDigest = digestBuilder.ComputeSha256Hex();

        var builder = new CanonicalFingerprintBuilder(Schema8SessionDomain)
            .WriteUtcTimestamp(startedAtUtc)
            .WriteNullableUtcTimestamp(completedAtUtc)
            .WriteString(itemsDigest);

        return builder.ComputeSha256Hex();
    }

    /// <summary>
    /// The logical merge identity of one archive learning workflow. From Schema 10 onward a workflow
    /// carries its own persistent <c>StableId</c>, and that identity — not a recomputed content
    /// fingerprint — is authoritative: it survives edits the fingerprint would not, and it is the same
    /// value on every installation that holds the workflow. A record without one is genuinely legacy
    /// (source schema &lt;= 9), and keeps the original Schema-8 content fingerprint so pre-Schema-10
    /// archives still match exactly as they did before.
    /// </summary>
    public static string ComputeSchema8SessionIdentity(
        BackupLearningWorkflowV2 session,
        IReadOnlyDictionary<string, FutureCardIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cardIdentitiesByArchiveId);

        if (Data.Schema10.LearningWorkflowStableId.IsValid(session.StableId))
        {
            return session.StableId!;
        }

        var orderedItems = session.QueueItems
            .OrderBy(item => item.QueueOrder)
            .Select(item => (ResolveFutureCardIdentity(item.CardId, cardIdentitiesByArchiveId), item.Rating))
            .ToList();

        return ComputeSchema8SessionIdentity(session.StartedAtUtc, session.CompletedAtUtc, orderedItems);
    }

    /// <summary>Schema-8/FutureCard sibling of <see cref="ComputeSessionCardIdentity(LearningSessionIdentity, LearningCardMatchIdentity, int)"/>.</summary>
    public static string ComputeSchema8QueueItemIdentity(string sessionIdentity, FutureCardIdentity cardIdentity, int queueOrder)
    {
        ArgumentNullException.ThrowIfNull(sessionIdentity);

        var builder = new CanonicalFingerprintBuilder(Schema8QueueItemDomain)
            .WriteString(sessionIdentity)
            .WriteString(cardIdentity.Value)
            .WriteInt32(queueOrder);

        return builder.ComputeSha256Hex();
    }

    /// <summary>Queue-row counterpart of the workflow rule above: a persisted <c>StableId</c> wins,
    /// a genuinely legacy row keeps the Schema-8 cascade from its parent session identity.</summary>
    public static string ComputeSchema8QueueItemIdentity(
        BackupLearningQueueItemV2 item,
        string sessionIdentity,
        IReadOnlyDictionary<string, FutureCardIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Data.Schema10.LearningWorkflowStableId.IsValid(item.StableId))
        {
            return item.StableId!;
        }

        var cardIdentity = ResolveFutureCardIdentity(item.CardId, cardIdentitiesByArchiveId);
        return ComputeSchema8QueueItemIdentity(sessionIdentity, cardIdentity, item.QueueOrder);
    }

    private static FutureCardIdentity ResolveFutureCardIdentity(
        string archiveCardId, IReadOnlyDictionary<string, FutureCardIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(cardIdentitiesByArchiveId);
        if (!cardIdentitiesByArchiveId.TryGetValue(archiveCardId, out var identity))
        {
            throw new KeyNotFoundException(
                $"No stable FutureCardIdentity supplied for archive card id '{archiveCardId}'.");
        }

        return identity;
    }
}
