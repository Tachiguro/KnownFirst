using KnownFirst.Core.Learning;
using KnownFirst.Data;
using SQLite;

namespace KnownFirst.Tests;

internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

internal sealed class TemporaryKnownFirstDatabase : IKnownFirstDatabase, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public TemporaryKnownFirstDatabase(string prefix = "knownfirst-mvp")
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db3");
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync()
    {
        _connection ??= new SQLiteAsyncConnection(DatabasePath);
        await DatabaseSchema.InitializeAsync(_connection);
    }

    public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
    {
        await _gate.WaitAsync();
        try
        {
            await InitializeAsync();
            return await operation(_connection!);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
    {
        await _gate.WaitAsync();
        try
        {
            await InitializeAsync();
            T? result = default;
            await _connection!.RunInTransactionAsync(connection => result = operation(connection));
            return result!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync()
    {
        await DisposeConnectionAsync();
        File.Delete(DatabasePath);
        await InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeConnectionAsync();
        File.Delete(DatabasePath);
        _gate.Dispose();
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.CloseAsync();
        _connection = null;
    }
}
