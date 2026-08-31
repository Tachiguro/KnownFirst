using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Migrations.Schema10;
using KnownFirst.Data.Migrations.Schema11;
using KnownFirst.Data.Migrations.Schema12;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

/// <summary>
/// Constructs the final Schema-13 shape only for a genuinely empty, unversioned database. It does not
/// run a historical migration, backfill a legacy row, or assign an intermediate schema version.
/// </summary>
public static class Schema13CleanBootstrap
{
    public static async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await connection.RunInTransactionAsync(CreateFinalShape).ConfigureAwait(false);
    }

    private static void CreateFinalShape(SQLiteConnection connection)
    {
        RequireGenuinelyFresh(connection);
        if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
        {
            throw new InvalidOperationException(
                "SQLite foreign-key enforcement must be enabled before clean Schema-13 bootstrap.");
        }

        CreateBaselineTables(connection);
        CreateSchema8Shape(connection);
        CreateSchema9IndexShape(connection);
        CreateSchema10Shape(connection);
        CreateSchema11Shape(connection);
        CreateSchema12Shape(connection);
        Schema13TargetShapeBuilder.Create(connection);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out var shapeFailureDetail))
        {
            throw new InvalidOperationException(
                $"Clean Schema-13 bootstrap produced an invalid shape: {shapeFailureDetail}");
        }

        if (!Schema13RuntimeIntegrityValidator.Validate(connection, out var runtimeFailureDetail))
        {
            throw new InvalidOperationException(
                $"Clean Schema-13 bootstrap produced invalid runtime integrity: {runtimeFailureDetail}");
        }

        var foreignKeyViolations = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_foreign_key_check");
        if (foreignKeyViolations != 0)
        {
            throw new InvalidOperationException(
                $"Clean Schema-13 bootstrap produced {foreignKeyViolations} foreign-key violation(s).");
        }

        connection.Execute($"PRAGMA user_version = {DatabaseSchema.CurrentVersion}");
    }

    private static void RequireGenuinelyFresh(SQLiteConnection connection)
    {
        var version = connection.ExecuteScalar<int>("PRAGMA user_version");
        var userObjectCount = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type IN ('table', 'index', 'view', 'trigger')
              AND name NOT LIKE 'sqlite_%'
            """);
        if (version != 0 || userObjectCount != 0)
        {
            throw new InvalidOperationException(
                "Clean Schema-13 bootstrap requires user_version 0 and no non-internal SQLite objects.");
        }
    }

    private static void CreateBaselineTables(SQLiteConnection connection)
    {
        connection.CreateTable<DocumentEntity>();
        connection.CreateTable<WordEntity>();
        connection.CreateTable<WordFormEntity>();
        connection.CreateTable<SentenceSpanEntity>();
        connection.CreateTable<WordOccurrenceEntity>();
        connection.CreateTable<MeaningEntity>();
        connection.CreateTable<ReviewStateEntity>();
        connection.CreateTable<ReviewSessionEntity>();
        connection.CreateTable<ReviewCandidateEntity>();
        connection.CreateTable<LexicalCacheEntity>();
        connection.CreateTable<PreparationSessionEntity>();
        connection.CreateTable<PreparationCandidateEntity>();
        connection.CreateTable<ContextSnapshotEntity>();
        connection.CreateTable<LearningCardEntity>();
        connection.CreateTable<LearningReviewEntity>();
        connection.CreateTable<LearningSessionEntity>();
        connection.CreateTable<LearningSessionCardEntity>();
    }

    private static void CreateSchema8Shape(SQLiteConnection connection)
    {
        connection.Execute(Schema8Ddl.CreateSenses);
        connection.Execute(Schema8Ddl.IndexSensesStableId);
        connection.Execute(Schema8Ddl.IndexSensesWordId);
        connection.Execute(Schema8Ddl.CreateAnswerVariants);
        connection.Execute(Schema8Ddl.IndexAnswerVariantsStableId);
        connection.Execute(Schema8Ddl.IndexAnswerVariantsSenseLanguageText);
        connection.Execute(Schema8Ddl.CreateSenseAnswerVariantAssignments);
        connection.Execute(Schema8Ddl.IndexAssignmentsStableId);
        connection.Execute(Schema8Ddl.IndexAssignmentsSenseDirectionVariant);
        connection.Execute(Schema8Ddl.IndexAssignmentsSenseDirectionPreferred);
        connection.Execute(Schema8Ddl.CreateAnswerVariantProgress);
        connection.Execute(Schema8Ddl.IndexProgressCardVariant);

        connection.Execute("ALTER TABLE Meanings ADD COLUMN SenseId INTEGER NULL");
        connection.Execute("ALTER TABLE Meanings ADD COLUMN StableId TEXT NULL");
        connection.Execute("ALTER TABLE ContextSnapshots ADD COLUMN SenseId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningReviews ADD COLUMN TargetAnswerVariantId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningReviews ADD COLUMN MatchedAnswerVariantId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningSessionCards ADD COLUMN TargetAnswerVariantId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningCards ADD COLUMN SenseId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningCards RENAME COLUMN MeaningId TO PreferredMeaningId");
        connection.Execute($"DROP INDEX IF EXISTS {Schema8Ddl.OldCardIndexName}");
        connection.Execute(Schema8Ddl.IndexLearningCardsSenseDirection);
    }

    private static void CreateSchema9IndexShape(SQLiteConnection connection)
    {
        var legacyIndexes = Schema9ShapeValidator.FindNonPartialUniqueSingleColumnIndexes(
            connection,
            "ReviewSessions",
            "DocumentId");
        if (legacyIndexes.Length != 1)
        {
            throw new InvalidOperationException(
                $"Clean Schema-13 bootstrap expected one baseline ReviewSessions(DocumentId) index, found {legacyIndexes.Length}.");
        }

        connection.Execute($"DROP INDEX \"{EscapeIdentifier(legacyIndexes[0])}\"");
        connection.Execute(Schema9Ddl.CreateNormalIndex);
        connection.Execute(Schema9Ddl.CreateActiveIndex);
    }

    private static void CreateSchema10Shape(SQLiteConnection connection)
    {
        connection.Execute(Schema10Ddl.AddSessionStableIdColumn);
        connection.Execute(Schema10Ddl.AddQueueStableIdColumn);
        connection.Execute(Schema10Ddl.CreateSessionStableIdIndex);
        connection.Execute(Schema10Ddl.CreateQueueStableIdIndex);
    }

    private static void CreateSchema11Shape(SQLiteConnection connection)
    {
        connection.Execute(Schema11Ddl.CreateTable);
        connection.Execute(Schema11Ddl.CreateOwnerIndex);
        connection.Execute(Schema11Ddl.CreateUniqueEvidenceIndex);
    }

    private static void CreateSchema12Shape(SQLiteConnection connection)
    {
        connection.Execute(Schema12Ddl.CreateStateTable);
        connection.Execute(Schema12Ddl.CreateGrantsTable);
        connection.Execute(Schema12Ddl.CreateGrantsDayOrdinalIndex);
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");
}
