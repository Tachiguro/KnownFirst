using KnownFirst.Data.Entities;
using Microsoft.Extensions.Logging;
using SQLite;

namespace KnownFirst.Data;

public sealed class KnownFirstDatabase(ILogger<KnownFirstDatabase> logger) : IKnownFirstDatabase
{
    private const string DatabaseFileName = "knownfirst.db3";
    private static readonly SQLiteOpenFlags DatabaseFlags =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache;

    private readonly SemaphoreSlim _databaseGate = new(1, 1);
    private SQLiteAsyncConnection? _connection;
    private bool _initialized;

    public string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);

    public async Task InitializeAsync()
    {
        await _databaseGate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "KnownFirst database initialization failed.");
            throw;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _databaseGate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            return await operation(_connection!);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "KnownFirst database read failed.");
            throw;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    public async Task ResetAsync()
    {
        await _databaseGate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection = null;
            }

            _initialized = false;
            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }

            await EnsureInitializedAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "KnownFirst database reset failed.");
            throw;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        _connection ??= new SQLiteAsyncConnection(DatabasePath, DatabaseFlags);
        await _connection.CreateTableAsync<DocumentEntity>();
        await _connection.CreateTableAsync<WordEntity>();
        await _connection.CreateTableAsync<WordFormEntity>();
        await _connection.CreateTableAsync<WordOccurrenceEntity>();
        await _connection.CreateTableAsync<MeaningEntity>();
        await _connection.CreateTableAsync<ReviewStateEntity>();
        _initialized = true;
    }
}
