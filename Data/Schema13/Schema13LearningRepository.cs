using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Data.Schema13;

/// <summary>
/// Policy-free runtime projection access for an explicitly created, validated Schema-13 database.
/// Scheduling state and due timestamps come only from <c>FsrsCardStates</c>; legacy scheduling columns
/// are deliberately absent from every selection predicate.
/// </summary>
public static class Schema13LearningRepository
{
    public static IReadOnlyList<Schema13LearningCardRow> LoadAllCards(SQLiteConnection connection)
    {
        EnsureRuntimeIntegrity(connection);
        return QueryCards(connection, null);
    }

    public static Schema13LearningCardRow? LoadCard(SQLiteConnection connection, int cardId)
    {
        EnsureRuntimeIntegrity(connection);
        return QueryCards(connection, cardId).SingleOrDefault();
    }

    public static int CountDueCards(SQLiteConnection connection, DateTimeOffset nowUtc)
    {
        EnsureRuntimeIntegrity(connection);
        return connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM LearningCards c
            JOIN FsrsCardStates f ON f.CardId = c.Id
            WHERE f.State IN (?, ?, ?)
              AND f.DueAtUtc IS NOT NULL
              AND f.DueAtUtc <= ?
            """,
            (int)Fsrs6CardState.Learning,
            (int)Fsrs6CardState.Review,
            (int)Fsrs6CardState.Relearning,
            Schema13TimestampCodec.FormatUtc(RequireUtc(nowUtc)));
    }

    public static int CountNewWords(SQLiteConnection connection)
    {
        EnsureRuntimeIntegrity(connection);
        return connection.ExecuteScalar<int>(
            """
            SELECT COUNT(DISTINCT c.WordId)
            FROM LearningCards c
            JOIN FsrsCardStates f ON f.CardId = c.Id
            WHERE f.State = ?
            """,
            (int)Fsrs6CardState.New);
    }

    public static DateTimeOffset? SelectNextDueAtUtc(SQLiteConnection connection)
    {
        EnsureRuntimeIntegrity(connection);
        var raw = connection.ExecuteScalar<string?>(
            """
            SELECT MIN(f.DueAtUtc)
            FROM LearningCards c
            JOIN FsrsCardStates f ON f.CardId = c.Id
            WHERE f.State IN (?, ?, ?)
              AND f.DueAtUtc IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM SenseAnswerVariantAssignments a
                  WHERE a.SenseId = c.SenseId
                    AND a.CardDirection = c.Direction
                    AND a.Requirement = ?
              )
            """,
            (int)Fsrs6CardState.Learning,
            (int)Fsrs6CardState.Review,
            (int)Fsrs6CardState.Relearning,
            (int)AnswerVariantRequirement.Required);
        return raw is null ? null : Schema13TimestampCodec.ParseUtcDateTimeOffset(raw);
    }

    /// <summary>
    /// Inserts the one allowed projection for a newly created card. This is intentionally a plain INSERT:
    /// duplicate creation is an integrity error and can never become an upsert.
    /// </summary>
    public static void InsertCleanNewState(SQLiteConnection connection, int cardId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (cardId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cardId));
        }

        connection.Execute(
            """
            INSERT INTO FsrsCardStates
                (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
            VALUES (?, ?, NULL, NULL, NULL, NULL, NULL)
            """,
            cardId,
            (int)Fsrs6CardState.New);
    }

    private static List<Schema13LearningCardRow> QueryCards(SQLiteConnection connection, int? cardId)
    {
        const string projection =
            "c.Id, c.WordId, c.SenseId, c.PreferredMeaningId, c.Direction, " +
            "f.State AS FsrsState, f.Stability, f.Difficulty, f.LastReviewedAtUtc AS FsrsLastReviewedAtUtc, " +
            "f.StepIndex, f.DueAtUtc AS FsrsDueAtUtc, c.CreatedAtUtc, c.UpdatedAtUtc";
        var probes = cardId.HasValue
            ? connection.Query<Schema13LearningCardProbe>(
                $"SELECT {projection} FROM LearningCards c JOIN FsrsCardStates f ON f.CardId = c.Id WHERE c.Id = ?",
                cardId.Value)
            : connection.Query<Schema13LearningCardProbe>(
                $"SELECT {projection} FROM LearningCards c JOIN FsrsCardStates f ON f.CardId = c.Id ORDER BY c.Id");

        return probes.Select(probe => new Schema13LearningCardRow(
            probe.Id,
            probe.WordId,
            probe.SenseId,
            probe.PreferredMeaningId,
            probe.Direction,
            probe.FsrsState,
            probe.Stability,
            probe.Difficulty,
            probe.FsrsLastReviewedAtUtc is null
                ? null
                : Schema13TimestampCodec.ParseUtcDateTimeOffset(probe.FsrsLastReviewedAtUtc),
            probe.StepIndex,
            probe.FsrsDueAtUtc is null
                ? null
                : Schema13TimestampCodec.ParseUtcDateTimeOffset(probe.FsrsDueAtUtc),
            probe.CreatedAtUtc,
            probe.UpdatedAtUtc)).ToList();
    }

    private static void EnsureRuntimeIntegrity(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Schema13RuntimeIntegrityValidator.Validate(connection, out var failureDetail))
        {
            throw new InvalidOperationException($"Schema-13 runtime integrity validation failed: {failureDetail}");
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value
        : throw new ArgumentException("Timestamp must use UTC offset zero.", nameof(value));

    private sealed class Schema13LearningCardProbe
    {
        public int Id { get; set; }
        public int WordId { get; set; }
        public int? SenseId { get; set; }
        public int PreferredMeaningId { get; set; }
        public CardDirection Direction { get; set; }
        public Fsrs6CardState FsrsState { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? FsrsLastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? FsrsDueAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}

public sealed record Schema13LearningCardRow(
    int Id,
    int WordId,
    int? SenseId,
    int PreferredMeaningId,
    CardDirection Direction,
    Fsrs6CardState State,
    double? Stability,
    double? Difficulty,
    DateTimeOffset? LastReviewedAtUtc,
    int? StepIndex,
    DateTimeOffset? DueAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
