namespace KnownFirst.Application.Learning;

using KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Persistence-neutral scheduling projection representing card scheduling state.
/// </summary>
public sealed record Fsrs6ScheduleProjection
{
    public Fsrs6CardState State { get; }
    public double? Stability { get; }
    public double? Difficulty { get; }
    public DateTimeOffset? LastReviewedAtUtc { get; }
    public int? StepIndex { get; }
    public DateTimeOffset? DueAtUtc { get; }

    public Fsrs6ScheduleProjection(
        Fsrs6CardState state,
        double? stability = null,
        double? difficulty = null,
        DateTimeOffset? lastReviewedAtUtc = null,
        int? stepIndex = null,
        DateTimeOffset? dueAtUtc = null)
    {
        State = state;
        Stability = stability;
        Difficulty = difficulty;
        LastReviewedAtUtc = lastReviewedAtUtc;
        StepIndex = stepIndex;
        DueAtUtc = dueAtUtc;

        Validate();
    }

    public static Fsrs6ScheduleProjection New(DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.New, null, null, null, null, dueAtUtc);

    public static Fsrs6ScheduleProjection Learning(
        double stability,
        double difficulty,
        DateTimeOffset lastReviewedAtUtc,
        int stepIndex = 0,
        DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.Learning, stability, difficulty, lastReviewedAtUtc, stepIndex, dueAtUtc);

    public static Fsrs6ScheduleProjection Review(
        double stability,
        double difficulty,
        DateTimeOffset lastReviewedAtUtc,
        DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.Review, stability, difficulty, lastReviewedAtUtc, null, dueAtUtc);

    public static Fsrs6ScheduleProjection Relearning(
        double stability,
        double difficulty,
        DateTimeOffset lastReviewedAtUtc,
        int stepIndex = 0,
        DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.Relearning, stability, difficulty, lastReviewedAtUtc, stepIndex, dueAtUtc);

    public static Fsrs6ScheduleProjection FromCard(Fsrs6Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new(
            card.State,
            card.Stability,
            card.Difficulty,
            card.LastReviewedAtUtc,
            card.StepIndex,
            card.DueAtUtc);
    }

    public void Validate()
    {
        try
        {
            _ = new Fsrs6Card(State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new LearningScheduleCorruptionException(
                $"Corrupt schedule projection: invalid FSRS-6 card state ({ex.Message})",
                ex);
        }
    }

    public Fsrs6Card ToCard()
    {
        Validate();
        return new Fsrs6Card(State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc);
    }
}