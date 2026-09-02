using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema12;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema12MigrationTests
{
    [TestMethod]
    public async Task Schema12Migration_AppliesCleanly_ThroughExplicitHistoricalChain()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var version = await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(12, version);

        var hasStateTable = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='LearningDayState'");
        Assert.AreEqual(1, hasStateTable);

        var hasGrantsTable = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='LearningDayGrants'");
        Assert.AreEqual(1, hasGrantsTable);

        var hasGrantsIndex = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_LearningDayGrants_DayOrdinal'");
        Assert.AreEqual(1, hasGrantsIndex);

        var isValid = false;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema12ShapeValidator.IsValidDatabase(conn, out _);
        });
        Assert.IsTrue(isValid);
    }
}
