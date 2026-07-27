using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// LearningReview event fingerprint per design §6: SHA-256 over the complete immutable field set
/// (stable LearningCard identity — never the raw archive-local CardId — plus ReviewedAtUtc, Rating,
/// WasTypedAnswer, WasCorrect, DueAtUtc, IntervalDays, EaseFactor). Two events with an identical
/// fingerprint are the same real-world event and dedupe; any field difference, even at an identical
/// timestamp, means they are distinct events and both are retained.
/// </summary>
public static class LearningReviewFingerprintPolicy
{
    private const string Domain = "KnownFirst.Merge.LearningReview.v1";

    public static LearningReviewFingerprint Compute(
        LearningCardMatchIdentity cardIdentity,
        DateTime reviewedAtUtc,
        BackupReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        DateTime dueAtUtc,
        int intervalDays,
        double easeFactor)
    {
        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(cardIdentity.Value)
            .WriteUtcTimestamp(reviewedAtUtc)
            .WriteEnum(rating)
            .WriteBoolean(wasTypedAnswer)
            .WriteBoolean(wasCorrect)
            .WriteUtcTimestamp(dueAtUtc)
            .WriteInt32(intervalDays)
            .WriteDouble(easeFactor);

        return new LearningReviewFingerprint(builder.ComputeSha256Hex());
    }

    public static LearningReviewFingerprint Compute(
        BackupLearningReview review,
        IReadOnlyDictionary<string, LearningCardMatchIdentity> cardIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(cardIdentitiesByArchiveId);

        if (!cardIdentitiesByArchiveId.TryGetValue(review.CardId, out var cardIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable learning-card identity supplied for archive card id '{review.CardId}'.");
        }

        return Compute(
            cardIdentity,
            review.ReviewedAtUtc,
            review.Rating,
            review.WasTypedAnswer,
            review.WasCorrect,
            review.DueAtUtc,
            review.IntervalDays,
            review.EaseFactor);
    }
}
