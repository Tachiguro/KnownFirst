namespace KnownFirst.Data.Targets;

using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema13;
using SQLite;

public sealed class TargetFsrsStateRepository
{
    private readonly IKnownFirstDatabase _database;

    public TargetFsrsStateRepository(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<Fsrs6Card?> LoadAsync(int targetId) =>
        _database.RunInTransactionAsync(conn => Load(conn, targetId));

    public Task SaveAsync(int targetId, Fsrs6Card card) =>
        _database.RunInTransactionAsync(conn =>
        {
            Save(conn, targetId, card);
            return true;
        });

    public static Fsrs6Card? Load(SQLiteConnection connection, int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }

        var row = connection.Find<TargetFsrsStateEntity>(targetId);
        if (row is null)
        {
            return null;
        }

        if (!Enum.IsDefined(row.State))
        {
            throw new InvalidOperationException($"Corrupt TargetFsrsState for TargetId {targetId}: invalid State value {(int)row.State}.");
        }

        DateTimeOffset? lastReviewedAtUtc = null;
        if (row.LastReviewedAtUtc is not null)
        {
            try
            {
                lastReviewedAtUtc = Schema13TimestampCodec.ParseUtcDateTimeOffset(row.LastReviewedAtUtc);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new InvalidOperationException($"Corrupt LastReviewedAtUtc '{row.LastReviewedAtUtc}' for TargetId {targetId}.", ex);
            }
        }

        DateTimeOffset? dueAtUtc = null;
        if (row.DueAtUtc is not null)
        {
            try
            {
                dueAtUtc = Schema13TimestampCodec.ParseUtcDateTimeOffset(row.DueAtUtc);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new InvalidOperationException($"Corrupt DueAtUtc '{row.DueAtUtc}' for TargetId {targetId}.", ex);
            }
        }

        try
        {
            return new Fsrs6Card(row.State, row.Stability, row.Difficulty, lastReviewedAtUtc, row.StepIndex, dueAtUtc);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException($"Corrupt TargetFsrsState domain invariants for TargetId {targetId}.", ex);
        }
    }

    public static void Save(SQLiteConnection connection, int targetId, Fsrs6Card card)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(card);

        string? lastReviewedStr = card.LastReviewedAtUtc.HasValue
            ? Schema13TimestampCodec.FormatUtc(card.LastReviewedAtUtc.Value)
            : null;

        string? dueStr = card.DueAtUtc.HasValue
            ? Schema13TimestampCodec.FormatUtc(card.DueAtUtc.Value)
            : null;

        connection.Execute("""
            INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT (TargetId) DO UPDATE SET
                State = excluded.State,
                Stability = excluded.Stability,
                Difficulty = excluded.Difficulty,
                LastReviewedAtUtc = excluded.LastReviewedAtUtc,
                StepIndex = excluded.StepIndex,
                DueAtUtc = excluded.DueAtUtc
            """,
            targetId,
            (int)card.State,
            card.Stability,
            card.Difficulty,
            lastReviewedStr,
            card.StepIndex,
            dueStr);
    }

    public static void InsertCleanNewState(SQLiteConnection connection, int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId));
        }

        connection.Execute(
            """
            INSERT INTO TargetFsrsStates
                (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
            VALUES (?, ?, NULL, NULL, NULL, NULL, NULL)
            ON CONFLICT (TargetId) DO NOTHING
            """,
            targetId,
            (int)Fsrs6CardState.New);
    }
}
