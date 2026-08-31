using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Text;
using KnownFirst.Data;
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
public sealed class Schema13LearningRuntimeRepositoryTests
{
    private static readonly DateTimeOffset ReviewTime =
        new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task LearningCapability_ValidSchema13_ReturnsSchema13Capability()
    {
        await using var fixture = await CreateSchema13Async(seedCard: false);

        LearningSchemaCapabilityResult? result = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            result = LearningSchemaCapability.Resolve(connection));

        Assert.IsInstanceOfType<LearningSchema13CapabilityResult>(result);
    }

    [TestMethod]
    public async Task PreparationCapability_ValidSchema13_ReturnsSchema13Capability()
    {
        await using var fixture = await CreateSchema13Async(seedCard: false);

        PreparationSchemaCapabilityResult? result = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            result = PreparationSchemaCapability.Resolve(connection));

        Assert.IsInstanceOfType<PreparationSchema13CapabilityResult>(result);
    }

    [TestMethod]
    public async Task Capabilities_Schema13WithInvalidShape_FailClosed()
    {
        await using var fixture = await CreateSchema13Async(seedCard: false);
        await fixture.Connection.ExecuteAsync("DROP INDEX IX_FsrsCardStates_State_DueAtUtc");

        await Assert.ThrowsExactlyAsync<LearningSchemaCapabilityException>(() =>
            fixture.Connection.RunInTransactionAsync(connection => LearningSchemaCapability.Resolve(connection)));
        await Assert.ThrowsExactlyAsync<PreparationSchemaCapabilityException>(() =>
            fixture.Connection.RunInTransactionAsync(connection => PreparationSchemaCapability.Resolve(connection)));
    }

    [TestMethod]
    public async Task Repository_UsesFsrsProjectionWhenLegacySchedulingColumnsConflict()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        var cardId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards");
        var wordId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT WordId FROM LearningCards");

        Fsrs6Card scheduled = null!;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var reviewEvent = new Fsrs6ReviewEvent(ReviewTime, ReviewRating.Good);
            FsrsReviewHistoryRepository.AppendEvent(connection, cardId, "slice2-review", reviewEvent);
            scheduled = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [reviewEvent]);
            FsrsCardStateRepository.Save(connection, cardId, scheduled);
            connection.Execute(
                "UPDATE LearningCards SET State = ?, DueAtUtc = ? WHERE Id = ?",
                (int)CardState.New,
                ReviewTime.AddYears(5).UtcDateTime,
                cardId);
        });

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var cards = Schema13LearningRepository.LoadAllCards(connection);

            Assert.HasCount(1, cards);
            Assert.AreEqual(Fsrs6CardState.Review, cards[0].State);
            Assert.AreEqual(scheduled.DueAtUtc, cards[0].DueAtUtc);
            Assert.AreEqual(1, Schema13LearningRepository.CountDueCards(connection, ReviewTime.AddYears(1)));
            Assert.AreEqual(0, Schema13LearningRepository.CountNewWords(connection));
            Assert.AreEqual(scheduled.DueAtUtc, Schema13LearningRepository.SelectNextDueAtUtc(connection));
            Assert.AreEqual(wordId, cards[0].WordId);
        });
    }

    [TestMethod]
    public async Task Repository_FsrsNewWinsOverLegacyDueReviewState()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET State = ?, DueAtUtc = ?",
            (int)CardState.Review,
            ReviewTime.AddDays(-1).UtcDateTime);

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(0, Schema13LearningRepository.CountDueCards(connection, ReviewTime));
            Assert.AreEqual(1, Schema13LearningRepository.CountNewWords(connection));
            Assert.IsNull(Schema13LearningRepository.SelectNextDueAtUtc(connection));
        });
    }

    [TestMethod]
    public async Task Repository_MissingFsrsState_FailsClosedWithoutLegacyFallback()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        await fixture.Connection.ExecuteAsync("DELETE FROM FsrsCardStates");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Connection.RunInTransactionAsync(connection =>
                Schema13LearningRepository.LoadAllCards(connection)));
    }

    [TestMethod]
    public async Task InsertCleanNewState_DuplicateAttemptFailsClosed()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        var cardId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards");

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            fixture.Connection.RunInTransactionAsync(connection =>
            {
                Schema13LearningRepository.InsertCleanNewState(connection, cardId);
            }));
    }

    [TestMethod]
    public async Task TextReviewDiagnostics_Schema13_UsesFsrsSchedulingProjection()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        var scheduled = await SetFsrsReviewWithConflictingLegacyNewAsync(fixture);
        var service = new TextReviewService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var diagnostics = await service.GetDiagnosticsAsync();

        Assert.HasCount(1, diagnostics.LearningCards);
        var card = diagnostics.LearningCards[0];
        Assert.AreEqual(CardState.Review, card.State);
        Assert.AreEqual<DateTime?>(scheduled.DueAtUtc?.UtcDateTime, card.DueAtUtc);
        Assert.IsNull(card.IntervalDays, "Schema-13 diagnostics must not expose the legacy interval as authority.");
        Assert.IsNull(card.EaseFactor, "Schema-13 diagnostics must not expose the legacy ease factor as authority.");
        Assert.IsNull(card.LastRating, "Schema-13 diagnostics must not expose the legacy last rating as authority.");
    }

    [TestMethod]
    public async Task LearningSessionSelection_Schema13_FsrsDueWinsOverLegacyNewFutureState()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        var scheduled = await SetFsrsReviewWithConflictingLegacyNewAsync(fixture);
        var service = CreateLearningService(fixture, scheduled.DueAtUtc!.Value.UtcDateTime.AddMinutes(1));

        var load = await service.GetOrStartAsync();

        Assert.IsNotNull(load.Card);
        Assert.AreEqual(CardState.Review, load.Card.State);
        Assert.IsTrue(load.Card.CardId > 0);
    }

    [TestMethod]
    public async Task LearningSummary_Schema13_NextDueComesFromFsrsProjection()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        var scheduled = await SetFsrsReviewWithConflictingLegacyNewAsync(fixture);
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var sessionId = Schema8LearningRepository.InsertSession(
                connection,
                ReviewTime.AddDays(-1).UtcDateTime,
                totalCards: 0);
            connection.Execute(
                "UPDATE LearningSessions SET Status = ?, CompletedAtUtc = ?, UpdatedAtUtc = ? WHERE Id = ?",
                (int)LearningSessionStatus.Completed,
                ReviewTime.AddHours(-1).UtcDateTime,
                ReviewTime.AddHours(-1).UtcDateTime,
                sessionId);
        });
        var service = CreateLearningService(fixture, ReviewTime.UtcDateTime);

        var load = await service.GetOrStartAsync();

        Assert.IsNull(load.Card);
        Assert.IsNotNull(load.CompletedSummary);
        Assert.AreEqual(scheduled.DueAtUtc?.UtcDateTime, load.CompletedSummary.NextDueAtUtc);
        Assert.AreNotEqual(ReviewTime.AddYears(5).UtcDateTime, load.CompletedSummary.NextDueAtUtc);
    }

    [TestMethod]
    public async Task Dashboard_Schema13_UsesValidatedCapabilityWithoutLegacyFallback()
    {
        await using var fixture = await CreateSchema13Async(seedCard: true);
        var service = new DashboardService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture));

        var statistics = await service.GetStatisticsAsync();

        Assert.AreEqual(1, statistics.PreparedAndLearningWordCount);
    }

    private static async Task<Schema7Fixture> CreateSchema13Async(bool seedCard)
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        if (seedCard)
        {
            await fixture.Connection.RunInTransactionAsync(SeedGraph);
        }

        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        return fixture;
    }

    private static async Task<Fsrs6Card> SetFsrsReviewWithConflictingLegacyNewAsync(
        Schema7Fixture fixture)
    {
        Fsrs6Card scheduled = null!;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var cardId = connection.ExecuteScalar<int>("SELECT Id FROM LearningCards");
            var reviewEvent = new Fsrs6ReviewEvent(ReviewTime, ReviewRating.Good);
            FsrsReviewHistoryRepository.AppendEvent(connection, cardId, "slice2-integration-review", reviewEvent);
            scheduled = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [reviewEvent]);
            FsrsCardStateRepository.Save(connection, cardId, scheduled);
            connection.Execute(
                "UPDATE LearningCards SET State = ?, DueAtUtc = ?, IntervalDays = 999, EaseFactor = 9.9, LastRating = ? WHERE Id = ?",
                (int)CardState.New,
                ReviewTime.AddYears(5).UtcDateTime,
                (int)ReviewRating.Easy,
                cardId);
        });
        return scheduled;
    }

    private static LearningService CreateLearningService(Schema7Fixture fixture, DateTime nowUtc) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        new FakeClock(nowUtc));

    private static void SeedGraph(SQLiteConnection connection)
    {
        var now = ReviewTime.UtcDateTime;
        connection.Execute(
            """
            INSERT INTO Words (
                Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
            VALUES ('en', 'projection', 'projection', 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            now,
            now);
        var wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO Senses (
                StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('slice2-sense', ?, 'en', 'en', 0, ?, ?)
            """,
            wordId,
            now,
            now);
        var senseId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO Meanings (
                WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
            VALUES (?, ?, 'en', 'en', 'projection', 'projection', '', 0, 'projection', 'projection', '', '', '[]',
                    'projection', 'test', 'test', 'projection', 'test', 1, ?, ?, ?, 'slice2-meaning')
            """,
            wordId,
            senseId,
            now,
            now,
            now);
        var meaningId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
        connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

        connection.Execute(
            """
            INSERT INTO LearningCards (
                WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, 0, 0, ?, 0, 2.5, 0, 0, ?, ?)
            """,
            wordId,
            senseId,
            meaningId,
            now,
            now,
            now);

        connection.Execute(
            """
            INSERT INTO AnswerVariants (
                StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                CreatedAtUtc, UpdatedAtUtc)
            VALUES ('slice2-variant', ?, 'en', 'projection', 'projection', ?, ?, ?)
            """,
            senseId,
            meaningId,
            now,
            now);
        var variantId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO SenseAnswerVariantAssignments (
                StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('slice2-assignment', ?, 0, ?, 0, 1, ?, ?, ?)
            """,
            senseId,
            variantId,
            now,
            now,
            now);
    }
}
