using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

public static class Schema13MigrationIntegrityValidator
{
    public static bool Validate(SQLiteConnection connection, out string? failureDetail)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Schema13ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        // 1. LearningCards vs FsrsCardStates count and 1:1 correspondence
        int cardCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards");
        int stateCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates");
        if (cardCount != stateCount)
        {
            failureDetail = $"LearningCards count ({cardCount}) does not match FsrsCardStates count ({stateCount}).";
            return false;
        }

        int orphanedStates = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM FsrsCardStates s LEFT JOIN LearningCards c ON c.Id = s.CardId WHERE c.Id IS NULL");
        if (orphanedStates > 0)
        {
            failureDetail = $"Found {orphanedStates} FsrsCardStates rows with no matching LearningCard.";
            return false;
        }

        int missingStates = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningCards c LEFT JOIN FsrsCardStates s ON s.CardId = c.Id WHERE s.CardId IS NULL");
        if (missingStates > 0)
        {
            failureDetail = $"Found {missingStates} LearningCards with no FsrsCardStates row.";
            return false;
        }

        // 2. Orphan detection for history and word controls (especially when foreign keys are disabled)
        int orphanedHistory = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries h LEFT JOIN LearningCards c ON c.Id = h.CardId WHERE c.Id IS NULL");
        if (orphanedHistory > 0)
        {
            failureDetail = $"Found {orphanedHistory} FsrsReviewHistoryEntries rows with no matching LearningCard.";
            return false;
        }

        int orphanedWordControls = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WordLearningControls wlc LEFT JOIN Words w ON w.Id = wlc.WordId WHERE w.Id IS NULL");
        if (orphanedWordControls > 0)
        {
            failureDetail = $"Found {orphanedWordControls} WordLearningControls rows with no matching Word.";
            return false;
        }

        // 3. SenseLearningControls must remain empty in Schema 12 -> 13 migration
        int senseControls = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls");
        if (senseControls > 0)
        {
            failureDetail = $"SenseLearningControls must be empty in Schema 12 -> 13 migration, but found {senseControls} rows.";
            return false;
        }

        // 4. Derive expected migration plan from surviving Schema 12 source facts
        Schema13BootstrapPlan expectedPlan;
        try
        {
            expectedPlan = Schema13LearningBootstrap.BuildPlan(connection);
        }
        catch (Exception ex)
        {
            failureDetail = $"Failed to build expected Schema 13 plan from source data: {ex.Message}";
            return false;
        }

        // 5. Validate WordLearningControls: exact source-to-target equivalence
        var actualWordControls = connection.Query<WordLearningControlCheckRow>(
            "SELECT WordId, DecidedAtUtc FROM WordLearningControls ORDER BY WordId ASC");
        if (actualWordControls.Count != expectedPlan.WordControls.Count)
        {
            failureDetail = $"WordLearningControls count mismatch: expected {expectedPlan.WordControls.Count}, found {actualWordControls.Count}.";
            return false;
        }

        for (int i = 0; i < actualWordControls.Count; i++)
        {
            var actual = actualWordControls[i];
            var expected = expectedPlan.WordControls[i];

            if (actual.WordId != expected.WordId)
            {
                failureDetail = $"WordLearningControls WordId mismatch at position {i}: expected {expected.WordId}, found {actual.WordId}.";
                return false;
            }

            if (!string.Equals(actual.DecidedAtUtc, expected.DecidedAtUtc, StringComparison.Ordinal))
            {
                failureDetail = $"WordLearningControls DecidedAtUtc mismatch for WordId {expected.WordId}: expected '{expected.DecidedAtUtc}', found '{actual.DecidedAtUtc}'.";
                return false;
            }
        }

        // 6. Validate FsrsReviewHistoryEntries: exact source-to-target equivalence
        var actualHistory = connection.Query<HistoryCheckRow>(
            "SELECT StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries ORDER BY CardId ASC, SequenceNumber ASC");
        if (actualHistory.Count != expectedPlan.ReviewHistory.Count)
        {
            failureDetail = $"FsrsReviewHistoryEntries count mismatch: expected {expectedPlan.ReviewHistory.Count}, found {actualHistory.Count}.";
            return false;
        }

