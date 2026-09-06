namespace KnownFirst.Data.Targets;

using SQLite;

internal static class TargetPersistenceShapeBuilder
{
    public static void Create(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.Execute(TargetPersistenceDdl.CreateLearningTargetsTable);
        connection.Execute(TargetPersistenceDdl.CreateLearningTargetsStableIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateLearningTargetsSenseIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateLearningTargetsSemanticIndex);

        connection.Execute(TargetPersistenceDdl.CreateTargetAnswerVariantsTable);
        connection.Execute(TargetPersistenceDdl.CreateTargetAnswerVariantsStableIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetAnswerVariantsTargetIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetAnswerVariantsNormalizedTextIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetAnswerVariantsPreferredIndex);

        connection.Execute(TargetPersistenceDdl.CreateTargetFsrsStatesTable);
        connection.Execute(TargetPersistenceDdl.CreateTargetFsrsStatesDueIndex);

        connection.Execute(TargetPersistenceDdl.CreateTargetFsrsReviewHistoryEntriesTable);
        connection.Execute(TargetPersistenceDdl.CreateTargetFsrsReviewHistoryEntriesStableIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetFsrsReviewHistoryEntriesTargetSequenceIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetFsrsReviewHistoryEntriesReplayIndex);

        connection.Execute(TargetPersistenceDdl.CreateTargetReviewsTable);
        connection.Execute(TargetPersistenceDdl.CreateTargetReviewsStableIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetReviewsTargetIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetReviewsSessionIdIndex);
        connection.Execute(TargetPersistenceDdl.CreateTargetReviewsTargetReviewedAtIndex);
    }
}
