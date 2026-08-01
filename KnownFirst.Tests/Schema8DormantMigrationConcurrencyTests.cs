using System.Threading;
using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 1 — proves the <c>SenseAnswerVariantAssignments</c> partial unique index
/// (architecture doc §2.6) is a real, engine-enforced singleton constraint: a single connection cannot
/// create a second preferred assignment for the same (SenseId, CardDirection), and two genuinely
/// concurrent connections racing to create the first one always produce exactly one committed winner.
/// Uses a <see cref="Barrier"/> to synchronize start, never a sleep.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema8DormantMigrationConcurrencyTests
{
    [TestMethod]
    public async Task PartialUniqueIndex_SecondPreferredAssignment_SameConnection_ThrowsConstraintViolation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("preferred-race");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "preferred-race", translation: "Vorzugsrennen");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);

        // MeaningToTerm already has a preferred assignment (the migrated term-side variant). A second
        // preferred assignment for the same (SenseId, CardDirection) must be rejected by the engine.
        var exception = await Assert.ThrowsExactlyAsync<SQLiteException>(() => fixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "INSERT INTO AnswerVariants (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'other-term', 'other-term', NULL, ?, ?)",
                Guid.NewGuid().ToString("N"), senseId, DateTime.UtcNow, DateTime.UtcNow);
            var variantId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
            connection.Execute(
                "INSERT INTO SenseAnswerVariantAssignments (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, 1, ?, ?)",
                Guid.NewGuid().ToString("N"), senseId, (int)CardDirection.MeaningToTerm, variantId, (int)AnswerVariantRequirement.AcceptedOnly, DateTime.UtcNow, DateTime.UtcNow);
        }));

        Assert.IsNotNull(exception);

        var preferredCount = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection,
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ? AND IsPreferred = 1",
            senseId, (int)CardDirection.MeaningToTerm);
        Assert.AreEqual(1, preferredCount); // the failed attempt never committed
    }

    [TestMethod]
    public async Task PartialUniqueIndex_TwoConcurrentConnections_ExactlyOneCommittedWinner()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("race");
        // No Translation -> migration creates no TermToMeaning assignment at all, so that direction starts
        // with zero preferred rows: both racing connections compete to create the very first one.
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "race", translation: "");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var databasePath = fixture.DatabasePath;

        // Release this fixture's pooled async connection so the two raw connections below race for the file
        // lock instead of contending with it. CloseAsync removes exactly this connection string's pooled
        // entry; a process-wide pool reset would also close handles owned by concurrently running tests.
        await fixture.Connection.CloseAsync();

        using var barrier = new Barrier(2);

        var taskA = Task.Run(() => RaceForPreferredAssignment(databasePath, senseId, "variant-a", barrier));
        var taskB = Task.Run(() => RaceForPreferredAssignment(databasePath, senseId, "variant-b", barrier));

        var results = await Task.WhenAll(taskA, taskB);

        var successCount = results.Count(r => r.Succeeded);
        Assert.AreEqual(1, successCount, $"Expected exactly one winner. A: succeeded={results[0].Succeeded} ({results[0].ErrorMessage}); B: succeeded={results[1].Succeeded} ({results[1].ErrorMessage})");
        Assert.IsTrue(results.Any(r => !r.Succeeded), "Expected the loser to fail with a constraint violation, not silently succeed.");

        using var verifyConnection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly);
        var preferredCount = verifyConnection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ? AND IsPreferred = 1",
            senseId, (int)CardDirection.TermToMeaning);
        Assert.AreEqual(1, preferredCount);
    }

    private static (bool Succeeded, string? ErrorMessage) RaceForPreferredAssignment(
        string databasePath, int senseId, string variantText, Barrier barrier)
    {
        using var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite);
        connection.BusyTimeout = TimeSpan.FromSeconds(10);

        barrier.SignalAndWait();

        try
        {
            connection.RunInTransaction(() =>
            {
                connection.Execute(
                    "INSERT INTO AnswerVariants (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', ?, ?, NULL, ?, ?)",
                    Guid.NewGuid().ToString("N"), senseId, variantText, variantText, DateTime.UtcNow, DateTime.UtcNow);
                var variantId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
                connection.Execute(
                    "INSERT INTO SenseAnswerVariantAssignments (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, 1, ?, ?)",
                    Guid.NewGuid().ToString("N"), senseId, (int)CardDirection.TermToMeaning, variantId, (int)AnswerVariantRequirement.AcceptedOnly, DateTime.UtcNow, DateTime.UtcNow);
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
