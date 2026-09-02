using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data.Schema13;

public sealed class FsrsReviewHistoryRepository
{
    private readonly IKnownFirstDatabase _database;

    public FsrsReviewHistoryRepository(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<FsrsPersistedReviewHistoryEntry> AppendEventAsync(int cardId, string stableId, Fsrs6ReviewEvent reviewEvent) =>
        _database.RunInTransactionAsync(conn => AppendEvent(conn, cardId, stableId, reviewEvent));

    public Task<IReadOnlyList<FsrsPersistedReviewHistoryEntry>> LoadHistoryAsync(int cardId) =>
        _database.RunInTransactionAsync(conn => LoadHistory(conn, cardId));

    public static FsrsPersistedReviewHistoryEntry AppendEvent(
        SQLiteConnection connection,
        int cardId,
        string stableId,
        Fsrs6ReviewEvent reviewEvent)
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

        var latest = connection.Query<HistoryTailRow>(
            "SELECT SequenceNumber, ReviewedAtUtc FROM FsrsReviewHistoryEntries WHERE CardId = ? ORDER BY SequenceNumber DESC LIMIT 1",
            cardId).FirstOrDefault();

        int nextSequence;
        if (latest is not null)
        {
            DateTimeOffset prevTime = Schema13TimestampCodec.ParseUtcDateTimeOffset(latest.ReviewedAtUtc);
            if (reviewEvent.ReviewedAtUtc < prevTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reviewEvent),
                    reviewEvent.ReviewedAtUtc,
                    $"New review event timestamp {reviewEvent.ReviewedAtUtc:O} cannot be earlier than previous event timestamp {prevTime:O}.");
            }
            nextSequence = latest.SequenceNumber + 1;
        }
        else
        {
            nextSequence = 1;
        }

        string formattedUtc = Schema13TimestampCodec.FormatUtc(reviewEvent.ReviewedAtUtc);
        connection.Execute("""
            INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
            VALUES (?, ?, ?, ?, ?)
            """,
            stableId,
            cardId,
            nextSequence,
            (int)reviewEvent.Rating,
            formattedUtc);

        var id = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
        return new FsrsPersistedReviewHistoryEntry(id, stableId, cardId, nextSequence, reviewEvent);
    }

    public static IReadOnlyList<FsrsPersistedReviewHistoryEntry> LoadHistory(
        SQLiteConnection connection,
        int cardId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (cardId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cardId), cardId, "CardId must be a positive integer.");
        }

        var rows = connection.Query<FsrsReviewHistoryEntryEntity>(
            "SELECT Id, StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries WHERE CardId = ? ORDER BY SequenceNumber ASC",
            cardId);

        var result = new List<FsrsPersistedReviewHistoryEntry>(rows.Count);
        DateTimeOffset? prevTime = null;
        int expectedSeq = 1;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.StableId))
            {
                throw new InvalidOperationException($"Corrupt history entry Id {row.Id}: empty StableId.");
            }
            if (row.SequenceNumber <= 0)
            {
                throw new InvalidOperationException($"Corrupt history entry Id {row.Id}: non-positive SequenceNumber {row.SequenceNumber}.");
            }
            if (row.SequenceNumber != expectedSeq)
            {
                throw new InvalidOperationException(
                    $"Corrupt history for CardId {cardId}: expected SequenceNumber {expectedSeq}, found {row.SequenceNumber} at history entry Id {row.Id}.");
            }
            if (!Enum.IsDefined(row.Rating))
            {
                throw new InvalidOperationException($"Corrupt history entry Id {row.Id}: invalid Rating {(int)row.Rating}.");
            }

            DateTimeOffset eventTime;
            try
            {
                eventTime = Schema13TimestampCodec.ParseUtcDateTimeOffset(row.ReviewedAtUtc);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new InvalidOperationException($"Corrupt history entry Id {row.Id}: invalid ReviewedAtUtc '{row.ReviewedAtUtc}'.", ex);
            }

            if (prevTime.HasValue && eventTime < prevTime.Value)
            {
                throw new InvalidOperationException(
                    $"Corrupt history for CardId {cardId}: event SequenceNumber {row.SequenceNumber} has timestamp {eventTime:O} earlier than previous {prevTime.Value:O}.");
            }

            prevTime = eventTime;
            result.Add(new FsrsPersistedReviewHistoryEntry(
                row.Id,
                row.StableId,
                row.CardId,
                row.SequenceNumber,
                new Fsrs6ReviewEvent(eventTime, row.Rating)));
            expectedSeq++;
        }

        return result;
    }

    private sealed class HistoryTailRow
    {
        public int SequenceNumber { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }
}
