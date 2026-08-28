namespace KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Platform-neutral immutable FSRS-6 card scheduling state.
/// </summary>
public sealed record Fsrs6Card
{
    public const double MinimumStability = 0.001;
    public const double MinimumDifficulty = 1.0;
    public const double MaximumDifficulty = 10.0;

    public Fsrs6CardState State { get; }
    public double? Stability { get; }
    public double? Difficulty { get; }
    public DateTimeOffset? LastReviewedAtUtc { get; }
    public int? StepIndex { get; }
    public DateTimeOffset? DueAtUtc { get; }

    public Fsrs6Card(
        Fsrs6CardState state,
        double? stability,
        double? difficulty,
        DateTimeOffset? lastReviewedAtUtc,
        int? stepIndex = null,
        DateTimeOffset? dueAtUtc = null)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Undefined card state.");
        }

        if (dueAtUtc.HasValue && dueAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Due timestamp must be in UTC (offset zero).", nameof(dueAtUtc));
        }

        switch (state)
        {
            case Fsrs6CardState.New:
                if (stability.HasValue)
                {
                    throw new ArgumentException("New cards must have null stability.", nameof(stability));
                }
                if (difficulty.HasValue)
                {
                    throw new ArgumentException("New cards must have null difficulty.", nameof(difficulty));
                }
                if (lastReviewedAtUtc.HasValue)
                {
                    throw new ArgumentException("New cards must have null last reviewed timestamp.", nameof(lastReviewedAtUtc));
                }
                if (stepIndex.HasValue)
                {
                    throw new ArgumentException("New cards must have null step index.", nameof(stepIndex));
                }
                break;

            case Fsrs6CardState.Learning:
                ValidateStability(stability);
                ValidateDifficulty(difficulty);
                ValidateLastReviewedAtUtc(lastReviewedAtUtc);
                if (!stepIndex.HasValue || stepIndex.Value != 0)
                {
                    throw new ArgumentException("Learning cards must have step index equal to 0.", nameof(stepIndex));
                }
                break;

            case Fsrs6CardState.Review:
                ValidateStability(stability);
                ValidateDifficulty(difficulty);
                ValidateLastReviewedAtUtc(lastReviewedAtUtc);
                if (stepIndex.HasValue)
                {
                    throw new ArgumentException("Review cards must have null step index.", nameof(stepIndex));
                }
                break;

            case Fsrs6CardState.Relearning:
                ValidateStability(stability);
                ValidateDifficulty(difficulty);
                ValidateLastReviewedAtUtc(lastReviewedAtUtc);
                if (!stepIndex.HasValue || stepIndex.Value != 0)
                {
                    throw new ArgumentException("Relearning cards must have step index equal to 0.", nameof(stepIndex));
                }
                break;
        }

        State = state;
        Stability = stability;
        Difficulty = difficulty;
        LastReviewedAtUtc = lastReviewedAtUtc;
        StepIndex = stepIndex;
        DueAtUtc = dueAtUtc;
    }

    public static Fsrs6Card New(DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.New, null, null, null, null, dueAtUtc);

    public static Fsrs6Card Learning(
        double stability,
        double difficulty,
        DateTimeOffset lastReviewedAtUtc,
        int stepIndex = 0,
        DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.Learning, stability, difficulty, lastReviewedAtUtc, stepIndex, dueAtUtc);

    public static Fsrs6Card Review(
        double stability,
        double difficulty,
        DateTimeOffset lastReviewedAtUtc,
        DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.Review, stability, difficulty, lastReviewedAtUtc, null, dueAtUtc);

    public static Fsrs6Card Relearning(
        double stability,
        double difficulty,
        DateTimeOffset lastReviewedAtUtc,
        int stepIndex = 0,
        DateTimeOffset? dueAtUtc = null) =>
        new(Fsrs6CardState.Relearning, stability, difficulty, lastReviewedAtUtc, stepIndex, dueAtUtc);

    private static void ValidateStability(double? stability)
    {
        if (!stability.HasValue)
        {
            throw new ArgumentException("Stability must not be null for active scheduling states.", nameof(stability));
        }
        if (!double.IsFinite(stability.Value))
        {
            throw new ArgumentException("Stability must be a finite number.", nameof(stability));
        }
        if (stability.Value < MinimumStability)
        {
            throw new ArgumentOutOfRangeException(nameof(stability), stability.Value, $"Stability must be at least {MinimumStability}.");
        }
    }

    private static void ValidateDifficulty(double? difficulty)
    {
        if (!difficulty.HasValue)
        {
            throw new ArgumentException("Difficulty must not be null for active scheduling states.", nameof(difficulty));
        }
        if (!double.IsFinite(difficulty.Value))
        {
            throw new ArgumentException("Difficulty must be a finite number.", nameof(difficulty));
        }
        if (difficulty.Value < MinimumDifficulty || difficulty.Value > MaximumDifficulty)
        {
            throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty.Value, $"Difficulty must be between {MinimumDifficulty} and {MaximumDifficulty}.");
        }
    }

    private static void ValidateLastReviewedAtUtc(DateTimeOffset? lastReviewedAtUtc)
    {
        if (!lastReviewedAtUtc.HasValue)
        {
            throw new ArgumentException("Last reviewed timestamp must not be null for active scheduling states.", nameof(lastReviewedAtUtc));
        }
        if (lastReviewedAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Last reviewed timestamp must be in UTC (offset zero).", nameof(lastReviewedAtUtc));
        }
    }
}
