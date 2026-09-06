using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema14;
using KnownFirst.Data.Targets;
using KnownFirst.Models.Backup;
using SQLite;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Transaction-local executor for the Schema-14 target-aware collections. Every mutation is an
/// explicit action from the freshly recomputed plan; semantic identities are resolved to local ids only
/// after the inherited graph writer has finished.
/// </summary>
internal static class Schema14MergeWriterExecutor
{
    internal static class Checkpoints
    {
        public const string AfterBaseGraph = "Schema14MergeWriter.AfterBaseGraph";
        public const string DuringControls = "Schema14MergeWriter.DuringControls";
        public const string DuringTargets = "Schema14MergeWriter.DuringTargets";
        public const string DuringVariants = "Schema14MergeWriter.DuringVariants";
        public const string DuringFsrsHistory = "Schema14MergeWriter.DuringFsrsHistory";
        public const string DuringFsrsState = "Schema14MergeWriter.DuringFsrsState";
        public const string DuringTargetReviews = "Schema14MergeWriter.DuringTargetReviews";
        public const string BeforeFinalValidation = "Schema14MergeWriter.BeforeFinalValidation";
    }

    public static void Execute(
        SQLiteConnection connection,
        MergeWriterTargetIndex targetIndex,
        MergeWriterExecutionMaps sourceMappings,
        BackupPayloadV4 source,
        MergePreflightPlan plan,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector)
    {
        var schemaPlan = plan.Schema14Plan
            ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        var ids = BuildSemanticIds(connection, targetIndex, sourceMappings, source);
        var mutationCount = 0;

        failureInjector?.AtCheckpoint(Checkpoints.AfterBaseGraph);

        // 1. Controls
        foreach (var action in schemaPlan.Actions)
        {
            switch (action.Classification)
            {
                case Schema14MergeActionClassification.AddWordLearningControl:
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

                case Schema14MergeActionClassification.ReconcileWordLearningControlTimestamp:
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

                case Schema14MergeActionClassification.AddSenseLearningControl:
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

                case Schema14MergeActionClassification.ReconcileSenseLearningControlTimestamp:
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

        // Build existing target mappings
        var targetIdByStableId = new Dictionary<string, int>(StringComparer.Ordinal);
        var targetIdBySemanticIdentity = new Dictionary<string, int>(StringComparer.Ordinal);

        var existingTargets = connection.Query<TargetLookupRow>(
            "SELECT Id, StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage FROM LearningTargets");
        foreach (var row in existingTargets)
        {
            targetIdByStableId[row.StableId] = row.Id;
            if (ids.SenseIdentityById.TryGetValue(row.SenseId, out var senseIdentity))
            {
                var targetIdentity = $"{senseIdentity}:{row.TargetKind}:{row.SourceLanguage.ToLowerInvariant()}->{row.TargetLanguage.ToLowerInvariant()}";
                targetIdBySemanticIdentity[targetIdentity] = row.Id;
            }
        }

        // 2. AddLearningTargets
        foreach (var action in schemaPlan.Actions.Where(a => a.Classification == Schema14MergeActionClassification.AddLearningTarget))
        {
            var fact = action.TargetFact ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            var senseId = RequireId(ids.SenseIds, fact.SenseSemanticIdentity);

            Mutate(
                connection,
                """
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                fact.StableId,
                senseId,
                (int)fact.TargetKind,
                fact.SourceLanguage,
                fact.TargetLanguage,
                fact.TypingOptOut ? 1 : 0,
                Schema13TimestampCodec.FormatUtc(fact.CreatedAtUtc),
                Schema13TimestampCodec.FormatUtc(fact.UpdatedAtUtc));

            var targetId = (int)SQLite3.LastInsertRowid(connection.Handle);
            targetIdByStableId[fact.StableId] = targetId;
            targetIdBySemanticIdentity[action.SemanticIdentity] = targetId;
            failureInjector?.AtCheckpoint(Checkpoints.DuringTargets);
        }

        // Build existing variant mappings
        var variantIdByStableId = new Dictionary<string, int>(StringComparer.Ordinal);
        var existingVariants = connection.Query<VariantLookupRow>("SELECT Id, StableId FROM TargetAnswerVariants");
        foreach (var row in existingVariants)
        {
            variantIdByStableId[row.StableId] = row.Id;
        }

        // 3. AddTargetAnswerVariants
        foreach (var action in schemaPlan.Actions.Where(a => a.Classification == Schema14MergeActionClassification.AddTargetAnswerVariant))
        {
            var fact = action.VariantFact ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            var targetId = RequireId(targetIdBySemanticIdentity, fact.TargetSemanticIdentity);
            int? sourceMeaningId = null;
            if (fact.SourceMeaningSemanticIdentity is not null)
            {
                sourceMeaningId = RequireId(ids.MeaningIds, fact.SourceMeaningSemanticIdentity);
            }

            Mutate(
                connection,
                """
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                fact.StableId,
                targetId,
                fact.AnswerLanguage,
                fact.DisplayText,
                fact.NormalizedText,
                (int)fact.Requirement,
                fact.IsPreferred ? 1 : 0,
                FormatOptional(fact.RequiredSinceUtc),
                sourceMeaningId,
                Schema13TimestampCodec.FormatUtc(fact.CreatedAtUtc),
                Schema13TimestampCodec.FormatUtc(fact.UpdatedAtUtc));

            var variantId = (int)SQLite3.LastInsertRowid(connection.Handle);
            variantIdByStableId[fact.StableId] = variantId;
            failureInjector?.AtCheckpoint(Checkpoints.DuringVariants);
        }

        // 4. AppendTargetFsrsReviewHistory
        foreach (var action in schemaPlan.Actions.Where(a => a.Classification == Schema14MergeActionClassification.AppendTargetFsrsReviewHistory))
        {
            var fact = action.FsrsReviewFact ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            var targetId = RequireId(targetIdBySemanticIdentity, action.SemanticIdentity);

            Mutate(
                connection,
                """
                INSERT INTO TargetFsrsReviewHistoryEntries
                    (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES (?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                fact.StableId,
                targetId,
                fact.SequenceNumber,
                (int)BackupEnumMappings.ToPersistence(fact.Rating),
                Schema13TimestampCodec.FormatUtc(fact.ReviewedAtUtc));

            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsHistory);
        }

        // 5. InsertTargetFsrsState / UpdateTargetFsrsState
        foreach (var action in schemaPlan.Actions.Where(a => a.Classification is
                     Schema14MergeActionClassification.InsertTargetFsrsState or
                     Schema14MergeActionClassification.UpdateTargetFsrsState))
        {
            var state = action.SourceState ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            var targetId = RequireId(targetIdBySemanticIdentity, action.SemanticIdentity);

            if (action.Classification == Schema14MergeActionClassification.InsertTargetFsrsState)
            {
                Mutate(
                    connection,
                    """
                    INSERT INTO TargetFsrsStates
                        (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                    """,
                    cancellationToken,
                    failureInjector,
                    ref mutationCount,
                    targetId,
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
                    UPDATE TargetFsrsStates
                    SET State = ?, Stability = ?, Difficulty = ?, LastReviewedAtUtc = ?, StepIndex = ?, DueAtUtc = ?
                    WHERE TargetId = ?
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
                    targetId);
            }

            failureInjector?.AtCheckpoint(Checkpoints.DuringFsrsState);
        }

        // 6. AddTargetReview
        foreach (var action in schemaPlan.Actions.Where(a => a.Classification == Schema14MergeActionClassification.AddTargetReview))
        {
            var fact = action.ReviewFact ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            var targetId = RequireId(targetIdBySemanticIdentity, fact.TargetSemanticIdentity);
            var sessionId = RequireId(ids.LearningSessionIds, fact.SessionArchiveId);

            int? targetVariantId = null;
            if (fact.TargetVariantStableId is not null)
            {
                targetVariantId = RequireId(variantIdByStableId, fact.TargetVariantStableId);
            }

            int? matchedVariantId = null;
            if (fact.MatchedVariantStableId is not null)
            {
                matchedVariantId = RequireId(variantIdByStableId, fact.MatchedVariantStableId);
            }

            Mutate(
                connection,
                """
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat, TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                fact.StableId,
                targetId,
                sessionId,
                (int)BackupEnumMappings.ToPersistence(fact.Rating),
                fact.WasTypedAnswer ? 1 : 0,
                fact.WasCorrect ? 1 : 0,
                fact.IsSessionRepeat ? 1 : 0,
                targetVariantId,
                matchedVariantId,
                Schema13TimestampCodec.FormatUtc(fact.ReviewedAtUtc),
                Schema13TimestampCodec.FormatUtc(fact.DueAtUtc));

            failureInjector?.AtCheckpoint(Checkpoints.DuringTargetReviews);
        }

        failureInjector?.AtCheckpoint(Checkpoints.BeforeFinalValidation);
        ValidateFinalState(connection, source, plan, schemaPlan);
    }

    private static Schema14SemanticIdMaps BuildSemanticIds(
        SQLiteConnection connection,
        MergeWriterTargetIndex targetIndex,
        MergeWriterExecutionMaps sourceMappings,
        BackupPayloadV4 source)
    {
        var words = targetIndex.WordIdByIdentity.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.Ordinal);
        var senses = targetIndex.SenseIdByIdentity.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.Ordinal);
        var meanings = new Dictionary<string, int>(StringComparer.Ordinal);
        var sessions = new Dictionary<string, int>(StringComparer.Ordinal);
        var senseIdentityById = new Dictionary<int, string>();

        foreach (var pair in targetIndex.SenseIdByIdentity)
        {
            senseIdentityById[pair.Value] = pair.Key.Value;
        }

        // Target meanings by stable ID
        var targetMeaningRows = connection.Query<MeaningLookupRow>("SELECT Id, StableId FROM Meanings");
        foreach (var row in targetMeaningRows)
        {
            meanings[row.StableId] = row.Id;
        }

        // Target learning sessions by stable ID
        var targetSessionRows = connection.Query<SessionLookupRow>("SELECT Id, StableId FROM LearningSessions");
        foreach (var row in targetSessionRows)
        {
            sessions[row.StableId] = row.Id;
        }

        // Source word identities
        var sourceWordIdentities = new Dictionary<string, VocabularyIdentity>(StringComparer.Ordinal);
        foreach (var item in source.Vocabulary)
        {
            var identity = VocabularyMergeIdentityPolicy.Compute(item);
            sourceWordIdentities[item.Id] = identity;
            AddConsistent(words, identity.Value, RequireId(sourceMappings.WordIds, item.Id));
        }

        // Source sense identities
        foreach (var item in source.Senses)
        {
            var identity = SemanticMeaningIdentityPolicy.Compute(
                item,
                RequireIdentity(sourceWordIdentities, item.VocabularyId));
            var localSenseId = RequireId(sourceMappings.SenseIds, item.Id);
            AddConsistent(senses, identity.Value, localSenseId);
            AddConsistent(senses, item.StableId, localSenseId);
            senseIdentityById[localSenseId] = item.StableId;
        }

        // Source meaning identities
        if (sourceMappings.MeaningIds is not null)
        {
            foreach (var item in source.PreparedLearning)
            {
                if (sourceMappings.MeaningIds.TryGetValue(item.Id, out var localMeaningId))
                {
                    meanings[item.StableId] = localMeaningId;
                }
            }
        }

        // Source session identities
        if (sourceMappings.LearningSessionIds is not null)
        {
            foreach (var item in source.Workflows.LearningSessions)
            {
                if (sourceMappings.LearningSessionIds.TryGetValue(item.Id, out var localSessionId))
                {
                    sessions[item.Id] = localSessionId;
                    if (item.StableId is not null)
                    {
                        sessions[item.StableId] = localSessionId;
                    }
                }
            }
        }

        return new Schema14SemanticIdMaps(words, senses, meanings, sessions, senseIdentityById);
    }

    private static void ValidateFinalState(
        SQLiteConnection connection,
        BackupPayloadV4 source,
        MergePreflightPlan originalPlan,
        Schema14MergePreflightPlan schemaPlan)
    {
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        string? shapeFailure = null;
        string? targetShapeFailure = null;
        if (!Schema13ShapeValidator.IsValidDatabase(connection, out shapeFailure)
            || !TargetPersistenceShapeValidator.Validate(connection, out targetShapeFailure))
        {
            throw new BackupFormatException(
                BackupErrorCodes.InvariantViolation,
                new InvalidOperationException(shapeFailure ?? targetShapeFailure));
        }

        var finalSnapshot = Schema14BackupSnapshotRepository.CapturePortableSnapshot(connection);
        var finalPayload = BackupModelMapperV4.MapToExternal(finalSnapshot);
        BackupModelContractV4.ValidatePayload(finalPayload);
        BackupArchiveWriterV4.ValidatePayloadGraphV4(finalPayload);

        var convergence = Schema14MergePreflightPlanner.CreateCombinedPlan(finalPayload, source, originalPlan.Manifest!);
        if (!convergence.IsExecutable
            || convergence.RequiresSchedulerReplay
            || convergence.PerEntity.Values.Any(counts => counts.TotalInsertableCount > 0)
            || convergence.Schema14Plan?.RequiresMutation == true)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }
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

    private sealed record Schema14SemanticIdMaps(
        IReadOnlyDictionary<string, int> WordIds,
        IReadOnlyDictionary<string, int> SenseIds,
        IReadOnlyDictionary<string, int> MeaningIds,
        IReadOnlyDictionary<string, int> LearningSessionIds,
        IReadOnlyDictionary<int, string> SenseIdentityById);

    private sealed class TargetLookupRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
        public int SenseId { get; set; }
        public int TargetKind { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
    }

    private sealed class VariantLookupRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
    }

    private sealed class MeaningLookupRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
    }

    private sealed class SessionLookupRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
    }
}
