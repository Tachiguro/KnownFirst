using KnownFirst.Data;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseSchemaFutureVersionTests
{
    [TestMethod]
    public async Task InitializeAsync_FutureVersionRejectsBeforeAnySchemaOrDataChange()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            using (var setup = Open(path))
            {
                setup.Execute("CREATE TABLE FutureSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
                setup.Execute("INSERT INTO FutureSentinel (Id, Value) VALUES (1, 'preserve-me')");
                setup.Execute("CREATE TABLE LexicalCache (Id INTEGER PRIMARY KEY, CacheKey TEXT NOT NULL, FutureColumn TEXT NOT NULL)");
                setup.Execute("INSERT INTO LexicalCache (Id, CacheKey, FutureColumn) VALUES (1, 'legacy|preserve-me', 'future-value')");
                setup.Execute($"PRAGMA user_version = {DatabaseSchema.CurrentVersion + 1}");
            }

            connection = new SQLiteAsyncConnection(path);
            var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(connection));

            Assert.AreEqual(DatabaseSchemaCompatibilityException.StableErrorCode, exception.ErrorCode);
            Assert.AreEqual(DatabaseSchema.CurrentVersion + 1, exception.FoundVersion);
            Assert.AreEqual(DatabaseSchema.CurrentVersion, exception.SupportedVersion);
            Assert.AreEqual(
                DatabaseSchema.CurrentVersion + 1,
                await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(
                "preserve-me",
                await connection.ExecuteScalarAsync<string>(
                    "SELECT Value FROM FutureSentinel WHERE Id = 1"));
            Assert.AreEqual(
                "legacy|preserve-me",
                await connection.ExecuteScalarAsync<string>(
                    "SELECT CacheKey FROM LexicalCache WHERE Id = 1"));
            Assert.AreEqual(
                "future-value",
                await connection.ExecuteScalarAsync<string>(
                    "SELECT FutureColumn FROM LexicalCache WHERE Id = 1"));
            Assert.AreEqual(
                0,
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Documents'"));
            Assert.AreEqual(
                "CREATE TABLE LexicalCache (Id INTEGER PRIMARY KEY, CacheKey TEXT NOT NULL, FutureColumn TEXT NOT NULL)",
                await connection.ExecuteScalarAsync<string>(
                    "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'LexicalCache'"));
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_CurrentVersionIsIdempotentAndPreservesRows()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await connection.ExecuteAsync(
                "CREATE TABLE CurrentSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
            await connection.ExecuteAsync(
                "INSERT INTO CurrentSentinel (Id, Value) VALUES (1, 'unchanged')");
            await connection.ExecuteAsync(
                "INSERT INTO LexicalCache (CacheKey) VALUES ('v2|synthetic')");
            var tableCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'");

            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(
                DatabaseSchema.CurrentVersion,
                await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(
                "unchanged",
                await connection.ExecuteScalarAsync<string>(
                    "SELECT Value FROM CurrentSentinel WHERE Id = 1"));
            Assert.AreEqual(
                1,
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM LexicalCache WHERE CacheKey = 'v2|synthetic'"));
            Assert.AreEqual(
                tableCount,
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'"));
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    private static string CreateTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-future-schema-{Guid.NewGuid():N}.db3");

    private static SQLiteConnection Open(string path) => new(
        path,
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

    private static async Task CloseAndDeleteAsync(
        SQLiteAsyncConnection? connection,
        string path)
    {
        if (connection is not null)
        {
            await connection.CloseAsync();
        }

        SQLiteAsyncConnection.ResetPool();
        foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
