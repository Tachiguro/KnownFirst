using KnownFirst.Data;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class DatabaseSchemaForeignKeyInitializationTests
{
    [TestMethod]
    public async Task InitializeAsync_EnablesAndVerifiesForeignKeyEnforcement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"knownfirst-foreign-key-cutover-{Guid.NewGuid():N}.db3");
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);

            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(13, DatabaseSchema.CurrentVersion);
            Assert.AreEqual(13, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys"));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }
}
