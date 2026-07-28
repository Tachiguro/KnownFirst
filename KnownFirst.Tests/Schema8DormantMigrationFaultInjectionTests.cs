using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 1 — crash-mid-migration rollback proofs (deterministic fault injection at named
/// checkpoints, never a real bug), retry-after-rollback, and the RENAME COLUMN vs table-rebuild fallback.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema8DormantMigrationFaultInjectionTests
{
    private sealed class InjectedTestFault : Exception
    {
        public InjectedTestFault(string checkpoint) : base($"injected-{checkpoint}")
        {
        }
    }

    private static async Task<(int wordId, int meaningId, int cardId)> SeedOneWordAsync(Schema7Fixture fixture)
    {
        var wordId = await fixture.InsertWordAsync("fault-test");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "fault-test", translation: "Fehlertest");
        var cardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        return (wordId, meaningId, cardId);
    }

    [TestMethod]
    public async Task FailureAfterTableCreation_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-table-creation")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task FailureAfterBackfill_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-backfill")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task FailureAfterOldIndexRemoval_BeforeVersionUpdate_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-old-index-drop")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Word_Direction"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Sense_Direction"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task RetryAfterRollback_SucceedsCleanly()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var (wordId, meaningId, cardId) = await SeedOneWordAsync(fixture);

        var failingOptions = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-backfill")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, failingOptions));
        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));

        var retryResult = await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, retryResult.Outcome);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.AreEqual(
            meaningId,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
    }

    [TestMethod]
    public async Task ForcedColumnRebuildFallback_ProducesIdenticalValidSchema8Shape()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var (wordId, meaningId, cardId) = await SeedOneWordAsync(fixture);
        var secondCardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, intervalDays: 7);
        await fixture.InsertReviewAsync(cardId);

        var options = new Schema8MigrationOptions { ForceColumnRebuildFallback = true };
        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection, options);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);
        Assert.IsTrue(result.UsedColumnRebuildFallback);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "PreferredMeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Sense_Direction"));

        // Row IDs and values survive the rebuild verbatim.
        Assert.AreEqual(meaningId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(7, await fixture.Connection.ExecuteScalarAsync<int>("SELECT IntervalDays FROM LearningCards WHERE Id = ?", secondCardId));
        Assert.AreEqual(2, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards"));
    }

    [TestMethod]
    public async Task BundledSqliteCapability_IsDetectedAndRenameColumnPathIsUsedByDefault()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        string? detectedVersion = null;
        bool supportsRename = false;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            detectedVersion = Schema8SqliteCapabilities.GetSqliteVersion(connection);
            supportsRename = Schema8SqliteCapabilities.SupportsRenameColumn(connection);
        });

        Console.WriteLine($"Bundled SQLite version detected: {detectedVersion}; supports RENAME COLUMN: {supportsRename}");
        Assert.IsTrue(supportsRename, $"Expected bundled SQLite ({detectedVersion}) to support ALTER TABLE ... RENAME COLUMN (>= 3.25.0).");

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        Assert.IsFalse(result.UsedColumnRebuildFallback, "Default migration should take the fast RENAME COLUMN path on this bundled SQLite.");
    }
}
