using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data.Schema13;

/// <summary>
/// Schema-13 counterpart of <see cref="Schema8BackupSnapshotRepository"/> (KF-BACKUP-006 Slice 2).
/// Captures and validates the complete Schema-13 persistence graph including WordLearningControls,
/// SenseLearningControls, FsrsReviewHistoryEntries, and FsrsCardStates.
/// </summary>
public static class Schema13BackupSnapshotRepository
{
    public static Schema13PortableSnapshotCaptureResult CapturePortableSnapshotForMergeSafetyCopy(SQLiteConnection connection)
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
            return new Schema13PortableSnapshotCaptureResult(PortableSnapshotCaptureStatus.BlockedByActiveWorkflow, null);
        }

        return new Schema13PortableSnapshotCaptureResult(
            PortableSnapshotCaptureStatus.Success,
            CapturePortableSnapshot(connection));
    }

    public static Schema13BackupSnapshot CapturePortableSnapshot(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw new BackupSchemaCapabilityException(13, shapeMismatch: true);
        }

        var baseSnapshot = Schema8BackupSnapshotRepository.WithSchema11DerivedEvidenceOwningCandidateIds(
            connection,
            Schema8BackupSnapshotRepository.WithSchema10LearningIdentities(
                connection,
                Schema8BackupSnapshotRepository.CapturePortableSnapshotSchema10(connection)));

        var (wordControls, senseControls, historyEntries, cardStates) =
            CaptureAndValidateSchema13Collections(connection, baseSnapshot);

        return new Schema13BackupSnapshot(baseSnapshot, wordControls, senseControls, historyEntries, cardStates);
    }

    public static Schema13BackupSnapshot CaptureSnapshot(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out var failureDetail))
        {
            throw new BackupSchemaCapabilityException(13, shapeMismatch: true);
        }

        var baseSnapshot = Schema8BackupSnapshotRepository.WithSchema11DerivedEvidenceOwningCandidateIds(
            connection,
            Schema8BackupSnapshotRepository.WithSchema10LearningIdentities(
                connection,
                Schema8BackupSnapshotRepository.CaptureSnapshot(connection)));

        var (wordControls, senseControls, historyEntries, cardStates) =
            CaptureAndValidateSchema13Collections(connection, baseSnapshot);

        return new Schema13BackupSnapshot(baseSnapshot, wordControls, senseControls, historyEntries, cardStates);
    }

    private static (
        IReadOnlyList<CapturedWordLearningControl> WordControls,
        IReadOnlyList<CapturedSenseLearningControl> SenseControls,
        IReadOnlyList<CapturedFsrsReviewHistoryEntry> HistoryEntries,
        IReadOnlyList<CapturedFsrsCardState> CardStates)
        CaptureAndValidateSchema13Collections(SQLiteConnection connection, Schema8BackupSnapshot baseSnapshot)
    {
        var wordIds = baseSnapshot.Words.Select(w => w.Id).ToHashSet();
        var senseIds = baseSnapshot.Senses.Select(s => s.Id).ToHashSet();
        var cardIds = baseSnapshot.LearningCards.Select(c => c.Id).ToHashSet();

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

        // 3. FsrsReviewHistoryEntries
        var rawHistory = GetBoundedTable<FsrsReviewHistoryEntryEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var historyEntries = new List<CapturedFsrsReviewHistoryEntry>(rawHistory.Count);
        var seenStableIds = new HashSet<string>(StringComparer.Ordinal);
        var historyByCardId = new Dictionary<int, List<CapturedFsrsReviewHistoryEntry>>();

        foreach (var row in rawHistory)
        {
            if (string.IsNullOrWhiteSpace(row.StableId))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
            if (!seenStableIds.Add(row.StableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (row.CardId <= 0 || !cardIds.Contains(row.CardId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (row.SequenceNumber <= 0 || !Enum.IsDefined(row.Rating))
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

            var captured = new CapturedFsrsReviewHistoryEntry(
                row.Id,
                row.StableId,
                row.CardId,
                row.SequenceNumber,
                row.Rating,
                reviewedAtUtc);

            historyEntries.Add(captured);

            if (!historyByCardId.TryGetValue(row.CardId, out var cardHistoryList))
            {
                cardHistoryList = [];
                historyByCardId[row.CardId] = cardHistoryList;
            }
            cardHistoryList.Add(captured);
        }

        // Validate per-card history continuity and ordering
        foreach (var (cardId, cardList) in historyByCardId)
        {
            cardList.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));
            DateTime? previousTime = null;

            for (var i = 0; i < cardList.Count; i++)
            {
                var entry = cardList[i];
                if (entry.SequenceNumber != i + 1)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (previousTime.HasValue && entry.ReviewedAtUtc < previousTime.Value)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                previousTime = entry.ReviewedAtUtc;
            }
        }

        // 4. FsrsCardStates
        var rawStates = GetBoundedTable<FsrsCardStateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var cardStates = new List<CapturedFsrsCardState>(rawStates.Count);
        var cardStatesById = new Dictionary<int, CapturedFsrsCardState>();

        foreach (var row in rawStates)
        {
            if (row.CardId <= 0 || !cardIds.Contains(row.CardId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!Enum.IsDefined(row.State))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            DateTime? lastReviewedAtUtc = null;
            if (row.LastReviewedAtUtc is not null)
            {
                try
                {
                    lastReviewedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.LastReviewedAtUtc);
                }
                catch (Exception ex)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
                }
            }

            DateTime? dueAtUtc = null;
            if (row.DueAtUtc is not null)
            {
                try
                {
                    dueAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DueAtUtc);
                }
                catch (Exception ex)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
                }
            }

            var captured = new CapturedFsrsCardState(
                row.CardId,
                row.State,
                row.Stability,
                row.Difficulty,
                lastReviewedAtUtc,
                row.StepIndex,
                dueAtUtc);

            if (!cardStatesById.TryAdd(row.CardId, captured))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            cardStates.Add(captured);
        }

        // Exactly one FsrsCardState per represented LearningCard
        if (cardStatesById.Count != baseSnapshot.LearningCards.Count)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }

        // Replay consistency check
        var replayer = new Fsrs6Replayer();
        foreach (var card in baseSnapshot.LearningCards)
        {
            if (!cardStatesById.TryGetValue(card.Id, out var state))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var cardHistory = historyByCardId.TryGetValue(card.Id, out var hList) ? hList : [];
            if (cardHistory.Count == 0)
            {
                if (state.State != Fsrs6CardState.New
                    || state.Stability.HasValue
                    || state.Difficulty.HasValue
                    || state.LastReviewedAtUtc.HasValue
                    || state.StepIndex.HasValue)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
            else
            {
                if (state.State == Fsrs6CardState.New)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                var events = new List<Fsrs6ReviewEvent>(cardHistory.Count);
                foreach (var h in cardHistory)
                {
                    events.Add(new Fsrs6ReviewEvent(
                        new DateTimeOffset(h.ReviewedAtUtc, TimeSpan.Zero),
                        h.Rating));
                }

                Fsrs6Card replayed;
                try
                {
                    replayed = replayer.Replay(Fsrs6Card.New(), events);
                }
                catch (Exception ex)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation, ex);
                }

                if (replayed.State != state.State)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (!AreExactDoublesEqual(replayed.Stability, state.Stability)
                    || !AreExactDoublesEqual(replayed.Difficulty, state.Difficulty))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (replayed.LastReviewedAtUtc?.UtcDateTime != state.LastReviewedAtUtc)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (replayed.StepIndex != state.StepIndex)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        return (wordControls, senseControls, historyEntries, cardStates);
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
