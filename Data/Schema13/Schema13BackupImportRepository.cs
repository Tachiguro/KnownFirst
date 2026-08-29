using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data.Schema13;

/// <summary>
/// Transaction-local empty-target restore for an already validated Schema-13 database. The caller owns
/// the enclosing transaction; this repository never changes <c>PRAGMA user_version</c> and never invokes
/// the dormant schema migration.
/// </summary>
public static class Schema13BackupImportRepository
{
    public static class Checkpoints
    {
        public const string AfterBaseGraph = "Schema13AfterBaseGraph";
        public const string DuringFsrsReviewHistoryInsertion = "Schema13DuringFsrsReviewHistoryInsertion";
        public const string DuringFsrsCardStateInsertion = "Schema13DuringFsrsCardStateInsertion";
        public const string BeforeFinalIntegrityValidation = "Schema13BeforeFinalIntegrityValidation";
    }

    public static bool HasDurableUserData(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return Schema8BackupImportRepository.HasDurableUserData(connection)
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates") != 0;
    }

    public static void ImportNativeV3IntoEmptyDatabase(
        SQLiteConnection connection,
        ValidatedSchema13Capability capability,
        BackupPayloadV3 payload,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(payload);

        // Defensive repository boundary: service routing supplies a reader-validated payload, but direct
        // callers cannot bypass the same complete field and graph validation before the first mutation.
        BackupModelContractV3.ValidatePayload(payload);
        BackupArchiveWriterV3.ValidatePayloadGraphV3(payload);
        ValidateEmptyTarget(connection);

        var basePayload = ToV2Payload(payload);
        var maps = Schema8BackupImportRepository.ImportIntoEmptySchema8DatabaseWithMappings(
            connection,
            new ValidatedSchema8Capability(),
            basePayload,
            cancellationToken,
            failureInjector);
        failureInjector?.AtCheckpoint(Checkpoints.AfterBaseGraph);

        var mutationCount = 0;
        foreach (var control in payload.WordLearningControls)
        {
            ExecuteMutation(
                connection,
                "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
                cancellationToken,
                failureInjector,
                ref mutationCount,
                RequireId(maps.WordIds, control.VocabularyId),
                Schema13TimestampCodec.FormatUtc(control.DecidedAtUtc));
        }

        foreach (var control in payload.SenseLearningControls)
        {
            ExecuteMutation(
                connection,
                "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)",
                cancellationToken,
                failureInjector,
                ref mutationCount,
                RequireId(maps.SenseIds, control.SenseId),
                Schema13TimestampCodec.FormatUtc(control.DecidedAtUtc));
        }

        foreach (var history in payload.FsrsReviewHistoryEntries)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO FsrsReviewHistoryEntries
                    (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES (?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                history.StableId,
                RequireId(maps.CardIds, history.CardId),
                history.SequenceNumber,
                (int)BackupEnumMappings.ToPersistence(history.Rating),
                Schema13TimestampCodec.FormatUtc(history.ReviewedAtUtc));
            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsReviewHistoryInsertion);
        }

