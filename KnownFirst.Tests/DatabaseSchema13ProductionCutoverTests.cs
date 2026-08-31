using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseSchema13ProductionCutoverTests
{
    [TestMethod]
    public async Task InitializeAsync_GenuinelyFreshDatabase_CreatesValidCleanSchema13Directly()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);

            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(13, DatabaseSchema.CurrentVersion);
            Assert.AreEqual(13, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys"));
            await connection.RunInTransactionAsync(sqliteConnection =>
            {
                Assert.IsTrue(
                    Schema13ShapeValidator.IsValidDatabase(sqliteConnection, out var shapeFailure),
                    shapeFailure);
                Assert.IsTrue(
                    Schema13RuntimeIntegrityValidator.Validate(sqliteConnection, out var runtimeFailure),
                    runtimeFailure);
                Assert.AreEqual(0, sqliteConnection.ExecuteScalar<int>("PRAGMA foreign_key_check"));
            });
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_PopulatedSchema12_RejectsWithoutMutationMigrationCleanupOrDeletion()
    {
        await using var fixture = await CreateSchema12FixtureAsync();
        await fixture.Connection.InsertAsync(new LexicalCacheEntity
        {
            CacheKey = "legacy|must-remain",
            SourceLanguage = "en",
            ExplanationLanguage = "de",
            NormalizedLemma = "preserve",
            LookupMode = LexicalLookupMode.Definition,
            TargetLanguage = string.Empty,
            CanonicalLookupTerm = "preserve",
            TokenKind = TokenKind.Word,
            Provider = "cutover-test",
            ProviderSchemaVersion = 1,
            ResultJson = "{}",
            SourceProject = "cutover-test",
            PageTitle = "Preserve",
            Attribution = "cutover-test",
            FetchedAtUtc = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc)
        });
        var before = await fixture.CapturePersistentStateAsync();

        var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
            () => DatabaseSchema.InitializeAsync(fixture.Connection));

        AssertReason(exception, "UnsupportedOlderVersion");
        CollectionAssert.AreEqual(before, await fixture.CapturePersistentStateAsync());
        Assert.AreEqual(12, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LexicalCache WHERE CacheKey = 'legacy|must-remain'"));
        Assert.AreEqual(
            0,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'FsrsCardStates'"));
        Assert.IsTrue(File.Exists(fixture.DatabasePath));
    }

    [TestMethod]
    public async Task InitializeAsync_ValidExistingSchema13_SucceedsWithoutReconstructionOrPersistentCleanup()
    {
        await using var fixture = await CreateSchema13FixtureAsync();
        await fixture.Connection.InsertAsync(new LexicalCacheEntity
        {
            CacheKey = "legacy|schema13-preserve",
            SourceLanguage = "en",
            ExplanationLanguage = "de",
            NormalizedLemma = "preserve",
            LookupMode = LexicalLookupMode.Definition,
            TargetLanguage = string.Empty,
            CanonicalLookupTerm = "preserve",
            TokenKind = TokenKind.Word,
            Provider = "cutover-test",
            ProviderSchemaVersion = 1,
            ResultJson = "{}",
            SourceProject = "cutover-test",
            PageTitle = "Preserve",
            Attribution = "cutover-test",
            FetchedAtUtc = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc)
        });
        var before = await fixture.CapturePersistentStateAsync();

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        CollectionAssert.AreEqual(before, await fixture.CapturePersistentStateAsync());
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LexicalCache WHERE CacheKey = 'legacy|schema13-preserve'"));
        await fixture.Connection.RunInTransactionAsync(sqliteConnection =>
        {
            Assert.IsTrue(
                Schema13RuntimeIntegrityValidator.Validate(sqliteConnection, out var failure),
                failure);
            Assert.AreEqual(0, sqliteConnection.ExecuteScalar<int>("PRAGMA foreign_key_check"));
        });
    }

    [TestMethod]
    public async Task InitializeAsync_MalformedWordLearningControlTimestamp_RejectsWithoutMutation()
    {
        const string malformedTimestamp = "not-a-utc-timestamp";
        const string privateContent = "private-word-control-content";
        Assert.ThrowsExactly<FormatException>(
            () => Schema13TimestampCodec.ParseUtcDateTime(malformedTimestamp));

        await using var fixture = await CreateSchema13FixtureAsync();
        var wordId = await fixture.InsertWordAsync(
            privateContent,
            status: KnownFirst.Models.WordStatus.Unreviewed);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
            wordId,
            malformedTimestamp);

        await AssertControlTimestampRejectedWithoutMutationAsync(
            fixture,
            "WordLearningControls",
            "WordId",
            wordId,
            malformedTimestamp,
            privateContent,
            $"WordLearningControl {wordId} has invalid DecidedAtUtc.");
    }

    [TestMethod]
    public async Task InitializeAsync_MalformedSenseLearningControlTimestamp_RejectsWithoutMutation()
    {
        const string malformedTimestamp = "malformed-sense-control-timestamp";
        const string privateContent = "private-sense-control-content";
        Assert.ThrowsExactly<FormatException>(
            () => Schema13TimestampCodec.ParseUtcDateTime(malformedTimestamp));

        await using var fixture = await CreateSchema13FixtureAsync();
        var wordId = await fixture.InsertWordAsync(
            "sense-control-owner",
            status: KnownFirst.Models.WordStatus.Unreviewed);
        var senseId = await fixture.InsertSenseAsync(wordId, providerSenseId: privateContent);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)",
            senseId,
            malformedTimestamp);

        await AssertControlTimestampRejectedWithoutMutationAsync(
            fixture,
            "SenseLearningControls",
            "SenseId",
            senseId,
            malformedTimestamp,
            privateContent,
            $"SenseLearningControl {senseId} has invalid DecidedAtUtc.");
    }

    [DataTestMethod]
    [DataRow("2026-08-30T09:00:00+00:00")]
    [DataRow("2026-08-30T09:00:00+02:00")]
    public async Task InitializeAsync_OffsetControlTimestamp_RejectsWithoutMutation(string offsetTimestamp)
    {
        Assert.ThrowsExactly<FormatException>(
            () => Schema13TimestampCodec.ParseUtcDateTime(offsetTimestamp));

        await using var fixture = await CreateSchema13FixtureAsync();
        var wordId = await fixture.InsertWordAsync(
            "private-offset-control-content",
            status: KnownFirst.Models.WordStatus.Unreviewed);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
            wordId,
            offsetTimestamp);

        await AssertControlTimestampRejectedWithoutMutationAsync(
            fixture,
            "WordLearningControls",
            "WordId",
            wordId,
            offsetTimestamp,
            "private-offset-control-content",
            $"WordLearningControl {wordId} has invalid DecidedAtUtc.");
    }

    [TestMethod]
    public async Task InitializeAsync_StrictUtcWordAndSenseLearningControlTimestamps_AreAcceptedWithoutMutation()
    {
        const string wordTimestamp = "2026-08-30T09:00:00Z";
        const string senseTimestamp = "2026-08-30T09:00:00.1Z";
        Assert.AreEqual(DateTimeKind.Utc, Schema13TimestampCodec.ParseUtcDateTime(wordTimestamp).Kind);
        Assert.AreEqual(DateTimeKind.Utc, Schema13TimestampCodec.ParseUtcDateTime(senseTimestamp).Kind);

        await using var fixture = await CreateSchema13FixtureAsync();
        var wordId = await fixture.InsertWordAsync(
            "valid-strict-control-owner",
            status: KnownFirst.Models.WordStatus.Unreviewed);
        var senseId = await fixture.InsertSenseAsync(wordId, providerSenseId: "valid-strict-sense-owner");
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
            wordId,
            wordTimestamp);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)",
            senseId,
            senseTimestamp);
        var before = await fixture.CapturePersistentStateAsync();

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        CollectionAssert.AreEqual(before, await fixture.CapturePersistentStateAsync());
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(
            wordTimestamp,
            await fixture.Connection.ExecuteScalarAsync<string>(
                "SELECT DecidedAtUtc FROM WordLearningControls WHERE WordId = ?",
                wordId));
        Assert.AreEqual(
            senseTimestamp,
            await fixture.Connection.ExecuteScalarAsync<string>(
                "SELECT DecidedAtUtc FROM SenseLearningControls WHERE SenseId = ?",
                senseId));
        Assert.IsTrue(File.Exists(fixture.DatabasePath));
    }

    [TestMethod]
    public async Task InitializeAsync_MalformedSchema13_RejectsAsInvalidCurrentSchemaWithoutRepair()
    {
        await using var fixture = await CreateSchema13FixtureAsync();
        await fixture.Connection.ExecuteAsync($"DROP INDEX {Schema13Ddl.FsrsCardStatesDueIndexName}");
        var before = await fixture.CapturePersistentStateAsync();

        var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
            () => DatabaseSchema.InitializeAsync(fixture.Connection));

        AssertReason(exception, "InvalidCurrentSchema");
        CollectionAssert.AreEqual(before, await fixture.CapturePersistentStateAsync());
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(
            0,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?",
                Schema13Ddl.FsrsCardStatesDueIndexName));
        Assert.IsTrue(File.Exists(fixture.DatabasePath));
    }

    [TestMethod]
    public async Task InitializeAsync_NonemptyUnversionedDatabase_RejectsWithoutMutation()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await connection.ExecuteAsync("CREATE TABLE UnversionedSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
            await connection.ExecuteAsync("INSERT INTO UnversionedSentinel (Id, Value) VALUES (1, 'preserve-me')");
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(connection));

            AssertReason(exception, "UnknownNonEmptyUnversionedDatabase");
            CollectionAssert.AreEqual(before, await PersistentDatabaseSnapshot.CaptureCompleteAsync(path));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(
                "preserve-me",
                await connection.ExecuteScalarAsync<string>(
                    "SELECT Value FROM UnversionedSentinel WHERE Id = 1"));
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_FutureVersion_RejectsWithSchema13SupportWithoutMutation()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await connection.ExecuteAsync("CREATE TABLE FutureSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
            await connection.ExecuteAsync("INSERT INTO FutureSentinel (Id, Value) VALUES (1, 'preserve-me')");
            await connection.ExecuteAsync("PRAGMA user_version = 14");
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(connection));

            Assert.AreEqual(13, DatabaseSchema.CurrentVersion);
            Assert.AreEqual(14, exception.FoundVersion);
            Assert.AreEqual(13, exception.SupportedVersion);
            AssertReason(exception, "UnsupportedFutureVersion");
            CollectionAssert.AreEqual(before, await PersistentDatabaseSnapshot.CaptureCompleteAsync(path));
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task CreatePortableArchiveAsync_FreshProductionDatabase_UsesArchiveV3Dispatch()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();
        var service = new BackupService(database, new FakePlatformInfo());
        using var archive = new MemoryStream();

        await service.CreatePortableArchiveAsync(archive, CancellationToken.None);

        archive.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(archive, CancellationToken.None);
        Assert.AreEqual(3, validated.FormatVersion);
        Assert.IsNotNull(validated.V3);
        Assert.AreEqual(13, validated.V3.Manifest.SourceDatabaseSchemaVersion);
    }

    [TestMethod]
    public void ProductionInitializer_HasNoDormantMigrationOrLexicalCacheCleanupPath()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Data", "DatabaseSchema.cs"));

        Assert.DoesNotContain("Schema8DormantMigration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema9DormantMigration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema10DormantMigration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema11DormantMigration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema12DormantMigration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema13DormantMigration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM LexicalCache", source, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertReason(DatabaseSchemaCompatibilityException exception, string expected)
    {
        var actual = exception.GetType().GetProperty("Reason")?.GetValue(exception)?.ToString();
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(DatabaseSchemaCompatibilityException.StableErrorCode, exception.ErrorCode);
    }

    private static async Task AssertControlTimestampRejectedWithoutMutationAsync(
        Schema7Fixture fixture,
        string tableName,
        string idColumn,
        int ownerId,
        string persistedTimestamp,
        string privateContent,
        string expectedDiagnostic)
    {
        var before = await fixture.CapturePersistentStateAsync();

        var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
            () => DatabaseSchema.InitializeAsync(fixture.Connection));

        AssertReason(exception, "InvalidCurrentSchema");
        Assert.AreEqual(expectedDiagnostic, exception.DiagnosticDetail);
        Assert.DoesNotContain(persistedTimestamp, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(privateContent, exception.Message, StringComparison.Ordinal);
        CollectionAssert.AreEqual(before, await fixture.CapturePersistentStateAsync());
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(
            persistedTimestamp,
            await fixture.Connection.ExecuteScalarAsync<string>(
                $"SELECT DecidedAtUtc FROM {tableName} WHERE {idColumn} = ?",
                ownerId));
        Assert.IsTrue(File.Exists(fixture.DatabasePath));
    }

    private static async Task<Schema7Fixture> CreateSchema12FixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        return fixture;
    }

    private static async Task<Schema7Fixture> CreateSchema13FixtureAsync()
    {
        var fixture = await CreateSchema12FixtureAsync();
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        return fixture;
    }

    private static string CreateTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-schema13-cutover-{Guid.NewGuid():N}.db3");

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

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-cutover-test";
    }

    internal sealed class ProductionInitializedDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLiteAsyncConnection? _connection;

        public ProductionInitializedDatabase()
        {
            DatabasePath = CreateTemporaryPath();
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            await DatabaseSchema.InitializeAsync(_connection);
        }

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                return await operation(_connection!);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation) =>
            RunInTransactionAsync(operation);

        public Task ResetAsync() => throw new AssertFailedException("Production cutover tests must never reset the database.");

        public async ValueTask DisposeAsync()
        {
            try
            {
                await TemporaryDatabaseFiles.CloseAndDeleteAsync(_connection, DatabasePath);
                _connection = null;
            }
            finally
            {
                _gate.Dispose();
            }
        }
    }
}
