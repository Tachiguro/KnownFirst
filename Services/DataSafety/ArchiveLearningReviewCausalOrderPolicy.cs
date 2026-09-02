using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

internal static class ArchiveLearningReviewCausalOrderPolicy
{
    internal const string RequiredFeature = "learning-review-causal-order-v1";

    internal static void ValidateV3Features(
        IReadOnlyList<string> requiredFeatures,
        IReadOnlyList<string> optionalFeatures)
    {
        if (optionalFeatures.Count != 0
            || requiredFeatures.Any(feature => !string.Equals(feature, RequiredFeature, StringComparison.Ordinal)))
        {
            throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
        }
    }

    internal static void ValidateV3ReviewOrder(
        IReadOnlyList<string> requiredFeatures,
        IReadOnlyList<BackupLearningReviewV2> reviews)
    {
        if (requiredFeatures.Contains(RequiredFeature, StringComparer.Ordinal))
        {
            return;
        }

        ThrowIfAmbiguous(reviews);
    }

    internal static void ThrowIfAmbiguous(IReadOnlyList<BackupLearningReviewV2> reviews)
    {
        var seen = new HashSet<(string CardId, long ReviewedAtUtcTicks)>();
        foreach (var review in reviews)
        {
            var key = (review.CardId, Schema8Utc.Normalize(review.ReviewedAtUtc).Ticks);
            if (!seen.Add(key))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
        }
    }
}
