using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13RuntimeIntegrityValidatorTests
{
    private sealed record GraphIds(int WordId, int SenseId, int CardId);

    private static async Task<(Schema7Fixture Fixture, GraphIds Graph)> CreateMigratedDatabaseAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);

        GraphIds graph = null!;
        await fixture.Connection.RunInTransactionAsync(connection => graph = SeedGraph(connection));
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        return (fixture, graph);
    }

    private static GraphIds SeedGraph(SQLiteConnection connection)
    {
        var now = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        connection.Execute(
            """
            INSERT INTO Words (
                Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
            VALUES ('en', 'runtime', 'runtime', 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            now,
            now);
        var wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO Senses (
                StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('runtime-sense', ?, 'en', 'en', 0, ?, ?)
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
            VALUES (?, ?, 'en', 'en', 'runtime', 'runtime', '', 0, 'runtime', 'runtime', '', '', '[]',
                    'runtime', 'test', 'test', 'runtime', 'test', 1, ?, ?, ?, 'runtime-meaning')
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
        var cardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        return new GraphIds(wordId, senseId, cardId);
    }

    private static void PersistRuntimeHistory(
        SQLiteConnection connection,
        int cardId,
        params Fsrs6ReviewEvent[] events)
    {
        for (var index = 0; index < events.Length; index++)
        {
            FsrsReviewHistoryRepository.AppendEvent(
                connection,
                cardId,
                $"runtime-event-{index + 1}",
                events[index]);
        }

        var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), events);
        FsrsCardStateRepository.Save(connection, cardId, replayed);
    }

    private static async Task<Schema13MigrationException> AssertAlreadyAppliedRejectedAsync(
        Schema7Fixture fixture,
        string expectedDetail)
    {
        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-already-applied-shape-invalid", exception.ErrorCode);
        StringAssert.Contains(exception.Message, expectedDetail);
        return exception;
    }

    [TestMethod]
    public async Task AlreadyApplied_AcceptsLegitimateRuntimeHistoryEqualTimestampsAndSenseControl()
    {
        var (fixture, graph) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            var sameTime = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
            await fixture.Connection.RunInTransactionAsync(connection =>
            {
                PersistRuntimeHistory(
                    connection,
                    graph.CardId,
                    new Fsrs6ReviewEvent(sameTime, ReviewRating.Hard),
                    new Fsrs6ReviewEvent(sameTime, ReviewRating.Good));
                SenseLearningControlRepository.Save(
                    connection,
                    graph.SenseId,
                    SenseLearningControl.Default.Stop(sameTime.UtcDateTime));

                Assert.IsFalse(
                    Schema13MigrationIntegrityValidator.Validate(connection, out var migrationDetail));
                StringAssert.Contains(migrationDetail!, "SenseLearningControls must be empty");
            });

            var result = await Schema13DormantMigration.ApplyAsync(fixture.Connection);

            Assert.AreEqual(Schema13MigrationOutcome.AlreadyApplied, result.Outcome);
            Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(2, await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE CardId = ?", graph.CardId));
            Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM SenseLearningControls WHERE SenseId = ?", graph.SenseId));
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsMissingCardState()
    {
        var (fixture, graph) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync("DELETE FROM FsrsCardStates WHERE CardId = ?", graph.CardId);
            await AssertAlreadyAppliedRejectedAsync(fixture, "LearningCards with no FsrsCardStates row");
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsOrphanCardState()
    {
        var (fixture, _) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA foreign_keys = OFF");
            await fixture.Connection.ExecuteAsync(
                "INSERT INTO FsrsCardStates (CardId, State) VALUES (999999, 0)");
            await AssertAlreadyAppliedRejectedAsync(fixture, "FsrsCardStates rows with no matching LearningCard");
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsOrphanReviewHistory()
    {
        var (fixture, _) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA foreign_keys = OFF");
            await fixture.Connection.ExecuteAsync(
                """
                INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('orphan-review', 999999, 1, 2, '2026-08-30T09:00:00.0000000Z')
                """);
            await AssertAlreadyAppliedRejectedAsync(
                fixture,
                "FsrsReviewHistoryEntries rows with no matching LearningCard");
        }
    }

    [DataTestMethod]
    [DataRow("word")]
    [DataRow("sense")]
    public async Task AlreadyApplied_RejectsOrphanLearningControl(string controlKind)
    {
        var (fixture, _) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA foreign_keys = OFF");
            if (controlKind == "word")
            {
                await fixture.Connection.ExecuteAsync(
                    "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (999999, '2026-08-30T09:00:00.0000000Z')");
                await AssertAlreadyAppliedRejectedAsync(
                    fixture,
                    "WordLearningControls rows with no matching Word");
            }
            else
            {
                await fixture.Connection.ExecuteAsync(
                    "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (999999, '2026-08-30T09:00:00.0000000Z')");
                await AssertAlreadyAppliedRejectedAsync(
                    fixture,
                    "SenseLearningControls rows with no matching Sense");
            }
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsHistoryGapAndWhitespaceStableId()
    {
        var (fixture, graph) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA ignore_check_constraints = ON");
            await fixture.Connection.ExecuteAsync(
                """
                INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('runtime-1', ?, 1, 2, '2026-08-30T09:00:00.0000000Z')
                """,
                graph.CardId);
            await fixture.Connection.ExecuteAsync(
                """
                INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('   ', ?, 3, 2, '2026-08-30T09:00:00.0000000Z')
                """,
                graph.CardId);

            var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
                () => Schema13DormantMigration.ApplyAsync(fixture.Connection));
            Assert.IsTrue(
                exception.Message.Contains("StableId", StringComparison.Ordinal)
                || exception.Message.Contains("SequenceNumber 2", StringComparison.Ordinal),
                exception.Message);
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsMissingStableIdUniquenessContract()
    {
        var (fixture, _) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync($"DROP INDEX {Schema13Ddl.FsrsReviewHistoryEntriesStableIdIndexName}");
            await AssertAlreadyAppliedRejectedAsync(
                fixture,
                Schema13Ddl.FsrsReviewHistoryEntriesStableIdIndexName);
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsDecreasingHistoryTimestamps()
    {
        var (fixture, graph) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync(
                """
                INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('runtime-1', ?, 1, 2, '2026-08-30T10:00:00.0000000Z')
                """,
                graph.CardId);
            await fixture.Connection.ExecuteAsync(
                """
                INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('runtime-2', ?, 2, 2, '2026-08-30T09:00:00.0000000Z')
                """,
                graph.CardId);

            await AssertAlreadyAppliedRejectedAsync(fixture, "earlier than previous");
        }
    }

    [DataTestMethod]
    [DataRow("state", "State mismatch")]
    [DataRow("stability", "Stability mismatch")]
    [DataRow("difficulty", "Difficulty mismatch")]
    [DataRow("last-reviewed", "LastReviewedAtUtc mismatch")]
    [DataRow("step-index", "StepIndex mismatch")]
    [DataRow("due", "DueAtUtc mismatch")]
    public async Task AlreadyApplied_RejectsReplayStateMismatchWithExactFields(
        string mutation,
        string expectedDetail)
    {
        var (fixture, graph) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            var reviewedAt = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
            await fixture.Connection.RunInTransactionAsync(connection =>
                PersistRuntimeHistory(
                    connection,
                    graph.CardId,
                    new Fsrs6ReviewEvent(reviewedAt, ReviewRating.Good)));

            switch (mutation)
            {
                case "state":
                    await fixture.Connection.ExecuteAsync(
                        "UPDATE FsrsCardStates SET State = 1, StepIndex = 0 WHERE CardId = ?",
                        graph.CardId);
                    break;
                case "stability":
                    var stability = await fixture.Connection.ExecuteScalarAsync<double>(
                        "SELECT Stability FROM FsrsCardStates WHERE CardId = ?",
                        graph.CardId);
                    await fixture.Connection.ExecuteAsync(
                        "UPDATE FsrsCardStates SET Stability = ? WHERE CardId = ?",
                        Math.BitIncrement(stability),
                        graph.CardId);
                    break;
                case "difficulty":
                    var difficulty = await fixture.Connection.ExecuteScalarAsync<double>(
                        "SELECT Difficulty FROM FsrsCardStates WHERE CardId = ?",
                        graph.CardId);
                    await fixture.Connection.ExecuteAsync(
                        "UPDATE FsrsCardStates SET Difficulty = ? WHERE CardId = ?",
                        Math.BitIncrement(difficulty),
                        graph.CardId);
                    break;
                case "last-reviewed":
                    await fixture.Connection.ExecuteAsync(
                        "UPDATE FsrsCardStates SET LastReviewedAtUtc = '2026-08-30T09:00:01.0000000Z' WHERE CardId = ?",
                        graph.CardId);
                    break;
                case "step-index":
                    await fixture.Connection.ExecuteAsync("PRAGMA ignore_check_constraints = ON");
                    await fixture.Connection.ExecuteAsync(
                        "UPDATE FsrsCardStates SET StepIndex = 0 WHERE CardId = ?",
                        graph.CardId);
                    break;
                case "due":
                    await fixture.Connection.ExecuteAsync(
                        "UPDATE FsrsCardStates SET DueAtUtc = '2026-08-31T09:00:01.0000000Z' WHERE CardId = ?",
                        graph.CardId);
                    break;
                default:
                    Assert.Fail($"Unknown mutation '{mutation}'.");
                    break;
            }

            await AssertAlreadyAppliedRejectedAsync(fixture, expectedDetail);
        }
    }

    [TestMethod]
    public async Task AlreadyApplied_RejectsAnyForeignKeyCheckViolation()
    {
        var (fixture, _) = await CreateMigratedDatabaseAsync();
        await using (fixture)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA foreign_keys = OFF");
            await fixture.Connection.ExecuteAsync(
                """
                CREATE TABLE RuntimeIntegrityProbe (
                    Id INTEGER PRIMARY KEY,
                    WordId INTEGER NOT NULL,
                    FOREIGN KEY (WordId) REFERENCES Words(Id))
                """);
            await fixture.Connection.ExecuteAsync(
                "INSERT INTO RuntimeIntegrityProbe (Id, WordId) VALUES (1, 999999)");

            await AssertAlreadyAppliedRejectedAsync(fixture, "foreign_key_check");
        }
    }
}
