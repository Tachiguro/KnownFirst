using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Services;

public sealed class DashboardService(IKnownFirstDatabase database) : IDashboardService
{
    public Task<DashboardStatistics> GetStatisticsAsync() =>
        database.ExecuteSnapshotAsync(connection =>
        {
            var capability = LearningSchemaCapability.Resolve(connection);
            var documentCount = connection.Table<DocumentEntity>().Count();
            var unreviewedWordCount = CountWords(connection, WordStatus.Unreviewed);
            var knownWordCount = CountWords(connection, WordStatus.Known);
            var unknownBacklogWordCount = CountWords(connection, WordStatus.UnknownBacklog);
            var preparedAndLearningWordCount = capability switch
            {
                LearningSchema7CapabilityResult =>
                    CountWords(connection, WordStatus.Prepared) + CountWords(connection, WordStatus.Learning),
                LearningSchema8CapabilityResult or LearningSchema9CapabilityResult or LearningSchema10CapabilityResult or LearningSchema11CapabilityResult or LearningSchema12CapabilityResult or LearningSchema13CapabilityResult => connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(DISTINCT s.WordId)
                    FROM Senses s
                    JOIN Words w ON w.Id = s.WordId
                    WHERE s.Status IN (?, ?)
                    """,
                    (int)SenseStatus.Prepared,
                    (int)SenseStatus.Learning),
                _ => throw new InvalidOperationException("Unsupported validated learning schema capability.")
            };

            return new DashboardStatistics(
                documentCount,
                unreviewedWordCount,
                knownWordCount,
                unknownBacklogWordCount,
                preparedAndLearningWordCount);
        });

    private static int CountWords(SQLiteConnection connection, WordStatus status) =>
        connection.Table<WordEntity>().Count(word => word.Status == status);
}
