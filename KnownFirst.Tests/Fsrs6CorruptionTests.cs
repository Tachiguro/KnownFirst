using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6CorruptionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Replay_RejectsChronologicalReversalWithoutSilentlySorting()
    {
        Fsrs6ReviewEvent[] reversed =
        [
            new(Start.AddDays(7), ReviewRating.Good),
            new(Start.AddDays(1), ReviewRating.Easy)
        ];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Fsrs6Replayer().Replay(Fsrs6Card.New(), reversed));
    }

    [TestMethod]
    public void InvalidRatingCannotEnterReplayThroughSupportedEventBoundary()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Fsrs6ReviewEvent(Start, (ReviewRating)999));
    }

    [TestMethod]
    public void InvalidStartingCardCannotEnterReplayThroughSupportedBoundaries()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Fsrs6Card.Review(0.0009, 5.0, Start));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new Fsrs6Replayer().Replay(null!, Array.Empty<Fsrs6ReviewEvent>()));
    }

    [TestMethod]
    public void Replay_ImpossibleNonFiniteIntermediateStopsAtFirstFailure()
    {
        var weights = Fsrs6Parameters.Default.Weights.ToArray();
        weights[8] = double.MaxValue;
        var replayer = new Fsrs6Replayer(new Fsrs6Scheduler(new Fsrs6Parameters(weights)));
        var initial = Fsrs6Card.Review(10.0, 5.0, Start, Start.AddDays(10));
        int yielded = 0;

        IEnumerable<Fsrs6ReviewEvent> Events()
        {
            yielded++;
            yield return new Fsrs6ReviewEvent(Start.AddMinutes(1), ReviewRating.Good);
            yielded++;
            yield return new Fsrs6ReviewEvent(Start.AddDays(12), ReviewRating.Good);
            yielded++;
            yield return new Fsrs6ReviewEvent(Start.AddDays(13), ReviewRating.Easy);
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => replayer.Replay(initial, Events()));
        Assert.AreEqual(2, yielded, "Replay must not enumerate or apply events after the first corrupt transition.");
        Assert.AreEqual(Fsrs6Card.Review(10.0, 5.0, Start, Start.AddDays(10)), initial);
    }

    [TestMethod]
    public void Replay_DoesNotMutateInputCollectionOrInitialCard()
    {
        var initial = Fsrs6Card.New(Start);
        Fsrs6ReviewEvent[] events =
        [
            new(Start, ReviewRating.Again),
            new(Start.AddMinutes(5), ReviewRating.Good)
        ];
        var snapshot = events.ToArray();

        var actual = new Fsrs6Replayer().Replay(initial, events);

        CollectionAssert.AreEqual(snapshot, events);
        Assert.AreEqual(Fsrs6Card.New(Start), initial);
        Assert.AreEqual(Fsrs6CardState.Review, actual.State);
    }

    [TestMethod]
    public void Replay_NullEventSequenceFailsClosedAndEmptySequenceIsDeterministic()
    {
        var initial = Fsrs6Card.New(Start);
        var replayer = new Fsrs6Replayer();

        Assert.ThrowsExactly<ArgumentNullException>(() => replayer.Replay(initial, null!));
        Assert.AreSame(initial, replayer.Replay(initial, Array.Empty<Fsrs6ReviewEvent>()));
        Assert.AreSame(initial, replayer.Replay(initial, Array.Empty<Fsrs6ReviewEvent>()));
    }
}
