using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema9;

/// <summary>
/// The Schema 8 -&gt; 9 migration engine: an index-only change to <c>ReviewSessions</c>. Replaces the
/// legacy Schema-7/8 bootstrap unique index on <c>ReviewSessions(DocumentId)</c> — structurally
/// discovered, never guessed or hardcoded by name — with a non-unique lookup index plus a unique partial
/// index restricted to Active sessions (<c>Status = 0</c>), so multiple Completed sessions can exist for
/// one Document while at most one Active session still can. Runs the complete transformation inside one
/// real SQLite transaction; any failure rolls back the dropped/created indexes and the version to the
/// original Schema-8 state. Never updates, copies, rebuilds, deletes, or renumbers any
/// <c>ReviewSessions</c> or <c>ReviewCandidates</c> row.
/// </summary>
public static class Schema9DormantMigration
{
    public const int SourceVersion = 8;
    public const int TargetVersion = 9;

    public static async Task<Schema9MigrationResult> ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sourceVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (sourceVersion > TargetVersion)
        {
            throw Schema9MigrationException.FutureVersion(sourceVersion);
        }

        if (sourceVersion == TargetVersion)
        {
            await connection.RunInTransactionAsync(ValidateAlreadyMigratedShape).ConfigureAwait(false);
            return new Schema9MigrationResult(Schema9MigrationOutcome.AlreadyApplied, sourceVersion, TargetVersion);
        }

        if (sourceVersion != SourceVersion)
        {
            throw Schema9MigrationException.UnsupportedSourceVersion(sourceVersion);
        }

        await connection.RunInTransactionAsync(RunMigration).ConfigureAwait(false);
        return new Schema9MigrationResult(Schema9MigrationOutcome.Migrated, sourceVersion, TargetVersion);
    }

    private static void ValidateAlreadyMigratedShape(SQLiteConnection connection)
    {
        if (!Schema9ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema9MigrationException.AlreadyAppliedShapeInvalid(failureDetail!);
        }
    }

    private static void RunMigration(SQLiteConnection connection)
    {
        if (!Schema8ShapeValidator.IsValidDatabase(connection, out var baseFailureDetail))
        {
            throw Schema9MigrationException.InvariantViolation($"Schema-8 source shape is invalid: {baseFailureDetail}");
        }

        var legacyIndexes = Schema9ShapeValidator.FindNonPartialUniqueSingleColumnIndexes(connection, "ReviewSessions", "DocumentId");
        if (legacyIndexes.Length == 0)
        {
            throw Schema9MigrationException.MissingLegacyIndex();
        }

        if (legacyIndexes.Length > 1)
        {
            throw Schema9MigrationException.AmbiguousLegacyIndex(legacyIndexes.Length);
        }

        connection.Execute($"DROP INDEX \"{EscapeIdentifier(legacyIndexes[0])}\"");
        connection.Execute(Schema9Ddl.CreateNormalIndex);
        connection.Execute(Schema9Ddl.CreateActiveIndex);

        if (!Schema9ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw Schema9MigrationException.InvariantViolation(failureDetail!);
        }

        connection.Execute($"PRAGMA user_version = {TargetVersion}");
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");
}
