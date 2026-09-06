using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema10;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Targets;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data.Schema14;

/// <summary>
/// Transaction-local empty-target restore for an already validated Schema-14 database (KF-LEARN-011 Slice 5).
/// The caller owns the enclosing transaction; this repository never changes PRAGMA user_version.
/// </summary>
public static class Schema14BackupImportRepository
{
    public static class Checkpoints
    {
        public const string AfterBaseGraph = "Schema14AfterBaseGraph";
        public const string DuringTargetInsertion = "Schema14DuringTargetInsertion";
        public const string DuringTargetAnswerVariantInsertion = "Schema14DuringTargetAnswerVariantInsertion";
        public const string DuringTargetFsrsStateInsertion = "Schema14DuringTargetFsrsStateInsertion";
        public const string DuringTargetFsrsReviewHistoryInsertion = "Schema14DuringTargetFsrsReviewHistoryInsertion";
        public const string DuringLearningSessionInsertion = "Schema14DuringLearningSessionInsertion";
        public const string DuringTargetReviewInsertion = "Schema14DuringTargetReviewInsertion";
        public const string BeforeFinalIntegrityValidation = "Schema14BeforeFinalIntegrityValidation";
    }

    public static bool HasDurableUserData(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return Schema13BackupImportRepository.HasDurableUserData(connection)
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetAnswerVariants") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetFsrsStates") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries") != 0
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetReviews") != 0;
    }

    public static void ImportNativeV4IntoEmptyDatabase(
        SQLiteConnection connection,
        ValidatedSchema14Capability capability,
        BackupPayloadV4 payload,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(payload);

        BackupModelContractV4.ValidatePayload(payload);
        BackupArchiveWriterV4.ValidatePayloadGraphV4(payload);
        ValidateEmptyTarget(connection);

        var baseWorkflows = new BackupWorkflowDataV2(
            payload.Workflows.VocabularyReviews,
            payload.Workflows.PreparationBatches,
            LearningSessions: []);

        var basePayload = new BackupPayloadV2(
            payload.SourceMaterials,
            payload.Vocabulary,
            payload.Senses,
            payload.PreparedLearning,
            [],
            [],
            [],
            new BackupLearningDataV2([], []),
            baseWorkflows,
            payload.DerivedTermEvidence,
            payload.Extensions);

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

        var targetIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var target in payload.LearningTargets)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                target.StableId,
                RequireId(maps.SenseIds, target.SenseId),
                (int)target.TargetKind,
                target.SourceLanguage,
                target.TargetLanguage,
                target.TypingOptOut ? 1 : 0,
                Schema13TimestampCodec.FormatUtc(target.CreatedAtUtc),
                Schema13TimestampCodec.FormatUtc(target.UpdatedAtUtc));

            var targetId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
            targetIds.Add(target.Id, targetId);
            failureInjector?.AtCheckpoint(Checkpoints.DuringTargetInsertion);
        }

        var meaningIds = maps.MeaningIds ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        var variantIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var variant in payload.TargetAnswerVariants)
        {
            int? sourceMeaningId = variant.SourceMeaningId is not null
                ? RequireId(meaningIds, variant.SourceMeaningId)
                : null;

            ExecuteMutation(
                connection,
                """
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                variant.StableId,
                RequireId(targetIds, variant.TargetId),
                variant.AnswerLanguage,
                variant.DisplayText,
                variant.NormalizedText,
                (int)variant.Requirement,
                variant.IsPreferred ? 1 : 0,
                variant.RequiredSinceUtc.HasValue ? Schema13TimestampCodec.FormatUtc(variant.RequiredSinceUtc.Value) : null,
                sourceMeaningId,
                Schema13TimestampCodec.FormatUtc(variant.CreatedAtUtc),
                Schema13TimestampCodec.FormatUtc(variant.UpdatedAtUtc));

            var variantId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
            variantIds.Add(variant.Id, variantId);
            failureInjector?.AtCheckpoint(Checkpoints.DuringTargetAnswerVariantInsertion);
        }

        foreach (var state in payload.TargetFsrsStates)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO TargetFsrsStates
                    (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                RequireId(targetIds, state.TargetId),
                (int)state.State,
                state.Stability,
                state.Difficulty,
                FormatOptionalUtc(state.LastReviewedAtUtc),
                state.StepIndex,
                FormatOptionalUtc(state.DueAtUtc));
            failureInjector?.AtCheckpoint(Checkpoints.DuringTargetFsrsStateInsertion);
        }

        foreach (var history in payload.TargetFsrsReviewHistoryEntries)
        {
            ExecuteMutation(
                connection,
                """
                INSERT INTO TargetFsrsReviewHistoryEntries
                    (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES (?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                history.StableId,
                RequireId(targetIds, history.TargetId),
                history.SequenceNumber,
                (int)BackupEnumMappings.ToPersistence(history.Rating),
                Schema13TimestampCodec.FormatUtc(history.ReviewedAtUtc));
            failureInjector?.AtCheckpoint(Checkpoints.DuringTargetFsrsReviewHistoryInsertion);
        }

        var learningSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var session in payload.Workflows.LearningSessions)
        {
            var sessionStableId = LearningWorkflowStableId.IsValid(session.StableId)
                ? session.StableId!
                : LearningWorkflowStableId.NewGuidForm();

            var sessionInsert = Schema10LearningIdentityWriter.BuildSessionInsert(
                connection,
                (int)BackupEnumMappings.ToPersistence(session.Status),
                session.TotalCards,
                session.CompletedCards,
                session.AgainCount,
                session.HardCount,
                session.GoodCount,
                session.EasyCount,
                session.StartedAtUtc,
                session.UpdatedAtUtc,
                session.CompletedAtUtc,
                sessionStableId);

            ExecuteMutation(
                connection,
                sessionInsert.Sql,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                sessionInsert.Arguments);

            var sessionId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
            learningSessionIds.Add(session.Id, sessionId);

            foreach (var item in session.QueueItems.OrderBy(item => item.QueueOrder))
            {
                var targetLocalId = RequireId(targetIds, item.CardId);
                int? variantLocalId = item.TargetAnswerVariantId is not null
                    ? RequireId(variantIds, item.TargetAnswerVariantId)
                    : null;
                var queueStableId = LearningWorkflowStableId.IsValid(item.StableId)
                    ? item.StableId!
                    : LearningWorkflowStableId.NewGuidForm();

                var queueInsert = Schema10LearningIdentityWriter.BuildQueueInsert(
                    connection,
                    sessionId,
                    targetLocalId,
                    item.QueueOrder,
                    item.IsDueCard,
                    item.IsAgainRepeat,
                    item.AnswerRevealed,
                    item.SpellingChecked,
                    item.SpellingCorrect,
                    item.IsCompleted,
                    item.Rating is null ? null : (int?)BackupEnumMappings.ToPersistence(item.Rating.Value),
                    item.CompletedAtUtc,
                    variantLocalId,
                    queueStableId);

                ExecuteMutation(
                    connection,
                    queueInsert.Sql,
                    cancellationToken,
                    failureInjector,
                    ref mutationCount,
                    queueInsert.Arguments);
            }

            failureInjector?.AtCheckpoint(Checkpoints.DuringLearningSessionInsertion);
        }

        foreach (var review in payload.TargetReviews)
        {
            int? targetVariantId = review.TargetAnswerVariantId is not null
                ? RequireId(variantIds, review.TargetAnswerVariantId)
                : null;
            int? matchedVariantId = review.MatchedAnswerVariantId is not null
                ? RequireId(variantIds, review.MatchedAnswerVariantId)
                : null;

            ExecuteMutation(
                connection,
                """
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat, TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken,
                failureInjector,
                ref mutationCount,
                review.StableId,
                RequireId(targetIds, review.TargetId),
                RequireId(learningSessionIds, review.SessionId),
                (int)BackupEnumMappings.ToPersistence(review.Rating),
                review.WasTypedAnswer ? 1 : 0,
                review.WasCorrect ? 1 : 0,
                review.IsSessionRepeat ? 1 : 0,
                targetVariantId,
                matchedVariantId,
                Schema13TimestampCodec.FormatUtc(review.ReviewedAtUtc),
                Schema13TimestampCodec.FormatUtc(review.DueAtUtc));
            failureInjector?.AtCheckpoint(Checkpoints.DuringTargetReviewInsertion);
        }

        failureInjector?.AtCheckpoint(Checkpoints.BeforeFinalIntegrityValidation);
        ValidateNativeV4PostWrite(connection, payload, maps, targetIds, variantIds, learningSessionIds);
    }

    private static void ValidateEmptyTarget(SQLiteConnection connection)
    {
        if (!Schema13ShapeValidator.IsValidDatabase(connection, out _)
            || !TargetPersistenceShapeValidator.Validate(connection, out _))
        {
            throw new BackupSchemaCapabilityException(14, shapeMismatch: true);
        }

        if (HasDurableUserData(connection))
        {
            throw new InvalidOperationException(BackupErrorCodes.TargetNotEmpty);
        }
    }

    private static void ValidateNativeV4PostWrite(
        SQLiteConnection connection,
        BackupPayloadV4 payload,
        Schema8BackupImportMaps maps,
        IReadOnlyDictionary<string, int> targetIds,
        IReadOnlyDictionary<string, int> variantIds,
        IReadOnlyDictionary<string, int> learningSessionIds)
    {
        string? shapeFailure = null;
        string? targetShapeFailure = null;
        if (!Schema13ShapeValidator.IsValidDatabase(connection, out shapeFailure)
            || !TargetPersistenceShapeValidator.Validate(connection, out targetShapeFailure))
        {
            throw new BackupFormatException(
                BackupErrorCodes.InvariantViolation,
                new InvalidOperationException(shapeFailure ?? targetShapeFailure));
        }

        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        RequireCount(connection, "WordLearningControls", payload.WordLearningControls.Count);
        RequireCount(connection, "SenseLearningControls", payload.SenseLearningControls.Count);
        RequireCount(connection, "LearningTargets", payload.LearningTargets.Count);
        RequireCount(connection, "TargetAnswerVariants", payload.TargetAnswerVariants.Count);
        RequireCount(connection, "TargetFsrsStates", payload.TargetFsrsStates.Count);
        RequireCount(connection, "TargetFsrsReviewHistoryEntries", payload.TargetFsrsReviewHistoryEntries.Count);
        RequireCount(connection, "TargetReviews", payload.TargetReviews.Count);
        RequireCount(connection, "LearningSessions", payload.Workflows.LearningSessions.Count);
        RequireCount(connection, "LearningSessionCards", payload.Workflows.LearningSessions.Sum(s => s.QueueItems.Count));

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

        foreach (var target in payload.LearningTargets)
        {
            var targetLocalId = RequireId(targetIds, target.Id);
            var actual = connection.Query<NativeTargetCheckRow>(
                "SELECT StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc FROM LearningTargets WHERE Id = ?",
                targetLocalId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            if (actual.StableId != target.StableId
                || actual.SenseId != RequireId(maps.SenseIds, target.SenseId)
                || actual.TargetKind != (int)target.TargetKind
                || actual.SourceLanguage != target.SourceLanguage
                || actual.TargetLanguage != target.TargetLanguage
                || (actual.TypingOptOut == 1) != target.TypingOptOut)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            RequireEqual(Schema13TimestampCodec.FormatUtc(target.CreatedAtUtc), actual.CreatedAtUtc);
            RequireEqual(Schema13TimestampCodec.FormatUtc(target.UpdatedAtUtc), actual.UpdatedAtUtc);
        }

        var meaningIds = maps.MeaningIds!;
        foreach (var variant in payload.TargetAnswerVariants)
        {
            var variantLocalId = RequireId(variantIds, variant.Id);
            var actual = connection.Query<NativeVariantCheckRow>(
                "SELECT StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc FROM TargetAnswerVariants WHERE Id = ?",
                variantLocalId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            int? expectedMeaningId = variant.SourceMeaningId is not null
                ? RequireId(meaningIds, variant.SourceMeaningId)
                : null;

            if (actual.StableId != variant.StableId
                || actual.TargetId != RequireId(targetIds, variant.TargetId)
                || actual.AnswerLanguage != variant.AnswerLanguage
                || actual.DisplayText != variant.DisplayText
                || actual.NormalizedText != variant.NormalizedText
                || actual.Requirement != (int)variant.Requirement
                || (actual.IsPreferred == 1) != variant.IsPreferred
                || actual.SourceMeaningId != expectedMeaningId)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            RequireEqual(FormatOptionalUtc(variant.RequiredSinceUtc), actual.RequiredSinceUtc);
            RequireEqual(Schema13TimestampCodec.FormatUtc(variant.CreatedAtUtc), actual.CreatedAtUtc);
            RequireEqual(Schema13TimestampCodec.FormatUtc(variant.UpdatedAtUtc), actual.UpdatedAtUtc);
        }

        foreach (var expected in payload.TargetFsrsStates)
        {
            var targetLocalId = RequireId(targetIds, expected.TargetId);
            var actual = connection.Query<NativeTargetStateCheckRow>(
                "SELECT TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc FROM TargetFsrsStates WHERE TargetId = ?",
                targetLocalId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            if (actual.TargetId != targetLocalId
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

        foreach (var expected in payload.TargetFsrsReviewHistoryEntries)
        {
            var actual = connection.Query<NativeTargetHistoryCheckRow>(
                "SELECT StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc FROM TargetFsrsReviewHistoryEntries WHERE StableId = ?",
                expected.StableId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            if (actual.TargetId != RequireId(targetIds, expected.TargetId)
                || actual.SequenceNumber != expected.SequenceNumber
                || actual.Rating != (int)BackupEnumMappings.ToPersistence(expected.Rating))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            RequireEqual(Schema13TimestampCodec.FormatUtc(expected.ReviewedAtUtc), actual.ReviewedAtUtc);
        }

        foreach (var session in payload.Workflows.LearningSessions)
        {
            var localSessionId = RequireId(learningSessionIds, session.Id);
            var actual = connection.Query<NativeLearningSessionCheckRow>(
                "SELECT Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StableId FROM LearningSessions WHERE Id = ?",
                localSessionId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            if (actual.Status != (int)BackupEnumMappings.ToPersistence(session.Status)
                || actual.TotalCards != session.TotalCards
                || actual.CompletedCards != session.CompletedCards
                || actual.AgainCount != session.AgainCount
                || actual.HardCount != session.HardCount
                || actual.GoodCount != session.GoodCount
                || actual.EasyCount != session.EasyCount)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            if (session.StableId is not null && actual.StableId != session.StableId)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            foreach (var item in session.QueueItems)
            {
                var targetLocalId = RequireId(targetIds, item.CardId);
                int? variantLocalId = item.TargetAnswerVariantId is not null
                    ? RequireId(variantIds, item.TargetAnswerVariantId)
                    : null;

                var actualQueue = connection.Query<NativeQueueItemCheckRow>(
                    "SELECT SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, SpellingCorrect, IsCompleted, Rating, TargetAnswerVariantId, StableId FROM LearningSessionCards WHERE SessionId = ? AND QueueOrder = ?",
                    localSessionId, item.QueueOrder).SingleOrDefault()
                    ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

                if (actualQueue.CardId != targetLocalId
                    || actualQueue.TargetAnswerVariantId != variantLocalId
                    || (actualQueue.IsDueCard == 1) != item.IsDueCard
                    || (actualQueue.IsAgainRepeat == 1) != item.IsAgainRepeat
                    || (actualQueue.AnswerRevealed == 1) != item.AnswerRevealed
                    || (actualQueue.SpellingChecked == 1) != item.SpellingChecked
                    || (actualQueue.SpellingCorrect == 1) != item.SpellingCorrect
                    || (actualQueue.IsCompleted == 1) != item.IsCompleted
                    || actualQueue.Rating != (item.Rating is null ? null : (int?)BackupEnumMappings.ToPersistence(item.Rating.Value)))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (item.StableId is not null && actualQueue.StableId != item.StableId)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        foreach (var review in payload.TargetReviews)
        {
            var actual = connection.Query<NativeTargetReviewCheckRow>(
                "SELECT StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat, TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc FROM TargetReviews WHERE StableId = ?",
                review.StableId).SingleOrDefault()
                ?? throw new BackupFormatException(BackupErrorCodes.InvariantViolation);

            int? targetVariantId = review.TargetAnswerVariantId is not null
                ? RequireId(variantIds, review.TargetAnswerVariantId)
                : null;
            int? matchedVariantId = review.MatchedAnswerVariantId is not null
                ? RequireId(variantIds, review.MatchedAnswerVariantId)
                : null;

            if (actual.TargetId != RequireId(targetIds, review.TargetId)
                || actual.SessionId != RequireId(learningSessionIds, review.SessionId)
                || actual.Rating != (int)BackupEnumMappings.ToPersistence(review.Rating)
                || (actual.WasTypedAnswer == 1) != review.WasTypedAnswer
                || (actual.WasCorrect == 1) != review.WasCorrect
                || (actual.IsSessionRepeat == 1) != review.IsSessionRepeat
                || actual.TargetAnswerVariantId != targetVariantId
                || actual.MatchedAnswerVariantId != matchedVariantId)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            RequireEqual(Schema13TimestampCodec.FormatUtc(review.ReviewedAtUtc), actual.ReviewedAtUtc);
            RequireEqual(Schema13TimestampCodec.FormatUtc(review.DueAtUtc), actual.DueAtUtc);
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

    private sealed class NativeTargetCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int SenseId { get; set; }
        public int TargetKind { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public int TypingOptOut { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class NativeVariantCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int TargetId { get; set; }
        public string AnswerLanguage { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;
        public int Requirement { get; set; }
        public int IsPreferred { get; set; }
        public string? RequiredSinceUtc { get; set; }
        public int? SourceMeaningId { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class NativeTargetStateCheckRow
    {
        public int TargetId { get; set; }
        public int State { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
    }

    private sealed class NativeTargetHistoryCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int TargetId { get; set; }
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }

    private sealed class NativeLearningSessionCheckRow
    {
        public int Status { get; set; }
        public int TotalCards { get; set; }
        public int CompletedCards { get; set; }
        public int AgainCount { get; set; }
        public int HardCount { get; set; }
        public int GoodCount { get; set; }
        public int EasyCount { get; set; }
        public string? StableId { get; set; }
    }

    private sealed class NativeQueueItemCheckRow
    {
        public int SessionId { get; set; }
        public int CardId { get; set; }
        public int QueueOrder { get; set; }
        public int IsDueCard { get; set; }
        public int IsAgainRepeat { get; set; }
        public int AnswerRevealed { get; set; }
        public int SpellingChecked { get; set; }
        public int SpellingCorrect { get; set; }
        public int IsCompleted { get; set; }
        public int? Rating { get; set; }
        public int? TargetAnswerVariantId { get; set; }
        public string? StableId { get; set; }
    }

    private sealed class NativeTargetReviewCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int TargetId { get; set; }
        public int SessionId { get; set; }
        public int Rating { get; set; }
        public int WasTypedAnswer { get; set; }
        public int WasCorrect { get; set; }
        public int IsSessionRepeat { get; set; }
        public int? TargetAnswerVariantId { get; set; }
        public int? MatchedAnswerVariantId { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
        public string DueAtUtc { get; set; } = string.Empty;
    }
}
