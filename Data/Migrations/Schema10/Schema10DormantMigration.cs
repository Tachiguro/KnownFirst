using KnownFirst.Data.Migrations.Schema9;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema10;

/// <summary>
/// The Schema 9 -&gt; 10 migration engine (KF-BACKUP-005A): gives every learning-workflow row a persistent,
/// immutable identity. Adds a <c>StableId</c> column to <c>LearningSessions</c> and
/// <c>LearningSessionCards</c>, backfills every existing row through
/// <see cref="Schema10LearningIdentityBootstrap"/>, and creates the unique index on each column — all
/// inside one real SQLite transaction, so any failure rolls the columns, the values and the version back
/// to the original Schema-9 state.
///
/// <para>Never updates, copies, rebuilds, deletes, or renumbers any learning row: the only write to
/// pre-existing data is populating the new column. Re-running against an already-migrated database
/// revalidates the shape and returns <see cref="Schema10MigrationOutcome.AlreadyApplied"/> without
/// touching a single identity — an assigned StableId is immutable, and that includes being immune to
/// repeated initialization.</para>
/// </summary>
public static class Schema10DormantMigration
{
    public const int SourceVersion = 9;
    public const int TargetVersion = 10;

    public static async Task<Schema10MigrationResult> ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sourceVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (sourceVersion > TargetVersion)
        {
            throw Schema10MigrationException.FutureVersion(sourceVersion);
        }

        if (sourceVersion == TargetVersion)
        {
            await connection.RunInTransactionAsync(ValidateAlreadyMigratedShape).ConfigureAwait(false);
            return new Schema10MigrationResult(Schema10MigrationOutcome.AlreadyApplied, sourceVersion, TargetVersion);
        }

        if (sourceVersion != SourceVersion)
        {
            throw Schema10MigrationException.UnsupportedSourceVersion(sourceVersion);
        }

        await connection.RunInTransactionAsync(RunMigration).ConfigureAwait(false);
        return new Schema10MigrationResult(Schema10MigrationOutcome.Migrated, sourceVersion, TargetVersion);
    }

    private static void ValidateAlreadyMigratedShape(SQLiteConnection connection)
    {
        if (!Schema10ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema10MigrationException.AlreadyAppliedShapeInvalid(failureDetail!);
        }
    }

    private static void RunMigration(SQLiteConnection connection)
    {
        if (!Schema9ShapeValidator.IsValidDatabase(connection, out var baseFailureDetail))
        {
            throw Schema10MigrationException.InvariantViolation($"Schema-9 source shape is invalid: {baseFailureDetail}");
        }

        // A Schema-9 database cannot already carry these columns; the guard exists so a partially applied
        // shape produces the validator's precise failure below rather than a raw duplicate-column error.
        if (!Schema10ShapeValidator.HasStableIdColumn(connection, Schema10Ddl.SessionTable))
        {
            connection.Execute(Schema10Ddl.AddSessionStableIdColumn);
        }

        if (!Schema10ShapeValidator.HasStableIdColumn(connection, Schema10Ddl.QueueTable))
        {
            connection.Execute(Schema10Ddl.AddQueueStableIdColumn);
        }

        Schema10LearningIdentityBootstrap.Apply(connection);

        // Created only after the backfill: a unique index over a column that is still entirely NULL would
        // succeed (SQLite permits repeated NULLs) and would therefore prove nothing about the values.
        connection.Execute(Schema10Ddl.CreateSessionStableIdIndex);
        connection.Execute(Schema10Ddl.CreateQueueStableIdIndex);

        if (!Schema10ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema10MigrationException.InvariantViolation(failureDetail!);
        }

        connection.Execute($"PRAGMA user_version = {TargetVersion}");
    }
}
