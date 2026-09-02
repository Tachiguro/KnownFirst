using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models.Backup;
using SQLite;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Transaction-local executor for the four Schema-13 extension collections. Every mutation is an
/// explicit action from the freshly recomputed plan; semantic identities are resolved to local ids only
/// after the inherited graph writer has finished.
/// </summary>
internal static class Schema13MergeWriterExecutor
{
    internal static class Checkpoints
    {
        public const string AfterBaseGraph = "Schema13MergeWriter.AfterBaseGraph";
        public const string DuringControls = "Schema13MergeWriter.DuringControls";
        public const string DuringFsrsHistory = "Schema13MergeWriter.DuringFsrsHistory";
        public const string DuringFsrsState = "Schema13MergeWriter.DuringFsrsState";
        public const string BeforeFinalValidation = "Schema13MergeWriter.BeforeFinalValidation";
    }

    public static void Execute(
        SQLiteConnection connection,
        MergeWriterTargetIndex targetIndex,
        MergeWriterExecutionMaps sourceMappings,
        BackupPayloadV3 source,
        MergePreflightPlan plan,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector)
    {
        var schemaPlan = plan.Schema13Plan
            ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        var ids = BuildSemanticIds(targetIndex, sourceMappings, source);
        var mutationCount = 0;

        failureInjector?.AtCheckpoint(Checkpoints.AfterBaseGraph);

        foreach (var action in schemaPlan.Actions)
        {
            switch (action.Classification)
            {
                case Schema13MergeActionClassification.AddWordLearningControl:
                    Mutate(
                        connection,
                        "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
                        cancellationToken,
                        failureInjector,
                        ref mutationCount,
                        RequireId(ids.WordIds, action.SemanticIdentity),
                        FormatRequired(action.SourceControlDecidedAtUtc));
                    failureInjector?.AtCheckpoint(Checkpoints.DuringControls);
                    break;

                case Schema13MergeActionClassification.ReconcileWordLearningControlTimestamp:
                    Mutate(
                        connection,
                        "UPDATE WordLearningControls SET DecidedAtUtc = ? WHERE WordId = ?",
                        cancellationToken,
                        failureInjector,
                        ref mutationCount,
                        FormatRequired(action.SourceControlDecidedAtUtc),
                        RequireId(ids.WordIds, action.SemanticIdentity));
                    failureInjector?.AtCheckpoint(Checkpoints.DuringControls);
                    break;

                case Schema13MergeActionClassification.AddSenseLearningControl:
                    Mutate(
                        connection,
                        "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)",
                        cancellationToken,
                        failureInjector,
                        ref mutationCount,
                        RequireId(ids.SenseIds, action.SemanticIdentity),
                        FormatRequired(action.SourceControlDecidedAtUtc));
                    failureInjector?.AtCheckpoint(Checkpoints.DuringControls);
                    break;

                case Schema13MergeActionClassification.ReconcileSenseLearningControlTimestamp:
                    Mutate(
                        connection,
                        "UPDATE SenseLearningControls SET DecidedAtUtc = ? WHERE SenseId = ?",
                        cancellationToken,
                        failureInjector,
                        ref mutationCount,
                        FormatRequired(action.SourceControlDecidedAtUtc),
                        RequireId(ids.SenseIds, action.SemanticIdentity));
                    failureInjector?.AtCheckpoint(Checkpoints.DuringControls);
                    break;
            }
        }

        foreach (var action in schemaPlan.Actions.Where(item =>
                     item.Classification == Schema13MergeActionClassification.AppendFsrsReviewHistory))
        {
            var fact = action.ReviewFact
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            Mutate(
                connection,
                """
                INSERT INTO FsrsReviewHistoryEntries
                    (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES (?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                fact.StableId,
                RequireId(ids.CardIds, action.SemanticIdentity),
                fact.SequenceNumber,
                (int)BackupEnumMappings.ToPersistence(fact.Rating),
                Schema13TimestampCodec.FormatUtc(fact.ReviewedAtUtc));
            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsHistory);
        }

        foreach (var action in schemaPlan.Actions.Where(item => item.Classification is
                     Schema13MergeActionClassification.InsertFsrsCardState or
                     Schema13MergeActionClassification.UpdateFsrsCardState))
        {
            var state = action.SourceCardState
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            var cardId = RequireId(ids.CardIds, action.SemanticIdentity);
            if (action.Classification == Schema13MergeActionClassification.InsertFsrsCardState)
            {
                Mutate(
                    connection,
                    """
                    INSERT INTO FsrsCardStates
                        (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                    """,
                    cancellationToken,
                    failureInjector,
                    ref mutationCount,
                    cardId,
                    (int)BackupEnumMappings.ToCore(state.State),
                    state.Stability,
                    state.Difficulty,
                    FormatOptional(state.LastReviewedAtUtc),
                    state.StepIndex,
                    FormatOptional(state.DueAtUtc));
            }
            else
            {
                Mutate(
                    connection,
                    """
                    UPDATE FsrsCardStates
                    SET State = ?, Stability = ?, Difficulty = ?, LastReviewedAtUtc = ?, StepIndex = ?, DueAtUtc = ?
                    WHERE CardId = ?
                    """,
                    cancellationToken,
                    failureInjector,
                    ref mutationCount,
                    (int)BackupEnumMappings.ToCore(state.State),
                    state.Stability,
                    state.Difficulty,
                    FormatOptional(state.LastReviewedAtUtc),
                    state.StepIndex,
                    FormatOptional(state.DueAtUtc),
                    cardId);
            }
            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsState);
        }

        failureInjector?.AtCheckpoint(Checkpoints.BeforeFinalValidation);
        ValidateFinalState(connection, source, plan, schemaPlan, ids);
    }

    private static SemanticIdMaps BuildSemanticIds(
        MergeWriterTargetIndex targetIndex,
        MergeWriterExecutionMaps sourceMappings,
        BackupPayloadV3 source)
    {
        var words = targetIndex.WordIdByIdentity.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.Ordinal);
        var senses = targetIndex.SenseIdByIdentity.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.Ordinal);
        var cards = targetIndex.CardIdByIdentity.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.Ordinal);

        var sourceWordIdentities = new Dictionary<string, VocabularyIdentity>(StringComparer.Ordinal);
        foreach (var item in source.Vocabulary)
        {
            var identity = VocabularyMergeIdentityPolicy.Compute(item);
            sourceWordIdentities[item.Id] = identity;
            AddConsistent(words, identity.Value, RequireId(sourceMappings.WordIds, item.Id));
        }

        var sourceSenseIdentities = new Dictionary<string, SemanticMeaningIdentity>(StringComparer.Ordinal);
        foreach (var item in source.Senses)
        {
            var identity = SemanticMeaningIdentityPolicy.Compute(
                item,
                RequireIdentity(sourceWordIdentities, item.VocabularyId));
            sourceSenseIdentities[item.Id] = identity;
            AddConsistent(senses, identity.Value, RequireId(sourceMappings.SenseIds, item.Id));
        }

        foreach (var item in source.Learning.Cards)
        {
            var identity = FutureCardIdentityPolicy.Compute(
                RequireIdentity(sourceSenseIdentities, item.SenseId),
                item.Direction);
            AddConsistent(cards, identity.Value, RequireId(sourceMappings.CardIds, item.Id));
        }

        return new SemanticIdMaps(words, senses, cards);
    }

    private static void ValidateFinalState(
        SQLiteConnection connection,
        BackupPayloadV3 source,
        MergePreflightPlan originalPlan,
        Schema13MergePreflightPlan schemaPlan,
        SemanticIdMaps ids)
    {
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        if (!Schema13RuntimeIntegrityValidator.Validate(connection, out var runtimeFailureDetail))
        {
            throw new BackupFormatException(
                BackupErrorCodes.InvariantViolation,
                new InvalidOperationException(runtimeFailureDetail));
        }

        // Capture validates the complete inherited archive graph and the checks below verify every
        // action-carried persisted fact byte-for-byte.
        var finalSnapshot = Schema13BackupSnapshotRepository.CapturePortableSnapshot(connection);

        foreach (var action in schemaPlan.Actions)
        {
            switch (action.Classification)
            {
                case Schema13MergeActionClassification.AddWordLearningControl:
                case Schema13MergeActionClassification.ReconcileWordLearningControlTimestamp:
                    RequireText(
                        FormatRequired(action.SourceControlDecidedAtUtc),
                        connection.ExecuteScalar<string>(
                            "SELECT DecidedAtUtc FROM WordLearningControls WHERE WordId = ?",
                            RequireId(ids.WordIds, action.SemanticIdentity)));
                    break;
                case Schema13MergeActionClassification.AddSenseLearningControl:
                case Schema13MergeActionClassification.ReconcileSenseLearningControlTimestamp:
                    RequireText(
                        FormatRequired(action.SourceControlDecidedAtUtc),
                        connection.ExecuteScalar<string>(
                            "SELECT DecidedAtUtc FROM SenseLearningControls WHERE SenseId = ?",
                            RequireId(ids.SenseIds, action.SemanticIdentity)));
                    break;
                case Schema13MergeActionClassification.AppendFsrsReviewHistory:
                    ValidateHistoryAction(connection, action, ids);
                    break;
                case Schema13MergeActionClassification.InsertFsrsCardState:
                case Schema13MergeActionClassification.UpdateFsrsCardState:
                    ValidateStateAction(connection, action, ids);
                    break;
            }
        }

        var finalPayload = BackupModelMapperV3.MapToExternal(finalSnapshot);
        BackupModelContractV3.ValidatePayload(finalPayload);
        BackupArchiveWriterV3.ValidatePayloadGraphV3(finalPayload);
        var convergence = Schema13MergePreflightPlanner.CreateCombinedPlan(finalPayload, source, originalPlan.Manifest!);
        if (!convergence.IsExecutable
            || convergence.RequiresSchedulerReplay
            || convergence.PerEntity.Values.Any(counts => counts.TotalInsertableCount > 0)
            || convergence.Schema13Plan?.RequiresMutation == true)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateHistoryAction(SQLiteConnection connection, Schema13MergeAction action, SemanticIdMaps ids)
    {
        var fact = action.ReviewFact ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        var row = connection.Query<HistoryCheckRow>(
            "SELECT StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries WHERE StableId = ?",
            fact.StableId).SingleOrDefault() ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        if (row.CardId != RequireId(ids.CardIds, action.SemanticIdentity)
            || row.SequenceNumber != fact.SequenceNumber
            || row.Rating != (int)BackupEnumMappings.ToPersistence(fact.Rating))
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
        RequireText(Schema13TimestampCodec.FormatUtc(fact.ReviewedAtUtc), row.ReviewedAtUtc);
    }

    private static void ValidateStateAction(SQLiteConnection connection, Schema13MergeAction action, SemanticIdMaps ids)
    {
        var expected = action.SourceCardState ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        var cardId = RequireId(ids.CardIds, action.SemanticIdentity);
        var row = connection.Query<StateCheckRow>(
            "SELECT CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc FROM FsrsCardStates WHERE CardId = ?",
            cardId).SingleOrDefault() ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        if (row.CardId != cardId
            || row.State != (int)BackupEnumMappings.ToCore(expected.State)
            || !ExactDouble(row.Stability, expected.Stability)
            || !ExactDouble(row.Difficulty, expected.Difficulty)
            || row.StepIndex != expected.StepIndex)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
        RequireText(FormatOptional(expected.LastReviewedAtUtc), row.LastReviewedAtUtc);
        RequireText(FormatOptional(expected.DueAtUtc), row.DueAtUtc);
    }

    private static void Mutate(
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

    private static void AddConsistent(Dictionary<string, int> map, string identity, int id)
    {
        if (map.TryGetValue(identity, out var existing) && existing != id)
        {
            throw new BackupFormatException(BackupErrorCodes.DuplicateId);
        }
        map[identity] = id;
    }

    private static TIdentity RequireIdentity<TIdentity>(IReadOnlyDictionary<string, TIdentity> map, string localId) where TIdentity : notnull =>
        map.TryGetValue(localId, out var identity)
            ? identity
            : throw new BackupFormatException(BackupErrorCodes.MissingReference);

    private static int RequireId(IReadOnlyDictionary<string, int> map, string identity) =>
        map.TryGetValue(identity, out var id)
            ? id
            : throw new BackupFormatException(BackupErrorCodes.MissingReference);

    private static string FormatRequired(DateTime? value) =>
        value.HasValue
            ? Schema13TimestampCodec.FormatUtc(value.Value)
            : throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

    private static string? FormatOptional(DateTime? value) =>
        value.HasValue ? Schema13TimestampCodec.FormatUtc(value.Value) : null;

    private static bool ExactDouble(double? left, double? right) =>
        !left.HasValue || !right.HasValue
            ? left.HasValue == right.HasValue
            : BitConverter.DoubleToInt64Bits(left.Value) == BitConverter.DoubleToInt64Bits(right.Value);

    private static void RequireText(string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
    }

    private sealed record SemanticIdMaps(
        IReadOnlyDictionary<string, int> WordIds,
        IReadOnlyDictionary<string, int> SenseIds,
        IReadOnlyDictionary<string, int> CardIds);

    private sealed class HistoryCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int CardId { get; set; }
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }

    private sealed class StateCheckRow
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
