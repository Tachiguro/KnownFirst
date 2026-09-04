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
    }
}
