using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

internal static class Schema13TargetShapeBuilder
{
    public static void Create(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.Execute(Schema13Ddl.CreateFsrsCardStatesTable);
        connection.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
        connection.Execute(Schema13Ddl.CreateWordLearningControlsTable);
        connection.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
    }
}
