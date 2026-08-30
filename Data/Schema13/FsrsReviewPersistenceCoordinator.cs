using KnownFirst.Core.Learning.Fsrs6;
using SQLite;

namespace KnownFirst.Data.Schema13;

public sealed class FsrsReviewPersistenceCoordinator
{
    private readonly IKnownFirstDatabase _database;

    public FsrsReviewPersistenceCoordinator(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<FsrsPersistedReviewHistoryEntry> PersistReviewAsync(
        int cardId,
        string stableId,
        Fsrs6ReviewEvent reviewEvent,
        Fsrs6Card resultingCard) =>
        _database.RunInTransactionAsync(conn => PersistReview(conn, cardId, stableId, reviewEvent, resultingCard));

    public static FsrsPersistedReviewHistoryEntry PersistReview(
        SQLiteConnection connection,
        int cardId,
        string stableId,
        Fsrs6ReviewEvent reviewEvent,
        Fsrs6Card resultingCard)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (cardId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cardId), cardId, "CardId must be a positive integer.");
        }
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException("StableId must be a non-empty string.", nameof(stableId));
        }
        if (!Enum.IsDefined(reviewEvent.Rating))
        {
            throw new ArgumentOutOfRangeException(nameof(reviewEvent), reviewEvent.Rating, "Undefined ReviewRating.");
        }
        if (reviewEvent.ReviewedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Review event timestamp must be in UTC (offset zero).", nameof(reviewEvent));
        }
        ArgumentNullException.ThrowIfNull(resultingCard);
        if (resultingCard.State == Fsrs6CardState.New)
        {
            throw new ArgumentException("Resulting card state cannot be New after a review.", nameof(resultingCard));
        }
        if (!resultingCard.LastReviewedAtUtc.HasValue)
        {
            throw new ArgumentException("Resulting card must have a LastReviewedAtUtc timestamp.", nameof(resultingCard));
        }
        if (resultingCard.LastReviewedAtUtc.Value != reviewEvent.ReviewedAtUtc)
        {
            throw new ArgumentException(
                $"Resulting card LastReviewedAtUtc ({resultingCard.LastReviewedAtUtc.Value:O}) does not match review event ReviewedAtUtc ({reviewEvent.ReviewedAtUtc:O}).",
                nameof(resultingCard));
        }

        var persistedEvent = FsrsReviewHistoryRepository.AppendEvent(
            connection, cardId, stableId, reviewEvent);
        FsrsCardStateRepository.Save(connection, cardId, resultingCard);
        return persistedEvent;
    }
}
