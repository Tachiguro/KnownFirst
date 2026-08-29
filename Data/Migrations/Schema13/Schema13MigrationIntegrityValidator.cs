using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

public static class Schema13MigrationIntegrityValidator
{
    private const double FloatingPointTolerance = 1e-9;

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

        // 2. WordLearningControls 1:1 with Words(Status = Known)
        int expectedWordControls = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Words WHERE Status = 1");
        int actualWordControls = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls");
        if (expectedWordControls != actualWordControls)
        {
            failureDetail = $"Expected {expectedWordControls} WordLearningControls for Known words, but found {actualWordControls}.";
            return false;
        }

        int invalidWordControls = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WordLearningControls wlc LEFT JOIN Words w ON w.Id = wlc.WordId WHERE w.Id IS NULL OR w.Status != 1");
        if (invalidWordControls > 0)
        {
            failureDetail = $"Found {invalidWordControls} invalid WordLearningControls rows not matching Words with Status = Known.";
            return false;
        }

        // 3. SenseLearningControls must remain empty
        int senseControls = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls");
        if (senseControls > 0)
        {
            failureDetail = $"SenseLearningControls must be empty in Schema 12 -> 13 migration, but found {senseControls} rows.";
            return false;
        }

        // 4. History sequence and replay reproducibility
        var cardStates = connection.Query<CardStateCheckRow>(
            "SELECT CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc FROM FsrsCardStates ORDER BY CardId ASC");

        var historyRows = connection.Query<HistoryCheckRow>(
            "SELECT CardId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries ORDER BY CardId ASC, SequenceNumber ASC");

        var historyByCardId = historyRows
            .GroupBy(h => h.CardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var replayer = new Fsrs6Replayer();

        foreach (var state in cardStates)
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

                // Verify source card is genuinely unreviewed
                var sourceCards = connection.Query<LegacyCardRow>(
                    "SELECT Id AS CardId, State, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating FROM LearningCards WHERE Id = ?",
                    state.CardId);
                var sourceCard = sourceCards.FirstOrDefault();
                if (sourceCard is not null && !Schema13LearningBootstrap.IsGenuinelyUnreviewed(sourceCard))
                {
                    failureDetail = $"Card {state.CardId} was persisted as New but source LearningCard shows prior progress without history.";
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
                DateTimeOffset? prevTime = null;

                for (int i = 0; i < cardHistory.Count; i++)
                {
                    var h = cardHistory[i];
                    int expectedSeq = i + 1;
                    if (h.SequenceNumber != expectedSeq)
                    {
                        failureDetail = $"Card {state.CardId} history sequence broken at index {i}: expected SequenceNumber {expectedSeq}, found {h.SequenceNumber}.";
                        return false;
                    }

                    if (h.Rating < 0 || h.Rating > 3)
                    {
                        failureDetail = $"Card {state.CardId} history entry has invalid Rating {h.Rating}.";
                        return false;
                    }

                    DateTimeOffset eventTime;
                    try
                    {
                        eventTime = Schema13TimestampCodec.ParseUtcDateTimeOffset(h.ReviewedAtUtc);
                    }
                    catch (Exception ex)
                    {
                        failureDetail = $"Card {state.CardId} history entry has corrupt ReviewedAtUtc '{h.ReviewedAtUtc}': {ex.Message}";
                        return false;
                    }

                    if (prevTime.HasValue && eventTime < prevTime.Value)
                    {
                        failureDetail = $"Card {state.CardId} history entry SequenceNumber {h.SequenceNumber} has timestamp {eventTime:O} earlier than previous {prevTime.Value:O}.";
                        return false;
                    }

                    prevTime = eventTime;
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

                if (!AreDoublesClose(replayed.Stability, state.Stability))
                {
                    failureDetail = $"Card {state.CardId} Stability mismatch: replayed={replayed.Stability}, persisted={state.Stability}.";
                    return false;
                }

                if (!AreDoublesClose(replayed.Difficulty, state.Difficulty))
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

    private static bool AreDoublesClose(double? a, double? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) < FloatingPointTolerance;
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
        public int CardId { get; set; }
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }
}
