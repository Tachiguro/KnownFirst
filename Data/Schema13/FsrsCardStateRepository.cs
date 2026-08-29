using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data.Schema13;

public sealed class FsrsCardStateRepository
{
    private readonly IKnownFirstDatabase _database;

    public FsrsCardStateRepository(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<Fsrs6Card?> LoadAsync(int cardId) =>
        _database.RunInTransactionAsync(conn => Load(conn, cardId));

    public Task SaveAsync(int cardId, Fsrs6Card card) =>
        _database.RunInTransactionAsync(conn =>
        {
            Save(conn, cardId, card);
            return true;
        });

    public static Fsrs6Card? Load(SQLiteConnection connection, int cardId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (cardId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cardId), cardId, "CardId must be a positive integer.");
        }

        var row = connection.Find<FsrsCardStateEntity>(cardId);
        if (row is null)
        {
            return null;
        }

        if (!Enum.IsDefined(row.State))
        {
            throw new InvalidOperationException($"Corrupt FsrsCardState for CardId {cardId}: invalid State value {(int)row.State}.");
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
                throw new InvalidOperationException($"Corrupt LastReviewedAtUtc '{row.LastReviewedAtUtc}' for CardId {cardId}.", ex);
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
                throw new InvalidOperationException($"Corrupt DueAtUtc '{row.DueAtUtc}' for CardId {cardId}.", ex);
            }
        }

        try
        {
            return new Fsrs6Card(row.State, row.Stability, row.Difficulty, lastReviewedAtUtc, row.StepIndex, dueAtUtc);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException($"Corrupt FsrsCardState domain invariants for CardId {cardId}.", ex);
        }
    }

    public static void Save(SQLiteConnection connection, int cardId, Fsrs6Card card)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (cardId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cardId), cardId, "CardId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(card);

        string? lastReviewedStr = card.LastReviewedAtUtc.HasValue
            ? Schema13TimestampCodec.FormatUtc(card.LastReviewedAtUtc.Value)
            : null;

        string? dueStr = card.DueAtUtc.HasValue
            ? Schema13TimestampCodec.FormatUtc(card.DueAtUtc.Value)
            : null;

        connection.Execute("""
            INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT (CardId) DO UPDATE SET
                State = excluded.State,
                Stability = excluded.Stability,
                Difficulty = excluded.Difficulty,
                LastReviewedAtUtc = excluded.LastReviewedAtUtc,
                StepIndex = excluded.StepIndex,
                DueAtUtc = excluded.DueAtUtc
            """,
            cardId,
            (int)card.State,
            card.Stability,
            card.Difficulty,
            lastReviewedStr,
            card.StepIndex,
            dueStr);
    }
}
