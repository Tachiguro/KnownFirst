using System.Globalization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// The two Schema-9 <c>LearningReview</c> merge contracts that <see cref="MergePreflightPlannerV2"/> and
/// <see cref="MergeWriterExecutor"/> must agree on exactly, kept in one place so planner and writer can
/// never drift apart.
///
/// <para><b>Action key.</b> A <c>LearningReview</c> is the one planned entity with no archive-local id of
/// its own, so <see cref="MergePlanAction.ArchiveLocalId"/> carries a synthesized positional label. It is
/// derived from the row's position in <c>archive.Learning.ReviewEvents</c> alone, so two physically
/// distinct rows can never share a key — the writer indexes its per-row action lookup by this value, and a
/// shared key silently collapses two rows onto one action. It is a lookup label only: never a merge
/// identity, never a fingerprint, never a target-local id.</para>
///
/// <para><b>Event fingerprint.</b> Design §6's exact-duplicate rule is that any field difference at the
/// same <c>(stableCardKey, ReviewedAtUtc)</c> means the two rows are not the same event. The v1 domain
/// stopped at <c>EaseFactor</c> and never consulted the emitted answer-variant references, so two reviews
/// exercising genuinely different answer variants collapsed into one. This v2 domain adds both variant
/// references as stable, content-derived <see cref="AnswerVariantIdentity"/> values — never the raw
/// archive-local <c>av-*</c> id and never a target-local numeric id, so equivalent variants held under
/// different local ids on two installations still match.</para>
///
/// <para><b>Deliberately excluded: <c>LearningSessionId</c>.</b> Design §6 defines the immutable
/// real-world event identity without it; the Schema-8 meaning-centric correction requires only the two
/// answer-variant references to be incorporated; and Package D states that its archive-ordering material,
/// which does include the mapped <c>LearningSessionId</c>, must never be reused as merge identity. A
/// review's session is referential workflow attachment and provenance, not event identity: folding it in
/// would couple event deduplication to workflow-session identity stability and duplicate historical
/// reviews across installations whose session rows differ on any identity-bearing field.</para>
///
/// <para>Both values are ephemeral merge contracts computed per plan. Neither is persisted, and neither is
/// an archive-format field. Slice 1's <see cref="LearningReviewFingerprintPolicy"/> and its own
/// <c>KnownFirst.Merge.LearningReview.v1</c> domain are unchanged for their existing callers.</para>
/// </summary>
internal static class Schema9LearningReviewMergeIdentity
{
    private const string EventDomain = "KnownFirst.Merge.Preflight.MeaningAwareLearningReview.v2";

    /// <summary>The synthesized positional lookup label for the archive review row at
    /// <paramref name="archiveRowIndex"/> in <c>archive.Learning.ReviewEvents</c>.</summary>
    public static string ArchiveActionKey(int archiveRowIndex) =>
        "lr#" + archiveRowIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The meaning-aware event fingerprint. <paramref name="futureCardIdentity"/> is the card's stable
    /// semantic identity, and the two variant parameters are stable <see cref="AnswerVariantIdentity"/>
    /// values or <see langword="null"/> when the review carries no such reference — null presence is
    /// encoded explicitly by the canonical builder, so absent and present never collide.
    /// </summary>
    public static string ComputeEventFingerprint(
        string futureCardIdentity,
        DateTime reviewedAtUtc,
        BackupReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        DateTime dueAtUtc,
        int intervalDays,
        double easeFactor,
        string? targetAnswerVariantIdentity,
        string? matchedAnswerVariantIdentity) =>
        new CanonicalFingerprintBuilder(EventDomain)
            .WriteString(futureCardIdentity)
            .WriteUtcTimestamp(reviewedAtUtc)
            .WriteEnum(rating)
            .WriteBoolean(wasTypedAnswer)
            .WriteBoolean(wasCorrect)
            .WriteUtcTimestamp(dueAtUtc)
            .WriteInt32(intervalDays)
            .WriteDouble(easeFactor)
            .WriteNullableString(targetAnswerVariantIdentity)
            .WriteNullableString(matchedAnswerVariantIdentity)
            .ComputeSha256Hex();
}