        for (int i = 0; i < actualHistory.Count; i++)
        {
            var actual = actualHistory[i];
            var expected = expectedPlan.ReviewHistory[i];

            if (actual.CardId != expected.CardId)
            {
                failureDetail = $"FsrsReviewHistoryEntries CardId mismatch at position {i}: expected {expected.CardId}, found {actual.CardId}.";
                return false;
            }

            if (actual.SequenceNumber != expected.SequenceNumber)
            {
                failureDetail = $"Card {expected.CardId} history sequence broken at index {i}: expected SequenceNumber {expected.SequenceNumber}, found {actual.SequenceNumber}.";
                return false;
            }

            if (!string.Equals(actual.StableId, expected.StableId, StringComparison.Ordinal))
            {
                failureDetail = $"FsrsReviewHistoryEntries StableId mismatch for CardId {expected.CardId}, SequenceNumber {expected.SequenceNumber}: expected '{expected.StableId}', found '{actual.StableId}'.";
                return false;
            }

            if (actual.Rating != expected.Rating)
            {
                failureDetail = $"FsrsReviewHistoryEntries Rating mismatch for CardId {expected.CardId}, SequenceNumber {expected.SequenceNumber}: expected {expected.Rating}, found {actual.Rating}.";
                return false;
            }

            if (!string.Equals(actual.ReviewedAtUtc, expected.ReviewedAtUtc, StringComparison.Ordinal))
            {
                failureDetail = $"FsrsReviewHistoryEntries ReviewedAtUtc mismatch for CardId {expected.CardId}, SequenceNumber {expected.SequenceNumber}: expected '{expected.ReviewedAtUtc}', found '{actual.ReviewedAtUtc}'.";
                return false;
            }
        }

        // 7. Validate target history sequence continuity, ratings, and timestamp ordering per card
        var historyByCardId = actualHistory
            .GroupBy(h => h.CardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (cId, cardHistory) in historyByCardId)
        {
            DateTimeOffset? prevTime = null;
            for (int i = 0; i < cardHistory.Count; i++)
            {
                var h = cardHistory[i];
                int expectedSeq = i + 1;
                if (h.SequenceNumber != expectedSeq)
                {
                    failureDetail = $"Card {cId} history sequence broken at index {i}: expected SequenceNumber {expectedSeq}, found {h.SequenceNumber}.";
                    return false;
                }

                if (h.Rating < 0 || h.Rating > 3)
                {
                    failureDetail = $"Card {cId} history entry has invalid Rating {h.Rating}.";
                    return false;
                }

                DateTimeOffset eventTime;
                try
                {
                    eventTime = Schema13TimestampCodec.ParseUtcDateTimeOffset(h.ReviewedAtUtc);
                }
                catch (Exception ex)
                {
                    failureDetail = $"Card {cId} history entry has corrupt ReviewedAtUtc '{h.ReviewedAtUtc}': {ex.Message}";
                    return false;
                }

                if (prevTime.HasValue && eventTime < prevTime.Value)
                {
                    failureDetail = $"Card {cId} history entry SequenceNumber {h.SequenceNumber} has timestamp {eventTime:O} earlier than previous {prevTime.Value:O}.";
                    return false;
                }

                prevTime = eventTime;
            }
        }

        // 8. Validate FsrsCardStates: exact source-to-target equivalence
        var actualCardStates = connection.Query<CardStateCheckRow>(
            "SELECT CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc FROM FsrsCardStates ORDER BY CardId ASC");
        if (actualCardStates.Count != expectedPlan.CardStates.Count)
        {
            failureDetail = $"FsrsCardStates count mismatch: expected {expectedPlan.CardStates.Count}, found {actualCardStates.Count}.";
            return false;
        }

        for (int i = 0; i < actualCardStates.Count; i++)
        {
            var actual = actualCardStates[i];
            var expected = expectedPlan.CardStates[i];

            if (actual.CardId != expected.CardId)
            {
                failureDetail = $"FsrsCardStates CardId mismatch at position {i}: expected {expected.CardId}, found {actual.CardId}.";
                return false;
            }

            if (actual.State != (int)expected.Card.State)
            {
                failureDetail = $"Card {actual.CardId} State mismatch: expected={(int)expected.Card.State}, persisted={actual.State}.";
                return false;
            }

            if (!AreExactDoublesEqual(expected.Card.Stability, actual.Stability))
            {
                failureDetail = $"Card {actual.CardId} Stability mismatch: expected={expected.Card.Stability}, persisted={actual.Stability}.";
                return false;
            }

            if (!AreExactDoublesEqual(expected.Card.Difficulty, actual.Difficulty))
            {
                failureDetail = $"Card {actual.CardId} Difficulty mismatch: expected={expected.Card.Difficulty}, persisted={actual.Difficulty}.";
                return false;
            }

            string? expectedLastReviewed = expected.Card.LastReviewedAtUtc.HasValue
                ? Schema13TimestampCodec.FormatUtc(expected.Card.LastReviewedAtUtc.Value)
                : null;
            if (!string.Equals(actual.LastReviewedAtUtc, expectedLastReviewed, StringComparison.Ordinal))
            {
                failureDetail = $"Card {actual.CardId} LastReviewedAtUtc mismatch: expected='{expectedLastReviewed}', persisted='{actual.LastReviewedAtUtc}'.";
                return false;
            }

            if (actual.StepIndex != expected.Card.StepIndex)
            {
                failureDetail = $"Card {actual.CardId} StepIndex mismatch: expected={expected.Card.StepIndex}, persisted={actual.StepIndex}.";
                return false;
            }

            string? expectedDue = expected.Card.DueAtUtc.HasValue
                ? Schema13TimestampCodec.FormatUtc(expected.Card.DueAtUtc.Value)
                : null;
            if (!string.Equals(actual.DueAtUtc, expectedDue, StringComparison.Ordinal))
            {
                failureDetail = $"Card {actual.CardId} DueAtUtc mismatch: expected='{expectedDue}', persisted='{actual.DueAtUtc}'.";
                return false;
            }
        }

