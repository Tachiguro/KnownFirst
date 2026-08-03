using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Schema-9 activation tests (backup-merge data-integrity program, Package 1): index-only Schema 8 -&gt;
/// Schema 9 migration through <see cref="DatabaseSchema.InitializeAsync"/>. Every database is an isolated
/// temporary SQLite file; no real user database is ever opened. Deliberately does not reference any
/// not-yet-existing production type (e.g. a future <c>Schema9DormantMigration</c>) — every fixture below
/// expresses its own expectations independently via raw SQLite DDL/PRAGMA introspection, so the tests can
/// never pass merely because they share an oracle with the production code under test.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema9ActivationTests
{
    [TestMethod]
    public async Task InitializeAsync_FreshDatabase_ActivatesSchema9()
    {
        var path = TemporaryPath("fresh-schema9");
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(9, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(1, await IndexCountAsync(connection, Schema9RawFixture.NormalIndexName));
            Assert.AreEqual(1, await IndexCountAsync(connection, Schema9RawFixture.ActiveIndexName));
            Assert.AreEqual(
                0,
                (await Schema9RawFixture.FindLegacyUniqueDocumentIdIndexesAsync(connection)).Count);
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_PinnedSchema8Database_UpgradesToSchema9AndPreservesRows()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var documentId = await fixture.InsertDocumentAsync(content: "schema9 upgrade evidence");
        var wordId = await fixture.InsertWordAsync("upgrade", status: WordStatus.Learning);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "upgrade", translation: "Aktualisierung");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        // Pinned Schema-8 source built only through existing production/test infrastructure — never through
        // DatabaseSchema.InitializeAsync, which after implementation activates Schema 9.
        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var completedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var sessionId = await InsertReviewSessionAsync(
            fixture.Connection, documentId, ReviewSessionStatus.Completed, completedAt);
        var candidateId = await InsertReviewCandidateAsync(fixture.Connection, sessionId, wordId, order: 0);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        Assert.AreEqual(9, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(1, await IndexCountAsync(fixture.Connection, Schema9RawFixture.NormalIndexName));
        Assert.AreEqual(1, await IndexCountAsync(fixture.Connection, Schema9RawFixture.ActiveIndexName));
        Assert.AreEqual(
            0,
            (await Schema9RawFixture.FindLegacyUniqueDocumentIdIndexesAsync(fixture.Connection)).Count);

        Assert.AreEqual(
            sessionId,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM ReviewSessions WHERE Id = ?", sessionId));
        Assert.AreEqual(
            (int)ReviewSessionStatus.Completed,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM ReviewSessions WHERE Id = ?", sessionId));
        Assert.AreEqual(
            candidateId,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM ReviewCandidates WHERE Id = ?", candidateId));
        Assert.AreEqual(
            1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
    }

    [TestMethod]
    public async Task InitializeAsync_AlreadyValidSchema9_IsIdempotent()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(9, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
    }

    [TestMethod]
    public async Task InitializeAsync_MissingLegacyReviewSessionIndex_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var legacyIndexes = await Schema9RawFixture.FindLegacyUniqueDocumentIdIndexesAsync(fixture.Connection);
        Assert.AreEqual(1, legacyIndexes.Count, "Fixture setup expects exactly one legacy index before removing it.");
        await fixture.Connection.ExecuteAsync($"DROP INDEX \"{legacyIndexes[0]}\"");

        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        Exception? caught = null;
        try
        {
            await DatabaseSchema.InitializeAsync(fixture.Connection);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(
            caught,
            "Expected initialization to fail closed when no legacy ReviewSessions(DocumentId) index can be found.");
        Assert.AreEqual(8, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task InitializeAsync_AmbiguousLegacyReviewSessionIndexes_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();

        // A second, differently named unique non-partial single-column index on ReviewSessions(DocumentId),
        // alongside the original sqlite-net-generated one, makes exactly-one-match discovery ambiguous.
        await fixture.Connection.ExecuteAsync(
            "CREATE UNIQUE INDEX IX_ReviewSessions_DocumentId_Duplicate ON ReviewSessions (DocumentId)");

        var legacyIndexes = await Schema9RawFixture.FindLegacyUniqueDocumentIdIndexesAsync(fixture.Connection);
        Assert.AreEqual(2, legacyIndexes.Count, "Fixture setup expects exactly two ambiguous legacy indexes.");

        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        Exception? caught = null;
        try
        {
            await DatabaseSchema.InitializeAsync(fixture.Connection);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(
            caught,
            "Expected initialization to fail closed when multiple candidate legacy indexes exist.");
        Assert.AreEqual(8, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task InitializeAsync_MidMigrationIndexNameCollision_RollsBackLegacyIndexAndUserVersion()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();

        // Pre-create a conflicting global index name (SQLite index names are global, not per-table) that
        // will cause creation of the target Schema-9 lookup index to fail after the legacy index would
        // already have been dropped inside the migration's own transaction.
        await fixture.Connection.ExecuteAsync(
            $"CREATE INDEX {Schema9RawFixture.NormalIndexName} ON Words (Id)");

        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        Exception? caught = null;
        try
        {
            await DatabaseSchema.InitializeAsync(fixture.Connection);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(
            caught,
            "Expected initialization to fail when the target lookup index name collides with an existing index.");
        Assert.AreEqual(8, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var legacyIndexesAfter = await Schema9RawFixture.FindLegacyUniqueDocumentIdIndexesAsync(fixture.Connection);
        Assert.AreEqual(
            1,
            legacyIndexesAfter.Count,
            "The original unique ReviewSessions(DocumentId) index must still exist after rollback.");
        Assert.AreEqual(0, await IndexCountAsync(fixture.Connection, Schema9RawFixture.ActiveIndexName));

        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task InitializeAsync_MigratedDatabase_AllowsMultipleCompletedReviewSessionsForDocument()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var documentId = await fixture.InsertDocumentAsync();
        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        await InsertReviewSessionAsync(
            fixture.Connection, documentId, ReviewSessionStatus.Completed, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await InsertReviewSessionAsync(
            fixture.Connection, documentId, ReviewSessionStatus.Completed, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var count = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ReviewSessions WHERE DocumentId = ? AND Status = ?",
            documentId,
            (int)ReviewSessionStatus.Completed);
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task InitializeAsync_MigratedDatabase_RejectsSecondActiveReviewSessionForDocument()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var documentId = await fixture.InsertDocumentAsync();
        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        await InsertReviewSessionAsync(fixture.Connection, documentId, ReviewSessionStatus.Active, completedAt: null);

        await Assert.ThrowsExactlyAsync<SQLiteException>(() =>
            InsertReviewSessionAsync(fixture.Connection, documentId, ReviewSessionStatus.Active, completedAt: null));
    }

    [TestMethod]
    public async Task InitializeAsync_FutureSchema10_FailsClosedBeforeMutation()
    {
        var path = TemporaryPath("future-schema10");
        SQLiteAsyncConnection? connection = null;
        try
        {
            using (var setup = new SQLiteConnection(
                path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache))
            {
                setup.Execute("CREATE TABLE FutureSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
                setup.Execute("INSERT INTO FutureSentinel (Id, Value) VALUES (1, 'preserve-me')");
                setup.Execute("PRAGMA user_version = 10");
            }

            connection = new SQLiteAsyncConnection(path);
            var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(connection));

            Assert.AreEqual(10, exception.FoundVersion);
            Assert.AreEqual(9, exception.SupportedVersion);
            Assert.AreEqual(10, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(
                0,
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Documents'"));
            Assert.AreEqual(
                "preserve-me",
                await connection.ExecuteScalarAsync<string>("SELECT Value FROM FutureSentinel WHERE Id = 1"));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    // ---- SCHEMA9_MALFORMED_SHAPE_FAIL_CLOSED_TEST_HARDENING: already-applied Schema-9 malformed shapes ----

    [TestMethod]
    public Task InitializeAsync_MalformedSchema9_MissingActivePartialIndex_FailsClosedWithoutRepair() =>
        AssertMalformedSchema9RemainsUnchangedAsync(
            connection => connection.ExecuteAsync($"DROP INDEX \"{Schema9RawFixture.ActiveIndexName}\""),
            "schema9-migration-already-applied-shape-invalid");

    [TestMethod]
    public Task InitializeAsync_MalformedSchema9_ActiveIndexWithWrongPredicate_FailsClosedWithoutRepair() =>
        AssertMalformedSchema9RemainsUnchangedAsync(
            async connection =>
            {
                await connection.ExecuteAsync($"DROP INDEX \"{Schema9RawFixture.ActiveIndexName}\"");
                await connection.ExecuteAsync(
                    $"CREATE UNIQUE INDEX {Schema9RawFixture.ActiveIndexName} ON ReviewSessions (DocumentId) WHERE Status = 1");
            },
            "schema9-migration-already-applied-shape-invalid");

    [TestMethod]
    public Task InitializeAsync_MalformedSchema9_LookupIndexIsUnique_FailsClosedWithoutRepair() =>
        AssertMalformedSchema9RemainsUnchangedAsync(
            async connection =>
            {
                await connection.ExecuteAsync($"DROP INDEX \"{Schema9RawFixture.NormalIndexName}\"");
                await connection.ExecuteAsync(
                    $"CREATE UNIQUE INDEX {Schema9RawFixture.NormalIndexName} ON ReviewSessions (DocumentId)");
            },
            "schema9-migration-already-applied-shape-invalid");

    [TestMethod]
    public Task InitializeAsync_MalformedSchema9_SurvivingLegacyUniqueIndex_FailsClosedWithoutRepair() =>
        AssertMalformedSchema9RemainsUnchangedAsync(
            connection => connection.ExecuteAsync(
                "CREATE UNIQUE INDEX IX_ReviewSessions_DocumentId_LegacySurvivor ON ReviewSessions (DocumentId)"),
            "schema9-migration-already-applied-shape-invalid");

    /// <summary>
    /// Builds a valid Schema-9 database via the independent <see cref="Schema9RawFixture"/> (never through
    /// production migration code), applies the given raw-SQL corruption, then proves
    /// <see cref="DatabaseSchema.InitializeAsync"/> fails closed with the expected
    /// <see cref="Schema9MigrationException.ErrorCode"/>, leaves <c>PRAGMA user_version</c> at 9, and
    /// leaves the complete persistent snapshot (every table's DDL, columns, indexes, and rows) byte-for-byte
    /// unchanged.
    /// </summary>
    private static async Task AssertMalformedSchema9RemainsUnchangedAsync(
        Func<SQLiteAsyncConnection, Task> corrupt,
        string expectedErrorCode)
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        await corrupt(fixture.Connection);

        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        var exception = await Assert.ThrowsExactlyAsync<Schema9MigrationException>(
            () => DatabaseSchema.InitializeAsync(fixture.Connection));
        Assert.AreEqual(expectedErrorCode, exception.ErrorCode);
        Assert.AreEqual(9, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
    }

    // ---- SCHEMA9_MALFORMED_SHAPE_FAIL_CLOSED_TEST_HARDENING: legacy-index discovery edge cases on a
    // pinned Schema-8 source (the legitimate old unconditional unique ReviewSessions(DocumentId) index is
    // structurally discovered and removed, then replaced by a shape that must NOT be mistaken for it) ----

    [TestMethod]
    public Task InitializeAsync_Schema8SourceWithOnlyPartialUniqueDocumentIdIndex_FailsClosedWithoutMutation() =>
        AssertSchema8SourceMissingLegacyIndexFailsClosedAsync(
            connection => connection.ExecuteAsync(
                "CREATE UNIQUE INDEX IX_ReviewSessions_DocumentId_PartialOnly ON ReviewSessions (DocumentId) WHERE Status = 0"));

    [TestMethod]
    public Task InitializeAsync_Schema8SourceWithUniqueWrongColumnIndex_FailsClosedWithoutMutation() =>
        AssertSchema8SourceMissingLegacyIndexFailsClosedAsync(
            connection => connection.ExecuteAsync(
                "CREATE UNIQUE INDEX IX_ReviewSessions_TotalCandidates_WrongColumn ON ReviewSessions (TotalCandidates)"));

    /// <summary>
    /// Builds a pinned valid Schema-8 fixture (via <see cref="Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync"/>,
    /// never through <see cref="DatabaseSchema.InitializeAsync"/>), structurally discovers and drops the
    /// one legitimate legacy unique <c>ReviewSessions(DocumentId)</c> index, replaces it with a shape that a
    /// correct migration must NOT treat as the legacy source index (a partial index, or a unique index on a
    /// different column), then proves initialization fails with the migration's own
    /// missing-legacy-index contract, leaves <c>PRAGMA user_version</c> at 8, and leaves the complete
    /// persistent snapshot byte-for-byte unchanged.
    /// </summary>
    private static async Task AssertSchema8SourceMissingLegacyIndexFailsClosedAsync(
        Func<SQLiteAsyncConnection, Task> createReplacementIndex)
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var legacyIndexes = await Schema9RawFixture.FindLegacyUniqueDocumentIdIndexesAsync(fixture.Connection);
        Assert.AreEqual(1, legacyIndexes.Count, "Fixture setup expects exactly one legacy index before removing it.");
        await fixture.Connection.ExecuteAsync($"DROP INDEX \"{legacyIndexes[0].Replace("\"", "\"\"")}\"");
        await createReplacementIndex(fixture.Connection);

        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        var exception = await Assert.ThrowsExactlyAsync<Schema9MigrationException>(
            () => DatabaseSchema.InitializeAsync(fixture.Connection));
        Assert.AreEqual("schema9-migration-missing-legacy-index", exception.ErrorCode);
        Assert.AreEqual(8, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
    }

    private static string TemporaryPath(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-schema9-activation-{suffix}-{Guid.NewGuid():N}.db3");

    private static Task<int> IndexCountAsync(SQLiteAsyncConnection connection, string index) =>
        connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", index);

    private static async Task<int> InsertReviewSessionAsync(
        SQLiteAsyncConnection connection,
        int documentId,
        ReviewSessionStatus status,
        DateTime? completedAt)
    {
        var startedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, ?, 1, 1, 1, 0, 0, 1, ?, ?)
            """,
            documentId, (int)status, startedAt, completedAt);
        return await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    private static async Task<int> InsertReviewCandidateAsync(
        SQLiteAsyncConnection connection,
        int sessionId,
        int wordId,
        int order)
    {
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await connection.ExecuteAsync(
            """
            INSERT INTO ReviewCandidates
                (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount,
                 PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
            VALUES (?, ?, ?, ?, ?, 1, 1, ?, 1, 0, ?)
            """,
            sessionId, wordId, order, (int)WordStatus.Known, (int)WordStatus.UnknownBacklog, timestamp, timestamp);
        return await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }
}

/// <summary>
/// Independent, test-only oracle for the expected Schema-9 <c>ReviewSessions</c> index shape, built and
/// verified through raw SQLite DDL/PRAGMA introspection alone. Deliberately never calls any production
/// Schema-9 migration or validator code — the whole point is that these tests cannot pass merely because
/// they share an oracle with the code under test.
/// </summary>
internal static class Schema9RawFixture
{
    public const string NormalIndexName = "IX_ReviewSessions_DocumentId";
    public const string ActiveIndexName = "IX_ReviewSessions_DocumentId_Active";

    /// <summary>
    /// Builds a pinned valid Schema-8 fixture (via <see cref="Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync"/>,
    /// never through <see cref="DatabaseSchema.InitializeAsync"/>), then structurally discovers and drops
    /// the legacy unique <c>ReviewSessions(DocumentId)</c> index and creates the expected non-unique and
    /// partial-unique Schema-9 indexes directly via raw DDL, and finally sets <c>PRAGMA user_version = 9</c>.
    /// </summary>
    public static async Task<Schema7Fixture> CreateValidSchema9FixtureAsync()
    {
        var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        await ReplaceLegacyIndexAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");
        return fixture;
    }

    public static async Task ReplaceLegacyIndexAsync(SQLiteAsyncConnection connection)
    {
        var legacyIndexNames = await FindLegacyUniqueDocumentIdIndexesAsync(connection);
        if (legacyIndexNames.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one legacy unique ReviewSessions(DocumentId) index on the pinned Schema-8 "
                + $"fixture, found {legacyIndexNames.Count}.");
        }

        await connection.ExecuteAsync($"DROP INDEX \"{legacyIndexNames[0].Replace("\"", "\"\"")}\"");
        await connection.ExecuteAsync($"CREATE INDEX {NormalIndexName} ON ReviewSessions (DocumentId)");
        await connection.ExecuteAsync(
            $"CREATE UNIQUE INDEX {ActiveIndexName} ON ReviewSessions (DocumentId) WHERE Status = 0");
    }

    /// <summary>
    /// Structurally finds every non-partial unique single-column index on <c>ReviewSessions(DocumentId)</c>
    /// via <c>PRAGMA index_list</c>/<c>PRAGMA index_xinfo</c> — the same independent structural predicate a
    /// correct migration must use to discover (and a correct validator must use to reject the continued
    /// presence of) the legacy index, expressed here without any reference to production code.
    /// </summary>
    public static async Task<List<string>> FindLegacyUniqueDocumentIdIndexesAsync(SQLiteAsyncConnection connection)
    {
        var indexes = await connection.QueryAsync<IndexListRow>("PRAGMA index_list(\"ReviewSessions\")");
        var matches = new List<string>();
        foreach (var index in indexes)
        {
            if (index.Unique != 1 || index.Partial != 0)
            {
                continue;
            }

            var columns = (await connection.QueryAsync<IndexXInfoRow>(
                    $"PRAGMA index_xinfo(\"{index.Name.Replace("\"", "\"\"")}\")"))
                .Where(column => column.Key == 1)
                .OrderBy(column => column.SequenceNumber)
                .ToArray();
            if (columns.Length == 1
                && columns[0].ColumnId >= 0
                && columns[0].Descending == 0
                && string.Equals(columns[0].Collation, "BINARY", StringComparison.OrdinalIgnoreCase)
                && string.Equals(columns[0].Name, "DocumentId", StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(index.Name);
            }
        }

        return matches;
    }

    private sealed class IndexListRow
    {
        public string Name { get; set; } = string.Empty;

        [Column("unique")]
        public int Unique { get; set; }

        [Column("partial")]
        public int Partial { get; set; }
    }

    private sealed class IndexXInfoRow
    {
        [Column("seqno")]
        public int SequenceNumber { get; set; }

        [Column("cid")]
        public int ColumnId { get; set; }

        public string? Name { get; set; }

        [Column("desc")]
        public int Descending { get; set; }

        [Column("coll")]
        public string? Collation { get; set; }

        [Column("key")]
        public int Key { get; set; }
    }
}
