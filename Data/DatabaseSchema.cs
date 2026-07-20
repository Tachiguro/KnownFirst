using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data;

public static class DatabaseSchema
{
    public const int CurrentVersion = 7;

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
        await connection.CreateTableAsync<LexicalCacheEntity>();
        await connection.CreateTableAsync<PreparationSessionEntity>();
        await connection.CreateTableAsync<PreparationCandidateEntity>();
        await connection.CreateTableAsync<ContextSnapshotEntity>();
        await connection.CreateTableAsync<LearningCardEntity>();
        await connection.CreateTableAsync<LearningReviewEntity>();
        await connection.CreateTableAsync<LearningSessionEntity>();
        await connection.CreateTableAsync<LearningSessionCardEntity>();
        await connection.ExecuteAsync("DELETE FROM LexicalCache WHERE CacheKey NOT LIKE 'v2|%'");
        await connection.ExecuteAsync($"PRAGMA user_version = {CurrentVersion}");
    }
}
