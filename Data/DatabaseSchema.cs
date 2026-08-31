using KnownFirst.Data.Migrations.Schema13;
using SQLite;

namespace KnownFirst.Data;

public static class DatabaseSchema
{
    public const int CurrentVersion = 13;

    public static async Task InitializeAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.ExecuteAsync("PRAGMA foreign_keys = ON");
        var foreignKeysEnabled = await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys");
        if (foreignKeysEnabled != 1)
        {
            throw new InvalidOperationException(
                "SQLite foreign-key enforcement could not be enabled for this database connection.");
        }

        var existingVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        var hasUserObjects = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type IN ('table', 'index', 'view', 'trigger')
              AND name NOT LIKE 'sqlite_%'
            """) > 0;

        if (existingVersion == 0)
        {
            if (hasUserObjects)
            {
                throw new DatabaseSchemaCompatibilityException(
                    existingVersion,
                    CurrentVersion,
                    DatabaseSchemaCompatibilityReason.UnknownNonEmptyUnversionedDatabase);
            }

            await Schema13CleanBootstrap.ApplyAsync(connection);
            return;
        }

        if (existingVersion is >= 1 and < CurrentVersion)
        {
            throw new DatabaseSchemaCompatibilityException(
                existingVersion,
                CurrentVersion,
                DatabaseSchemaCompatibilityReason.UnsupportedOlderVersion);
        }

        if (existingVersion > CurrentVersion)
        {
            throw new DatabaseSchemaCompatibilityException(
                existingVersion,
                CurrentVersion,
                DatabaseSchemaCompatibilityReason.UnsupportedFutureVersion);
        }

        await connection.RunInTransactionAsync(sqliteConnection =>
        {
            if (!Schema13RuntimeIntegrityValidator.Validate(sqliteConnection, out var failureDetail))
            {
                throw new DatabaseSchemaCompatibilityException(
                    existingVersion,
                    CurrentVersion,
                    DatabaseSchemaCompatibilityReason.InvalidCurrentSchema,
                    failureDetail);
            }
        });
    }
}
