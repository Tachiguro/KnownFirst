using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema10;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-BACKUP-005A: Schema-10 activation and the one-time stable-identity backfill.
///
/// <para>Every test asserts that initialization actually reached <c>PRAGMA user_version = 10</c>
/// <em>before</em> it reads any Schema-10-only column. That ordering is deliberate: the pre-implementation
/// failure must be a behavioural assertion about the schema version, never a raw
/// <c>no such column: StableId</c> SQL error.</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema10MigrationTests
{
    private const int Schema10 = 10;

    [TestMethod]
    public void SchemaVersion_IsAtLeastSchema10()
    {
        Assert.IsTrue(
            DatabaseSchema.CurrentVersion >= Schema10,
            "DatabaseSchema.CurrentVersion must be at least Schema 10.");
    }

    [TestMethod]
    public async Task FreshDatabase_DirectlyInitializesCurrentSchemaWithoutDuplicateSchema10Columns()
    {
        var path = TemporaryDatabasePath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);

            // Clean production bootstrap creates the current final shape directly. If the Schema-10
            // columns were also added to the baseline entities, construction would fail with a duplicate
            // column error rather than reaching the current version.
            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(DatabaseSchema.CurrentVersion, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.IsTrue(await HasColumnAsync(connection, "LearningSessions", "StableId"));
            Assert.IsTrue(await HasColumnAsync(connection, "LearningSessionCards", "StableId"));
        }
        finally
        {
            await CloseAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task LegacySchema9Database_MigratesToSchema10AndBackfillsEveryLearningWorkflowRow()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        Assert.AreEqual(9, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await Schema10DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(
            Schema10,
            await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            "The explicit historical Schema-10 migration must reach its target version.");

        var sessions = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture);
        Assert.HasCount(1, sessions);
        foreach (var session in sessions)
        {
            Assert.IsTrue(
                Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(session.StableId),
                $"LearningSessions row {session.Id} has an unacceptable StableId '{session.StableId}'.");
        }

        var queueRows = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);
        Assert.HasCount(1, queueRows);
        foreach (var row in queueRows)
        {
            Assert.IsTrue(
                Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(row.StableId),
                $"LearningSessionCards row {row.Id} has an unacceptable StableId '{row.StableId}'.");
        }
    }

    [TestMethod]
    public async Task Schema10Migration_IsIdempotentAndNeverRegeneratesAnAlreadyAssignedStableId()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();

        await Schema10DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var firstSessions = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();
        var firstQueueRows = (await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();

        // Re-running the historical migration on an already-migrated database revalidates the shape; it must never
        // reassign an identity that is already persisted.
        await Schema10DormantMigration.ApplyAsync(fixture.Connection);

        var secondSessions = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();
        var secondQueueRows = (await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();

        CollectionAssert.AreEqual(firstSessions, secondSessions);
        CollectionAssert.AreEqual(firstQueueRows, secondQueueRows);
    }

    [TestMethod]
    public async Task Schema10Migration_AssignsUniqueStableIdsAcrossEveryLearningWorkflowRow()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateTwoCompletedSessionsSharingStartedAtUtcAsync();

        await Schema10DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var sessionIds = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();
        Assert.HasCount(2, sessionIds);
        Assert.AreEqual(
            sessionIds.Count,
            sessionIds.Distinct(StringComparer.Ordinal).Count(),
            "Every LearningSessions StableId must be unique within the table.");

        var queueIds = (await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();
        Assert.HasCount(2, queueIds);
        Assert.AreEqual(
            queueIds.Count,
            queueIds.Distinct(StringComparer.Ordinal).Count(),
            "Every LearningSessionCards StableId must be unique within the table.");
    }

    [TestMethod]
    public async Task Schema10Database_AssignsStableIdsToSessionsAndQueueRowsCreatedAfterMigration()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await Schema10DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var cardId = await Schema10LegacyLearningFixtures.SingleCardIdAsync(fixture);
        var before = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture))
            .Select(row => row.StableId).ToList();

        int newSessionId = 0;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            newSessionId = KnownFirst.Data.Schema8.Schema8LearningRepository.InsertSession(
                connection, new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc), totalCards: 1);
            KnownFirst.Data.Schema8.Schema8LearningRepository.InsertQueueRow(
                connection, newSessionId, cardId, queueOrder: 0, isDueCard: true, targetAnswerVariantId: FirstVariantId(connection));
        });

        var after = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture);
        var created = after.Single(row => row.Id == newSessionId);
        Assert.IsTrue(
            Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(created.StableId),
            "A session created through the Schema-10 repository must carry a StableId.");
        Assert.IsFalse(
            before.Contains(created.StableId, StringComparer.Ordinal),
            "A newly created session must receive a fresh identity, never one already in use.");

        var createdQueueRow = (await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture))
            .Single(row => row.SessionId == newSessionId);
        Assert.IsTrue(
            Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(createdQueueRow.StableId),
            "A queue row created through the Schema-10 repository must carry a StableId.");
    }

    private static int FirstVariantId(SQLiteConnection connection) =>
        connection.ExecuteScalar<int>("SELECT Id FROM AnswerVariants ORDER BY Id LIMIT 1");

    private static async Task<bool> HasColumnAsync(SQLiteAsyncConnection connection, string table, string column)
    {
        var columns = await connection.QueryAsync<TableColumnRow>($"PRAGMA table_info(\"{table}\")");
        return columns.Any(item => string.Equals(item.Name, column, StringComparison.OrdinalIgnoreCase));
    }

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-schema10-{Guid.NewGuid():N}.db3");

    /// <summary>The repository-sanctioned teardown: closes exactly this connection's pooled entry and
    /// deletes exactly its own files. Never the process-wide <c>SQLiteAsyncConnection.ResetPool()</c> —
    /// see <see cref="TemporaryDatabaseFiles"/> for the reproduced host crash that rule exists for.</summary>
    private static Task CloseAsync(SQLiteAsyncConnection? connection, string path) =>
        TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);

    private sealed class TableColumnRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
