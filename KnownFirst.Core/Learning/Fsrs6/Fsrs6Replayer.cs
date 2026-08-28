namespace KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Deterministically derives an FSRS-6 card by replaying an ordered factual review history.
/// </summary>
public sealed class Fsrs6Replayer
{
    private readonly Fsrs6Scheduler _scheduler;

    public Fsrs6Replayer(Fsrs6Scheduler? scheduler = null)
    {
        _scheduler = scheduler ?? new Fsrs6Scheduler();
    }

    public Fsrs6Card Replay(
        Fsrs6Card initialCard,
        IEnumerable<Fsrs6ReviewEvent> events)
    {
        ArgumentNullException.ThrowIfNull(initialCard);
        ArgumentNullException.ThrowIfNull(events);

        var currentCard = initialCard;
        var previousTimestamp = initialCard.LastReviewedAtUtc;

        foreach (var reviewEvent in events)
        {
            _ = new Fsrs6ReviewEvent(reviewEvent.ReviewedAtUtc, reviewEvent.Rating);

            if (previousTimestamp.HasValue
                && reviewEvent.ReviewedAtUtc < previousTimestamp.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(events),
                    reviewEvent.ReviewedAtUtc,
                    "Review events must be supplied in non-decreasing timestamp order.");
            }

            currentCard = _scheduler.Schedule(
                currentCard,
                reviewEvent.Rating,
                reviewEvent.ReviewedAtUtc);
            previousTimestamp = reviewEvent.ReviewedAtUtc;
        }

        return currentCard;
    }
}
