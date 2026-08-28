using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6InitialTransitionTests
{
    private static readonly DateTimeOffset ReviewTime = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(ReviewRating.Again, Fsrs6CardState.Learning, 10)]
    [DataRow(ReviewRating.Hard, Fsrs6CardState.Learning, 15)]
    public void NewCard_AgainOrHard_EntersLearningAtApprovedStep(
        ReviewRating rating,
        Fsrs6CardState expectedState,
        int expectedMinutes)
    {
        var result = new Fsrs6Scheduler().Schedule(Fsrs6Card.New(), rating, ReviewTime);

        Assert.AreEqual(expectedState, result.State);
        Assert.AreEqual(0, result.StepIndex);
        Assert.AreEqual(ReviewTime.AddMinutes(expectedMinutes), result.DueAtUtc);
        Assert.AreEqual(ReviewTime, result.LastReviewedAtUtc);
    }

    [TestMethod]
    [DataRow(ReviewRating.Good)]
    [DataRow(ReviewRating.Easy)]
    public void NewCard_GoodOrEasy_GraduatesToReviewWithCalculatedInterval(ReviewRating rating)
    {
        var result = new Fsrs6Scheduler().Schedule(Fsrs6Card.New(), rating, ReviewTime);
        var expectedDays = Math.Clamp(
            (int)Math.Round(ExpectedInitialStability(rating), MidpointRounding.ToEven),
            1,
            Fsrs6Parameters.Default.MaximumIntervalDays);

        Assert.AreEqual(Fsrs6CardState.Review, result.State);
        Assert.IsNull(result.StepIndex);
        Assert.AreEqual(ReviewTime.AddDays(expectedDays), result.DueAtUtc);
    }

    [TestMethod]
    [DataRow(ReviewRating.Again)]
    [DataRow(ReviewRating.Hard)]
    [DataRow(ReviewRating.Good)]
    [DataRow(ReviewRating.Easy)]
    public void NewCard_InitialMemoryStateMatchesPublishedEquations(ReviewRating rating)
    {
        var result = new Fsrs6Scheduler().Schedule(Fsrs6Card.New(), rating, ReviewTime);

        Assert.AreEqual(ExpectedInitialStability(rating), result.Stability!.Value, 1e-12);
        Assert.AreEqual(ExpectedInitialDifficulty(rating), result.Difficulty!.Value, 1e-12);
    }

    [TestMethod]
    public void InitialMemoryState_DiffersMonotonicallyByRating()
    {
        var scheduler = new Fsrs6Scheduler();
        var again = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Again, ReviewTime);
        var hard = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Hard, ReviewTime);
        var good = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Good, ReviewTime);
        var easy = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Easy, ReviewTime);

        Assert.IsTrue(again.Stability < hard.Stability && hard.Stability < good.Stability && good.Stability < easy.Stability);
        Assert.IsTrue(again.Difficulty > hard.Difficulty && hard.Difficulty > good.Difficulty && good.Difficulty > easy.Difficulty);
    }

    [TestMethod]
    public void Schedule_SameInput_IsDeterministicWithoutFuzz()
    {
        var scheduler = new Fsrs6Scheduler();

        var first = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Easy, ReviewTime);
        var second = scheduler.Schedule(Fsrs6Card.New(), ReviewRating.Easy, ReviewTime);

        Assert.AreEqual(first, second);
    }

    private static double ExpectedInitialStability(ReviewRating rating)
    {
        var grade = Grade(rating);
        return Fsrs6Parameters.Default.Weights[grade - 1];
    }

    private static double ExpectedInitialDifficulty(ReviewRating rating)
    {
        var grade = Grade(rating);
        var weights = Fsrs6Parameters.Default.Weights;
        return Math.Clamp(
            weights[4] - Math.Exp(weights[5] * (grade - 1)) + 1,
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
