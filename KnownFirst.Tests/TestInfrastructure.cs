using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
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

    public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
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

/// <summary>
/// A <see cref="TemporaryKnownFirstDatabase"/>-equivalent test fixture whose database is migrated to
/// Schema 8 (via the still-dormant <see cref="Schema8DormantMigration"/>) immediately after the ordinary,
/// unmodified <see cref="DatabaseSchema.InitializeAsync"/> creates it — i.e. a synthetic
/// already-migrated fixture, never a real application database, per the dormancy boundary
/// (KF-MEANING-001 Slice 3). Everything else about normal-use import/preparation flows works identically
/// because every table the ordinary Text-import/Preparation-selection pipeline touches
/// (Documents/Words/WordOccurrences/...) is untouched by the migration.
/// </summary>
internal sealed class TemporarySchema8Database : IKnownFirstDatabase, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Schema8MigrationOptions? _migrationOptions;
    private SQLiteAsyncConnection? _connection;
    private bool _migrated;

    public TemporarySchema8Database(string prefix = "knownfirst-schema8-prep", Schema8MigrationOptions? migrationOptions = null)
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db3");
        _migrationOptions = migrationOptions;
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync()
    {
        if (_migrated)
        {
            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            return;
        }

        _connection ??= new SQLiteAsyncConnection(DatabasePath);
        await DatabaseSchema.InitializeAsync(_connection);
        await Schema8DormantMigration.ApplyAsync(_connection, _migrationOptions);
        _migrated = true;
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

    public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
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
        _migrated = false;
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
