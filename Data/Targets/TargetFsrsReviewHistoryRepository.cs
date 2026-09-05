namespace KnownFirst.Data.Targets;

using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema13;
using SQLite;

public sealed record PersistedTargetFsrsReviewHistoryEntry(
    int Id,
    string StableId,
    int TargetId,
    int SequenceNumber,
    Fsrs6ReviewEvent ReviewEvent);

public sealed class TargetFsrsReviewHistoryRepository
{
    public static PersistedTargetFsrsReviewHistoryEntry AppendEvent(
        SQLiteConnection connection,
        int targetId,
        string stableId,
        Fsrs6ReviewEvent reviewEvent)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
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
            "SELECT SequenceNumber, ReviewedAtUtc FROM TargetFsrsReviewHistoryEntries WHERE TargetId = ? ORDER BY SequenceNumber DESC LIMIT 1",
            targetId).FirstOrDefault();

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
            INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
            VALUES (?, ?, ?, ?, ?)
            """,
            stableId,
            targetId,
            nextSequence,
            (int)reviewEvent.Rating,
            formattedUtc);

        var id = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
        return new PersistedTargetFsrsReviewHistoryEntry(id, stableId, targetId, nextSequence, reviewEvent);
    }

    public static IReadOnlyList<PersistedTargetFsrsReviewHistoryEntry> LoadHistory(
        SQLiteConnection connection,
        int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }

        var rows = connection.Query<TargetFsrsReviewHistoryEntryEntity>(
            "SELECT Id, StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc FROM TargetFsrsReviewHistoryEntries WHERE TargetId = ? ORDER BY SequenceNumber ASC",
            targetId);

        var result = new List<PersistedTargetFsrsReviewHistoryEntry>(rows.Count);
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
                    $"Corrupt history for TargetId {targetId}: expected SequenceNumber {expectedSeq}, found {row.SequenceNumber} at history entry Id {row.Id}.");
            }
            if (!Enum.IsDefined(row.Rating))
            {
                throw new InvalidOperationException($"Corrupt history entry Id {row.Id}: invalid Rating {(int)row.Rating}.");
            }

            DateTimeOffset eventTime = Schema13TimestampCodec.ParseUtcDateTimeOffset(row.ReviewedAtUtc);
            if (prevTime.HasValue && eventTime < prevTime.Value)
            {
                throw new InvalidOperationException(
                    $"Corrupt history for TargetId {targetId}: event SequenceNumber {row.SequenceNumber} has timestamp {eventTime:O} earlier than previous {prevTime.Value:O}.");
            }

            prevTime = eventTime;
            result.Add(new PersistedTargetFsrsReviewHistoryEntry(
                row.Id,
                row.StableId,
                row.TargetId,
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