        // 9. Independent target replay verification
        var replayer = new Fsrs6Replayer();
        foreach (var state in actualCardStates)
        {
            var cardHistory = historyByCardId.TryGetValue(state.CardId, out var hList)
                ? hList
                : [];

            if (cardHistory.Count == 0)
            {
                if (state.State != 0
                    || state.Stability is not null
                    || state.Difficulty is not null
                    || state.LastReviewedAtUtc is not null
                    || state.StepIndex is not null
                    || state.DueAtUtc is not null)
                {
                    failureDetail = $"Card {state.CardId} has zero history entries but FsrsCardStates is not clean New.";
                    return false;
                }
            }
            else
            {
                if (state.State == 0)
                {
                    failureDetail = $"Card {state.CardId} has {cardHistory.Count} history entries but FsrsCardStates is New.";
                    return false;
                }

                var events = new List<Fsrs6ReviewEvent>(cardHistory.Count);
                foreach (var h in cardHistory)
                {
                    var eventTime = Schema13TimestampCodec.ParseUtcDateTimeOffset(h.ReviewedAtUtc);
                    events.Add(new Fsrs6ReviewEvent(eventTime, (ReviewRating)h.Rating));
                }

                Fsrs6Card replayed;
                try
                {
                    replayed = replayer.Replay(Fsrs6Card.New(), events);
                }
                catch (Exception ex)
                {
                    failureDetail = $"FSRS replay validation failed for Card {state.CardId}: {ex.Message}";
                    return false;
                }

                if ((int)replayed.State != state.State)
                {
                    failureDetail = $"Card {state.CardId} State mismatch: replayed={(int)replayed.State}, persisted={state.State}.";
                    return false;
                }

                if (!AreExactDoublesEqual(replayed.Stability, state.Stability))
                {
                    failureDetail = $"Card {state.CardId} Stability mismatch: replayed={replayed.Stability}, persisted={state.Stability}.";
                    return false;
                }

                if (!AreExactDoublesEqual(replayed.Difficulty, state.Difficulty))
                {
                    failureDetail = $"Card {state.CardId} Difficulty mismatch: replayed={replayed.Difficulty}, persisted={state.Difficulty}.";
                    return false;
                }

                string? replayedLastReviewed = replayed.LastReviewedAtUtc.HasValue
                    ? Schema13TimestampCodec.FormatUtc(replayed.LastReviewedAtUtc.Value)
                    : null;
                if (replayedLastReviewed != state.LastReviewedAtUtc)
                {
                    failureDetail = $"Card {state.CardId} LastReviewedAtUtc mismatch: replayed='{replayedLastReviewed}', persisted='{state.LastReviewedAtUtc}'.";
                    return false;
                }

                if (replayed.StepIndex != state.StepIndex)
                {
                    failureDetail = $"Card {state.CardId} StepIndex mismatch: replayed={replayed.StepIndex}, persisted={state.StepIndex}.";
                    return false;
                }

                string? replayedDue = replayed.DueAtUtc.HasValue
                    ? Schema13TimestampCodec.FormatUtc(replayed.DueAtUtc.Value)
                    : null;
                if (replayedDue != state.DueAtUtc)
                {
                    failureDetail = $"Card {state.CardId} DueAtUtc mismatch: replayed='{replayedDue}', persisted='{state.DueAtUtc}'.";
                    return false;
                }
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool AreExactDoublesEqual(double? a, double? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return BitConverter.DoubleToInt64Bits(a.Value) == BitConverter.DoubleToInt64Bits(b.Value);
    }

    private sealed class WordLearningControlCheckRow
    {
        public int WordId { get; set; }
        public string DecidedAtUtc { get; set; } = string.Empty;
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
