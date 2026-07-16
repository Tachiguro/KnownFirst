using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data;

public static class DatabaseSchema
{
    public const int CurrentVersion = 3;

    public static async Task InitializeAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.CreateTableAsync<DocumentEntity>();
        await connection.CreateTableAsync<WordEntity>();
        await connection.CreateTableAsync<WordFormEntity>();
        await connection.CreateTableAsync<SentenceSpanEntity>();
        await connection.CreateTableAsync<WordOccurrenceEntity>();
        await connection.CreateTableAsync<MeaningEntity>();
        await connection.CreateTableAsync<ReviewStateEntity>();
        await connection.CreateTableAsync<ReviewSessionEntity>();
        await connection.CreateTableAsync<ReviewCandidateEntity>();
        await connection.ExecuteAsync($"PRAGMA user_version = {CurrentVersion}");
    }
}
