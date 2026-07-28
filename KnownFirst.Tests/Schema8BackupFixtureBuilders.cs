using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>Shared fixture helpers for KF-MEANING-001 Slice 2 archive-v2/Schema-8 backup tests.</summary>
internal static class Schema8BackupFixtureBuilders
{
    internal sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

    /// <summary>
    /// Builds a representative Schema-7 fixture (a grouped Sense pair via a shared provider sense id, a
    /// split Sense pair with no discriminator, both card directions, reviews, queue items, and automatic
    /// progress state), then migrates it in place via <see cref="Schema8DormantMigration.ApplyAsync"/> —
    /// producing a real Schema-8 database for capture/restore/parity tests.
    /// </summary>
    public static async Task<Schema7Fixture> CreateAndMigrateRepresentativeFixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();

        var documentId = await fixture.InsertDocumentAsync(title: "Doc A", content: "the bank text");
        var wordId = await fixture.InsertWordAsync("bank", status: KnownFirst.Models.WordStatus.Learning);

        // Grouped pair: same provider sense id (reliable discriminator) — becomes one Sense.
        var groupedMeaning1 = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "finance-1",
            definition: "financial institution");
        var groupedMeaning2 = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Kreditinstitut", selectedMeaningId: "finance-1",
            definition: "money-handling institution");

        var termCardId = await fixture.InsertCardAsync(wordId, groupedMeaning1, CardDirection.MeaningToTerm, intervalDays: 3);
        var meaningCardId = await fixture.InsertCardAsync(wordId, groupedMeaning1, CardDirection.TermToMeaning, intervalDays: 5);

        var sessionId = await fixture.InsertLearningSessionAsync();
        await fixture.InsertContextAsync(groupedMeaning1, wordId, sourceDocumentId: documentId, sourceDocumentTitle: "Doc A", text: "the bank text", targetStart: 4, targetLength: 4);
        await fixture.InsertReviewAsync(termCardId, sessionId: sessionId, wasTypedAnswer: true, wasCorrect: true);
        await fixture.InsertReviewAsync(meaningCardId, sessionId: sessionId, wasTypedAnswer: false, wasCorrect: true);
        await fixture.InsertQueueItemAsync(sessionId: sessionId, cardId: termCardId, queueOrder: 1);
        await fixture.InsertQueueItemAsync(sessionId: sessionId, cardId: meaningCardId, queueOrder: 2, isCompleted: true);

        // Split pair (a second word): no reliable discriminator, differing content — must split.
        var wordId2 = await fixture.InsertWordAsync("light");
        await fixture.InsertMeaningAsync(wordId2, displayTerm: "light", translation: "Licht", definition: "illumination");
        await fixture.InsertMeaningAsync(wordId2, displayTerm: "light", translation: "leicht", definition: "not heavy");

        return fixture;
    }

    /// <summary>Migrates the fixture's underlying database (must be a still-Schema-7 fixture) to Schema 8.</summary>
    public static async Task MigrateAsync(Schema7Fixture fixture)
    {
        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        if (result.Outcome is not (Schema8MigrationOutcome.Migrated or Schema8MigrationOutcome.AlreadyApplied))
        {
            throw new InvalidOperationException("Expected the fixture to migrate to Schema 8.");
        }
    }

    /// <summary>Convenience: build, migrate, and return a synchronously-usable Schema-8 fixture in one call.</summary>
    public static async Task<Schema7Fixture> CreateSchema8FixtureAsync()
    {
        var fixture = await CreateAndMigrateRepresentativeFixtureAsync();
        await MigrateAsync(fixture);
        return fixture;
    }

    /// <summary>Builds an empty (zero-row) Schema-8 fixture — a Schema-7 fixture with no data, migrated.</summary>
    public static async Task<Schema7Fixture> CreateEmptySchema8FixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await MigrateAsync(fixture);
        return fixture;
    }

    /// <summary>
    /// <see cref="IKnownFirstDatabase"/> adapter over an already-Schema-8 <see cref="Schema7Fixture"/>
    /// connection — lets <see cref="Services.DataSafety.BackupService"/> and
    /// <see cref="Services.DataSafety.Merge.MergeSafetyCopyService"/> be exercised directly against a
    /// synthetic Schema-8 database in tests, without ever going through
    /// <see cref="DatabaseSchema.InitializeAsync"/> (which never creates Schema-8 shape).
    /// </summary>
    internal sealed class Schema8DatabaseAdapter(Schema7Fixture fixture) : IKnownFirstDatabase
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public string DatabasePath => fixture.DatabasePath;

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                return await operation(fixture.Connection);
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
                T? result = default;
                await fixture.Connection.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                T? result = default;
                await fixture.Connection.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ResetAsync() => throw new NotSupportedException("Not used by these tests.");
    }
}
