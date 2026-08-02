using KnownFirst.Core.Learning;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 8 scheduler-replay correction — pure unit tests for
/// <see cref="CardScheduleReplayer"/>: matches the ordinary application scheduler transition, breaks a
/// same-timestamp tie deterministically regardless of input enumeration order, deduplicates by
/// fingerprint, and never depends on wall-clock time.
/// </summary>
[TestClass]
public sealed class CardScheduleReplayerTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ISpacedRepetitionScheduler Scheduler = new SimpleSpacedRepetitionScheduler();

    [TestMethod]
    public void Replay_SingleEvent_MatchesOrdinarySchedulerTransition()
    {
        var reviewedAt = CreatedAt.AddDays(1);
        var initial = CardSchedule.New(CreatedAt);
        var expected = Scheduler.Schedule(initial, ReviewRating.Good, reviewedAt);

        var actual = CardScheduleReplayer.Replay(
            initial,
            [new CardScheduleReplayer.ReviewEvent(reviewedAt, ReviewRating.Good, "fp-1")],
            Scheduler);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Replay_MultipleEvents_FoldsThroughSchedulerInReviewedAtOrder()
    {
        var initial = CardSchedule.New(CreatedAt);
        var t1 = CreatedAt.AddDays(1);
        var t2 = CreatedAt.AddDays(5);
        var expectedAfterFirst = Scheduler.Schedule(initial, ReviewRating.Good, t1);
        var expected = Scheduler.Schedule(expectedAfterFirst, ReviewRating.Easy, t2);

        // Fed in reverse chronological order — the helper must still replay by ReviewedAtUtc, not input order.
        var actual = CardScheduleReplayer.Replay(
            initial,
            [
                new CardScheduleReplayer.ReviewEvent(t2, ReviewRating.Easy, "fp-2"),
                new CardScheduleReplayer.ReviewEvent(t1, ReviewRating.Good, "fp-1"),
            ],
            Scheduler);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Replay_SameTimestamp_TieBreaksByFingerprintOrdinalRegardlessOfInputOrder()
    {
        var initial = CardSchedule.New(CreatedAt);
        var t = CreatedAt.AddDays(2);
        var eventA = new CardScheduleReplayer.ReviewEvent(t, ReviewRating.Good, "aaa");
        var eventB = new CardScheduleReplayer.ReviewEvent(t, ReviewRating.Hard, "bbb");
        // "aaa" precedes "bbb" ordinally, so eventA is always the fixed tie-break winner.
        var expected = Scheduler.Schedule(Scheduler.Schedule(initial, eventA.Rating, t), eventB.Rating, t);

        var forwardOrder = CardScheduleReplayer.Replay(initial, [eventA, eventB], Scheduler);
        var reverseOrder = CardScheduleReplayer.Replay(initial, [eventB, eventA], Scheduler);

        Assert.AreEqual(expected, forwardOrder);
        Assert.AreEqual(expected, reverseOrder);
    }

    [TestMethod]
    public void Replay_DuplicateFingerprint_AppliedOnlyOnce()
    {
        var initial = CardSchedule.New(CreatedAt);
        var t = CreatedAt.AddDays(3);
        var expected = Scheduler.Schedule(initial, ReviewRating.Good, t);

        var actual = CardScheduleReplayer.Replay(
            initial,
            [
                new CardScheduleReplayer.ReviewEvent(t, ReviewRating.Good, "dup"),
                new CardScheduleReplayer.ReviewEvent(t, ReviewRating.Good, "dup"),
            ],
            Scheduler);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Replay_IsDeterministic_ForIdenticalInputs()
    {
        var initial = CardSchedule.New(CreatedAt);
        CardScheduleReplayer.ReviewEvent[] events =
        [
            new(CreatedAt.AddDays(1), ReviewRating.Hard, "fp-1"),
            new(CreatedAt.AddDays(9), ReviewRating.Good, "fp-2"),
            new(CreatedAt.AddDays(40), ReviewRating.Easy, "fp-3")
        ];

        var first = CardScheduleReplayer.Replay(initial, events, Scheduler);
        var second = CardScheduleReplayer.Replay(initial, events, Scheduler);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Replay_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var initial = CardSchedule.New(CreatedAt);

        Assert.ThrowsExactly<OperationCanceledException>(() => CardScheduleReplayer.Replay(
            initial,
            [new CardScheduleReplayer.ReviewEvent(CreatedAt.AddDays(1), ReviewRating.Good, "fp-1")],
            Scheduler,
            cts.Token));
    }
}
