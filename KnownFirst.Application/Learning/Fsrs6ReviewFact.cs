namespace KnownFirst.Application.Learning;

using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Factual review input for deterministic scheduling replay.
/// </summary>
public readonly record struct Fsrs6ReviewFact
{
    private readonly bool _isInitialized;

    public DateTimeOffset ReviewedAtUtc { get; }
    public ReviewRating Rating { get; }

    public Fsrs6ReviewFact(DateTimeOffset reviewedAtUtc, ReviewRating rating)
    {
        if (reviewedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new LearningScheduleCorruptionException("Review timestamp must be in UTC (offset zero).");
        }

        if (!Enum.IsDefined(rating))
        {
            throw new LearningScheduleCorruptionException($"Review rating {(int)rating} is invalid.");
        }

        _isInitialized = true;
        ReviewedAtUtc = reviewedAtUtc;
        Rating = rating;
    }

    public Fsrs6ReviewEvent ToReviewEvent()
    {
        if (!_isInitialized || ReviewedAtUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(Rating))
        {
            throw new LearningScheduleCorruptionException("Corrupt review fact cannot be mapped to Core review event.");
        }

        return new Fsrs6ReviewEvent(ReviewedAtUtc, Rating);
    }

    public static Fsrs6ReviewFact FromReviewEvent(Fsrs6ReviewEvent reviewEvent) =>
        new(reviewEvent.ReviewedAtUtc, reviewEvent.Rating);
}