        foreach (var state in payload.FsrsCardStates)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO FsrsCardStates
                    (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                RequireId(maps.CardIds, state.CardId),
                (int)state.State,
                state.Stability,
                state.Difficulty,
                FormatOptionalUtc(state.LastReviewedAtUtc),
                state.StepIndex,
                FormatOptionalUtc(state.DueAtUtc));
            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsCardStateInsertion);
        }

        failureInjector?.AtCheckpoint(Checkpoints.BeforeFinalIntegrityValidation);
        ValidateNativeV3PostWrite(connection, payload, maps);
    }

    public static void AdaptLegacyIntoEmptyDatabase(
        SQLiteConnection connection,
        ValidatedSchema13Capability capability,
        BackupPayloadV2 payload,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(payload);

        BackupModelContractV2.ValidatePayload(payload);
        BackupArchiveWriterV2.ValidatePayloadGraphV2(payload);
        ValidateEmptyTarget(connection);

        _ = Schema8BackupImportRepository.ImportIntoEmptySchema8DatabaseWithMappings(
            connection,
            new ValidatedSchema8Capability(),
            payload,
            cancellationToken,
            failureInjector);
        failureInjector?.AtCheckpoint(Checkpoints.AfterBaseGraph);

        // This is the executable Schema-12 -> 13 transformation oracle. It derives only from the
        // just-restored legacy facts and fails closed for progressed cards without factual review history.
        var plan = Schema13LearningBootstrap.BuildPlan(connection);
        var mutationCount = 0;
        foreach (var control in plan.WordControls)
        {
            ExecuteMutation(
                connection,
                "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
                cancellationToken,
                failureInjector,
                ref mutationCount,
                control.WordId,
                control.DecidedAtUtc);
        }

        // Schema13LearningBootstrap deliberately derives no SenseLearningControls from legacy state.
        foreach (var history in plan.ReviewHistory)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO FsrsReviewHistoryEntries
                    (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES (?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                history.StableId,
                history.CardId,
                history.SequenceNumber,
                history.Rating,
                history.ReviewedAtUtc);
            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsReviewHistoryInsertion);
        }

        foreach (var state in plan.CardStates)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO FsrsCardStates
                    (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
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
            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsCardStateInsertion);
        }

        failureInjector?.AtCheckpoint(Checkpoints.BeforeFinalIntegrityValidation);
        if (!Schema13MigrationIntegrityValidator.Validate(connection, out var failureDetail))
        {
            throw new BackupFormatException(
                BackupErrorCodes.InvariantViolation,
                new InvalidOperationException(failureDetail));
        }
    }

    private static void ValidateEmptyTarget(SQLiteConnection connection)
    {
        if (!Schema13ShapeValidator.IsValidDatabase(connection, out var shapeFailure))
        {
            throw new BackupSchemaCapabilityException(ValidatedSchema13Capability.SchemaVersion, shapeMismatch: true);
        }

        if (HasDurableUserData(connection))
        {
            throw new InvalidOperationException(BackupErrorCodes.TargetNotEmpty);
        }
    }

    private static BackupPayloadV2 ToV2Payload(BackupPayloadV3 payload) => new(
        payload.SourceMaterials,
        payload.Vocabulary,
        payload.Senses,
        payload.PreparedLearning,
        payload.AnswerVariants,
        payload.SenseAnswerVariantAssignments,
        payload.AnswerVariantProgress,
        payload.Learning,
        payload.Workflows,
        payload.DerivedTermEvidence,
        payload.Extensions);

    private static void ValidateNativeV3PostWrite(
        SQLiteConnection connection,
        BackupPayloadV3 payload,
        Schema8BackupImportMaps maps)
    {
        if (!Schema13ShapeValidator.IsValidDatabase(connection, out var shapeFailure))
        {
            throw new BackupFormatException(
                BackupErrorCodes.InvariantViolation,
                new InvalidOperationException(shapeFailure));
        }

        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        RequireCount(connection, "WordLearningControls", payload.WordLearningControls.Count);
        RequireCount(connection, "SenseLearningControls", payload.SenseLearningControls.Count);
        RequireCount(connection, "FsrsReviewHistoryEntries", payload.FsrsReviewHistoryEntries.Count);
        RequireCount(connection, "FsrsCardStates", payload.FsrsCardStates.Count);

        foreach (var control in payload.WordLearningControls)
        {
            var actual = connection.ExecuteScalar<string>(
                "SELECT DecidedAtUtc FROM WordLearningControls WHERE WordId = ?",
                RequireId(maps.WordIds, control.VocabularyId));
            RequireEqual(Schema13TimestampCodec.FormatUtc(control.DecidedAtUtc), actual);
        }

        foreach (var control in payload.SenseLearningControls)
        {
            var actual = connection.ExecuteScalar<string>(
                "SELECT DecidedAtUtc FROM SenseLearningControls WHERE SenseId = ?",
                RequireId(maps.SenseIds, control.SenseId));
            RequireEqual(Schema13TimestampCodec.FormatUtc(control.DecidedAtUtc), actual);
        }

        foreach (var expected in payload.FsrsReviewHistoryEntries)
        {
            var actual = connection.Query<NativeHistoryCheckRow>(
                "SELECT StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries WHERE StableId = ?",
                expected.StableId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            if (actual.CardId != RequireId(maps.CardIds, expected.CardId)
                || actual.SequenceNumber != expected.SequenceNumber
                || actual.Rating != (int)BackupEnumMappings.ToPersistence(expected.Rating))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            RequireEqual(Schema13TimestampCodec.FormatUtc(expected.ReviewedAtUtc), actual.ReviewedAtUtc);
        }

        foreach (var expected in payload.FsrsCardStates)
        {
            var targetCardId = RequireId(maps.CardIds, expected.CardId);
            var actual = connection.Query<NativeStateCheckRow>(
                "SELECT CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc FROM FsrsCardStates WHERE CardId = ?",
                targetCardId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            if (actual.CardId != targetCardId
                || actual.State != (int)expected.State
                || !ExactDoubleEquals(actual.Stability, expected.Stability)
                || !ExactDoubleEquals(actual.Difficulty, expected.Difficulty)
                || actual.StepIndex != expected.StepIndex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            RequireEqual(FormatOptionalUtc(expected.LastReviewedAtUtc), actual.LastReviewedAtUtc);
            RequireEqual(FormatOptionalUtc(expected.DueAtUtc), actual.DueAtUtc);
        }
    }

    private static void RequireCount(SQLiteConnection connection, string table, int expected)
    {
        var actual = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");
        if (actual != expected)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void RequireEqual(string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
    }

    private static bool ExactDoubleEquals(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return BitConverter.DoubleToInt64Bits(left.Value) == BitConverter.DoubleToInt64Bits(right.Value);
    }

    private static string? FormatOptionalUtc(DateTime? value) =>
        value.HasValue ? Schema13TimestampCodec.FormatUtc(value.Value) : null;

    private static int RequireId(IReadOnlyDictionary<string, int> ids, string archiveId) =>
        ids.TryGetValue(archiveId, out var id)
            ? id
            : throw new BackupFormatException(BackupErrorCodes.MissingReference);

    private static void ExecuteMutation(
        SQLiteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector,
        ref int mutationCount,
        params object?[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Execute(sql, arguments);
        mutationCount++;
        failureInjector?.AfterMutation(mutationCount);
    }

    private sealed class NativeHistoryCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int CardId { get; set; }
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }

    private sealed class NativeStateCheckRow
    {
        public int CardId { get; set; }
        public int State { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
    }
}
