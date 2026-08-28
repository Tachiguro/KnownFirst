using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6OracleVectorTests
{
    private const double AbsoluteTolerance = 1e-12;
    private const double RelativeTolerance = 1e-10;

    [TestMethod]
    public void Scheduler_MatchesPinnedOracleAtEveryTransition()
    {
        var scheduler = new Fsrs6Scheduler();

        foreach (var history in Fsrs6OracleVectors.All)
        {
            var actual = history.InitialCard;
            DateTimeOffset? priorTimestamp = actual.LastReviewedAtUtc;

            for (int i = 0; i < history.Steps.Count; i++)
            {
                var step = history.Steps[i];
                if (priorTimestamp.HasValue)
                {
                    Assert.IsGreaterThanOrEqualTo(
                        priorTimestamp.Value,
                        step.Event.ReviewedAtUtc,
                        $"{history.Name} step {i}: oracle chronology must be non-decreasing.");
                }

                actual = scheduler.Schedule(actual, step.Event.Rating, step.Event.ReviewedAtUtc);
                AssertCardMatches(step.ExpectedCard, actual, $"{history.Name} step {i}");
                priorTimestamp = step.Event.ReviewedAtUtc;
            }
        }
    }

    [TestMethod]
    public void Replay_MatchesPinnedOracleForCompleteHistories()
    {
        var completeHistories = Fsrs6OracleVectors.All.Where(history => history.Steps.Count > 1).ToArray();
        Assert.HasCount(5, completeHistories);

        var replayer = new Fsrs6Replayer();
        foreach (var history in completeHistories)
        {
            var events = history.Steps.Select(step => step.Event).ToArray();
            var actual = replayer.Replay(history.InitialCard, events);

            AssertCardMatches(history.Steps[^1].ExpectedCard, actual, history.Name);
        }
    }

    [TestMethod]
    public void Fixture_RecordsPinnedProvenanceAndRequiredCoverage()
    {
        Assert.AreEqual("open-spaced-repetition/py-fsrs", Fsrs6OracleVectors.UpstreamProject);
        Assert.AreEqual("v6.3.2", Fsrs6OracleVectors.UpstreamVersion);
        Assert.AreEqual("9446cb06605c597a063aeee49f7d188d42e34dc2", Fsrs6OracleVectors.UpstreamCommit);
        Assert.AreEqual("fsrs/scheduler.py", Fsrs6OracleVectors.ReferenceFile);
        CollectionAssert.AreEqual(Fsrs6Parameters.Default.Weights.ToArray(), Fsrs6OracleVectors.Parameters.ToArray());
        Assert.AreEqual(0.90, Fsrs6OracleVectors.DesiredRetention);
        CollectionAssert.AreEqual(new[] { 10 }, Fsrs6OracleVectors.LearningStepsMinutes.ToArray());
        CollectionAssert.AreEqual(new[] { 10 }, Fsrs6OracleVectors.RelearningStepsMinutes.ToArray());
        Assert.AreEqual(36_500, Fsrs6OracleVectors.MaximumIntervalDays);
        Assert.IsFalse(Fsrs6OracleVectors.FuzzEnabled);
        Assert.HasCount(38, Fsrs6OracleVectors.All);

        string[] requiredNames =
        [
            "initial-again",
            "initial-hard",
            "initial-good",
            "initial-easy",
            "new-again-learning-good",
            "new-hard-learning-hard",
            "repeated-again",
            "same-day-hard-successful-recall",
            "learning-good-graduation",
            "learning-easy-graduation",
            "relearning-same-day-again",
            "relearning-same-day-hard",
            "relearning-same-day-good",
            "relearning-same-day-easy",
            "lapse-relearning-graduation-history",
            "long-term-mixed-history",
            "minimum-stability-and-interval-clamp",
            "difficulty-near-one",
            "difficulty-near-ten",
            "same-day-zero-elapsed",
            "exact-twenty-four-hour-boundary",
            "maximum-interval-clamp"
        ];

        var names = Fsrs6OracleVectors.All.Select(history => history.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var requiredName in requiredNames)
        {
            CollectionAssert.Contains(names.ToArray(), requiredName);
        }

        foreach (int days in new[] { 1, 7, 30, 365 })
        {
            foreach (string rating in new[] { "again", "hard", "good", "easy" })
            {
                CollectionAssert.Contains(names.ToArray(), $"delayed-{days}-days-{rating}");
            }
        }
    }

    private static void AssertCardMatches(Fsrs6Card expected, Fsrs6Card actual, string context)
    {
        Assert.AreEqual(expected.State, actual.State, $"{context}: state");
        Assert.AreEqual(expected.StepIndex, actual.StepIndex, $"{context}: step index");
        Assert.AreEqual(expected.LastReviewedAtUtc, actual.LastReviewedAtUtc, $"{context}: last review");
        Assert.AreEqual(expected.DueAtUtc, actual.DueAtUtc, $"{context}: due timestamp");
        AssertNullableClose(expected.Stability, actual.Stability, $"{context}: stability");
        AssertNullableClose(expected.Difficulty, actual.Difficulty, $"{context}: difficulty");
    }

    private static void AssertNullableClose(double? expected, double? actual, string context)
    {
        Assert.AreEqual(expected.HasValue, actual.HasValue, $"{context}: nullability");
        if (!expected.HasValue)
        {
            return;
        }

        var difference = Math.Abs(expected.Value - actual!.Value);
        var scale = Math.Max(Math.Abs(expected.Value), Math.Abs(actual.Value));
        var relativeDifference = scale == 0.0 ? 0.0 : difference / scale;
        if (difference <= AbsoluteTolerance || relativeDifference <= RelativeTolerance)
        {
            return;
        }

        Assert.Fail(
            $"{context}: expected {expected.Value:R}, actual {actual.Value:R}, " +
            $"absolute difference {difference:R}, relative difference {relativeDifference:R}.");
    }
}
