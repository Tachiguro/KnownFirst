using KnownFirst.Core.Learning;
using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data.Schema13;

public sealed class SenseLearningControlRepository
{
    private readonly IKnownFirstDatabase _database;

    public SenseLearningControlRepository(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<SenseLearningControl> LoadAsync(int senseId) =>
        _database.RunInTransactionAsync(conn => Load(conn, senseId));

    public Task SaveAsync(int senseId, SenseLearningControl control) =>
        _database.RunInTransactionAsync(conn =>
        {
            Save(conn, senseId, control);
            return true;
        });

    public static SenseLearningControl Load(SQLiteConnection connection, int senseId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (senseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senseId), senseId, "SenseId must be a positive integer.");
        }

        var row = connection.Find<SenseLearningControlEntity>(senseId);
        if (row is null)
        {
            return SenseLearningControl.Default;
        }

        var decidedAtUtc = Schema13TimestampCodec.ParseUtcDateTime(row.DecidedAtUtc);
        return new SenseLearningControl(new StopLearningDecision(decidedAtUtc));
    }

    public static void Save(SQLiteConnection connection, int senseId, SenseLearningControl control)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (senseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senseId), senseId, "SenseId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(control);

        if (!control.IsStopped)
        {
            connection.Execute("DELETE FROM SenseLearningControls WHERE SenseId = ?", senseId);
        }
        else
        {
            var decidedAtUtc = Schema13TimestampCodec.FormatUtc(control.StopLearning!.DecidedAtUtc);
            connection.Execute(
                "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?) " +
                "ON CONFLICT (SenseId) DO UPDATE SET DecidedAtUtc = excluded.DecidedAtUtc",
                senseId,
                decidedAtUtc);
        }
    }
}
