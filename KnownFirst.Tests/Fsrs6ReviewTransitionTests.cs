using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6ReviewTransitionTests
{
    private static readonly DateTimeOffset LastReview = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DelayedReview_Again_UsesForgetEquationAndEntersRelearning()
    {
        var reviewedAt = LastReview.AddDays(12);
        var card = Fsrs6Card.Review(10.0, 5.0, LastReview);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Again, reviewedAt);

        Assert.AreEqual(ExpectedForgetStability(10.0, 5.0, 12), result.Stability!.Value, 1e-12);
        Assert.AreEqual(ExpectedDifficulty(5.0, ReviewRating.Again), result.Difficulty!.Value, 1e-12);
        Assert.AreEqual(Fsrs6CardState.Relearning, result.State);
        Assert.AreEqual(0, result.StepIndex);
        Assert.AreEqual(reviewedAt.AddMinutes(10), result.DueAtUtc);
    }

    [TestMethod]
    [DataRow(ReviewRating.Hard)]
    [DataRow(ReviewRating.Good)]
    [DataRow(ReviewRating.Easy)]
    public void DelayedReview_Success_UsesPublishedRecallEquationAndRemainsReview(ReviewRating rating)
    {
        var reviewedAt = LastReview.AddDays(12);
        var card = Fsrs6Card.Review(10.0, 5.0, LastReview);

        var result = new Fsrs6Scheduler().Schedule(card, rating, reviewedAt);

        Assert.AreEqual(ExpectedRecallStability(10.0, 5.0, 12, rating), result.Stability!.Value, 1e-12);
        Assert.AreEqual(ExpectedDifficulty(5.0, rating), result.Difficulty!.Value, 1e-12);
        Assert.AreEqual(Fsrs6CardState.Review, result.State);
        Assert.IsNull(result.StepIndex);
    }

    [TestMethod]
    public void DelayedReview_HardGoodEasy_ProduceOrderedStabilityAndSchedules()
    {
        var scheduler = new Fsrs6Scheduler();
        var card = Fsrs6Card.Review(10.0, 5.0, LastReview);
        var reviewedAt = LastReview.AddDays(12);

        var hard = scheduler.Schedule(card, ReviewRating.Hard, reviewedAt);
        var good = scheduler.Schedule(card, ReviewRating.Good, reviewedAt);
        var easy = scheduler.Schedule(card, ReviewRating.Easy, reviewedAt);

        Assert.IsTrue(hard.Stability < good.Stability && good.Stability < easy.Stability);
        Assert.IsTrue(hard.DueAtUtc < good.DueAtUtc && good.DueAtUtc < easy.DueAtUtc);
    }

    [TestMethod]
    public void DelayedReview_RetrievabilityVariesWithElapsedWholeDays()
    {
        var scheduler = new Fsrs6Scheduler();
        var card = Fsrs6Card.Review(10.0, 5.0, LastReview);

        var earlier = scheduler.Schedule(card, ReviewRating.Good, LastReview.AddDays(5));
        var later = scheduler.Schedule(card, ReviewRating.Good, LastReview.AddDays(30));

        Assert.IsGreaterThan(earlier.Stability!.Value, later.Stability!.Value);
    }

    [TestMethod]
    public void Interval_DefaultRetentionMakesStabilityTheUnroundedInterval()
    {
        var card = Fsrs6Card.Review(10.0, 5.0, LastReview);
        var reviewedAt = LastReview.AddHours(1);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, reviewedAt);

        Assert.AreEqual(reviewedAt.AddDays(10), result.DueAtUtc);
    }

    [TestMethod]
    public void Interval_MidpointUsesToEvenRounding()
    {
        var weights = Fsrs6Parameters.Default.Weights.ToArray();
        weights[2] = 2.5;
        var scheduler = new Fsrs6Scheduler(new Fsrs6Parameters(weights));

        var result = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Good, LastReview);

        Assert.AreEqual(LastReview.AddDays(2), result.DueAtUtc);
    }

    [TestMethod]
    public void Interval_IsClampedToMinimumOneDay()
    {
        var card = Fsrs6Card.Review(Fsrs6Card.MinimumStability, 5.0, LastReview);
        var reviewedAt = LastReview.AddMinutes(1);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, reviewedAt);

        Assert.AreEqual(reviewedAt.AddDays(1), result.DueAtUtc);
    }

    [TestMethod]
    public void Interval_IsClampedToMaximum36500Days()
    {
        var card = Fsrs6Card.Review(1e100, 5.0, LastReview);
        var reviewedAt = LastReview.AddMinutes(1);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Easy, reviewedAt);

        Assert.AreEqual(reviewedAt.AddDays(Fsrs6Parameters.Default.MaximumIntervalDays), result.DueAtUtc);
    }

    [TestMethod]
    public void DifficultyAndStability_RemainWithinValidatedBounds()
    {
        var scheduler = new Fsrs6Scheduler();
        var hardCard = Fsrs6Card.Review(Fsrs6Card.MinimumStability, Fsrs6Card.MaximumDifficulty, LastReview);
        var easyCard = Fsrs6Card.Review(Fsrs6Card.MinimumStability, Fsrs6Card.MinimumDifficulty, LastReview);

        var again = scheduler.Schedule(hardCard, ReviewRating.Again, LastReview.AddDays(1));
        var easy = scheduler.Schedule(easyCard, ReviewRating.Easy, LastReview.AddDays(1));

        Assert.IsGreaterThanOrEqualTo(Fsrs6Card.MinimumStability, again.Stability!.Value);
        Assert.IsInRange(Fsrs6Card.MinimumDifficulty, Fsrs6Card.MaximumDifficulty, again.Difficulty!.Value);
        Assert.IsInRange(Fsrs6Card.MinimumDifficulty, Fsrs6Card.MaximumDifficulty, easy.Difficulty!.Value);
    }

    [TestMethod]
    public void Schedule_FailsClosedWhenFormulaProducesNonFiniteState()
    {
        var weights = Fsrs6Parameters.Default.Weights.ToArray();
        weights[8] = double.MaxValue;
        var scheduler = new Fsrs6Scheduler(new Fsrs6Parameters(weights));
        var card = Fsrs6Card.Review(10.0, 5.0, LastReview);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            scheduler.Schedule(card, ReviewRating.Good, LastReview.AddDays(12)));
    }

    [TestMethod]
    public void Schedule_FailsClosedWhenDueDateCannotBeRepresented()
    {
        var reviewedAt = new DateTimeOffset(9999, 12, 31, 23, 59, 0, TimeSpan.Zero);
        var card = Fsrs6Card.Review(2.5, 5.0, reviewedAt.AddDays(-1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, reviewedAt));
    }

    private static double ExpectedRetrievability(double stability, int elapsedDays)
    {
        var weights = Fsrs6Parameters.Default.Weights;
        var factor = Math.Pow(0.9, -1.0 / weights[20]) - 1.0;
        return Math.Pow(1.0 + factor * elapsedDays / stability, -weights[20]);
    }

    private static double ExpectedRecallStability(double stability, double difficulty, int elapsedDays, ReviewRating rating)
    {
        var weights = Fsrs6Parameters.Default.Weights;
        var retrievability = ExpectedRetrievability(stability, elapsedDays);
        var hardPenalty = rating == ReviewRating.Hard ? weights[15] : 1.0;
        var easyBonus = rating == ReviewRating.Easy ? weights[16] : 1.0;
        return Math.Max(
            Fsrs6Card.MinimumStability,
            stability * (1.0
                + Math.Exp(weights[8])
                * (11.0 - difficulty)
                * Math.Pow(stability, -weights[9])
                * (Math.Exp((1.0 - retrievability) * weights[10]) - 1.0)
                * hardPenalty
                * easyBonus));
    }

    private static double ExpectedForgetStability(double stability, double difficulty, int elapsedDays)
    {
        var weights = Fsrs6Parameters.Default.Weights;
        var retrievability = ExpectedRetrievability(stability, elapsedDays);
        var longTerm = weights[11]
            * Math.Pow(difficulty, -weights[12])
            * (Math.Pow(stability + 1.0, weights[13]) - 1.0)
            * Math.Exp((1.0 - retrievability) * weights[14]);
        var shortTermUpperBound = stability / Math.Exp(weights[17] * weights[18]);
        return Math.Max(Fsrs6Card.MinimumStability, Math.Min(longTerm, shortTermUpperBound));
    }

    private static double ExpectedDifficulty(double difficulty, ReviewRating rating)
    {
        var weights = Fsrs6Parameters.Default.Weights;
        var initialEasy = weights[4] - Math.Exp(weights[5] * 3.0) + 1.0;
        var delta = -weights[6] * (Grade(rating) - 3);
        var damped = difficulty + (10.0 - difficulty) * delta / 9.0;
        return Math.Clamp(
            weights[7] * initialEasy + (1.0 - weights[7]) * damped,
            Fsrs6Card.MinimumDifficulty,
            Fsrs6Card.MaximumDifficulty);
    }

    private static int Grade(ReviewRating rating) => rating switch
    {
        ReviewRating.Again => 1,
        ReviewRating.Hard => 2,
        ReviewRating.Good => 3,
        ReviewRating.Easy => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(rating))
    };
}
