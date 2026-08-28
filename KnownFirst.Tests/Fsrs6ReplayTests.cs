using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6ReplayTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Replay_AppliesEveryEventThroughSchedulerInSuppliedOrder()
    {
        Fsrs6ReviewEvent[] events =
        [
            new(Start, ReviewRating.Good),
            new(Start.AddDays(7), ReviewRating.Hard),
            new(Start.AddDays(30), ReviewRating.Easy)
        ];
        var scheduler = new Fsrs6Scheduler();
        var expected = Fsrs6Card.New();
        foreach (var reviewEvent in events)
        {
            expected = scheduler.Schedule(expected, reviewEvent.Rating, reviewEvent.ReviewedAtUtc);
        }

        var actual = new Fsrs6Replayer(scheduler).Replay(Fsrs6Card.New(), events);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Replay_EqualTimestampsUseCallerSuppliedTotalOrderDeterministically()
    {
        Fsrs6ReviewEvent[] events =
        [
            new(Start, ReviewRating.Again),
            new(Start, ReviewRating.Good),
            new(Start, ReviewRating.Hard)
        ];
        var scheduler = new Fsrs6Scheduler();
        var expected = Fsrs6Card.New();
        foreach (var reviewEvent in events)
        {
            expected = scheduler.Schedule(expected, reviewEvent.Rating, reviewEvent.ReviewedAtUtc);
        }

        var first = new Fsrs6Replayer(scheduler).Replay(Fsrs6Card.New(), events);
        var second = new Fsrs6Replayer(scheduler).Replay(Fsrs6Card.New(), events);

        Assert.AreEqual(expected, first);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Replay_EventOlderThanStartingCardFailsClosed()
    {
        var initial = Fsrs6Card.Review(10.0, 5.0, Start);
        Fsrs6ReviewEvent[] events = [new(Start.AddTicks(-1), ReviewRating.Good)];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Fsrs6Replayer().Replay(initial, events));
    }

    [TestMethod]
    public void Replay_EmptyHistoryReturnsValidatedInitialInstanceUnchanged()
    {
        var initial = Fsrs6Card.Review(10.0, 5.0, Start, Start.AddDays(10));
        var replayer = new Fsrs6Replayer();

        var first = replayer.Replay(initial, Array.Empty<Fsrs6ReviewEvent>());
        var second = replayer.Replay(initial, Array.Empty<Fsrs6ReviewEvent>());

        Assert.AreSame(initial, first);
        Assert.AreSame(first, second);
    }
}
