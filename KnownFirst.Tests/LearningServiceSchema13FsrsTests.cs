using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
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

    private static LearningService CreateLearningService(
        Fixture fixture,
        IClock clock,
        IFsrs6SchedulingService fsrsSchedulingService)
    {
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture.DatabaseFixture);
        return new LearningService(
            database,
            new ThrowingLegacyScheduler(),
            new SpellingAnswerComparer(),
            clock,
            null,
            null,
            null,
            fsrsSchedulingService);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);

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
