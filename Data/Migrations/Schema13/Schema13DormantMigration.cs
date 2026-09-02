using KnownFirst.Data.Migrations.Schema12;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

public static class Schema13DormantMigration
{
    public const int SourceVersion = 12;
    public const int TargetVersion = 13;

    public static async Task<Schema13MigrationResult> ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sourceVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (sourceVersion > TargetVersion)
        {
            throw Schema13MigrationException.FutureVersion(sourceVersion);
        }

        if (sourceVersion == TargetVersion)
        {
            await connection.RunInTransactionAsync(ValidateAlreadyApplied).ConfigureAwait(false);
            return new Schema13MigrationResult(Schema13MigrationOutcome.AlreadyApplied, sourceVersion, TargetVersion);
        }

        if (sourceVersion != SourceVersion)
        {
            throw Schema13MigrationException.UnsupportedSourceVersion(sourceVersion);
        }

        await connection.RunInTransactionAsync(RunMigration).ConfigureAwait(false);
        return new Schema13MigrationResult(Schema13MigrationOutcome.Migrated, sourceVersion, TargetVersion);
    }

    private static void ValidateAlreadyApplied(SQLiteConnection connection)
    {
        if (!Schema13RuntimeIntegrityValidator.Validate(connection, out var integrityFailureDetail))
        {
            throw Schema13MigrationException.AlreadyAppliedShapeInvalid(
                $"Runtime integrity is invalid: {integrityFailureDetail}");
        }
    }

    private static void RunMigration(SQLiteConnection connection)
    {
        if (!Schema12ShapeValidator.IsValidDatabase(connection, out var sourceFailureDetail))
        {
            throw Schema13MigrationException.InvariantViolation(
                $"Schema-12 source shape is invalid: {sourceFailureDetail}");
        }

        RejectPreExistingTargetArtifacts(connection);
        Schema13TargetShapeBuilder.Create(connection);

        var plan = Schema13LearningBootstrap.BuildPlan(connection);
        MaterializePlan(connection, plan);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out var shapeFailureDetail))
        {
            throw Schema13MigrationException.InvariantViolation(
                $"Schema-13 target shape is invalid: {shapeFailureDetail}");
        }

        if (!Schema13MigrationIntegrityValidator.Validate(connection, out var integrityFailureDetail))
        {
            throw Schema13MigrationException.InvariantViolation(
                $"Schema-13 source-to-target migration integrity is invalid: {integrityFailureDetail}");
        }

        if (!Schema13RuntimeIntegrityValidator.Validate(connection, out var runtimeIntegrityFailureDetail))
        {
            throw Schema13MigrationException.InvariantViolation(
                $"Schema-13 runtime integrity is invalid: {runtimeIntegrityFailureDetail}");
        }

        connection.Execute($"PRAGMA user_version = {TargetVersion}");
    }

    private static void RejectPreExistingTargetArtifacts(SQLiteConnection connection)
    {
        var artifacts = new (string Type, string Name)[]
        {
            ("table", Schema13Ddl.FsrsCardStatesTableName),
            ("table", Schema13Ddl.FsrsReviewHistoryEntriesTableName),
            ("table", Schema13Ddl.WordLearningControlsTableName),
            ("table", Schema13Ddl.SenseLearningControlsTableName),
            ("index", Schema13Ddl.FsrsCardStatesDueIndexName),
            ("index", Schema13Ddl.FsrsReviewHistoryEntriesStableIdIndexName),
            ("index", Schema13Ddl.FsrsReviewHistoryEntriesCardSequenceIndexName),
            ("index", Schema13Ddl.FsrsReviewHistoryEntriesReplayIndexName)
        };

        foreach (var (type, name) in artifacts)
        {
            var exists = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = ? AND name = ?",
                type,
                name) > 0;
            if (exists)
            {
                throw Schema13MigrationException.InvariantViolation(
                    $"Schema-12 source already contains Schema-13 target {type} '{name}'.");
            }
        }
    }

    private static void MaterializePlan(SQLiteConnection connection, Schema13BootstrapPlan plan)
    {
        foreach (var control in plan.WordControls)
        {
            connection.Execute(
                "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
                control.WordId,
                control.DecidedAtUtc);
        }

        foreach (var state in plan.CardStates)
        {
            connection.Execute(
                """
                INSERT INTO FsrsCardStates (
                    CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                state.CardId,
                (int)state.Card.State,
                state.Card.Stability,
                state.Card.Difficulty,
                state.Card.LastReviewedAtUtc.HasValue
                    ? Schema13TimestampCodec.FormatUtc(state.Card.LastReviewedAtUtc.Value)
                    : null,
                state.Card.StepIndex,
                state.Card.DueAtUtc.HasValue
                    ? Schema13TimestampCodec.FormatUtc(state.Card.DueAtUtc.Value)
                    : null);
        }

        foreach (var history in plan.ReviewHistory)
        {
            connection.Execute(
                """
                INSERT INTO FsrsReviewHistoryEntries (
                    StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES (?, ?, ?, ?, ?)
                """,
                history.StableId,
                history.CardId,
                history.SequenceNumber,
                history.Rating,
                history.ReviewedAtUtc);
        }
    }
}
