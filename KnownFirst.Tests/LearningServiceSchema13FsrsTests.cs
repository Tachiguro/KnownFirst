using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LearningServiceSchema13FsrsTests
{
    private static readonly DateTime ReviewTime =
        new(2026, 8, 30, 9, 15, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Slice5_CheckSpellingAsync_CorrectSchema13DefersReviewAndRatingPersistsOneAttributedFsrsFact()
    {
        await using var fixture = await CreateFixtureAsync();
        await ConfigureMeaningToTermAsync(fixture);
        var clock = new CountingClock(ReviewTime);
        var fsrs = new CountingFsrsSchedulingService(new Fsrs6SchedulingService(clock));
        var service = CreateLearningService(fixture, clock, fsrs, LearningMode.Typing);

        var spelling = await service.CheckSpellingAsync(fixture.QueueItemId, "fact");

        Assert.IsTrue(spelling.IsCorrect);
        Assert.IsFalse(spelling.RatingWasPersisted);
        Assert.AreEqual(fixture.TargetAnswerVariantId, spelling.MatchedAnswerVariantId);
        Assert.AreEqual(0, await CountRowsAsync(fixture, "FsrsReviewHistoryEntries"));
        Assert.AreEqual(0, await CountRowsAsync(fixture, "LearningReviews"));
        Assert.AreEqual(0, fsrs.ScheduleCallCount);

        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        Assert.AreEqual(1, await CountRowsAsync(fixture, "FsrsReviewHistoryEntries"));
        Assert.AreEqual(1, await CountRowsAsync(fixture, "LearningReviews"));
        Assert.AreEqual(1, fsrs.ScheduleCallCount);
        var review = (await fixture.DatabaseFixture.Connection.QueryAsync<ReviewRow>(
            "SELECT * FROM LearningReviews WHERE CardId = ?", fixture.CardId)).Single();
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsTrue(review.WasCorrect);
        Assert.AreEqual(fixture.TargetAnswerVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(fixture.TargetAnswerVariantId, review.MatchedAnswerVariantId);
    }

    [TestMethod]
    public async Task Slice5_CheckSpellingAsync_IncorrectSchema13PersistsOneAgainAndOneTailRepeat()
    {
        await using var fixture = await CreateFixtureAsync();
        await ConfigureMeaningToTermAsync(fixture);
        var queuedAheadId = await AddIncompleteQueueRowForSameCardAsync(fixture);
        var clock = new CountingClock(ReviewTime);
        var fsrs = new CountingFsrsSchedulingService(new Fsrs6SchedulingService(clock));
        var service = CreateLearningService(fixture, clock, fsrs, LearningMode.Typing);
        var expected = new Fsrs6SchedulingService(new FixedClock(ReviewTime)).Schedule(
            Fsrs6ScheduleProjection.New(),
            ReviewRating.Again,
            new DateTimeOffset(ReviewTime, TimeSpan.Zero));

        var spelling = await service.CheckSpellingAsync(fixture.QueueItemId, "wrong");

        Assert.IsFalse(spelling.IsCorrect);
        Assert.IsTrue(spelling.RatingWasPersisted);
        Assert.AreEqual(1, fsrs.ScheduleCallCount);
        var history = await fixture.DatabaseFixture.Connection.QueryAsync<HistoryAttemptRow>(
            "SELECT StableId, SequenceNumber FROM FsrsReviewHistoryEntries WHERE CardId = ? ORDER BY SequenceNumber",
            fixture.CardId);
        Assert.HasCount(1, history);
        Assert.AreEqual(fixture.QueueStableId, history[0].StableId);
        Assert.AreEqual(1, history[0].SequenceNumber);

        var rows = await LoadQueueAttemptRowsAsync(fixture);
        Assert.HasCount(3, rows);
        Assert.IsFalse(rows.Single(row => row.Id == queuedAheadId).IsCompleted);
        Assert.AreEqual(1, rows.Single(row => row.Id == queuedAheadId).QueueOrder);
        var repeat = rows.Single(row => row.IsAgainRepeat);
        Assert.AreEqual(2, repeat.QueueOrder);
        Assert.IsFalse(repeat.IsCompleted);

        var review = (await fixture.DatabaseFixture.Connection.QueryAsync<ReviewRow>(
            "SELECT * FROM LearningReviews WHERE CardId = ?", fixture.CardId)).Single();
        Assert.AreEqual(ReviewRating.Again, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsFalse(review.WasCorrect);
        Assert.AreEqual(fixture.TargetAnswerVariantId, review.TargetAnswerVariantId);
        Assert.IsNull(review.MatchedAnswerVariantId);

        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            var state = FsrsCardStateRepository.Load(connection, fixture.CardId);
            Assert.IsNotNull(state);
            Assert.AreEqual(expected.State, state.State);
            AssertExactDouble(expected.Stability, state.Stability, nameof(state.Stability));
            AssertExactDouble(expected.Difficulty, state.Difficulty, nameof(state.Difficulty));
            Assert.AreEqual(expected.DueAtUtc, state.DueAtUtc);
        });
    }

    [TestMethod]
    public async Task Slice5_CheckSpellingAsync_IncorrectSchema13FailureRollsBackAndRetryConvergesOnce()
    {
        await using var fixture = await CreateFixtureAsync();
        await ConfigureMeaningToTermAsync(fixture);
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)),
            LearningMode.Typing);
        var before = await CaptureTransactionFactsAsync(fixture);
        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "CREATE TRIGGER fail_slice5_typed_session_update BEFORE UPDATE ON LearningSessions BEGIN SELECT RAISE(ABORT, 'injected typed failure'); END");

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            service.CheckSpellingAsync(fixture.QueueItemId, "wrong"));

        CollectionAssert.AreEqual(before, await CaptureTransactionFactsAsync(fixture));
        Assert.AreEqual(0, await CountRowsAsync(fixture, "FsrsReviewHistoryEntries"));
        Assert.AreEqual(0, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsAgainRepeat = 1"));

        await fixture.DatabaseFixture.Connection.ExecuteAsync("DROP TRIGGER fail_slice5_typed_session_update");
        var retried = await service.CheckSpellingAsync(fixture.QueueItemId, "wrong");

        Assert.IsTrue(retried.RatingWasPersisted);
        Assert.AreEqual(1, await CountRowsAsync(fixture, "FsrsReviewHistoryEntries"));
        Assert.AreEqual(1, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsAgainRepeat = 1"));
    }

    [TestMethod]
    public async Task Slice5_MarkPermanentlyKnownAsync_Schema13PersistsCleanWordControlAndPreservesFactsAndGraph()
    {
        await using var fixture = await CreateFixtureAsync();
        await AddSiblingSenseAsync(fixture);
        var historicalTime = ReviewTime.AddDays(-2);
        await AddHistoricalInteractionAsync(
            fixture, 1, historicalTime, ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            fixture.TargetAnswerVariantId, null, LegacySnapshot(historicalTime.AddDays(1), intervalDays: 1));
        var historyBefore = await LoadFactualHistoryFactsAsync(fixture);
        var stateBefore = await LoadFsrsStateFactsAsync(fixture);
        var graphBefore = await LoadSemanticGraphCountsAsync(fixture);
        var wordStatusBefore = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Status FROM Words WHERE Id = ?", fixture.WordId);
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));

        Assert.IsTrue(await service.MarkPermanentlyKnownAsync(fixture.WordId, confirmed: true));

        Assert.AreEqual(1, await CountRowsAsync(fixture, "WordLearningControls"));
        var decidedAtUtc = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<string>(
            "SELECT DecidedAtUtc FROM WordLearningControls WHERE WordId = ?", fixture.WordId);
        Assert.AreEqual(ReviewTime, Schema13TimestampCodec.ParseUtcDateTime(decidedAtUtc));
        CollectionAssert.AreEqual(historyBefore, await LoadFactualHistoryFactsAsync(fixture));
        CollectionAssert.AreEqual(stateBefore, await LoadFsrsStateFactsAsync(fixture));
        CollectionAssert.AreEqual(graphBefore, await LoadSemanticGraphCountsAsync(fixture));
        Assert.AreEqual(wordStatusBefore, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Status FROM Words WHERE Id = ?", fixture.WordId));
        Assert.AreEqual(0, await CountRowsAsync(fixture, "SenseLearningControls"));
        Assert.AreEqual(0, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards q JOIN LearningCards c ON c.Id = q.CardId WHERE c.WordId = ? AND q.IsCompleted = 0",
            fixture.WordId));

        var load = await service.GetOrStartAsync();
        Assert.IsNull(load.Card, "A clean AlreadyKnown control must make every card of the word ineligible.");
    }

    [TestMethod]
    public async Task Slice5_MarkPermanentlyKnownAsync_Schema13FailureRollsBackAndRetryConverges()
    {
        await using var fixture = await CreateFixtureAsync();
        var historicalTime = ReviewTime.AddDays(-2);
        await AddHistoricalInteractionAsync(
            fixture, 1, historicalTime, ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            fixture.TargetAnswerVariantId, null, LegacySnapshot(historicalTime.AddDays(1), intervalDays: 1));
        var before = await CaptureTransactionFactsAsync(fixture);
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));
        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "CREATE TRIGGER fail_slice5_known_session_update BEFORE UPDATE ON LearningSessions BEGIN SELECT RAISE(ABORT, 'injected known failure'); END");

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            service.MarkPermanentlyKnownAsync(fixture.WordId, confirmed: true));

        Assert.AreEqual(0, await CountRowsAsync(fixture, "WordLearningControls"));
        CollectionAssert.AreEqual(before, await CaptureTransactionFactsAsync(fixture));

        await fixture.DatabaseFixture.Connection.ExecuteAsync("DROP TRIGGER fail_slice5_known_session_update");
        Assert.IsTrue(await service.MarkPermanentlyKnownAsync(fixture.WordId, confirmed: true));
        Assert.AreEqual(1, await CountRowsAsync(fixture, "WordLearningControls"));
        Assert.AreEqual(0, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards q JOIN LearningCards c ON c.Id = q.CardId WHERE c.WordId = ? AND q.IsCompleted = 0",
            fixture.WordId));
    }

    [TestMethod]
    public async Task Slice5_MarkPermanentlyKnownAsync_Schema13RepeatedCallPreservesOriginalDecision()
    {
        await using var fixture = await CreateFixtureAsync();
        var first = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));
        var secondTime = ReviewTime.AddHours(1);
        var second = CreateLearningService(
            fixture,
            new FixedClock(secondTime),
            new Fsrs6SchedulingService(new FixedClock(secondTime)));

        Assert.IsTrue(await first.MarkPermanentlyKnownAsync(fixture.WordId, confirmed: true));
        Assert.IsTrue(await second.MarkPermanentlyKnownAsync(fixture.WordId, confirmed: true));

        Assert.AreEqual(1, await CountRowsAsync(fixture, "WordLearningControls"));
        var decidedAtUtc = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<string>(
            "SELECT DecidedAtUtc FROM WordLearningControls WHERE WordId = ?", fixture.WordId);
        Assert.AreEqual(ReviewTime, Schema13TimestampCodec.ParseUtcDateTime(decidedAtUtc));
    }

    [TestMethod]
    public async Task RateAsync_Again_AppendsOneTailRepeatAndSkipsUnrelatedFutureDueWork()
    {
        await using var fixture = await CreateFixtureAsync();
        var unrelatedQueueItemId = await AddFutureDueUnrelatedQueueRowAsync(fixture);
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));

        var result = await service.RateAsync(fixture.QueueItemId, ReviewRating.Again);

        var rows = await LoadQueueAttemptRowsAsync(fixture);
        var original = rows.Single(row => row.Id == fixture.QueueItemId);
        var unrelated = rows.Single(row => row.Id == unrelatedQueueItemId);
        var repeat = rows.Single(row => row.IsAgainRepeat);
        DateTimeOffset? fsrsDueAtUtc = null;
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
            fsrsDueAtUtc = FsrsCardStateRepository.Load(connection, fixture.CardId)?.DueAtUtc);
        var session = await fixture.DatabaseFixture.Connection.QueryAsync<SessionRow>(
            "SELECT Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, CompletedAtUtc FROM LearningSessions WHERE Id = ?",
            fixture.SessionId);

        Assert.HasCount(3, rows);
        Assert.IsTrue(original.IsCompleted);
        Assert.IsFalse(original.IsAgainRepeat);
        Assert.AreEqual(0, original.QueueOrder);
        Assert.IsFalse(unrelated.IsCompleted);
        Assert.IsFalse(unrelated.IsAgainRepeat);
        Assert.AreEqual(1, unrelated.QueueOrder);
        Assert.AreEqual(fixture.CardId, repeat.CardId);
        Assert.AreEqual(fixture.TargetAnswerVariantId, repeat.TargetAnswerVariantId);
        Assert.AreEqual(2, repeat.QueueOrder);
        Assert.IsFalse(repeat.IsCompleted);
        Assert.AreNotEqual(fixture.QueueStableId, repeat.StableId);
        Assert.IsNotNull(fsrsDueAtUtc);
        Assert.IsGreaterThan(new DateTimeOffset(ReviewTime, TimeSpan.Zero), fsrsDueAtUtc.Value);
        Assert.IsNotNull(result.Card);
        Assert.AreEqual(repeat.Id, result.Card.QueueItemId,
            "The future-due ordinary row must remain suppressed while the active-session repeat is selectable.");
        Assert.IsTrue(result.Card.IsAgainRepeat);
        Assert.AreEqual(3, session.Single().TotalCards);
        Assert.AreEqual(1, session.Single().CompletedCards);
        Assert.AreEqual(1, session.Single().AgainCount);
        Assert.AreEqual(LearningSessionStatus.Active, session.Single().Status);
    }

    [TestMethod]
    public async Task RateAsync_AgainOnRepeat_AppendsAnotherTailRepeatWithoutCap()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));

        var firstResult = await service.RateAsync(fixture.QueueItemId, ReviewRating.Again);
        Assert.IsNotNull(firstResult.Card);
        var firstRepeatId = firstResult.Card.QueueItemId;
        await service.RevealAnswerAsync(firstRepeatId);
        var secondResult = await service.RateAsync(firstRepeatId, ReviewRating.Again);

        var rows = await LoadQueueAttemptRowsAsync(fixture);
        var history = await fixture.DatabaseFixture.Connection.QueryAsync<HistoryAttemptRow>(
            "SELECT StableId, SequenceNumber FROM FsrsReviewHistoryEntries WHERE CardId = ? ORDER BY SequenceNumber",
            fixture.CardId);

        Assert.HasCount(3, rows);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(row => row.QueueOrder).ToArray());
        Assert.AreEqual(2, rows.Count(row => row.IsCompleted));
        Assert.AreEqual(2, rows.Count(row => row.IsAgainRepeat));
        Assert.AreEqual(1, rows.Count(row => !row.IsCompleted));
        Assert.IsNotNull(secondResult.Card);
        Assert.AreEqual(rows[2].Id, secondResult.Card.QueueItemId);
        Assert.AreEqual(fixture.QueueStableId, history[0].StableId);
        Assert.AreEqual(rows[1].StableId, history[1].StableId);
        CollectionAssert.AreEqual(new[] { 1, 2 }, history.Select(row => row.SequenceNumber).ToArray());
        Assert.AreNotEqual(rows[0].StableId, rows[1].StableId);
        Assert.AreNotEqual(rows[1].StableId, rows[2].StableId);
    }

    [DataTestMethod]
    [DataRow(ReviewRating.Hard)]
    [DataRow(ReviewRating.Good)]
    [DataRow(ReviewRating.Easy)]
    public async Task RateAsync_NonAgain_AppendsNoRepeat(ReviewRating rating)
    {
        await using var fixture = await CreateFixtureAsync();
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));

        await service.RateAsync(fixture.QueueItemId, rating);

        var rows = await LoadQueueAttemptRowsAsync(fixture);
        Assert.HasCount(1, rows);
        Assert.AreEqual(0, rows.Count(row => row.IsAgainRepeat));
        Assert.AreEqual(1, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT TotalCards FROM LearningSessions WHERE Id = ?", fixture.SessionId));
    }

    [TestMethod]
    public async Task RateAsync_Again_FailureAfterRepeatInsertionRollsBackWithoutOrphanAndRetriesOnce()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));
        var before = await CaptureTransactionFactsAsync(fixture);
        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "CREATE TRIGGER fail_schema13_again_session_update BEFORE UPDATE ON LearningSessions BEGIN SELECT RAISE(ABORT, 'injected session failure'); END");

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            service.RateAsync(fixture.QueueItemId, ReviewRating.Again));

        CollectionAssert.AreEqual(before, await CaptureTransactionFactsAsync(fixture));
        Assert.AreEqual(0, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsAgainRepeat = 1"));

        await fixture.DatabaseFixture.Connection.ExecuteAsync("DROP TRIGGER fail_schema13_again_session_update");
        var retried = await service.RateAsync(fixture.QueueItemId, ReviewRating.Again);

        Assert.IsNotNull(retried.Card);
        Assert.AreEqual(1, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsAgainRepeat = 1"));
        Assert.AreEqual(1, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE CardId = ?", fixture.CardId));
    }

    [TestMethod]
    public async Task RateAsync_ValidSchema13_PersistsOneFactualReviewAndExactFsrsState()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new CountingClock(ReviewTime);
        var fsrs = new CountingFsrsSchedulingService(new Fsrs6SchedulingService(clock));
        var service = CreateLearningService(fixture, clock, fsrs);
        var expected = new Fsrs6SchedulingService(new FixedClock(ReviewTime)).Schedule(
            Fsrs6ScheduleProjection.New(),
            ReviewRating.Good,
            new DateTimeOffset(ReviewTime, TimeSpan.Zero));

        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        Assert.AreEqual(1, clock.ReadCount, "One rating must capture exactly one clock timestamp.");
        Assert.AreEqual(1, fsrs.ScheduleCallCount, "The factual rating must call the FSRS service exactly once.");

        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            var history = FsrsReviewHistoryRepository.LoadHistory(connection, fixture.CardId);
            Assert.HasCount(1, history);
            Assert.AreEqual(fixture.QueueStableId, history[0].StableId);
            Assert.AreEqual(fixture.CardId, history[0].CardId);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(ReviewRating.Good, history[0].Event.Rating);
            Assert.AreEqual(new DateTimeOffset(ReviewTime, TimeSpan.Zero), history[0].Event.ReviewedAtUtc);

            var state = FsrsCardStateRepository.Load(connection, fixture.CardId);
            Assert.IsNotNull(state);
            Assert.AreEqual(expected.State, state.State);
            AssertExactDouble(expected.Stability, state.Stability, nameof(state.Stability));
            AssertExactDouble(expected.Difficulty, state.Difficulty, nameof(state.Difficulty));
            Assert.AreEqual(expected.LastReviewedAtUtc, state.LastReviewedAtUtc);
            Assert.AreEqual(expected.StepIndex, state.StepIndex);
            Assert.AreEqual(expected.DueAtUtc, state.DueAtUtc);

            var reviews = connection.Query<ReviewRow>(
                "SELECT CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId FROM LearningReviews");
            Assert.HasCount(1, reviews);
            Assert.AreEqual(fixture.CardId, reviews[0].CardId);
            Assert.AreEqual(fixture.SessionId, reviews[0].SessionId);
            Assert.AreEqual(ReviewRating.Good, reviews[0].Rating);
            Assert.IsFalse(reviews[0].WasTypedAnswer);
            Assert.IsTrue(reviews[0].WasCorrect);
            Assert.AreEqual(ReviewTime.Ticks, Schema8Utc.Normalize(reviews[0].ReviewedAtUtc).Ticks);
            Assert.AreEqual(fixture.LegacyDueAtUtc.Ticks, Schema8Utc.Normalize(reviews[0].DueAtUtc).Ticks);
            Assert.AreEqual(fixture.LegacyIntervalDays, reviews[0].IntervalDays);
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(fixture.LegacyEaseFactor),
                BitConverter.DoubleToInt64Bits(reviews[0].EaseFactor));
            Assert.AreEqual(fixture.TargetAnswerVariantId, reviews[0].TargetAnswerVariantId);
            Assert.IsNull(reviews[0].MatchedAnswerVariantId);

            var progress = connection.Query<ProgressRow>(
                "SELECT CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc, MasteryReviewExtensionScheduled, IsMastered FROM AnswerVariantProgress");
            Assert.HasCount(1, progress);
            Assert.AreEqual(fixture.CardId, progress[0].CardId);
            Assert.AreEqual(fixture.TargetAnswerVariantId, progress[0].AnswerVariantId);
            Assert.AreEqual(LearningInteractionMode.Reading, progress[0].InteractionMode);
            Assert.AreEqual(1, progress[0].ConsecutiveReadingSuccessCount);
            Assert.AreEqual(0, progress[0].ConsecutiveTypingSuccessCount);
            Assert.AreEqual(0, progress[0].ConsecutiveTypingFailureCount);
            Assert.AreEqual(ReviewTime.Ticks, Schema8Utc.Normalize(progress[0].LastAssessedAtUtc!.Value).Ticks);
            Assert.IsFalse(progress[0].MasteryReviewExtensionScheduled);
            Assert.IsFalse(progress[0].IsMastered);

            var legacyCard = connection.Query<LegacyCardRow>(
                "SELECT State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating FROM LearningCards WHERE Id = ?",
                fixture.CardId).Single();
            Assert.AreEqual(CardState.New, legacyCard.State);
            Assert.AreEqual(fixture.LegacyDueAtUtc.Ticks, Schema8Utc.Normalize(legacyCard.DueAtUtc).Ticks);
            Assert.AreEqual(fixture.LegacyIntervalDays, legacyCard.IntervalDays);
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(fixture.LegacyEaseFactor),
                BitConverter.DoubleToInt64Bits(legacyCard.EaseFactor));
            Assert.AreEqual(0, legacyCard.SuccessfulReviewCount);
            Assert.AreEqual(0, legacyCard.LapseCount);
            Assert.IsNull(legacyCard.LastReviewedAtUtc);
            Assert.IsNull(legacyCard.LastRating);

            var queue = connection.Query<QueueRow>(
                "SELECT IsCompleted, Rating, CompletedAtUtc, StableId FROM LearningSessionCards WHERE Id = ?",
                fixture.QueueItemId).Single();
            Assert.IsTrue(queue.IsCompleted);
            Assert.AreEqual(ReviewRating.Good, queue.Rating);
            Assert.AreEqual(ReviewTime.Ticks, Schema8Utc.Normalize(queue.CompletedAtUtc!.Value).Ticks);
            Assert.AreEqual(fixture.QueueStableId, queue.StableId);

            var session = connection.Query<SessionRow>(
                "SELECT Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, CompletedAtUtc FROM LearningSessions WHERE Id = ?",
                fixture.SessionId).Single();
            Assert.AreEqual(LearningSessionStatus.Completed, session.Status);
            Assert.AreEqual(1, session.TotalCards);
            Assert.AreEqual(1, session.CompletedCards);
            Assert.AreEqual(0, session.AgainCount);
            Assert.AreEqual(0, session.HardCount);
            Assert.AreEqual(1, session.GoodCount);
            Assert.AreEqual(0, session.EasyCount);
            Assert.AreEqual(ReviewTime.Ticks, Schema8Utc.Normalize(session.CompletedAtUtc!.Value).Ticks);
        });
    }

    [TestMethod]
    public async Task RateAsync_FailureAfterFsrsWrites_RollsBackEverythingAndSameAttemptRetries()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new CountingClock(ReviewTime);
        var fsrs = new CountingFsrsSchedulingService(new Fsrs6SchedulingService(clock));
        var service = CreateLearningService(fixture, clock, fsrs);
        var before = await CaptureTransactionFactsAsync(fixture);

        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "CREATE TRIGGER fail_schema13_review BEFORE INSERT ON LearningReviews BEGIN SELECT RAISE(ABORT, 'injected compatibility failure'); END");

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            service.RateAsync(fixture.QueueItemId, ReviewRating.Good));

        CollectionAssert.AreEqual(before, await CaptureTransactionFactsAsync(fixture));

        await fixture.DatabaseFixture.Connection.ExecuteAsync("DROP TRIGGER fail_schema13_review");
        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        var afterRetry = await CaptureTransactionFactsAsync(fixture);
        Assert.AreEqual(1, afterRetry.Count(value => value.StartsWith("history|", StringComparison.Ordinal)));
        StringAssert.Contains(string.Join('\n', afterRetry), $"history|{fixture.QueueStableId}|");
    }

    [TestMethod]
    public async Task RateAsync_CompletedAttemptResubmission_AppendsNoSecondFactualEvent()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new CountingClock(ReviewTime);
        var fsrs = new CountingFsrsSchedulingService(new Fsrs6SchedulingService(clock));
        var service = CreateLearningService(fixture, clock, fsrs);

        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);
        var committed = await CaptureTransactionFactsAsync(fixture);

        await Assert.ThrowsAsync<Exception>(() =>
            service.RateAsync(fixture.QueueItemId, ReviewRating.Good));

        CollectionAssert.AreEqual(committed, await CaptureTransactionFactsAsync(fixture));
        Assert.AreEqual(1, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE StableId = ?", fixture.QueueStableId));
    }

    [TestMethod]
    public async Task RateAsync_StableIdCollision_FailsClosedWithoutPartialProgress()
    {
        await using var fixture = await CreateFixtureAsync();
        const string collisionStableId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var priorTime = new DateTimeOffset(ReviewTime.AddDays(-1), TimeSpan.Zero);
        var priorEvent = new Fsrs6ReviewEvent(priorTime, ReviewRating.Hard);
        var priorState = new Fsrs6Scheduler().Schedule(Fsrs6Card.New(), ReviewRating.Hard, priorTime);

        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            FsrsReviewPersistenceCoordinator.PersistReview(
                connection, fixture.CardId, collisionStableId, priorEvent, priorState);
            connection.Execute(
                "UPDATE LearningSessionCards SET StableId = ? WHERE Id = ?",
                collisionStableId,
                fixture.QueueItemId);
        });
        var before = await CaptureTransactionFactsAsync(fixture);
        var clock = new CountingClock(ReviewTime);
        var fsrs = new CountingFsrsSchedulingService(new Fsrs6SchedulingService(clock));
        var service = CreateLearningService(fixture, clock, fsrs);

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            service.RateAsync(fixture.QueueItemId, ReviewRating.Good));

        CollectionAssert.AreEqual(before, await CaptureTransactionFactsAsync(fixture));
        Assert.AreEqual(1, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE StableId = ?", collisionStableId));
    }

    [TestMethod]
    public async Task RateAsync_Repeated365DayCompatibilityHistory_DoesNotSynthesizeLegacyMasteryOrExtension()
    {
        await using var fixture = await CreateFixtureAsync();
        await SetRequiredSinceAsync(fixture, ReviewTime.AddDays(-500));
        var firstReview = ReviewTime.AddDays(-400);
        var masterySnapshot = LegacySnapshot(firstReview.AddDays(365), intervalDays: 365);
        await AddHistoricalInteractionAsync(
            fixture, 1, firstReview, ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            fixture.TargetAnswerVariantId, fixture.TargetAnswerVariantId, masterySnapshot);
        await AddHistoricalInteractionAsync(
            fixture, 2, firstReview.AddDays(1), ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            fixture.TargetAnswerVariantId, fixture.TargetAnswerVariantId, masterySnapshot);
        await AddHistoricalInteractionAsync(
            fixture, 3, firstReview.AddDays(2), ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            fixture.TargetAnswerVariantId, null, masterySnapshot);

        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));
        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        var progress = await LoadProgressAsync(fixture, fixture.TargetAnswerVariantId);
        Assert.IsFalse(progress.MasteryReviewExtensionScheduled);
        Assert.IsFalse(progress.IsMastered);
        Assert.AreEqual(LearningInteractionMode.Typing, progress.InteractionMode);
        Assert.AreEqual(2, progress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(2, progress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(4, await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE CardId = ?", fixture.CardId));
        var legacyCard = await fixture.DatabaseFixture.Connection.QueryAsync<LegacyCardRow>(
            "SELECT State, DueAtUtc, IntervalDays, EaseFactor FROM LearningCards WHERE Id = ?",
            fixture.CardId);
        Assert.AreEqual(CardState.Review, legacyCard.Single().State);
        Assert.AreEqual(365, legacyCard.Single().IntervalDays);
    }

    [TestMethod]
    public async Task GetOrStartAsync_LegacyMasteryCannotSuppressPreferredSchema13QueueTarget()
    {
        await using var fixture = await CreateFixtureAsync();
        await SetRequiredSinceAsync(fixture, ReviewTime.AddDays(-500));
        var alternateVariantId = await AddAnswerVariantAsync(
            fixture, "schema13-alternate-required", "alternate", AnswerVariantRequirement.Required);
        var firstReview = ReviewTime.AddDays(-400);
        var masterySnapshot = LegacySnapshot(firstReview.AddDays(365), intervalDays: 365);
        await AddHistoricalInteractionAsync(
            fixture, 1, firstReview, ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            fixture.TargetAnswerVariantId, fixture.TargetAnswerVariantId, masterySnapshot);
        await AddHistoricalInteractionAsync(
            fixture, 2, firstReview.AddDays(1), ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            fixture.TargetAnswerVariantId, fixture.TargetAnswerVariantId, masterySnapshot);
        await CompleteSeedSessionAsync(fixture);

        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));
        var result = await service.GetOrStartAsync();

        Assert.IsNotNull(result.Card);
        var selectedTarget = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT TargetAnswerVariantId FROM LearningSessionCards WHERE Id = ?", result.Card.QueueItemId);
        Assert.AreEqual(fixture.TargetAnswerVariantId, selectedTarget);
        Assert.AreNotEqual(alternateVariantId, selectedTarget);
    }

    [TestMethod]
    public async Task GetOrStartAsync_AutomaticModeIsIndependentOfCompatibilityScheduleFields()
    {
        await using var identicalSchedules = await CreateFixtureAsync();
        await using var differentSchedules = await CreateFixtureAsync();
        await ConfigureMeaningToTermAsync(identicalSchedules);
        await ConfigureMeaningToTermAsync(differentSchedules);
        await SetRequiredSinceAsync(identicalSchedules, ReviewTime.AddDays(-500));
        await SetRequiredSinceAsync(differentSchedules, ReviewTime.AddDays(-500));
        var reviewedAtUtc = ReviewTime.AddDays(-400);
        var scheduleA = LegacySnapshot(reviewedAtUtc.AddDays(1), intervalDays: 1);
        var scheduleB = LegacySnapshot(reviewedAtUtc.AddDays(2), intervalDays: 2);

        foreach (var fixture in new[] { identicalSchedules, differentSchedules })
        {
            await AddHistoricalInteractionAsync(
                fixture, 1, reviewedAtUtc, ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
                fixture.TargetAnswerVariantId, null, scheduleA);
        }
        await AddHistoricalInteractionAsync(
            identicalSchedules, 2, reviewedAtUtc, ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            identicalSchedules.TargetAnswerVariantId, null, scheduleA);
        await AddHistoricalInteractionAsync(
            differentSchedules, 2, reviewedAtUtc, ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            differentSchedules.TargetAnswerVariantId, null, scheduleB);

        var identicalResult = await CreateLearningService(
            identicalSchedules,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime))).GetOrStartAsync();
        var differentResult = await CreateLearningService(
            differentSchedules,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime))).GetOrStartAsync();

        Assert.IsNotNull(identicalResult.Card);
        Assert.IsNotNull(differentResult.Card);
        Assert.AreEqual(LearningInteractionMode.Typing, identicalResult.Card.InteractionMode);
        Assert.AreEqual(identicalResult.Card.InteractionMode, differentResult.Card.InteractionMode);
    }

    [TestMethod]
    public async Task RateAsync_RebuildsCompleteRequiredProgressAndPreservesAcceptedOnlyRows()
    {
        await using var fixture = await CreateFixtureAsync();
        await SetRequiredSinceAsync(fixture, ReviewTime.AddDays(-500));
        var uncreditedRequiredVariantId = await AddAnswerVariantAsync(
            fixture, "schema13-uncredited-required", "uncredited", AnswerVariantRequirement.Required);
        var acceptedOnlyVariantId = await AddAnswerVariantAsync(
            fixture, "schema13-accepted-only", "accepted", AnswerVariantRequirement.AcceptedOnly);
        var firstReview = ReviewTime.AddDays(-10);
        await AddHistoricalInteractionAsync(
            fixture, 1, firstReview, ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            fixture.TargetAnswerVariantId, null, LegacySnapshot(firstReview.AddDays(1), intervalDays: 1));

        var requiredSinceUtc = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<DateTime>(
            "SELECT RequiredSinceUtc FROM SenseAnswerVariantAssignments WHERE AnswerVariantId = ?",
            fixture.TargetAnswerVariantId);
        var uncreditedRequiredSinceUtc = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<DateTime>(
            "SELECT RequiredSinceUtc FROM SenseAnswerVariantAssignments WHERE AnswerVariantId = ?",
            uncreditedRequiredVariantId);
        var acceptedOnlyCreatedAt = ReviewTime.AddDays(-30);
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            Schema8LearningRepository.InsertProgress(connection, new AnswerVariantProgressRow
            {
                CardId = fixture.CardId,
                AnswerVariantId = fixture.TargetAnswerVariantId,
                InteractionMode = LearningInteractionMode.Reading,
                ConsecutiveReadingSuccessCount = 0,
                ConsecutiveTypingSuccessCount = 0,
                ConsecutiveTypingFailureCount = 0,
                LastAssessedAtUtc = firstReview,
                MasteryReviewExtensionScheduled = true,
                IsMastered = true,
                ReplayVersion = Schema8LearningReviewReplayPolicy.ReplayVersion,
                CreatedAtUtc = requiredSinceUtc,
                UpdatedAtUtc = firstReview
            });
            Schema8LearningRepository.InsertProgress(connection, new AnswerVariantProgressRow
            {
                CardId = fixture.CardId,
                AnswerVariantId = uncreditedRequiredVariantId,
                InteractionMode = LearningInteractionMode.Typing,
                ConsecutiveReadingSuccessCount = 2,
                ConsecutiveTypingSuccessCount = 2,
                ConsecutiveTypingFailureCount = 0,
                LastAssessedAtUtc = firstReview,
                MasteryReviewExtensionScheduled = true,
                IsMastered = true,
                ReplayVersion = Schema8LearningReviewReplayPolicy.ReplayVersion,
                CreatedAtUtc = uncreditedRequiredSinceUtc,
                UpdatedAtUtc = firstReview
            });
            Schema8LearningRepository.InsertProgress(connection, new AnswerVariantProgressRow
            {
                CardId = fixture.CardId,
                AnswerVariantId = acceptedOnlyVariantId,
                InteractionMode = LearningInteractionMode.Typing,
                ConsecutiveReadingSuccessCount = 2,
                ConsecutiveTypingSuccessCount = 1,
                ConsecutiveTypingFailureCount = 1,
                LastAssessedAtUtc = acceptedOnlyCreatedAt,
                MasteryReviewExtensionScheduled = true,
                IsMastered = true,
                ReplayVersion = 0,
                CreatedAtUtc = acceptedOnlyCreatedAt,
                UpdatedAtUtc = acceptedOnlyCreatedAt
            });
        });

        var service = CreateLearningService(
            fixture,
            new FixedClock(ReviewTime),
            new Fsrs6SchedulingService(new FixedClock(ReviewTime)));
        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        var rebuilt = await LoadProgressAsync(fixture, fixture.TargetAnswerVariantId);
        Assert.AreEqual(LearningInteractionMode.Typing, rebuilt.InteractionMode);
        Assert.AreEqual(2, rebuilt.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, rebuilt.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, rebuilt.ConsecutiveTypingFailureCount);
        Assert.IsFalse(rebuilt.MasteryReviewExtensionScheduled);
        Assert.IsFalse(rebuilt.IsMastered);

        var uncreditedRequired = await LoadProgressAsync(fixture, uncreditedRequiredVariantId);
        Assert.AreEqual(LearningInteractionMode.Reading, uncreditedRequired.InteractionMode);
        Assert.AreEqual(0, uncreditedRequired.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, uncreditedRequired.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, uncreditedRequired.ConsecutiveTypingFailureCount);
        Assert.IsNull(uncreditedRequired.LastAssessedAtUtc);
        Assert.IsFalse(uncreditedRequired.MasteryReviewExtensionScheduled);
        Assert.IsFalse(uncreditedRequired.IsMastered);
        Assert.AreEqual(
            uncreditedRequiredSinceUtc.Ticks,
            Schema8Utc.Normalize(uncreditedRequired.CreatedAtUtc).Ticks);

        var acceptedOnly = await LoadProgressAsync(fixture, acceptedOnlyVariantId);
        Assert.AreEqual(LearningInteractionMode.Typing, acceptedOnly.InteractionMode);
        Assert.AreEqual(2, acceptedOnly.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(1, acceptedOnly.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(1, acceptedOnly.ConsecutiveTypingFailureCount);
        Assert.IsTrue(acceptedOnly.MasteryReviewExtensionScheduled);
        Assert.IsTrue(acceptedOnly.IsMastered);
        Assert.AreEqual(0, acceptedOnly.ReplayVersion);
        Assert.AreEqual(acceptedOnlyCreatedAt.Ticks, Schema8Utc.Normalize(acceptedOnly.CreatedAtUtc).Ticks);
        Assert.AreEqual(acceptedOnlyCreatedAt.Ticks, Schema8Utc.Normalize(acceptedOnly.UpdatedAtUtc).Ticks);
    }

    private static async Task<int> AddFutureDueUnrelatedQueueRowAsync(Fixture fixture)
    {
        var queueItemId = 0;
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO SenseAnswerVariantAssignments (
                    StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                    RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('schema13-future-assignment', ?, 1, ?, 0, 1, ?, ?, ?)
                """,
                fixture.SenseId,
                fixture.TargetAnswerVariantId,
                fixture.LegacyDueAtUtc,
                fixture.LegacyDueAtUtc,
                fixture.LegacyDueAtUtc);
            connection.Execute(
                """
                INSERT INTO LearningCards (
                    WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                    EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, 1, 2, ?, 3, 2.5, 1, 0, ?, ?)
                """,
                fixture.WordId,
                fixture.SenseId,
                fixture.MeaningId,
                ReviewTime.AddDays(-30),
                fixture.LegacyDueAtUtc,
                fixture.LegacyDueAtUtc);
            var cardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            var reviewedAtUtc = new DateTimeOffset(ReviewTime, TimeSpan.Zero);
            var reviewEvent = new Fsrs6ReviewEvent(reviewedAtUtc, ReviewRating.Good);
            var scheduled = new Fsrs6Scheduler().Schedule(
                Fsrs6Card.New(), ReviewRating.Good, reviewedAtUtc);
            FsrsReviewPersistenceCoordinator.PersistReview(
                connection,
                cardId,
                "schema13-future-card-review",
                reviewEvent,
                scheduled);
            Schema8LearningRepository.InsertQueueRow(
                connection,
                fixture.SessionId,
                cardId,
                queueOrder: 1,
                isDueCard: false,
                targetAnswerVariantId: fixture.TargetAnswerVariantId);
            queueItemId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                "UPDATE LearningSessions SET TotalCards = 2 WHERE Id = ?",
                fixture.SessionId);
        });
        return queueItemId;
    }

    private static async Task<List<QueueAttemptRow>> LoadQueueAttemptRowsAsync(Fixture fixture) =>
        await fixture.DatabaseFixture.Connection.QueryAsync<QueueAttemptRow>(
            """
            SELECT Id, CardId, QueueOrder, IsAgainRepeat, IsCompleted, TargetAnswerVariantId, StableId
            FROM LearningSessionCards
            WHERE SessionId = ?
            ORDER BY QueueOrder, Id
            """,
            fixture.SessionId);

    private static Task ConfigureMeaningToTermAsync(Fixture fixture) =>
        fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "UPDATE LearningCards SET Direction = ? WHERE Id = ?",
                (int)CardDirection.MeaningToTerm,
                fixture.CardId);
            connection.Execute(
                "UPDATE SenseAnswerVariantAssignments SET CardDirection = ? WHERE AnswerVariantId = ?",
                (int)CardDirection.MeaningToTerm,
                fixture.TargetAnswerVariantId);
            connection.Execute(
                "UPDATE LearningSessionCards SET AnswerRevealed = 0 WHERE Id = ?",
                fixture.QueueItemId);
        });

    private static async Task<int> AddIncompleteQueueRowForSameCardAsync(Fixture fixture)
    {
        var queueItemId = 0;
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            Schema8LearningRepository.InsertQueueRow(
                connection,
                fixture.SessionId,
                fixture.CardId,
                queueOrder: 1,
                isDueCard: true,
                targetAnswerVariantId: fixture.TargetAnswerVariantId);
            queueItemId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                "UPDATE LearningSessions SET TotalCards = 2 WHERE Id = ?",
                fixture.SessionId);
        });
        return queueItemId;
    }

    private static Task AddSiblingSenseAsync(Fixture fixture) =>
        fixture.DatabaseFixture.Connection.ExecuteAsync(
            """
            INSERT INTO Senses (
                StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('schema13-sibling-sense', ?, 'en', 'en', 0, ?, ?)
            """,
            fixture.WordId,
            fixture.LegacyDueAtUtc,
            fixture.LegacyDueAtUtc);

    private static Task<int> CountRowsAsync(Fixture fixture, string tableName) =>
        fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}");

    private static async Task<string[]> LoadFactualHistoryFactsAsync(Fixture fixture) =>
        (await fixture.DatabaseFixture.Connection.QueryAsync<ValueRow>(
            """
            SELECT StableId||'|'||CardId||'|'||SequenceNumber||'|'||Rating||'|'||ReviewedAtUtc AS Value
            FROM FsrsReviewHistoryEntries
            ORDER BY CardId, SequenceNumber
            """)).Select(row => row.Value).ToArray();

    private static async Task<string[]> LoadFsrsStateFactsAsync(Fixture fixture) =>
        (await fixture.DatabaseFixture.Connection.QueryAsync<ValueRow>(
            """
            SELECT CardId||'|'||State||'|'||quote(Stability)||'|'||quote(Difficulty)||'|'||
                   quote(LastReviewedAtUtc)||'|'||quote(StepIndex)||'|'||quote(DueAtUtc) AS Value
            FROM FsrsCardStates
            ORDER BY CardId
            """)).Select(row => row.Value).ToArray();

    private static async Task<string[]> LoadSemanticGraphCountsAsync(Fixture fixture)
    {
        var counts = new List<string>();
        foreach (var table in new[] { "Words", "Senses", "Meanings", "AnswerVariants", "LearningCards" })
        {
            counts.Add($"{table}|{await CountRowsAsync(fixture, table)}");
        }
        return [.. counts];
    }

    private static LearningService CreateLearningService(
        Fixture fixture,
        IClock clock,
        IFsrs6SchedulingService fsrsSchedulingService,
        LearningMode? learningMode = null)
    {
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture.DatabaseFixture);
        return new LearningService(
            database,
            new ThrowingLegacyScheduler(),
            new SpellingAnswerComparer(),
            clock,
            learningMode.HasValue ? new FixedAppSettings(learningMode.Value) : null,
            null,
            null,
            fsrsSchedulingService);
    }

    private static CardSchedule LegacySnapshot(DateTime dueAtUtc, int intervalDays) => new(
        CardState.Review,
        dueAtUtc,
        intervalDays,
        2.5,
        0,
        0,
        null,
        null);

    private static async Task AddHistoricalInteractionAsync(
        Fixture fixture,
        int ordinal,
        DateTime reviewedAtUtc,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        int targetAnswerVariantId,
        int? matchedAnswerVariantId,
        CardSchedule legacySnapshot)
    {
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                UPDATE LearningCards
                SET State = ?, DueAtUtc = ?, IntervalDays = ?, EaseFactor = ?, SuccessfulReviewCount = ?,
                    LapseCount = ?, LastReviewedAtUtc = ?, LastRating = ?
                WHERE Id = ?
                """,
                (int)legacySnapshot.State,
                legacySnapshot.DueAtUtc,
                legacySnapshot.IntervalDays,
                legacySnapshot.EaseFactor,
                legacySnapshot.SuccessfulReviewCount,
                legacySnapshot.LapseCount,
                legacySnapshot.LastReviewedAtUtc,
                legacySnapshot.LastRating,
                fixture.CardId);
            var current = FsrsCardStateRepository.Load(connection, fixture.CardId)
                ?? throw new AssertFailedException("Schema-13 fixture has no FSRS card state.");
            var reviewedAtOffset = new DateTimeOffset(reviewedAtUtc, TimeSpan.Zero);
            var next = new Fsrs6Scheduler().Schedule(current, rating, reviewedAtOffset);
            FsrsReviewPersistenceCoordinator.PersistReview(
                connection,
                fixture.CardId,
                ordinal.ToString("x32"),
                new Fsrs6ReviewEvent(reviewedAtOffset, rating),
                next);
            var reviewId = Schema8LearningRepository.InsertSchema13CompatibilityReview(
                connection,
                fixture.CardId,
                fixture.SessionId,
                rating,
                wasTypedAnswer,
                wasCorrect,
                reviewedAtUtc,
                legacySnapshot);
            Schema8LearningRepository.UpdateReviewAttribution(
                connection, reviewId, targetAnswerVariantId, matchedAnswerVariantId);
        });
    }

    private static async Task<int> AddAnswerVariantAsync(
        Fixture fixture,
        string stableId,
        string text,
        AnswerVariantRequirement? requirement)
    {
        var variantId = 0;
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO AnswerVariants (
                    StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                    CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, 'en', ?, ?, ?, ?, ?)
                """,
                stableId,
                fixture.SenseId,
                text,
                text,
                fixture.MeaningId,
                fixture.LegacyDueAtUtc,
                fixture.LegacyDueAtUtc);
            variantId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            if (requirement.HasValue)
            {
                connection.Execute(
                    """
                    INSERT INTO SenseAnswerVariantAssignments (
                        StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                        RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, 0, ?, ?, 0, ?, ?, ?)
                    """,
                    $"{stableId}-assignment",
                    fixture.SenseId,
                    variantId,
                    (int)requirement.Value,
                    requirement == AnswerVariantRequirement.Required ? fixture.LegacyDueAtUtc : null,
                    fixture.LegacyDueAtUtc,
                    fixture.LegacyDueAtUtc);
            }
        });
        return variantId;
    }

    private static async Task CompleteSeedSessionAsync(Fixture fixture)
    {
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "UPDATE LearningSessionCards SET IsCompleted = 1, Rating = ?, CompletedAtUtc = ? WHERE Id = ?",
                (int)ReviewRating.Good,
                ReviewTime.AddDays(-399),
                fixture.QueueItemId);
            connection.Execute(
                "UPDATE LearningSessions SET Status = 1, CompletedCards = TotalCards, CompletedAtUtc = ?, UpdatedAtUtc = ? WHERE Id = ?",
                ReviewTime.AddDays(-399),
                ReviewTime.AddDays(-399),
                fixture.SessionId);
        });
    }

    private static Task SetRequiredSinceAsync(Fixture fixture, DateTime requiredSinceUtc) =>
        fixture.DatabaseFixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET RequiredSinceUtc = ? WHERE AnswerVariantId = ?",
            requiredSinceUtc,
            fixture.TargetAnswerVariantId);

    private static async Task<AnswerVariantProgressRow> LoadProgressAsync(Fixture fixture, int answerVariantId) =>
        (await fixture.DatabaseFixture.Connection.QueryAsync<AnswerVariantProgressRow>(
            "SELECT * FROM AnswerVariantProgress WHERE CardId = ? AND AnswerVariantId = ?",
            fixture.CardId,
            answerVariantId)).Single();

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        const int legacyIntervalDays = 0;
        const double legacyEaseFactor = 2.5;
        var legacyDueAtUtc = ReviewTime.AddDays(-5);
        var ids = new int[5];
        string queueStableId = string.Empty;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO Words (
                    Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                    TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                    ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                    MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
                VALUES ('en', 'factual', 'factual', 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
                """,
                legacyDueAtUtc,
                legacyDueAtUtc);
            ids[0] = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO Senses (
                    StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('schema13-factual-sense', ?, 'en', 'en', 0, ?, ?)
                """,
                ids[0],
                legacyDueAtUtc,
                legacyDueAtUtc);
            ids[1] = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO Meanings (
                    WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                    GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                    AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                    ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
                VALUES (?, ?, 'en', 'en', 'factual', 'factual', '', 0, 'fact', 'fact', '', '', '[]',
                        'fact', 'test', 'test', 'factual', 'test', 1, ?, ?, ?, 'schema13-factual-meaning')
                """,
                ids[0],
                ids[1],
                legacyDueAtUtc,
                legacyDueAtUtc,
                legacyDueAtUtc);
            ids[2] = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", ids[2], ids[1]);

            connection.Execute(
                """
                INSERT INTO LearningCards (
                    WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                    EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, 0, 0, ?, ?, ?, 0, 0, ?, ?)
                """,
                ids[0],
                ids[1],
                ids[2],
                legacyDueAtUtc,
                legacyIntervalDays,
                legacyEaseFactor,
                legacyDueAtUtc,
                legacyDueAtUtc);
            ids[3] = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO AnswerVariants (
                    StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                    CreatedAtUtc, UpdatedAtUtc)
                VALUES ('schema13-factual-variant', ?, 'en', 'fact', 'fact', ?, ?, ?)
                """,
                ids[1],
                ids[2],
                legacyDueAtUtc,
                legacyDueAtUtc);
            ids[4] = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO SenseAnswerVariantAssignments (
                    StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                    RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('schema13-factual-assignment', ?, 0, ?, 0, 1, ?, ?, ?)
                """,
                ids[1],
                ids[4],
                legacyDueAtUtc,
                legacyDueAtUtc,
                legacyDueAtUtc);

            var sessionId = Schema8LearningRepository.InsertSession(connection, ReviewTime.AddMinutes(-10), 1);
            Schema8LearningRepository.InsertQueueRow(connection, sessionId, ids[3], 0, true, ids[4]);
            var queueItemId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute("UPDATE LearningSessionCards SET AnswerRevealed = 1 WHERE Id = ?", queueItemId);
            queueStableId = connection.ExecuteScalar<string>(
                "SELECT StableId FROM LearningSessionCards WHERE Id = ?", queueItemId);
            ids[0] = sessionId;
            ids[1] = queueItemId;
        });

        var wordId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT WordId FROM LearningCards WHERE Id = ?", ids[3]);
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM LearningCards WHERE Id = ?", ids[3]);
        var meaningId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE Id = ?", ids[3]);
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        return new Fixture(
            fixture,
            wordId,
            senseId,
            meaningId,
            ids[3],
            ids[4],
            ids[0],
            ids[1],
            queueStableId,
            legacyDueAtUtc,
            legacyIntervalDays,
            legacyEaseFactor);
    }

    private static async Task<string[]> CaptureTransactionFactsAsync(Fixture fixture)
    {
        string[] result = [];
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            var facts = new List<string>();
            facts.AddRange(connection.Query<ValueRow>(
                "SELECT 'history|'||StableId||'|'||CardId||'|'||SequenceNumber||'|'||Rating||'|'||ReviewedAtUtc AS Value FROM FsrsReviewHistoryEntries ORDER BY Id")
                .Select(row => row.Value));
            facts.AddRange(connection.Query<ValueRow>(
                "SELECT 'state|'||CardId||'|'||State||'|'||quote(Stability)||'|'||quote(Difficulty)||'|'||quote(LastReviewedAtUtc)||'|'||quote(StepIndex)||'|'||quote(DueAtUtc) AS Value FROM FsrsCardStates ORDER BY CardId")
                .Select(row => row.Value));
            facts.AddRange(connection.Query<ValueRow>(
                "SELECT 'review|'||Id||'|'||CardId||'|'||SessionId||'|'||Rating||'|'||ReviewedAtUtc AS Value FROM LearningReviews ORDER BY Id")
                .Select(row => row.Value));
            facts.AddRange(connection.Query<ValueRow>(
                "SELECT 'progress|'||Id||'|'||CardId||'|'||AnswerVariantId||'|'||InteractionMode||'|'||ConsecutiveReadingSuccessCount||'|'||ConsecutiveTypingSuccessCount||'|'||ConsecutiveTypingFailureCount||'|'||quote(LastAssessedAtUtc)||'|'||MasteryReviewExtensionScheduled||'|'||IsMastered AS Value FROM AnswerVariantProgress ORDER BY Id")
                .Select(row => row.Value));
            facts.AddRange(connection.Query<ValueRow>(
                "SELECT 'queue|'||Id||'|'||StableId||'|'||IsCompleted||'|'||quote(Rating)||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessionCards ORDER BY Id")
                .Select(row => row.Value));
            facts.AddRange(connection.Query<ValueRow>(
                "SELECT 'session|'||Id||'|'||Status||'|'||TotalCards||'|'||CompletedCards||'|'||AgainCount||'|'||HardCount||'|'||GoodCount||'|'||EasyCount||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessions ORDER BY Id")
                .Select(row => row.Value));
            result = [.. facts];
        });
        return result;
    }

    private static void AssertExactDouble(double? expected, double? actual, string name)
    {
        Assert.AreEqual(expected.HasValue, actual.HasValue, $"{name} nullability differs.");
        if (expected.HasValue)
        {
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(expected.Value),
                BitConverter.DoubleToInt64Bits(actual!.Value),
                $"{name} must round-trip with exact binary64 bits.");
        }
    }

    private sealed record Fixture(
        Schema7Fixture DatabaseFixture,
        int WordId,
        int SenseId,
        int MeaningId,
        int CardId,
        int TargetAnswerVariantId,
        int SessionId,
        int QueueItemId,
        string QueueStableId,
        DateTime LegacyDueAtUtc,
        int LegacyIntervalDays,
        double LegacyEaseFactor) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => DatabaseFixture.DisposeAsync();
    }

    private sealed class CountingClock(DateTime utcNow) : IClock
    {
        public int ReadCount { get; private set; }

        public DateTime UtcNow
        {
            get
            {
                ReadCount++;
                return utcNow;
            }
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class FixedAppSettings(LearningMode learningMode) : IAppSettingsService
    {
        public int PreparationLimit => PreparationLimitPolicy.DefaultLimit;
        public IReadOnlyList<int> SupportedPreparationLimits => [PreparationLimitPolicy.DefaultLimit];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => learningMode;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => false;
        public LearningTimezoneMode LearningTimezoneMode => LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId => null;
        public int LearningDayCutoffMinutes => LearningDayConfiguration.DefaultCutoffMinutes;
        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool enabled) => throw new NotSupportedException();
        public void SetLearningTimezoneMode(LearningTimezoneMode mode) => throw new NotSupportedException();
        public void SetExplicitLearningTimezoneId(string? timezoneId) => throw new NotSupportedException();
        public void SetLearningDayCutoffMinutes(int minutes) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class CountingFsrsSchedulingService(IFsrs6SchedulingService inner) : IFsrs6SchedulingService
    {
        public int ScheduleCallCount { get; private set; }

        public Fsrs6ScheduleProjection Schedule(Fsrs6ScheduleProjection currentProjection, ReviewRating rating)
        {
            ScheduleCallCount++;
            return inner.Schedule(currentProjection, rating);
        }

        public Fsrs6ScheduleProjection Schedule(
            Fsrs6ScheduleProjection currentProjection,
            ReviewRating rating,
            DateTimeOffset reviewedAtUtc)
        {
            ScheduleCallCount++;
            return inner.Schedule(currentProjection, rating, reviewedAtUtc);
        }

        public Fsrs6ScheduleProjection Replay(
            Fsrs6ScheduleProjection initialProjection,
            IEnumerable<Fsrs6ReviewFact> reviewFacts) =>
            inner.Replay(initialProjection, reviewFacts);
    }

    private sealed class ThrowingLegacyScheduler : ISpacedRepetitionScheduler
    {
        public CardSchedule Schedule(CardSchedule current, ReviewRating rating, DateTime reviewedAtUtc) =>
            throw new AssertFailedException("Schema 13 must never invoke the legacy scheduler.");
    }

    private sealed class ReviewRow
    {
        public int CardId { get; set; }
        public int SessionId { get; set; }
        public ReviewRating Rating { get; set; }
        public bool WasTypedAnswer { get; set; }
        public bool WasCorrect { get; set; }
        public DateTime ReviewedAtUtc { get; set; }
        public DateTime DueAtUtc { get; set; }
        public int IntervalDays { get; set; }
        public double EaseFactor { get; set; }
        public int? TargetAnswerVariantId { get; set; }
        public int? MatchedAnswerVariantId { get; set; }
    }

    private sealed class LegacyCardRow
    {
        public CardState State { get; set; }
        public DateTime DueAtUtc { get; set; }
        public int IntervalDays { get; set; }
        public double EaseFactor { get; set; }
        public int SuccessfulReviewCount { get; set; }
        public int LapseCount { get; set; }
        public DateTime? LastReviewedAtUtc { get; set; }
        public ReviewRating? LastRating { get; set; }
    }

    private sealed class ProgressRow
    {
        public int CardId { get; set; }
        public int AnswerVariantId { get; set; }
        public LearningInteractionMode InteractionMode { get; set; }
        public int ConsecutiveReadingSuccessCount { get; set; }
        public int ConsecutiveTypingSuccessCount { get; set; }
        public int ConsecutiveTypingFailureCount { get; set; }
        public DateTime? LastAssessedAtUtc { get; set; }
        public bool MasteryReviewExtensionScheduled { get; set; }
        public bool IsMastered { get; set; }
    }

    private sealed class QueueRow
    {
        public bool IsCompleted { get; set; }
        public ReviewRating? Rating { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string StableId { get; set; } = string.Empty;
    }

    private sealed class QueueAttemptRow
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public int QueueOrder { get; set; }
        public bool IsAgainRepeat { get; set; }
        public bool IsCompleted { get; set; }
        public int TargetAnswerVariantId { get; set; }
        public string StableId { get; set; } = string.Empty;
    }

    private sealed class HistoryAttemptRow
    {
        public string StableId { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
    }

    private sealed class SessionRow
    {
        public LearningSessionStatus Status { get; set; }
        public int TotalCards { get; set; }
        public int CompletedCards { get; set; }
        public int AgainCount { get; set; }
        public int HardCount { get; set; }
        public int GoodCount { get; set; }
        public int EasyCount { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    private sealed class ValueRow
    {
        public string Value { get; set; } = string.Empty;
    }
}
