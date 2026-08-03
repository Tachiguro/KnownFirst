using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using SQLite;

namespace KnownFirst.Data;

public static class DatabaseSchema
{
    public const int CurrentVersion = 9;

    public static async Task InitializeAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var existingVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        if (existingVersion > CurrentVersion)
        {
            throw new DatabaseSchemaCompatibilityException(existingVersion, CurrentVersion);
        }

        if (existingVersion <= 6)
        {
            await InitializeSchema7BaselineAsync(connection, existingVersion);
        }

        // An existing Schema-9 database must never rerun Schema8DormantMigration.ApplyAsync: its own
        // future-version guard (target version 8) would reject a source of 9. Schema-7/8 sources (and a
        // freshly baselined database, now at 7) still need it to reach 8 before Schema9DormantMigration runs.
        if (existingVersion <= Schema8DormantMigration.TargetVersion)
        {
            await Schema8DormantMigration.ApplyAsync(connection);
        }

        await Schema9DormantMigration.ApplyAsync(connection);
        await connection.ExecuteAsync("DELETE FROM LexicalCache WHERE CacheKey NOT LIKE 'v2|%'");
    }

    private static async Task InitializeSchema7BaselineAsync(
        SQLiteAsyncConnection connection,
        int existingVersion)
    {
        await connection.CreateTableAsync<DocumentEntity>();
        await connection.CreateTableAsync<WordEntity>();
        await connection.CreateTableAsync<WordFormEntity>();
        await connection.CreateTableAsync<SentenceSpanEntity>();
        await connection.CreateTableAsync<WordOccurrenceEntity>();
        await connection.CreateTableAsync<MeaningEntity>();
        await connection.CreateTableAsync<ReviewStateEntity>();
        await connection.CreateTableAsync<ReviewSessionEntity>();
        await connection.CreateTableAsync<ReviewCandidateEntity>();
        await connection.CreateTableAsync<LexicalCacheEntity>();
        await connection.CreateTableAsync<PreparationSessionEntity>();
        await connection.CreateTableAsync<PreparationCandidateEntity>();
        await connection.CreateTableAsync<ContextSnapshotEntity>();
        await connection.CreateTableAsync<LearningCardEntity>();
        await connection.CreateTableAsync<LearningReviewEntity>();
        await connection.CreateTableAsync<LearningSessionEntity>();
        await connection.CreateTableAsync<LearningSessionCardEntity>();

        await connection.RunInTransactionAsync(transaction =>
        {
            if (existingVersion < 3)
            {
                transaction.Execute(
                    "UPDATE Words SET TokenKind = ? WHERE TokenKind IS NULL",
                    (int)TokenKind.Word);
            }

            if (existingVersion < 4)
            {
                transaction.Execute(
                    "UPDATE Words SET PreparationState = ? WHERE PreparationState IS NULL",
                    (int)PreparationState.Unprepared);
                transaction.Execute(
                    "UPDATE Meanings SET TokenKind = ? WHERE TokenKind IS NULL",
                    (int)TokenKind.Word);
            }

            if (existingVersion < 5)
            {
                transaction.Execute(
                    "UPDATE WordOccurrences SET TechnicalFamily = ? WHERE TechnicalFamily IS NULL",
                    (int)TechnicalTokenFamily.None);
            }

            if (existingVersion < 6)
            {
                transaction.Execute(
                    "UPDATE Documents SET LookupMode = ? WHERE LookupMode IS NULL",
                    (int)LexicalLookupMode.Definition);
                transaction.Execute(
                    "UPDATE LexicalCache SET LookupMode = ? WHERE LookupMode IS NULL",
                    (int)LexicalLookupMode.Definition);
            }

            transaction.Execute(
                "UPDATE Words SET AutomaticInteractionMode = ? WHERE AutomaticInteractionMode IS NULL",
                (int)LearningInteractionMode.Reading);
            transaction.Execute($"PRAGMA user_version = {Schema8DormantMigration.SourceVersion}");
        });
    }
}
