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
public sealed class LearningServiceCardDirectionBehaviorTests
{
    private static readonly DateTime ReviewTime =
        new(2026, 8, 30, 9, 15, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task TermToMeaning_ConfiguredLearningModeTyping_ResolvesAsReading_RevealsAndRatesWithoutSpelling()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 0 WHERE Id = ?",
            fixture.QueueItemId);

        var clock = new FixedClock(ReviewTime);
        var fsrs = new Fsrs6SchedulingService(clock);
        var service = CreateLearningService(fixture, clock, fsrs, LearningMode.Typing);

        // 1. Projection must report Reading interaction mode for TermToMeaning even when mode is Typing
        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(CardDirection.TermToMeaning, load.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Reading, load.Card.InteractionMode);

        // 2. RevealAnswerAsync must succeed directly without throwing InvalidQueueState
        await service.RevealAnswerAsync(fixture.QueueItemId);

        // 3. RateAsync must succeed without requiring spelling check
        var rated = await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);
        Assert.IsNotNull(rated);

        // 4. Persisted review fact must truthfully record WasTypedAnswer = false
        var reviews = await fixture.DatabaseFixture.Connection.QueryAsync<ReviewRow>(
            "SELECT * FROM LearningReviews WHERE CardId = ?", fixture.CardId);
        Assert.HasCount(1, reviews);
        Assert.IsFalse(reviews[0].WasTypedAnswer, "TermToMeaning reviews must never be recorded as typed answers.");
        Assert.IsTrue(reviews[0].WasCorrect);
        Assert.AreEqual(ReviewRating.Good, reviews[0].Rating);
    }

    [TestMethod]
    public async Task TermToMeaning_AutomaticProgressionInTypingState_ResolvesAsReading_RevealsAndRates()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET RequiredSinceUtc = ? WHERE AnswerVariantId = ?",
            ReviewTime.AddDays(-30), fixture.TargetAnswerVariantId);
        // Add 2 consecutive recall successes in historical reviews so that progression state reaches Typing
        await AddHistoricalInteractionAsync(
            fixture, 1, ReviewTime.AddDays(-20), ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            fixture.TargetAnswerVariantId, null);
        await AddHistoricalInteractionAsync(
            fixture, 2, ReviewTime.AddDays(-19), ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true,
            fixture.TargetAnswerVariantId, null);

        // Complete the initial fixture session so that GetOrStartAsync creates a fresh active session
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "UPDATE LearningSessionCards SET IsCompleted = 1, Rating = 3, CompletedAtUtc = ? WHERE Id = ?",
                ReviewTime.AddDays(-19), fixture.QueueItemId);
            connection.Execute(
                "UPDATE LearningSessions SET Status = 1, CompletedCards = 1, CompletedAtUtc = ?, UpdatedAtUtc = ? WHERE Id = ?",
                ReviewTime.AddDays(-19), ReviewTime.AddDays(-19), fixture.SessionId);
        });

        var clock = new FixedClock(ReviewTime);
        var fsrs = new Fsrs6SchedulingService(clock);
        var service = CreateLearningService(fixture, clock, fsrs, LearningMode.Automatic);

        // 1. Projection must report Reading interaction mode for TermToMeaning even though progression reached Typing
        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(CardDirection.TermToMeaning, load.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Reading, load.Card.InteractionMode);

        // 2. RevealAnswerAsync must succeed
        await service.RevealAnswerAsync(load.Card.QueueItemId);

        // 3. RateAsync must succeed
        await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);

        // 4. Persisted review fact must record WasTypedAnswer = false
        var reviews = await fixture.DatabaseFixture.Connection.QueryAsync<ReviewRow>(
            "SELECT * FROM LearningReviews WHERE CardId = ? ORDER BY Id DESC", fixture.CardId);
        Assert.HasCount(3, reviews);
        Assert.IsFalse(reviews[0].WasTypedAnswer);
        Assert.IsTrue(reviews[0].WasCorrect);
    }

    [TestMethod]
    public async Task MeaningToTerm_ReadingMode_RevealsAndRatesWithWasTypedAnswerFalse()
    {
        await using var fixture = await CreateFixtureAsync();
        await ConfigureMeaningToTermAsync(fixture);
        await fixture.DatabaseFixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 0 WHERE Id = ?",
            fixture.QueueItemId);

        var clock = new FixedClock(ReviewTime);
        var fsrs = new Fsrs6SchedulingService(clock);
        var service = CreateLearningService(fixture, clock, fsrs, LearningMode.Reading);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(CardDirection.MeaningToTerm, load.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Reading, load.Card.InteractionMode);

        await service.RevealAnswerAsync(fixture.QueueItemId);
        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        var reviews = await fixture.DatabaseFixture.Connection.QueryAsync<ReviewRow>(
            "SELECT * FROM LearningReviews WHERE CardId = ?", fixture.CardId);
        Assert.HasCount(1, reviews);
        Assert.IsFalse(reviews[0].WasTypedAnswer);
        Assert.IsTrue(reviews[0].WasCorrect);
    }

    [TestMethod]
    public async Task MeaningToTerm_TypingMode_RequiresSpellingCheckAndPersistsWasTypedAnswerTrue()
    {
        await using var fixture = await CreateFixtureAsync();
        await ConfigureMeaningToTermAsync(fixture);

        var clock = new FixedClock(ReviewTime);
        var fsrs = new Fsrs6SchedulingService(clock);
        var service = CreateLearningService(fixture, clock, fsrs, LearningMode.Typing);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(CardDirection.MeaningToTerm, load.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Typing, load.Card.InteractionMode);

        var spelling = await service.CheckSpellingAsync(fixture.QueueItemId, "fact");
        Assert.IsTrue(spelling.IsCorrect);

        await service.RateAsync(fixture.QueueItemId, ReviewRating.Good);

        var reviews = await fixture.DatabaseFixture.Connection.QueryAsync<ReviewRow>(
            "SELECT * FROM LearningReviews WHERE CardId = ?", fixture.CardId);
        Assert.HasCount(1, reviews);
        Assert.IsTrue(reviews[0].WasTypedAnswer);
        Assert.IsTrue(reviews[0].WasCorrect);
    }

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
        });

    private static async Task AddHistoricalInteractionAsync(
        Fixture fixture,
        int ordinal,
        DateTime reviewedAtUtc,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        int targetAnswerVariantId,
        int? matchedAnswerVariantId)
    {
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
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
            var legacySnapshot = new CardSchedule(
                CardState.Review, reviewedAtUtc, 1, 2.5, 0, 0, null, null);
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

    private static LearningService CreateLearningService(
        Fixture fixture,
        IClock clock,
        IFsrs6SchedulingService fsrs,
        LearningMode learningMode)
    {
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture.DatabaseFixture);
        return new LearningService(
            database,
            new ThrowingLegacyScheduler(),
            new SpellingAnswerComparer(),
            clock,
            new FixedAppSettings(learningMode),
            null,
            null,
            fsrs);
    }

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
            queueStableId);
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
        string QueueStableId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => DatabaseFixture.DisposeAsync();
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
}
