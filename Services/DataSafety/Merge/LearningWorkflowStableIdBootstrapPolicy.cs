using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// The one-time deterministic bootstrap that gives a <b>historically portable</b> learning workflow its
/// persistent Schema-10 <c>StableId</c> (KF-BACKUP-005A).
///
/// <para><b>Why only Completed workflows.</b> A Completed LearningSession has been part of the ordinary
/// portable export since Beta 10, so the identical completed history can genuinely already exist on two
/// installations that exchanged an archive. Those two installations must independently arrive at the same
/// identity, or the first post-Schema-10 merge would duplicate history that is already shared. An
/// <b>Active</b> session was never portable through any supported path, so no such reconstruction problem
/// exists for it — it simply receives a fresh <c>LearningWorkflowStableId.NewGuidForm()</c> at migration
/// and keeps it forever. That asymmetry is the whole design.</para>
///
/// <para><b>Frozen material.</b> The hashed material is exactly the semantic content of the completed
/// workflow as the current schema stores it: <c>StartedAtUtc</c>, <c>CompletedAtUtc</c>, and the stable
/// ordered queue semantics — each queue position's <see cref="FutureCardIdentity"/> plus its recorded
/// Rating. Deliberately excluded, because none of them is stable across two installations holding the
/// same history: local SQLite row ids, archive ordinals, and any Package-D ordering fingerprint.</para>
///
/// <para><b>Domains are frozen.</b> The two domain strings below are part of the persisted data contract
/// from the moment the first database migrates. Changing either one silently re-partitions the identity
/// space and would make two installations that already agreed disagree — a new revision must be a new
/// domain string (<c>.v2</c>), never an edit of these.</para>
///
/// <para>Output is lowercase hex. <see cref="CanonicalFingerprintBuilder.ComputeSha256Hex"/> emits
/// uppercase (every other merge identity keeps that form), so the digest is lowercased exactly once, here,
/// to satisfy <c>LearningWorkflowStableId</c>'s canonical-lowercase rule. The hash <em>input</em> is
/// untouched by that, so this is a rendering choice, not an identity change.</para>
/// </summary>
public static class LearningWorkflowStableIdBootstrapPolicy
{
    /// <summary>Frozen identity domain for a legacy Completed LearningSession. Never edit; supersede.</summary>
    public const string CompletedSessionDomain = "KnownFirst.Identity.LearningSession.LegacyCompletedBootstrap.v1";

    /// <summary>Frozen identity domain for a queue row of a legacy Completed LearningSession. Never edit; supersede.</summary>
    public const string CompletedQueueItemDomain = "KnownFirst.Identity.LearningQueueItem.LegacyCompletedBootstrap.v1";

    /// <summary>
    /// The deterministic StableId of one legacy Completed LearningSession.
    /// <paramref name="orderedQueueItems"/> must already be in the workflow's own stable queue order
    /// (QueueOrder, then physical row id as the documented tie-break) — this method hashes the sequence it
    /// is given and never reorders it, so two installations must order identically before calling.
    /// </summary>
    public static string ComputeCompletedSessionStableId(
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        IReadOnlyList<(FutureCardIdentity CardIdentity, BackupReviewRating? Rating)> orderedQueueItems)
    {
        ArgumentNullException.ThrowIfNull(orderedQueueItems);

        var builder = new CanonicalFingerprintBuilder(CompletedSessionDomain)
            .WriteUtcTimestamp(startedAtUtc)
            .WriteNullableUtcTimestamp(completedAtUtc)
            .WriteInt32(orderedQueueItems.Count);

        foreach (var (cardIdentity, rating) in orderedQueueItems)
        {
            builder.WriteString(cardIdentity.Value);
            builder.WriteNullableEnum(rating);
        }

        return ToCanonicalLowercase(builder.ComputeSha256Hex());
    }

    /// <summary>
    /// The deterministic StableId of one queue row of a legacy Completed LearningSession. Cascades from
    /// the already-deterministic parent session identity, so a queue row can never be stable while its
    /// owning session is not. <paramref name="isAgainRepeat"/> is part of the material because an Again
    /// repeat is a genuinely different queue position from the original attempt at the same card, even
    /// when both share a QueueOrder-adjacent slot.
    /// </summary>
    public static string ComputeCompletedQueueItemStableId(
        string sessionStableId,
        FutureCardIdentity cardIdentity,
        int queueOrder,
        bool isAgainRepeat)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionStableId);

        var builder = new CanonicalFingerprintBuilder(CompletedQueueItemDomain)
            .WriteString(sessionStableId)
            .WriteString(cardIdentity.Value)
            .WriteInt32(queueOrder)
            .WriteBoolean(isAgainRepeat);

        return ToCanonicalLowercase(builder.ComputeSha256Hex());
    }

    private static string ToCanonicalLowercase(string uppercaseHex) => uppercaseHex.ToLowerInvariant();
}
