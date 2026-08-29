using KnownFirst.Core.Learning;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Data.Migrations.Schema13;

public static class Schema13HistoricalReviewStableIdPolicy
{
    public const string Domain = "KnownFirst.Identity.FsrsReviewHistoryEntry.LegacyMigration.v1";

    public static string Compute(
        string senseStableId,
        CardDirection direction,
        DateTime reviewedAtUtc,
        ReviewRating rating,
        int multiplicityOrdinal)
    {
        if (string.IsNullOrWhiteSpace(senseStableId))
        {
            throw new ArgumentException("SenseStableId must be a non-empty string.", nameof(senseStableId));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Undefined CardDirection.");
        }

        if (!Enum.IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "Undefined ReviewRating.");
        }

        if (multiplicityOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplicityOrdinal), multiplicityOrdinal, "MultiplicityOrdinal must be non-negative.");
        }

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(senseStableId.Trim())
            .WriteEnum(direction)
            .WriteUtcTimestamp(reviewedAtUtc)
            .WriteEnum(rating)
            .WriteInt32(multiplicityOrdinal);

        return builder.ComputeSha256Hex().ToLowerInvariant();
    }
}
