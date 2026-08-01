using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DashboardServiceTests
{
    [TestMethod]
    public async Task Schema7_PreservesLegacyWordStatusCalculation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.InsertDocumentAsync(content: "dashboard schema seven");
        await fixture.InsertWordAsync("prepared", status: WordStatus.Prepared);
        await fixture.InsertWordAsync("learning", status: WordStatus.Learning);
        await fixture.InsertWordAsync("mastered", status: WordStatus.Mastered);
        await fixture.InsertWordAsync("known", status: WordStatus.Known);
        var service = new DashboardService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture));

        var statistics = await service.GetStatisticsAsync();

        Assert.AreEqual(1, statistics.DocumentCount);
        Assert.AreEqual(1, statistics.KnownWordCount);
        Assert.AreEqual(2, statistics.PreparedAndLearningWordCount);
    }

    [TestMethod]
    public async Task Schema8_PreparedAndLearningSensesCountDistinctWordsAndExcludeMasteredOnly()
    {
        await using var database = new TemporarySchema8Database("dashboard-sense-authority");
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            InsertSchema8WordWithSenses(connection, "two-prepared-senses", SenseStatus.Prepared, SenseStatus.Prepared);
            InsertSchema8WordWithSenses(connection, "one-learning-sense", SenseStatus.Learning);
            InsertSchema8WordWithSenses(connection, "mastered-only", SenseStatus.Mastered);
            return true;
        });

        var statistics = await new DashboardService(database).GetStatisticsAsync();

        Assert.AreEqual(2, statistics.PreparedAndLearningWordCount);
        Assert.AreEqual(3, statistics.UnknownBacklogWordCount);
    }

    [TestMethod]
    public async Task NormallyMigratedSchema8_ReportsSenseBasedDistinctWordCount()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var preparedWordId = await fixture.InsertWordAsync("prepared-migrated", status: WordStatus.Prepared);
        await fixture.InsertMeaningAsync(preparedWordId, translation: "vorbereitet");
        await fixture.InsertMeaningAsync(preparedWordId, translation: "bereit");
        var learningWordId = await fixture.InsertWordAsync("learning-migrated", status: WordStatus.Learning);
        await fixture.InsertMeaningAsync(learningWordId, translation: "lernend");
        var masteredWordId = await fixture.InsertWordAsync("mastered-migrated", status: WordStatus.Mastered);
        await fixture.InsertMeaningAsync(masteredWordId, translation: "gemeistert");

        await DatabaseSchema.InitializeAsync(fixture.Connection);
        var statistics = await new DashboardService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture)).GetStatisticsAsync();

        Assert.AreEqual(2, statistics.PreparedAndLearningWordCount);
        Assert.AreEqual(3, statistics.UnknownBacklogWordCount);
        Assert.AreEqual(2, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Senses WHERE WordId = ? AND Status = ?", preparedWordId, (int)SenseStatus.Prepared));
    }

    private static void InsertSchema8WordWithSenses(
        SQLiteConnection connection,
        string canonicalTerm,
        params SenseStatus[] statuses)
    {
        var now = new DateTime(2033, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        connection.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES ('en', ?, ?, ?, 0, 0, 0, 0, 0, 0, 0, 0, 0, ?, ?)
            """,
            canonicalTerm,
            canonicalTerm,
            (int)WordStatus.UnknownBacklog,
            now,
            now);
        var wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        foreach (var status in statuses)
        {
            connection.Execute(
                """
                INSERT INTO Senses
                    (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                     PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, 'en', 'de', '', '', '', '', '', NULL, ?, ?, ?)
                """,
                Guid.NewGuid().ToString("N"),
                wordId,
                (int)status,
                now,
                now);
        }
    }
}
