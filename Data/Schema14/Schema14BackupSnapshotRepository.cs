using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data.Schema14;

/// <summary>
/// Schema-14 snapshot repository (KF-LEARN-011 Slice 5).
/// Captures and validates the complete Schema-14 persistence graph including LearningTargets,
/// TargetAnswerVariants, TargetFsrsStates, TargetFsrsReviewHistoryEntries, and TargetReviews.
/// </summary>
public static class Schema14BackupSnapshotRepository
{
    public static Schema14PortableSnapshotCaptureResult CapturePortableSnapshotForMergeSafetyCopy(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var hasActiveReview = connection.Table<ReviewSessionEntity>().ToList()
            .Any(session => session.Status == ReviewSessionStatus.Active);
        var hasActivePreparation = connection.Table<PreparationSessionEntity>().ToList()
            .Any(session => session.Status == PreparationSessionStatus.Active);
        var hasActiveLearning = connection.Table<LearningSessionEntity>().ToList()
            .Any(session => session.Status == LearningSessionStatus.Active);

        if (hasActiveReview || hasActivePreparation || hasActiveLearning)
        {
            return new Schema14PortableSnapshotCaptureResult(PortableSnapshotCaptureStatus.BlockedByActiveWorkflow, null);
        }

        return new Schema14PortableSnapshotCaptureResult(
            PortableSnapshotCaptureStatus.Success,
            CapturePortableSnapshot(connection));
    }

