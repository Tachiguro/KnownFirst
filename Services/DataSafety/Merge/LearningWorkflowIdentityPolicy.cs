using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// LearningSession/LearningSessionCard identities per design §4.4. A LearningSession has no natural
/// foreign key, so its identity is a content fingerprint of (StartedAtUtc, CompletedAtUtc, SHA-256
/// digest over the ordered list of (stable LearningCardMatchIdentity, Rating) among its queue items).
/// LearningSessionCard identity cascades from the parent session's identity, one level down.
/// </summary>
public static class LearningWorkflowIdentityPolicy
{
    private const string SessionDomain = "KnownFirst.Merge.LearningSession.v1";
    private const string ItemsDigestDomain = "KnownFirst.Merge.LearningSession.ItemsDigest.v1";
    private const string SessionCardDomain = "KnownFirst.Merge.LearningSessionCard.v1";

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
}
