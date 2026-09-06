namespace KnownFirst.Data.Targets;

using KnownFirst.Core.Learning;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema13;
using SQLite;

public sealed record PersistedTargetReview(
    int Id,
    string StableId,
    int TargetId,
    int SessionId,
    ReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    bool IsSessionRepeat,
    int? TargetAnswerVariantId,
    int? MatchedAnswerVariantId,
    DateTimeOffset ReviewedAtUtc,
    DateTimeOffset DueAtUtc);

public static class TargetReviewRepository
{
    public static int InsertReview(
        SQLiteConnection connection,
        string stableId,
        int targetId,
        int sessionId,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        bool isSessionRepeat,
        int? targetAnswerVariantId,
        int? matchedAnswerVariantId,
        DateTimeOffset reviewedAtUtc,
        DateTimeOffset dueAtUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException("StableId must not be empty.", nameof(stableId));
        }
        if (targetId <= 0) throw new ArgumentOutOfRangeException(nameof(targetId));
        if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));

        var reviewedStr = Schema13TimestampCodec.FormatUtc(reviewedAtUtc);
        var dueStr = Schema13TimestampCodec.FormatUtc(dueAtUtc);

        connection.Execute(
            """
            INSERT INTO TargetReviews
                (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                 TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId,
            targetId,
            sessionId,
            (int)rating,
            wasTypedAnswer ? 1 : 0,
            wasCorrect ? 1 : 0,
            isSessionRepeat ? 1 : 0,
            targetAnswerVariantId,
            matchedAnswerVariantId,
            reviewedStr,
            dueStr);

        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    public static List<TargetInteractionEvent> LoadInteractionEventsForTarget(
        SQLiteConnection connection,
        int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var rows = connection.Query<TargetReviewEntity>(
            """
            SELECT Id, StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                   TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc
            FROM TargetReviews
            WHERE TargetId = ?
            ORDER BY ReviewedAtUtc ASC, Id ASC
            """,
            targetId);

        return rows.Select(r => new TargetInteractionEvent(
            Schema13TimestampCodec.ParseUtcDateTimeOffset(r.ReviewedAtUtc),
            r.Rating,
            r.WasTypedAnswer,
            r.WasCorrect,
            r.IsSessionRepeat)).ToList();
    }

    public static int CountReviewsWithRating(SQLiteConnection connection, int sessionId, ReviewRating rating)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM TargetReviews WHERE SessionId = ? AND Rating = ?", sessionId, (int)rating);
    }

    public static int CountReviewsForSession(SQLiteConnection connection, int sessionId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetReviews WHERE SessionId = ?", sessionId);
    }
}