    public static Schema14BackupSnapshot CapturePortableSnapshot(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out _)
            || !TargetPersistenceShapeValidator.Validate(connection, out _))
        {
            throw new BackupSchemaCapabilityException(14, shapeMismatch: true);
        }

        var baseSnapshot = Schema8BackupSnapshotRepository.WithSchema11DerivedEvidenceOwningCandidateIds(
            connection,
            Schema8BackupSnapshotRepository.WithSchema10LearningIdentities(
                connection,
                Schema8BackupSnapshotRepository.CapturePortableSnapshotSchema10(connection)));

        var collections = CaptureAndValidateSchema14Collections(connection, baseSnapshot);

        return new Schema14BackupSnapshot(
            baseSnapshot,
            collections.WordControls,
            collections.SenseControls,
            collections.Targets,
            collections.Variants,
            collections.States,
            collections.HistoryEntries,
            collections.Reviews);
    }

    public static Schema14BackupSnapshot CaptureSnapshot(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out _)
            || !TargetPersistenceShapeValidator.Validate(connection, out _))
        {
            throw new BackupSchemaCapabilityException(14, shapeMismatch: true);
        }

        var baseSnapshot = Schema8BackupSnapshotRepository.WithSchema11DerivedEvidenceOwningCandidateIds(
            connection,
            Schema8BackupSnapshotRepository.WithSchema10LearningIdentities(
                connection,
                Schema8BackupSnapshotRepository.CaptureSnapshot(connection)));

        var collections = CaptureAndValidateSchema14Collections(connection, baseSnapshot);

        return new Schema14BackupSnapshot(
            baseSnapshot,
            collections.WordControls,
            collections.SenseControls,
            collections.Targets,
            collections.Variants,
            collections.States,
            collections.HistoryEntries,
            collections.Reviews);
    }

    private static (
        IReadOnlyList<CapturedWordLearningControl> WordControls,
        IReadOnlyList<CapturedSenseLearningControl> SenseControls,
        IReadOnlyList<PersistedLearningTarget> Targets,
        IReadOnlyList<PersistedTargetAnswerVariant> Variants,
        IReadOnlyList<CapturedTargetFsrsState> States,
        IReadOnlyList<CapturedTargetFsrsReviewHistoryEntry> HistoryEntries,
        IReadOnlyList<CapturedTargetReview> Reviews)
        CaptureAndValidateSchema14Collections(SQLiteConnection connection, Schema8BackupSnapshot baseSnapshot)
    {
        var wordIds = baseSnapshot.Words.Select(w => w.Id).ToHashSet();
        var senseIds = baseSnapshot.Senses.Select(s => s.Id).ToHashSet();
        var sessionIds = baseSnapshot.LearningSessions.Select(ls => ls.Id).ToHashSet();

        // 1. WordLearningControls
        var rawWordControls = GetBoundedTable<WordLearningControlEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var wordControls = new List<CapturedWordLearningControl>(rawWordControls.Count);
        var seenWordIds = new HashSet<int>();

        foreach (var row in rawWordControls)
        {
            if (row.WordId <= 0 || !wordIds.Contains(row.WordId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!seenWordIds.Add(row.WordId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            DateTime decidedAtUtc;
            try
            {
                decidedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DecidedAtUtc);
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            wordControls.Add(new CapturedWordLearningControl(row.WordId, decidedAtUtc));
        }

        // 2. SenseLearningControls
        var rawSenseControls = GetBoundedTable<SenseLearningControlEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var senseControls = new List<CapturedSenseLearningControl>(rawSenseControls.Count);
        var seenSenseIds = new HashSet<int>();

        foreach (var row in rawSenseControls)
        {
            if (row.SenseId <= 0 || !senseIds.Contains(row.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!seenSenseIds.Add(row.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            DateTime decidedAtUtc;
            try
            {
                decidedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DecidedAtUtc);
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            senseControls.Add(new CapturedSenseLearningControl(row.SenseId, decidedAtUtc));
        }

        // 3. LearningTargets
        var rawTargets = GetBoundedTable<LearningTargetEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var targets = new List<PersistedLearningTarget>(rawTargets.Count);
        var targetIds = new HashSet<int>();
        var seenTargetStableIds = new HashSet<string>(StringComparer.Ordinal);
        var seenTargetSemantics = new HashSet<(int SenseId, LearningTargetKind Kind, string Src, string Tgt)>();

        foreach (var row in rawTargets)
        {
            if (row.Id <= 0 || !targetIds.Add(row.Id))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (string.IsNullOrWhiteSpace(row.StableId) || !seenTargetStableIds.Add(row.StableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (row.SenseId <= 0 || !senseIds.Contains(row.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (string.IsNullOrWhiteSpace(row.SourceLanguage) || string.IsNullOrWhiteSpace(row.TargetLanguage))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            var semanticKey = (row.SenseId, row.TargetKind, row.SourceLanguage.ToLowerInvariant(), row.TargetLanguage.ToLowerInvariant());
            if (!seenTargetSemantics.Add(semanticKey))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            DateTime createdAtUtc;
            DateTime updatedAtUtc;
            try
            {
                createdAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.CreatedAtUtc);
                updatedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.UpdatedAtUtc);
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            targets.Add(new PersistedLearningTarget(
                row.Id,
                row.StableId,
                row.SenseId,
                row.TargetKind,
                row.SourceLanguage,
                row.TargetLanguage,
                row.TypingOptOut,
                createdAtUtc,
                updatedAtUtc));
        }

        // 4. TargetAnswerVariants
        var rawVariants = GetBoundedTable<TargetAnswerVariantEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var variants = new List<PersistedTargetAnswerVariant>(rawVariants.Count);
        var variantIds = new HashSet<int>();
        var seenVariantStableIds = new HashSet<string>(StringComparer.Ordinal);
        var seenVariantNormalized = new HashSet<(int TargetId, string Normalized)>();
        var preferredCountByTarget = new Dictionary<int, int>();

        foreach (var row in rawVariants)
        {
            if (row.Id <= 0 || !variantIds.Add(row.Id))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (string.IsNullOrWhiteSpace(row.StableId) || !seenVariantStableIds.Add(row.StableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (row.TargetId <= 0 || !targetIds.Contains(row.TargetId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (string.IsNullOrWhiteSpace(row.DisplayText) || string.IsNullOrWhiteSpace(row.NormalizedText))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            if (!seenVariantNormalized.Add((row.TargetId, row.NormalizedText.ToLowerInvariant())))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            if (row.IsPreferred)
            {
                preferredCountByTarget.TryGetValue(row.TargetId, out var count);
                preferredCountByTarget[row.TargetId] = count + 1;
            }

            DateTime createdAtUtc;
            DateTime updatedAtUtc;
            DateTime? requiredSinceUtc = null;
            try
            {
                createdAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.CreatedAtUtc);
                updatedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.UpdatedAtUtc);
                if (row.RequiredSinceUtc is not null)
                {
                    requiredSinceUtc = Schema13TimestampCodec.ParseUtcDateTime(row.RequiredSinceUtc);
                }
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            if (row.Requirement == KnownFirst.Data.Migrations.Schema8.AnswerVariantRequirement.Required)
            {
                if (!requiredSinceUtc.HasValue)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
            else
            {
                if (requiredSinceUtc.HasValue)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }

            variants.Add(new PersistedTargetAnswerVariant(
                row.Id,
                row.StableId,
                row.TargetId,
                row.AnswerLanguage,
                row.DisplayText,
                row.NormalizedText,
                row.Requirement,
                row.IsPreferred,
                requiredSinceUtc,
                row.SourceMeaningId,
                createdAtUtc,
                updatedAtUtc));
        }

        foreach (var (_, count) in preferredCountByTarget)
        {
            if (count > 1)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
        }

        // 5. TargetFsrsStates
        var rawStates = GetBoundedTable<TargetFsrsStateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var states = new List<CapturedTargetFsrsState>(rawStates.Count);
        var seenStateTargetIds = new HashSet<int>();

        foreach (var row in rawStates)
        {
            if (row.TargetId <= 0 || !targetIds.Contains(row.TargetId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!seenStateTargetIds.Add(row.TargetId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            DateTime? lastReviewedAtUtc = null;
            DateTime? dueAtUtc = null;
            try
            {
                if (row.LastReviewedAtUtc is not null)
                {
                    lastReviewedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.LastReviewedAtUtc);
                }
                if (row.DueAtUtc is not null)
                {
                    dueAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DueAtUtc);
                }
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            states.Add(new CapturedTargetFsrsState(
                row.TargetId,
                row.State,
                row.Stability,
                row.Difficulty,
                lastReviewedAtUtc,
                row.StepIndex,
                dueAtUtc));
        }

        if (states.Count != targets.Count)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }

        // 6. TargetFsrsReviewHistoryEntries
        var rawHistory = GetBoundedTable<TargetFsrsReviewHistoryEntryEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var historyEntries = new List<CapturedTargetFsrsReviewHistoryEntry>(rawHistory.Count);
        var seenHistoryStableIds = new HashSet<string>(StringComparer.Ordinal);
        var historyByTarget = new Dictionary<int, List<CapturedTargetFsrsReviewHistoryEntry>>();

        foreach (var row in rawHistory)
        {
            if (string.IsNullOrWhiteSpace(row.StableId) || !seenHistoryStableIds.Add(row.StableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (row.TargetId <= 0 || !targetIds.Contains(row.TargetId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (row.SequenceNumber <= 0)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            DateTime reviewedAtUtc;
            try
            {
                reviewedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.ReviewedAtUtc);
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            var entry = new CapturedTargetFsrsReviewHistoryEntry(
                row.Id,
                row.StableId,
                row.TargetId,
                row.SequenceNumber,
                row.Rating,
                reviewedAtUtc);

            historyEntries.Add(entry);

            if (!historyByTarget.TryGetValue(row.TargetId, out var list))
            {
                list = [];
                historyByTarget[row.TargetId] = list;
            }
            list.Add(entry);
        }

        // Validate history sequences & FSRS replay consistency
        var replayer = new Fsrs6Replayer();
        var statesByTarget = states.ToDictionary(s => s.TargetId);

        foreach (var target in targets)
        {
            var targetState = statesByTarget[target.Id];
            var targetHistory = historyByTarget.TryGetValue(target.Id, out var hList) ? hList : [];

            targetHistory.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));
            DateTime? previousTime = null;

            for (var i = 0; i < targetHistory.Count; i++)
            {
                var h = targetHistory[i];
                if (h.SequenceNumber != i + 1)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (previousTime.HasValue && h.ReviewedAtUtc < previousTime.Value)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                previousTime = h.ReviewedAtUtc;
            }

            if (targetHistory.Count == 0)
            {
                if (targetState.State != Fsrs6CardState.New
                    || targetState.Stability.HasValue
                    || targetState.Difficulty.HasValue
                    || targetState.LastReviewedAtUtc.HasValue
                    || targetState.StepIndex.HasValue)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
            else
            {
                if (targetState.State == Fsrs6CardState.New)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                var events = targetHistory.Select(h => new Fsrs6ReviewEvent(
                    new DateTimeOffset(h.ReviewedAtUtc, TimeSpan.Zero),
                    h.Rating)).ToList();

                Fsrs6Card replayed;
                try
                {
                    replayed = replayer.Replay(Fsrs6Card.New(), events);
                }
                catch
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (replayed.State != targetState.State)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (!AreExactDoublesEqual(replayed.Stability, targetState.Stability)
                    || !AreExactDoublesEqual(replayed.Difficulty, targetState.Difficulty))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (replayed.LastReviewedAtUtc?.UtcDateTime != targetState.LastReviewedAtUtc)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (replayed.StepIndex != targetState.StepIndex)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        // 7. TargetReviews
        var rawReviews = GetBoundedTable<TargetReviewEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviews = new List<CapturedTargetReview>(rawReviews.Count);
        var seenReviewStableIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rawReviews)
        {
            if (string.IsNullOrWhiteSpace(row.StableId) || !seenReviewStableIds.Add(row.StableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (row.TargetId <= 0 || !targetIds.Contains(row.TargetId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (row.SessionId <= 0 || !sessionIds.Contains(row.SessionId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (row.TargetAnswerVariantId.HasValue && !variantIds.Contains(row.TargetAnswerVariantId.Value))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (row.MatchedAnswerVariantId.HasValue && !variantIds.Contains(row.MatchedAnswerVariantId.Value))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            DateTime reviewedAtUtc;
            DateTime dueAtUtc;
            try
            {
                reviewedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.ReviewedAtUtc);
                dueAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DueAtUtc);
            }
            catch (Exception ex)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
            }

            reviews.Add(new CapturedTargetReview(
                row.Id,
                row.StableId,
                row.TargetId,
                row.SessionId,
                row.Rating,
                row.WasTypedAnswer,
                row.WasCorrect,
                row.IsSessionRepeat,
                row.TargetAnswerVariantId,
                row.MatchedAnswerVariantId,
                reviewedAtUtc,
                dueAtUtc));
        }

        return (wordControls, senseControls, targets, variants, states, historyEntries, reviews);
    }

    private static bool AreExactDoublesEqual(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return BitConverter.DoubleToInt64Bits(left.Value) == BitConverter.DoubleToInt64Bits(right.Value);
    }

    private static List<T> GetBoundedTable<T>(SQLiteConnection connection, int limit) where T : new()
    {
        var count = connection.Table<T>().Count();
        if (count > limit)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }
        return connection.Table<T>().ToList();
    }
}
