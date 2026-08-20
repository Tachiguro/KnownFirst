using KnownFirst.Data.Migrations.Schema10;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema11;

public static class Schema11DormantMigration
{
    public const int SourceVersion = 10;
    public const int TargetVersion = 11;

    public static async Task<Schema11MigrationResult> ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sourceVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (sourceVersion > TargetVersion)
        {
            throw Schema11MigrationException.FutureVersion(sourceVersion);
        }

        if (sourceVersion == TargetVersion)
        {
            await connection.RunInTransactionAsync(ValidateAlreadyMigratedShape).ConfigureAwait(false);
            return new Schema11MigrationResult(Schema11MigrationOutcome.AlreadyApplied, sourceVersion, TargetVersion);
        }

        if (sourceVersion != SourceVersion)
        {
            throw Schema11MigrationException.UnsupportedSourceVersion(sourceVersion);
        }

        await connection.RunInTransactionAsync(RunMigration).ConfigureAwait(false);
        return new Schema11MigrationResult(Schema11MigrationOutcome.Migrated, sourceVersion, TargetVersion);
    }

    private static void ValidateAlreadyMigratedShape(SQLiteConnection connection)
    {
        if (!Schema11ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema11MigrationException.AlreadyAppliedShapeInvalid(failureDetail!);
        }
    }

    private static void RunMigration(SQLiteConnection connection)
    {
        if (!Schema10ShapeValidator.IsValidDatabase(connection, out var baseFailureDetail))
        {
            throw Schema11MigrationException.InvariantViolation($"Schema-10 source shape is invalid: {baseFailureDetail}");
        }

        if (!Schema11ShapeValidator.TableExists(connection, Schema11Ddl.TableName))
        {
            connection.Execute(Schema11Ddl.CreateTable);
        }

        connection.Execute(Schema11Ddl.CreateOwnerIndex);
        connection.Execute(Schema11Ddl.CreateUniqueEvidenceIndex);

        if (!Schema11ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema11MigrationException.InvariantViolation(failureDetail!);
        }

        connection.Execute($"PRAGMA user_version = {TargetVersion}");
    }
}
