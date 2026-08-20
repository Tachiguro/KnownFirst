using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Migrations.Schema10;
using KnownFirst.Data.Migrations.Schema11;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Schema11MigrationTests
{
    private const int Schema11 = 11;

    [TestMethod]
    public void CurrentVersion_IsSchema11()
    {
        Assert.AreEqual(
            Schema11,
            DatabaseSchema.CurrentVersion,
            "Schema 11 is the active schema when derived term evidence persistence is introduced.");
    }

    [TestMethod]
    public async Task FreshDatabase_InitializesToSchema11()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(Schema11, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DerivedTermEvidenceEntries'"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DerivedTermEvidenceEntries_ReviewCandidateId'"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DerivedTermEvidenceEntries_Candidate_Source_Range_Component'"));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task Schema11_Reinitialize_ValidatesWithoutRunningSchema10()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            Assert.AreEqual(Schema11, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

            // If Schema10DormantMigration were executed against a Schema-11 database, its future-version guard
            // (TargetVersion = 10) would throw Schema10MigrationException.FutureVersion(11).
            // Reinitialization must succeed cleanly without throwing.
            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(Schema11, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task Schema11_Reinitialize_FailsClosedWithoutRepair()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);

            // Corrupt Schema-11 shape by dropping the required evidence index
            await connection.ExecuteAsync("DROP INDEX IX_DerivedTermEvidenceEntries_Candidate_Source_Range_Component");

            var exception = await Assert.ThrowsExactlyAsync<Schema11MigrationException>(
                () => DatabaseSchema.InitializeAsync(connection));

            Assert.AreEqual("schema11-migration-already-applied-shape-invalid", exception.ErrorCode);
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task FutureSchemaVersion_IsRejectedBeforeAnyMutation()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            using (var setup = new SQLiteConnection(path))
            {
                setup.Execute("CREATE TABLE FutureSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
                setup.Execute("INSERT INTO FutureSentinel (Id, Value) VALUES (1, 'preserve-me')");
                setup.Execute("CREATE TABLE LexicalCache (Id INTEGER PRIMARY KEY, CacheKey TEXT NOT NULL)");
                setup.Execute("INSERT INTO LexicalCache (Id, CacheKey) VALUES (1, 'legacy|preserve-me')");
                setup.Execute($"PRAGMA user_version = {Schema11 + 1}");
            }

            connection = new SQLiteAsyncConnection(path);
            var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(connection));

            Assert.AreEqual(Schema11 + 1, exception.FoundVersion);
            Assert.AreEqual(Schema11, exception.SupportedVersion);
            Assert.AreEqual(Schema11 + 1, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual("preserve-me", await connection.ExecuteScalarAsync<string>(
                "SELECT Value FROM FutureSentinel WHERE Id = 1"));
            Assert.AreEqual("legacy|preserve-me", await connection.ExecuteScalarAsync<string>(
                "SELECT CacheKey FROM LexicalCache WHERE Id = 1"));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task ValidSchema10ToSchema11_PreservesExistingRowsAndCreatesEmptyEvidenceTable()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        // Upgrade to Schema 10 first
        await Schema10DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var sessionsBefore = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture);
        var queueRowsBefore = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);

        // Migrate to Schema 11 through DatabaseSchema
        await DatabaseSchema.InitializeAsync(fixture.Connection);

        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM DerivedTermEvidenceEntries"));

        var sessionsAfter = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture);
        var queueRowsAfter = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);

        CollectionAssert.AreEqual(
            sessionsBefore.Select(s => s.StableId).ToList(),
            sessionsAfter.Select(s => s.StableId).ToList());
        CollectionAssert.AreEqual(
            queueRowsBefore.Select(q => q.StableId).ToList(),
            queueRowsAfter.Select(q => q.StableId).ToList());
    }

    [TestMethod]
    public async Task SupportedOlderMigrationPath_TerminatesAtValidSchema11()
    {
        // Start from Schema 7
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("migration-test");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "migration-test", translation: "Test");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        Assert.AreEqual(7, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        // InitializeAsync runs full chain: 7 -> 8 -> 9 -> 10 -> 11
        await DatabaseSchema.InitializeAsync(fixture.Connection);

        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DerivedTermEvidenceEntries'"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Senses'"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LearningSessions'"));
    }

    private static string CreateTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-schema11-{Guid.NewGuid():N}.db3");
}
