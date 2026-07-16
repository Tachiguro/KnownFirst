using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;

namespace KnownFirst.Services;

public sealed class DashboardService(IKnownFirstDatabase database) : IDashboardService
{
    public Task<DashboardStatistics> GetStatisticsAsync() =>
        database.ReadAsync(async connection =>
        {
            var documentCount = await connection.Table<DocumentEntity>().CountAsync();
            var unreviewedWordCount = await CountWordsAsync(connection, WordStatus.Unreviewed);
            var knownWordCount = await CountWordsAsync(connection, WordStatus.Known);
            var unknownBacklogWordCount = await CountWordsAsync(connection, WordStatus.UnknownBacklog);
            var preparedWordCount = await CountWordsAsync(connection, WordStatus.Prepared);
            var learningWordCount = await CountWordsAsync(connection, WordStatus.Learning);

            return new DashboardStatistics(
                documentCount,
                unreviewedWordCount,
                knownWordCount,
                unknownBacklogWordCount,
                preparedWordCount + learningWordCount);
        });

    private static Task<int> CountWordsAsync(SQLite.SQLiteAsyncConnection connection, WordStatus status) =>
        connection.Table<WordEntity>().Where(word => word.Status == status).CountAsync();
}
