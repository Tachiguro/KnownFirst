namespace KnownFirst.Application.Learning;

using KnownFirst.Core.Learning;

/// <summary>
/// Application service boundary for FSRS-6 scheduling transitions and deterministic replay.
/// </summary>
public interface IFsrs6SchedulingService
{
    /// <summary>
    /// Schedules a single review rating transition using the injected clock for the review timestamp.
    /// </summary>
    Fsrs6ScheduleProjection Schedule(Fsrs6ScheduleProjection currentProjection, ReviewRating rating);

    /// <summary>
    /// Schedules a single review rating transition with an explicit UTC review timestamp.
    /// </summary>
    Fsrs6ScheduleProjection Schedule(Fsrs6ScheduleProjection currentProjection, ReviewRating rating, DateTimeOffset reviewedAtUtc);

    /// <summary>
    /// Deterministically replays factual review history over an initial scheduling projection.
    /// </summary>
    Fsrs6ScheduleProjection Replay(Fsrs6ScheduleProjection initialProjection, IEnumerable<Fsrs6ReviewFact> reviewFacts);
}