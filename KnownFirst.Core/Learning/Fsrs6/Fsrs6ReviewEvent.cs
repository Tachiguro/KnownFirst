using KnownFirst.Core.Learning;

namespace KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Validated scheduling review event input for FSRS-6.
/// </summary>
public readonly record struct Fsrs6ReviewEvent
{
    public DateTimeOffset ReviewedAtUtc { get; }
    public ReviewRating Rating { get; }

    public Fsrs6ReviewEvent(DateTimeOffset reviewedAtUtc, ReviewRating rating)
    {
        if (reviewedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Review timestamp must be in UTC (offset zero).", nameof(reviewedAtUtc));
        }

        if (!Enum.IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "Review rating is invalid.");
        }

        ReviewedAtUtc = reviewedAtUtc;
        Rating = rating;
    }
}
