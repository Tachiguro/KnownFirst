using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.DependencyInjection;
using SQLite;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class VocabularyOverviewServiceTests
{
    private static readonly DateTime BaseTime = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task EmptyDatabase_ReturnsEmptyOverviewList()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task SingleWord_ReturnsPreservedWordProperties()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var wordTime = BaseTime.AddMinutes(5);
        int wordId = await database.RunInTransactionAsync(connection =>
            InsertWord(connection, "Apfel", "de", WordStatus.Unreviewed, wordTime));

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        Assert.AreEqual(1, result.Count);
        var item = result[0];
        Assert.AreEqual(wordId, item.WordId);
        Assert.AreEqual(wordId, item.Id);
        Assert.AreEqual("Apfel", item.CanonicalTerm);
        Assert.AreEqual("de", item.SourceLanguage);
        Assert.AreEqual("de", item.Language);
        Assert.AreEqual(wordTime, item.CreatedAt);
    }

    [TestMethod]
    public async Task MultipleWords_ReturnsInDeterministicRecentOrder()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var t1 = BaseTime.AddMinutes(1);
        var t2 = BaseTime.AddMinutes(5);
        var t3 = BaseTime.AddMinutes(3);

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "word-first", "en", WordStatus.UnknownBacklog, t1);
            InsertWord(connection, "word-latest", "en", WordStatus.UnknownBacklog, t2);
            InsertWord(connection, "word-middle", "en", WordStatus.UnknownBacklog, t3);
            return true;
        });

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("word-latest", result[0].CanonicalTerm);
        Assert.AreEqual("word-middle", result[1].CanonicalTerm);
        Assert.AreEqual("word-first", result[2].CanonicalTerm);
    }

    [TestMethod]
    public async Task MoreThanThirtyRows_ReturnsBoundedResultCappedAtThirty()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            for (var i = 1; i <= 35; i++)
            {
                var timestamp = BaseTime.AddMinutes(i);
                InsertWord(connection, $"term-{i:D2}", "en", WordStatus.UnknownBacklog, timestamp);
            }
            return true;
        });

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        Assert.AreEqual(30, result.Count);
        // The most recent 30 should be term-35 down to term-06
        Assert.AreEqual("term-35", result[0].CanonicalTerm);
        Assert.AreEqual("term-06", result[29].CanonicalTerm);
    }

    [TestMethod]
    public async Task CustomLimit_RespectsBoundedLimitAndCapsAtThirty()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            for (var i = 1; i <= 35; i++)
            {
                InsertWord(connection, $"word-{i:D2}", "de", WordStatus.UnknownBacklog, BaseTime.AddMinutes(i));
            }
            return true;
        });

        var service = new VocabularyOverviewService(database);

        var limited10 = await service.GetRecentVocabularyAsync(limit: 10);
        Assert.AreEqual(10, limited10.Count);
        Assert.AreEqual("word-35", limited10[0].CanonicalTerm);
        Assert.AreEqual("word-26", limited10[9].CanonicalTerm);

        // Requested 50: must cap at MaxLimit (30)
        var capped50 = await service.GetRecentVocabularyAsync(limit: 50);
        Assert.AreEqual(30, capped50.Count);
        Assert.AreEqual("word-35", capped50[0].CanonicalTerm);
        Assert.AreEqual("word-06", capped50[29].CanonicalTerm);
    }

    [TestMethod]
    public async Task NonPositiveLimit_ReturnsEmptyListWithoutQuerying()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "sample", "en", WordStatus.UnknownBacklog, BaseTime);
            return true;
        });

        var service = new VocabularyOverviewService(database);

        var zeroResult = await service.GetRecentVocabularyAsync(limit: 0);
        Assert.AreEqual(0, zeroResult.Count);

        var negativeResult = await service.GetRecentVocabularyAsync(limit: -5);
        Assert.AreEqual(0, negativeResult.Count);
    }

    [TestMethod]
    public async Task StableOrdering_BreaksTimestampTiesByWordIdDescending()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var sharedTime = BaseTime.AddMinutes(10);
        var earlierTime = BaseTime.AddMinutes(5);
        var laterTime = BaseTime.AddMinutes(15);

        int id1 = 0, id2 = 0, id3 = 0, id4 = 0;
        await database.RunInTransactionAsync(connection =>
        {
            id1 = InsertWord(connection, "earlier", "en", WordStatus.UnknownBacklog, earlierTime);
            id2 = InsertWord(connection, "tie-lower-id", "en", WordStatus.UnknownBacklog, sharedTime);
            id3 = InsertWord(connection, "tie-higher-id", "en", WordStatus.UnknownBacklog, sharedTime);
            id4 = InsertWord(connection, "later", "en", WordStatus.UnknownBacklog, laterTime);
            return true;
        });

        Assert.IsTrue(id3 > id2, "Second insert at shared time must have higher Word ID.");

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(id4, result[0].WordId);
        Assert.AreEqual(id3, result[1].WordId);
        Assert.AreEqual(id2, result[2].WordId);
        Assert.AreEqual(id1, result[3].WordId);
    }

    [TestMethod]
    public async Task PreservesSourceLanguageIdentity_DistinctLanguagesRetained()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "Haus", "de", WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "house", "en", WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "maison", "fr", WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "casa", "es", WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            InsertWord(connection, "ie", "ja", WordStatus.Unreviewed, BaseTime.AddMinutes(5));
            return true;
        });

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        Assert.AreEqual(5, result.Count);
        var languages = result.Select(r => (r.CanonicalTerm, r.SourceLanguage)).ToList();
        CollectionAssert.Contains(languages, ("Haus", "de"));
        CollectionAssert.Contains(languages, ("house", "en"));
        CollectionAssert.Contains(languages, ("maison", "fr"));
        CollectionAssert.Contains(languages, ("casa", "es"));
        CollectionAssert.Contains(languages, ("ie", "ja"));
    }

    [TestMethod]
    public async Task ReadOnly_DoesNotMutateOrWriteVocabularyData()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "persisted-word", "de", WordStatus.UnknownBacklog, BaseTime);
            return true;
        });

        var service = new VocabularyOverviewService(database);

        var beforeRows = await database.ReadAsync(async conn =>
            await conn.QueryAsync<WordEntity>("SELECT * FROM Words ORDER BY Id"));

        _ = await service.GetRecentVocabularyAsync();
        _ = await service.GetRecentVocabularyAsync(limit: 5);

        var afterRows = await database.ReadAsync(async conn =>
            await conn.QueryAsync<WordEntity>("SELECT * FROM Words ORDER BY Id"));

        Assert.AreEqual(beforeRows.Count, afterRows.Count);
        Assert.AreEqual(beforeRows[0].Id, afterRows[0].Id);
        Assert.AreEqual(beforeRows[0].CanonicalTerm, afterRows[0].CanonicalTerm);
        Assert.AreEqual(beforeRows[0].Language, afterRows[0].Language);
        Assert.AreEqual(beforeRows[0].Status, afterRows[0].Status);
        Assert.AreEqual(beforeRows[0].CreatedAt, afterRows[0].CreatedAt);
        Assert.AreEqual(beforeRows[0].UpdatedAt, afterRows[0].UpdatedAt);
    }

    [TestMethod]
    public async Task NoRelianceOnObsoleteWordWideLearningStatus_ReturnsIdentitiesAcrossAllValidStatusesAndSenseStates()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        // In Schema 14, learning state is governed by Senses and Targets; obsolete word-wide
        // learning status (WordStatus.Learning = 4, Prepared = 3, Mastered = 5) on the Words table
        // is strictly prohibited by Schema 8..14 runtime integrity validation. Words undergoing
        // active learning carry Words.Status = UnknownBacklog while their Senses are Learning/Prepared.
        var validWordStatuses = new[]
        {
            WordStatus.Unreviewed,
            WordStatus.Known,
            WordStatus.UnknownBacklog,
            WordStatus.Ignored
        };

        await database.RunInTransactionAsync(connection =>
        {
            var idx = 0;
            foreach (var status in validWordStatuses)
            {
                InsertWord(connection, $"word-status-{status}", "en", status, BaseTime.AddMinutes(idx++));
            }

            // Also insert words that are in active Learning and Prepared states via Senses:
            var learningWordId = InsertWord(connection, "word-learning-sense", "en", WordStatus.UnknownBacklog, BaseTime.AddMinutes(idx++));
            InsertSense(connection, learningWordId, SenseStatus.Learning);

            var preparedWordId = InsertWord(connection, "word-prepared-sense", "en", WordStatus.UnknownBacklog, BaseTime.AddMinutes(idx++));
            InsertSense(connection, preparedWordId, SenseStatus.Prepared);

            return true;
        });

        var service = new VocabularyOverviewService(database);
        var result = await service.GetRecentVocabularyAsync();

        // 4 status words + 2 sense-learning words = 6 total
        Assert.AreEqual(6, result.Count);

        // Verify that learning words are included without filtering or dependency on obsolete word-wide learning status
        var terms = result.Select(r => r.CanonicalTerm).ToList();
        CollectionAssert.Contains(terms, "word-learning-sense");
        CollectionAssert.Contains(terms, "word-prepared-sense");
        CollectionAssert.Contains(terms, "word-status-Unreviewed");
        CollectionAssert.Contains(terms, "word-status-Known");
        CollectionAssert.Contains(terms, "word-status-UnknownBacklog");
        CollectionAssert.Contains(terms, "word-status-Ignored");
    }

    private static void InsertSense(SQLiteConnection connection, int wordId, SenseStatus status)
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
            BaseTime,
            BaseTime);
    }

    [TestMethod]
    public async Task ForwarderMethod_GetRecentOverviewAsync_DelegatesToGetRecentVocabularyAsync()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "delegated", "de", WordStatus.Unreviewed, BaseTime);
            return true;
        });

        IVocabularyOverviewService service = new VocabularyOverviewService(database);
        var result = await service.GetRecentOverviewAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("delegated", result[0].CanonicalTerm);
    }

    [TestMethod]
    public async Task DependencyInjection_MauiProgramRegistersVocabularyOverviewService()
    {
        var services = new ServiceCollection();
        await using var database = new ProductionInitializedDatabase();
        services.AddSingleton<IKnownFirstDatabase>(database);
        services.AddSingleton<IVocabularyOverviewService, VocabularyOverviewService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var service = provider.GetRequiredService<IVocabularyOverviewService>();
        Assert.IsNotNull(service);
        Assert.IsInstanceOfType<VocabularyOverviewService>(service);

        var repoRoot = FindRepositoryRoot();
        var mauiProgramSource = File.ReadAllText(Path.Combine(repoRoot, "MauiProgram.cs"));
        StringAssert.Contains(
            mauiProgramSource,
            "builder.Services.AddSingleton<IVocabularyOverviewService, VocabularyOverviewService>();");
    }

    private static int InsertWord(
        SQLiteConnection connection,
        string canonicalTerm,
        string language,
        WordStatus status,
        DateTime createdAt)
    {
        connection.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            language,
            canonicalTerm,
            canonicalTerm.ToLowerInvariant(),
            (int)status,
            createdAt,
            createdAt);

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the KnownFirst repository root.");
    }
}
