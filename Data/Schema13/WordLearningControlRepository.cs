using KnownFirst.Core.Learning;
using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data.Schema13;

public sealed class WordLearningControlRepository
{
    private readonly IKnownFirstDatabase _database;

    public WordLearningControlRepository(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<WordLearningControl> LoadAsync(int wordId) =>
        _database.RunInTransactionAsync(conn => Load(conn, wordId));

    public Task SaveAsync(int wordId, WordLearningControl control) =>
        _database.RunInTransactionAsync(conn =>
        {
            Save(conn, wordId, control);
            return true;
        });

    public static WordLearningControl Load(SQLiteConnection connection, int wordId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (wordId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wordId), wordId, "WordId must be a positive integer.");
        }

        var row = connection.Find<WordLearningControlEntity>(wordId);
        if (row is null)
        {
            return WordLearningControl.Default;
        }

        var decidedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DecidedAtUtc);
        return new WordLearningControl(new AlreadyKnownDecision(decidedAtUtc));
    }

    public static void Save(SQLiteConnection connection, int wordId, WordLearningControl control)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (wordId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wordId), wordId, "WordId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(control);

        if (!control.IsAlreadyKnown)
        {
            connection.Execute("DELETE FROM WordLearningControls WHERE WordId = ?", wordId);
        }
        else
        {
            var decidedAtUtc = Schema13TimestampCodec.FormatUtc(control.AlreadyKnown!.DecidedAtUtc);
            connection.Execute(
                "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?) " +
                "ON CONFLICT (WordId) DO UPDATE SET DecidedAtUtc = excluded.DecidedAtUtc",
                wordId,
                decidedAtUtc);
        }
    }
}
