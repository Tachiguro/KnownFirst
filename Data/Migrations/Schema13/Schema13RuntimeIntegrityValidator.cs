using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

/// <summary>
/// Validates the steady-state Schema-13 invariants without relying on surviving Schema-12 source facts.
/// </summary>
public static class Schema13RuntimeIntegrityValidator
{
    public static bool Validate(SQLiteConnection connection, out string? failureDetail)
    {
        ArgumentNullException.ThrowIfNull(connection);

        try
        {
            return ValidateCore(connection, out failureDetail);
        }
        catch (Exception ex)
        {
            failureDetail = $"Runtime integrity validation could not complete: {ex.Message}";
            return false;
        }
    }

    private static bool ValidateCore(SQLiteConnection connection, out string? failureDetail)
    {
        if (!Schema13ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        var orphanedStates = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM FsrsCardStates s LEFT JOIN LearningCards c ON c.Id = s.CardId WHERE c.Id IS NULL");
        if (orphanedStates > 0)
        {
            failureDetail = $"Found {orphanedStates} FsrsCardStates rows with no matching LearningCard.";
            return false;
        }

        var missingStates = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningCards c LEFT JOIN FsrsCardStates s ON s.CardId = c.Id WHERE s.CardId IS NULL");
        if (missingStates > 0)
        {
            failureDetail = $"Found {missingStates} LearningCards with no FsrsCardStates row.";
            return false;
        }

        var orphanedHistory = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries h LEFT JOIN LearningCards c ON c.Id = h.CardId WHERE c.Id IS NULL");
        if (orphanedHistory > 0)
        {
            failureDetail = $"Found {orphanedHistory} FsrsReviewHistoryEntries rows with no matching LearningCard.";
            return false;
        }

        var orphanedWordControls = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WordLearningControls wlc LEFT JOIN Words w ON w.Id = wlc.WordId WHERE w.Id IS NULL");
        if (orphanedWordControls > 0)
        {
            failureDetail = $"Found {orphanedWordControls} WordLearningControls rows with no matching Word.";
            return false;
        }

        var orphanedSenseControls = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM SenseLearningControls slc LEFT JOIN Senses s ON s.Id = slc.SenseId WHERE s.Id IS NULL");
        if (orphanedSenseControls > 0)
        {
            failureDetail = $"Found {orphanedSenseControls} SenseLearningControls rows with no matching Sense.";
            return false;
        }

        var history = connection.Query<HistoryCheckRow>(
            "SELECT StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries ORDER BY CardId ASC, SequenceNumber ASC");
        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var historyByCardId = new Dictionary<int, List<Fsrs6ReviewEvent>>();
        var expectedSequenceByCardId = new Dictionary<int, int>();
        var previousTimestampByCardId = new Dictionary<int, DateTimeOffset>();

        foreach (var row in history)
        {
            if (string.IsNullOrWhiteSpace(row.StableId))
            {
                failureDetail = $"Card {row.CardId} history entry SequenceNumber {row.SequenceNumber} has an empty or whitespace StableId.";
                return false;
            }
            if (!stableIds.Add(row.StableId))
            {
                failureDetail = $"FsrsReviewHistoryEntries StableId '{row.StableId}' is duplicated.";
                return false;
            }

            var expectedSequence = expectedSequenceByCardId.TryGetValue(row.CardId, out var nextSequence)
                ? nextSequence
                : 1;
            if (row.SequenceNumber != expectedSequence)
            {
                failureDetail = $"Card {row.CardId} history sequence is broken: expected SequenceNumber {expectedSequence}, found {row.SequenceNumber}.";
                return false;
            }
            expectedSequenceByCardId[row.CardId] = expectedSequence + 1;

            if (row.Rating < (int)ReviewRating.Again || row.Rating > (int)ReviewRating.Easy)
            {
                failureDetail = $"Card {row.CardId} history entry SequenceNumber {row.SequenceNumber} has invalid Rating {row.Rating}.";
                return false;
            }

            DateTimeOffset reviewedAtUtc;
            try
            {
                reviewedAtUtc = Schema13TimestampCodec.ParseUtcDateTimeOffset(row.ReviewedAtUtc);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                failureDetail = $"Card {row.CardId} history entry SequenceNumber {row.SequenceNumber} has invalid ReviewedAtUtc '{row.ReviewedAtUtc}': {ex.Message}";
                return false;
            }

            if (previousTimestampByCardId.TryGetValue(row.CardId, out var previousTimestamp)
                && reviewedAtUtc < previousTimestamp)
            {
                failureDetail = $"Card {row.CardId} history entry SequenceNumber {row.SequenceNumber} has timestamp {reviewedAtUtc:O} earlier than previous {previousTimestamp:O}.";
                return false;
            }
            previousTimestampByCardId[row.CardId] = reviewedAtUtc;

            if (!historyByCardId.TryGetValue(row.CardId, out var cardHistory))
            {
                cardHistory = [];
                historyByCardId.Add(row.CardId, cardHistory);
            }
            cardHistory.Add(new Fsrs6ReviewEvent(reviewedAtUtc, (ReviewRating)row.Rating));
        }

        var cardStates = connection.Query<CardStateCheckRow>(
            "SELECT CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc FROM FsrsCardStates ORDER BY CardId ASC");
        var replayer = new Fsrs6Replayer();
        foreach (var persisted in cardStates)
        {
            var events = historyByCardId.TryGetValue(persisted.CardId, out var cardHistory)
                ? cardHistory
                : [];

            Fsrs6Card replayed;
            try
            {
                replayed = replayer.Replay(Fsrs6Card.New(), events);
            }
            catch (Exception ex)
            {
                failureDetail = $"FSRS replay validation failed for Card {persisted.CardId}: {ex.Message}";
                return false;
            }

            if (persisted.State != (int)replayed.State)
            {
                failureDetail = $"Card {persisted.CardId} State mismatch: replayed={(int)replayed.State}, persisted={persisted.State}.";
                return false;
            }
            if (!AreExactDoublesEqual(persisted.Stability, replayed.Stability))
            {
                failureDetail = $"Card {persisted.CardId} Stability mismatch: replayed={replayed.Stability}, persisted={persisted.Stability}.";
                return false;
            }
            if (!AreExactDoublesEqual(persisted.Difficulty, replayed.Difficulty))
            {
                failureDetail = $"Card {persisted.CardId} Difficulty mismatch: replayed={replayed.Difficulty}, persisted={persisted.Difficulty}.";
                return false;
            }

            var replayedLastReviewed = replayed.LastReviewedAtUtc.HasValue
                ? Schema13TimestampCodec.FormatUtc(replayed.LastReviewedAtUtc.Value)
                : null;
            if (!string.Equals(persisted.LastReviewedAtUtc, replayedLastReviewed, StringComparison.Ordinal))
            {
                failureDetail = $"Card {persisted.CardId} LastReviewedAtUtc mismatch: replayed='{replayedLastReviewed}', persisted='{persisted.LastReviewedAtUtc}'.";
                return false;
            }
            if (persisted.StepIndex != replayed.StepIndex)
            {
                failureDetail = $"Card {persisted.CardId} StepIndex mismatch: replayed={replayed.StepIndex}, persisted={persisted.StepIndex}.";
                return false;
            }

            var replayedDue = replayed.DueAtUtc.HasValue
                ? Schema13TimestampCodec.FormatUtc(replayed.DueAtUtc.Value)
                : null;
            if (!string.Equals(persisted.DueAtUtc, replayedDue, StringComparison.Ordinal))
            {
                failureDetail = $"Card {persisted.CardId} DueAtUtc mismatch: replayed='{replayedDue}', persisted='{persisted.DueAtUtc}'.";
                return false;
            }
        }

        var foreignKeyViolations = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_foreign_key_check");
        if (foreignKeyViolations > 0)
        {
            failureDetail = $"PRAGMA foreign_key_check reported {foreignKeyViolations} violation(s).";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static bool AreExactDoublesEqual(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return BitConverter.DoubleToInt64Bits(left.Value) == BitConverter.DoubleToInt64Bits(right.Value);
    }

    private sealed class CardStateCheckRow
    {
        public int CardId { get; set; }
        public int State { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
    }

    private sealed class HistoryCheckRow
    {
        public string StableId { get; set; } = string.Empty;
        public int CardId { get; set; }
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }
}
