using KnownFirst.Data.Migrations.Schema11;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema12;

public static class Schema12DormantMigration
{
    public const int SourceVersion = 11;
    public const int TargetVersion = 12;

    public static async Task<Schema12MigrationResult> ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sourceVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (sourceVersion > TargetVersion)
        {
            throw Schema12MigrationException.FutureVersion(sourceVersion);
        }

        if (sourceVersion == TargetVersion)
        {
            await connection.RunInTransactionAsync(ValidateAlreadyMigratedShape).ConfigureAwait(false);
            return new Schema12MigrationResult(Schema12MigrationOutcome.AlreadyApplied, sourceVersion, TargetVersion);
        }

        if (sourceVersion != SourceVersion)
        {
            throw Schema12MigrationException.UnsupportedSourceVersion(sourceVersion);
        }

        await connection.RunInTransactionAsync(RunMigration).ConfigureAwait(false);
        return new Schema12MigrationResult(Schema12MigrationOutcome.Migrated, sourceVersion, TargetVersion);
    }

    private static void ValidateAlreadyMigratedShape(SQLiteConnection connection)
    {
        if (!Schema12ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema12MigrationException.AlreadyAppliedShapeInvalid(failureDetail!);
        }
    }

    private static void RunMigration(SQLiteConnection connection)
    {
        if (!Schema11ShapeValidator.IsValidDatabase(connection, out var baseFailureDetail))
        {
            throw Schema12MigrationException.InvariantViolation($"Schema-11 source shape is invalid: {baseFailureDetail}");
        }

        if (!Schema12ShapeValidator.TableExists(connection, Schema12Ddl.StateTableName))
        {
            connection.Execute(Schema12Ddl.CreateStateTable);
        }

        if (!Schema12ShapeValidator.TableExists(connection, Schema12Ddl.GrantsTableName))
        {
            connection.Execute(Schema12Ddl.CreateGrantsTable);
        }

        connection.Execute(Schema12Ddl.CreateGrantsDayOrdinalIndex);

        if (!Schema12ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema12MigrationException.InvariantViolation(failureDetail!);
        }

        connection.Execute($"PRAGMA user_version = {TargetVersion}");
    }
}
