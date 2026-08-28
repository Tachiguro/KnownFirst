using KnownFirst.Core.Learning;

namespace KnownFirst.Core.Learning.Fsrs6;

/// <summary>
/// Deterministic, platform-neutral FSRS-6 scheduling transitions.
/// </summary>
public sealed class Fsrs6Scheduler
{
    private const int LearningStepMinutes = 10;
    private const int HardLearningStepMinutes = 15;

    private readonly Fsrs6Parameters _parameters;
    private readonly double _factor;

    public Fsrs6Scheduler(Fsrs6Parameters? parameters = null)
    {
        _parameters = parameters ?? Fsrs6Parameters.Default;
        var decay = _parameters.Weights[20];
        _factor = Math.Pow(0.9, -1.0 / decay) - 1.0;
        EnsureFinitePositive(_factor, "The FSRS forgetting-curve factor is invalid.");
    }

    public Fsrs6Card Schedule(
        Fsrs6Card currentCard,
        ReviewRating rating,
        DateTimeOffset reviewedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(currentCard);
        _ = new Fsrs6ReviewEvent(reviewedAtUtc, rating);

        if (currentCard.LastReviewedAtUtc is { } lastReviewedAtUtc
            && reviewedAtUtc < lastReviewedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewedAtUtc),
                reviewedAtUtc,
                "Review timestamp cannot precede the card's last review timestamp.");
        }

        var grade = ToGrade(rating);
        double stability;
        double difficulty;

        if (currentCard.State == Fsrs6CardState.New)
        {
            stability = InitialStability(grade);
            difficulty = InitialDifficulty(grade, clamp: true);
        }
        else
        {
            var priorStability = currentCard.Stability!.Value;
            var priorDifficulty = currentCard.Difficulty!.Value;
            var elapsedDays = ElapsedWholeDays(currentCard.LastReviewedAtUtc!.Value, reviewedAtUtc);

            stability = elapsedDays == 0
                ? SameDayStability(priorStability, grade)
                : DelayedStability(priorStability, priorDifficulty, elapsedDays, rating);
            difficulty = NextDifficulty(priorDifficulty, grade);
        }

        EnsureFinitePositive(stability, "The computed FSRS stability is invalid.");
        EnsureFinite(difficulty, "The computed FSRS difficulty is invalid.");

        return CreateScheduledCard(
            currentCard.State,
            rating,
            stability,
            difficulty,
            reviewedAtUtc);
    }

    private Fsrs6Card CreateScheduledCard(
        Fsrs6CardState priorState,
        ReviewRating rating,
        double stability,
        double difficulty,
        DateTimeOffset reviewedAtUtc)
    {
        if (priorState == Fsrs6CardState.Review && rating == ReviewRating.Again)
        {
            return Fsrs6Card.Relearning(
                stability,
                difficulty,
                reviewedAtUtc,
                dueAtUtc: AddMinutesChecked(reviewedAtUtc, LearningStepMinutes));
        }

        if (priorState != Fsrs6CardState.Review && rating == ReviewRating.Again)
        {
            var dueAtUtc = AddMinutesChecked(reviewedAtUtc, LearningStepMinutes);
            return priorState == Fsrs6CardState.Relearning
                ? Fsrs6Card.Relearning(stability, difficulty, reviewedAtUtc, dueAtUtc: dueAtUtc)
                : Fsrs6Card.Learning(stability, difficulty, reviewedAtUtc, dueAtUtc: dueAtUtc);
        }

        if (priorState != Fsrs6CardState.Review && rating == ReviewRating.Hard)
        {
            var dueAtUtc = AddMinutesChecked(reviewedAtUtc, HardLearningStepMinutes);
            return priorState == Fsrs6CardState.Relearning
                ? Fsrs6Card.Relearning(stability, difficulty, reviewedAtUtc, dueAtUtc: dueAtUtc)
                : Fsrs6Card.Learning(stability, difficulty, reviewedAtUtc, dueAtUtc: dueAtUtc);
        }

        var intervalDays = CalculateIntervalDays(stability);
        var reviewDueAtUtc = AddDaysChecked(reviewedAtUtc, intervalDays);
        return Fsrs6Card.Review(stability, difficulty, reviewedAtUtc, reviewDueAtUtc);
    }

    private double InitialStability(int grade)
    {
        var stability = _parameters.Weights[grade - 1];
        EnsureFinite(stability, "The computed initial FSRS stability is invalid.");
        return Math.Max(Fsrs6Card.MinimumStability, stability);
    }

    private double InitialDifficulty(int grade, bool clamp)
    {
        var weights = _parameters.Weights;
        var difficulty = weights[4] - Math.Exp(weights[5] * (grade - 1)) + 1.0;
        EnsureFinite(difficulty, "The computed initial FSRS difficulty is invalid.");
        return clamp
            ? Math.Clamp(difficulty, Fsrs6Card.MinimumDifficulty, Fsrs6Card.MaximumDifficulty)
            : difficulty;
    }

    private double SameDayStability(double stability, int grade)
    {
        var weights = _parameters.Weights;
        var increase = Math.Exp(weights[17] * (grade - 3 + weights[18]))
            * Math.Pow(stability, -weights[19]);
        EnsureFinite(increase, "The computed same-day FSRS stability increase is invalid.");

        if (grade >= ToGrade(ReviewRating.Hard))
        {
            increase = Math.Max(increase, 1.0);
        }

        var nextStability = stability * increase;
        EnsureFinite(nextStability, "The computed same-day FSRS stability is invalid.");
        return Math.Max(Fsrs6Card.MinimumStability, nextStability);
    }

    private double DelayedStability(
        double stability,
        double difficulty,
        int elapsedDays,
        ReviewRating rating)
    {
        var retrievability = Retrievability(stability, elapsedDays);
        var nextStability = rating == ReviewRating.Again
            ? ForgetStability(stability, difficulty, retrievability)
            : RecallStability(stability, difficulty, retrievability, rating);

        EnsureFinite(nextStability, "The computed delayed FSRS stability is invalid.");
        return Math.Max(Fsrs6Card.MinimumStability, nextStability);
    }

    private double Retrievability(double stability, int elapsedDays)
    {
        var retrievability = Math.Pow(
            1.0 + _factor * elapsedDays / stability,
            -_parameters.Weights[20]);
        EnsureFinite(retrievability, "The computed FSRS retrievability is invalid.");
        if (retrievability < 0.0 || retrievability > 1.0)
        {
            throw new InvalidOperationException("The computed FSRS retrievability is outside [0, 1].");
        }

        return retrievability;
    }

    private double RecallStability(
        double stability,
        double difficulty,
        double retrievability,
        ReviewRating rating)
    {
        var weights = _parameters.Weights;
        var hardPenalty = rating == ReviewRating.Hard ? weights[15] : 1.0;
        var easyBonus = rating == ReviewRating.Easy ? weights[16] : 1.0;
        var nextStability = stability * (1.0
            + Math.Exp(weights[8])
            * (11.0 - difficulty)
            * Math.Pow(stability, -weights[9])
            * (Math.Exp((1.0 - retrievability) * weights[10]) - 1.0)
            * hardPenalty
            * easyBonus);

        EnsureFinite(nextStability, "The computed FSRS recall stability is invalid.");
        return nextStability;
    }

    private double ForgetStability(
        double stability,
        double difficulty,
        double retrievability)
    {
        var weights = _parameters.Weights;
        var longTerm = weights[11]
            * Math.Pow(difficulty, -weights[12])
            * (Math.Pow(stability + 1.0, weights[13]) - 1.0)
            * Math.Exp((1.0 - retrievability) * weights[14]);
        var shortTermUpperBound = stability / Math.Exp(weights[17] * weights[18]);

        EnsureFinite(longTerm, "The computed FSRS forget stability is invalid.");
        EnsureFinite(shortTermUpperBound, "The computed FSRS forget stability upper bound is invalid.");
        return Math.Min(longTerm, shortTermUpperBound);
    }

    private double NextDifficulty(double difficulty, int grade)
    {
        var weights = _parameters.Weights;
        var initialEasy = InitialDifficulty(ToGrade(ReviewRating.Easy), clamp: false);
        var delta = -weights[6] * (grade - 3);
        var damped = difficulty + (10.0 - difficulty) * delta / 9.0;
        var nextDifficulty = weights[7] * initialEasy + (1.0 - weights[7]) * damped;

        EnsureFinite(nextDifficulty, "The computed FSRS difficulty is invalid.");
        return Math.Clamp(
            nextDifficulty,
            Fsrs6Card.MinimumDifficulty,
            Fsrs6Card.MaximumDifficulty);
    }

    private int CalculateIntervalDays(double stability)
    {
        var decay = -_parameters.Weights[20];
        var interval = stability / _factor
            * (Math.Pow(_parameters.DesiredRetention, 1.0 / decay) - 1.0);
        EnsureFinite(interval, "The computed FSRS interval is invalid.");

        var rounded = Math.Round(interval, MidpointRounding.ToEven);
        EnsureFinite(rounded, "The rounded FSRS interval is invalid.");
        var clamped = Math.Clamp(rounded, 1.0, _parameters.MaximumIntervalDays);
        return checked((int)clamped);
    }

    private static int ElapsedWholeDays(DateTimeOffset lastReviewedAtUtc, DateTimeOffset reviewedAtUtc)
    {
        var totalDays = (reviewedAtUtc - lastReviewedAtUtc).TotalDays;
        if (!double.IsFinite(totalDays) || totalDays < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewedAtUtc),
                reviewedAtUtc,
                "Review timestamp cannot precede the card's last review timestamp.");
        }

        return checked((int)Math.Max(0.0, Math.Floor(totalDays)));
    }

    private static DateTimeOffset AddMinutesChecked(DateTimeOffset timestamp, int minutes) =>
        timestamp.AddMinutes(minutes);

    private static DateTimeOffset AddDaysChecked(DateTimeOffset timestamp, int days) =>
        timestamp.AddDays(days);

    private static void EnsureFinite(double value, string message)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureFinitePositive(double value, string message)
    {
        EnsureFinite(value, message);
        if (value <= 0.0)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int ToGrade(ReviewRating rating) => rating switch
    {
        ReviewRating.Again => 1,
        ReviewRating.Hard => 2,
        ReviewRating.Good => 3,
        ReviewRating.Easy => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, "Review rating is invalid.")
    };
}
