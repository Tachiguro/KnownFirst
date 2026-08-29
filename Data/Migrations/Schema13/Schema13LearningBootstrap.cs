using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

public static class Schema13LearningBootstrap
{
    public static DateTime NormalizeUtcDateTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => throw new Schema13MigrationException(
                "schema13-migration-local-timestamp",
                $"Local timestamp '{value:O}' is not permitted; legacy timestamps must be UTC."),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    public static DateTimeOffset NormalizeUtcDateTimeOffset(DateTime value)
    {
        var utc = NormalizeUtcDateTime(value);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static bool IsGenuinelyUnreviewed(LegacyCardRow card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card.State == (int)CardState.New
            && card.LastReviewedAtUtc is null
            && card.LastRating is null
            && card.SuccessfulReviewCount == 0
            && card.LapseCount == 0
            && card.IntervalDays == 0;
    }

    public static IReadOnlyList<MigratedWordLearningControl> ExtractWordLearningControls(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Status = 1 is WordStatus.Known
        var knownWords = connection.Query<LegacyWordKnownRow>(
            "SELECT Id, UpdatedAt FROM Words WHERE Status = 1 ORDER BY Id ASC");

        var controls = new List<MigratedWordLearningControl>(knownWords.Count);
        foreach (var word in knownWords)
        {
            var utc = NormalizeUtcDateTime(word.UpdatedAt);
            controls.Add(new MigratedWordLearningControl(
                word.Id,
                Schema13TimestampCodec.FormatUtc(utc)));
        }

        return controls;
    }

    public static (IReadOnlyList<MigratedCardState> CardStates, IReadOnlyList<MigratedReviewHistoryEntry> ReviewHistory)
        BuildCardBootstrap(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var cards = connection.Query<LegacyCardRow>(
            """
            SELECT 
                c.Id AS CardId,
                c.SenseId AS SenseId,
                s.StableId AS SenseStableId,
                c.Direction AS Direction,
                c.State AS State,
                c.IntervalDays AS IntervalDays,
                c.EaseFactor AS EaseFactor,
                c.SuccessfulReviewCount AS SuccessfulReviewCount,
                c.LapseCount AS LapseCount,
                c.LastReviewedAtUtc AS LastReviewedAtUtc,
                c.LastRating AS LastRating,
                c.DueAtUtc AS DueAtUtc
            FROM LearningCards c
            LEFT JOIN Senses s ON s.Id = c.SenseId
            ORDER BY c.Id ASC
            """);

        var reviews = connection.Query<LegacyReviewRow>(
            """
            SELECT 
                Id,
                CardId,
                Rating,
                ReviewedAtUtc
            FROM LearningReviews
            ORDER BY CardId ASC, ReviewedAtUtc ASC, Id ASC
            """);

        var reviewsByCardId = reviews
            .GroupBy(r => r.CardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cardStates = new List<MigratedCardState>(cards.Count);
        var reviewHistory = new List<MigratedReviewHistoryEntry>(reviews.Count);
        var replayer = new Fsrs6Replayer();

        foreach (var card in cards)
        {
            if (card.SenseId is null || string.IsNullOrWhiteSpace(card.SenseStableId))
            {
                throw Schema13MigrationException.InvariantViolation(
                    $"Card {card.CardId} references missing or invalid Sense (SenseId={card.SenseId}).");
            }

            if (!Enum.IsDefined((CardDirection)card.Direction))
            {
                throw Schema13MigrationException.InvariantViolation(
                    $"Card {card.CardId} has undefined CardDirection {card.Direction}.");
            }

            var cardReviews = reviewsByCardId.TryGetValue(card.CardId, out var rList)
                ? rList
                : [];

            if (cardReviews.Count == 0)
            {
                if (!IsGenuinelyUnreviewed(card))
                {
                    throw Schema13MigrationException.MissingReviewHistory(
                        card.CardId,
                        $"Card {card.CardId} shows prior learning progress (State={card.State}, LastReviewedAtUtc={card.LastReviewedAtUtc}, SuccessfulReviewCount={card.SuccessfulReviewCount}, LapseCount={card.LapseCount}, IntervalDays={card.IntervalDays}) but has zero surviving review history.");
                }

                cardStates.Add(new MigratedCardState(card.CardId, Fsrs6Card.New(dueAtUtc: null)));
            }
            else
            {
                // Multiplicity ordinal tracks identical (timestamp, rating) encounters on this card
                var encounterCounts = new Dictionary<(long UtcTicks, int Rating), int>();
                var reviewEvents = new List<Fsrs6ReviewEvent>(cardReviews.Count);
                var cardHistoryEntries = new List<MigratedReviewHistoryEntry>(cardReviews.Count);

                int seq = 1;
                foreach (var review in cardReviews)
                {
                    if (review.Rating < 0 || review.Rating > 3)
                    {
                        throw Schema13MigrationException.CorruptReviewRating(review.Id, review.Rating);
                    }

                    var utc = NormalizeUtcDateTime(review.ReviewedAtUtc);
                    var dto = new DateTimeOffset(utc, TimeSpan.Zero);
                    var encounterKey = (utc.Ticks, review.Rating);
                    int ordinal = encounterCounts.TryGetValue(encounterKey, out var currentCount) ? currentCount : 0;
                    encounterCounts[encounterKey] = ordinal + 1;

                    string stableId = Schema13HistoricalReviewStableIdPolicy.Compute(
                        card.SenseStableId,
                        (CardDirection)card.Direction,
                        utc,
                        (ReviewRating)review.Rating,
                        ordinal);

                    string formattedReviewedAtUtc = Schema13TimestampCodec.FormatUtc(dto);

                    cardHistoryEntries.Add(new MigratedReviewHistoryEntry(
                        stableId,
                        card.CardId,
                        seq++,
                        review.Rating,
                        formattedReviewedAtUtc));

                    reviewEvents.Add(new Fsrs6ReviewEvent(dto, (ReviewRating)review.Rating));
                }

                Fsrs6Card replayedCard;
                try
                {
                    replayedCard = replayer.Replay(Fsrs6Card.New(), reviewEvents);
                }
                catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
                {
                    throw Schema13MigrationException.InvariantViolation(
                        $"FSRS replay failed for Card {card.CardId}: {ex.Message}");
                }

                cardStates.Add(new MigratedCardState(card.CardId, replayedCard));
                reviewHistory.AddRange(cardHistoryEntries);
            }
        }

        return (cardStates, reviewHistory);
    }

    public static Schema13BootstrapPlan BuildPlan(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var wordControls = ExtractWordLearningControls(connection);
        var (cardStates, reviewHistory) = BuildCardBootstrap(connection);

        return new Schema13BootstrapPlan(wordControls, reviewHistory, cardStates);
    }
}
