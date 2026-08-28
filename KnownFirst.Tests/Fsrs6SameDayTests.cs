using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6SameDayTests
{
    private static readonly DateTimeOffset LastReview = new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Learning_UnderTwentyFourHours_UsesElapsedDayZeroEquation()
    {
        var card = Fsrs6Card.Learning(2.5, 5.0, LastReview);
        var reviewedAt = LastReview.AddHours(23).AddMinutes(59);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, reviewedAt);

        Assert.AreEqual(ExpectedSameDayStability(2.5, ReviewRating.Good), result.Stability!.Value, 1e-12);
        Assert.AreEqual(ExpectedDifficulty(5.0, ReviewRating.Good), result.Difficulty!.Value, 1e-12);
        Assert.AreEqual(Fsrs6CardState.Review, result.State);
    }

    [TestMethod]
    public void SameDay_AcrossUtcMidnight_StillUsesElapsedWholeDays()
    {
        var lastReview = new DateTimeOffset(2026, 8, 28, 23, 59, 0, TimeSpan.Zero);
        var card = Fsrs6Card.Learning(2.5, 5.0, lastReview);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, lastReview.AddMinutes(2));

        Assert.AreEqual(ExpectedSameDayStability(2.5, ReviewRating.Good), result.Stability!.Value, 1e-12);
    }

    [TestMethod]
    public void ExactTwentyFourHours_UsesDelayedReviewEquationDeterministically()
    {
        var card = Fsrs6Card.Learning(2.5, 5.0, LastReview);
        var scheduler = new Fsrs6Scheduler();

        var beforeBoundary = scheduler.Schedule(card, ReviewRating.Good, LastReview.AddDays(1).AddTicks(-1));
        var atBoundary = scheduler.Schedule(card, ReviewRating.Good, LastReview.AddDays(1));

        Assert.AreNotEqual(beforeBoundary.Stability, atBoundary.Stability);
        Assert.AreEqual(ExpectedSameDayStability(2.5, ReviewRating.Good), beforeBoundary.Stability!.Value, 1e-12);
        Assert.AreEqual(ExpectedDelayedStability(2.5, 5.0, 1, ReviewRating.Good), atBoundary.Stability!.Value, 1e-12);
    }

    [TestMethod]
    public void Learning_Hard_IsSuccessfulAndRemainsLearningForFifteenMinutes()
    {
        var card = Fsrs6Card.Learning(10.0, 5.0, LastReview);

        var result = new Fsrs6Scheduler().Schedule(card, ReviewRating.Hard, LastReview.AddMinutes(5));

        Assert.AreEqual(Fsrs6CardState.Learning, result.State);
        Assert.AreEqual(0, result.StepIndex);
        Assert.AreEqual(LastReview.AddMinutes(20), result.DueAtUtc);
        Assert.IsGreaterThanOrEqualTo(10.0, result.Stability!.Value);
    }

    [TestMethod]
    [DataRow(ReviewRating.Good)]
    [DataRow(ReviewRating.Easy)]
    public void Learning_GoodOrEasy_GraduatesToReview(ReviewRating rating)
    {
        var reviewedAt = LastReview.AddMinutes(5);
        var card = Fsrs6Card.Learning(2.5, 5.0, LastReview);

        var result = new Fsrs6Scheduler().Schedule(card, rating, reviewedAt);

        Assert.AreEqual(Fsrs6CardState.Review, result.State);
        Assert.IsNull(result.StepIndex);
        Assert.IsGreaterThan(reviewedAt, result.DueAtUtc!.Value);
    }

    [TestMethod]
    [DataRow(ReviewRating.Again, Fsrs6CardState.Relearning, 10)]
    [DataRow(ReviewRating.Hard, Fsrs6CardState.Relearning, 15)]
    [DataRow(ReviewRating.Good, Fsrs6CardState.Review, 0)]
    [DataRow(ReviewRating.Easy, Fsrs6CardState.Review, 0)]
    public void Relearning_RatingsFollowApprovedOneStepBehavior(
        ReviewRating rating,
        Fsrs6CardState expectedState,
        int expectedMinutes)
    {
        var reviewedAt = LastReview.AddMinutes(5);
        var card = Fsrs6Card.Relearning(2.5, 5.0, LastReview);

        var result = new Fsrs6Scheduler().Schedule(card, rating, reviewedAt);

        Assert.AreEqual(expectedState, result.State);
        Assert.AreEqual(expectedState == Fsrs6CardState.Review ? null : 0, result.StepIndex);
        if (expectedMinutes > 0)
        {
            Assert.AreEqual(reviewedAt.AddMinutes(expectedMinutes), result.DueAtUtc);
        }
        else
        {
            Assert.IsGreaterThan(reviewedAt, result.DueAtUtc!.Value);
        }
    }

    [TestMethod]
    public void RepeatedSameDayLearningAndRelearning_AreDeterministic()
    {
        var scheduler = new Fsrs6Scheduler();
        var learning = Fsrs6Card.Learning(2.5, 5.0, LastReview);
        var relearning = Fsrs6Card.Relearning(2.5, 5.0, LastReview);

        var learningFirst = scheduler.Schedule(learning, ReviewRating.Again, LastReview.AddMinutes(1));
        var learningSecond = scheduler.Schedule(learningFirst, ReviewRating.Hard, LastReview.AddMinutes(2));
        var relearningFirst = scheduler.Schedule(relearning, ReviewRating.Again, LastReview.AddMinutes(1));
        var relearningSecond = scheduler.Schedule(relearningFirst, ReviewRating.Hard, LastReview.AddMinutes(2));

        Assert.AreEqual(learningSecond.Stability, relearningSecond.Stability);
        Assert.AreEqual(learningSecond.Difficulty, relearningSecond.Difficulty);
        Assert.AreEqual(Fsrs6CardState.Learning, learningSecond.State);
        Assert.AreEqual(Fsrs6CardState.Relearning, relearningSecond.State);
    }

    [TestMethod]
    public void SuccessfulSameDayRatings_DoNotReduceStability()
    {
        var scheduler = new Fsrs6Scheduler();
        var card = Fsrs6Card.Learning(50.0, 5.0, LastReview);

        foreach (var rating in new[] { ReviewRating.Hard, ReviewRating.Good, ReviewRating.Easy })
        {
            var result = scheduler.Schedule(card, rating, LastReview.AddMinutes(1));
            Assert.IsGreaterThanOrEqualTo(card.Stability!.Value, result.Stability!.Value, rating.ToString());
        }
    }

    [TestMethod]
    public void Schedule_RejectsReviewBeforeLastReview()
    {
        var card = Fsrs6Card.Review(2.5, 5.0, LastReview);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, LastReview.AddTicks(-1)));
    }

    [TestMethod]
    public void Schedule_RejectsNonUtcReviewTimestamp()
    {
        var card = Fsrs6Card.Review(2.5, 5.0, LastReview);
        var nonUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.ThrowsExactly<ArgumentException>(() =>
            new Fsrs6Scheduler().Schedule(card, ReviewRating.Good, nonUtc));
    }

    private static double ExpectedSameDayStability(double stability, ReviewRating rating)
    {
        var weights = Fsrs6Parameters.Default.Weights;
        var increase = Math.Exp(weights[17] * (Grade(rating) - 3 + weights[18])) * Math.Pow(stability, -weights[19]);
        if (rating is ReviewRating.Hard or ReviewRating.Good or ReviewRating.Easy)
        {
            increase = Math.Max(increase, 1.0);
        }

        return Math.Max(Fsrs6Card.MinimumStability, stability * increase);
    }

    private static double ExpectedDelayedStability(double stability, double difficulty, int elapsedDays, ReviewRating rating)
    {
        var weights = Fsrs6Parameters.Default.Weights;
        var factor = Math.Pow(0.9, -1.0 / weights[20]) - 1.0;
        var retrievability = Math.Pow(1.0 + factor * elapsedDays / stability, -weights[20]);
        var penalty = rating == ReviewRating.Hard ? weights[15] : 1.0;
        var bonus = rating == ReviewRating.Easy ? weights[16] : 1.0;
        return stability * (1.0
            + Math.Exp(weights[8])
            * (11.0 - difficulty)
            * Math.Pow(stability, -weights[9])
            * (Math.Exp((1.0 - retrievability) * weights[10]) - 1.0)
            * penalty
            * bonus);
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
