namespace KnownFirst.Application.Learning;

using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Orchestrates Core FSRS-6 scheduler and replayer behind the clean application boundary.
/// </summary>
public sealed class Fsrs6SchedulingService : IFsrs6SchedulingService
{
    private readonly IClock _clock;
    private readonly Fsrs6Scheduler _scheduler;
    private readonly Fsrs6Replayer _replayer;

    public Fsrs6SchedulingService(
        IClock clock,
        Fsrs6Scheduler? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _scheduler = scheduler ?? new Fsrs6Scheduler();
        _replayer = new Fsrs6Replayer(_scheduler);
    }

    public Fsrs6ScheduleProjection Schedule(Fsrs6ScheduleProjection currentProjection, ReviewRating rating)
    {
        ArgumentNullException.ThrowIfNull(currentProjection);

        var utcNow = _clock.UtcNow;
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"Injected clock must provide UTC timestamp (got DateTimeKind.{utcNow.Kind}).");
        }

        var reviewedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Schedule(currentProjection, rating, reviewedAtUtc);
    }

    public Fsrs6ScheduleProjection Schedule(
        Fsrs6ScheduleProjection currentProjection,
        ReviewRating rating,
        DateTimeOffset reviewedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(currentProjection);

        if (!Enum.IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "Review rating is invalid.");
        }

        if (reviewedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Review timestamp must be in UTC (offset zero).", nameof(reviewedAtUtc));
        }

        var card = currentProjection.ToCard();

        try
        {
            var scheduledCard = _scheduler.Schedule(card, rating, reviewedAtUtc);
            return Fsrs6ScheduleProjection.FromCard(scheduledCard);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException or InvalidOperationException)
        {
            throw new LearningScheduleCorruptionException(
                $"Failed to schedule transition due to invalid or corrupt state ({ex.Message})",
                ex);
        }
    }

    public Fsrs6ScheduleProjection Replay(
        Fsrs6ScheduleProjection initialProjection,
        IEnumerable<Fsrs6ReviewFact> reviewFacts)
    {
        ArgumentNullException.ThrowIfNull(initialProjection);
        ArgumentNullException.ThrowIfNull(reviewFacts);

        var initialCard = initialProjection.ToCard();

        // Materialize and validate application review facts outside the Core replay catch boundary.
        // Unrelated enumerable, caller, or iterator exceptions propagate unchanged without reclassification.
        // Any uninitialized or corrupt review fact will throw LearningScheduleCorruptionException directly.
        var coreEvents = reviewFacts.Select(fact => fact.ToReviewEvent()).ToArray();

        try
        {
            var resultCard = _replayer.Replay(initialCard, coreEvents);
            return Fsrs6ScheduleProjection.FromCard(resultCard);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException or InvalidOperationException)
        {
            throw new LearningScheduleCorruptionException(
                $"Replay failed due to invalid or corrupt review history ({ex.Message})",
                ex);
        }
    }
}