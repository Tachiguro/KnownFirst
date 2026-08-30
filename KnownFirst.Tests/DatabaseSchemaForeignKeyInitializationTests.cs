using KnownFirst.Data;

namespace KnownFirst.Tests;

[TestClass]
public sealed class DatabaseSchemaForeignKeyInitializationTests
{
    [TestMethod]
    public async Task InitializeAsync_EnablesAndVerifiesForeignKeyEnforcement()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        Assert.AreEqual(12, DatabaseSchema.CurrentVersion);
        Assert.AreEqual(12, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys"));
    }
}
