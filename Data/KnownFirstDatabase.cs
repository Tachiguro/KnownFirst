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
        logger.LogDebug("KnownFirst database initialization was requested.");
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
            logger.LogTrace("KnownFirst database read operation started.");
            await EnsureInitializedAsync();
            var result = await operation(_connection!);
            logger.LogTrace("KnownFirst database read operation completed.");
            return result;
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

    public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _databaseGate.WaitAsync();
        try
        {
            logger.LogTrace("KnownFirst database transaction started.");
            await EnsureInitializedAsync();
            T? result = default;
            await _connection!.RunInTransactionAsync(connection => result = operation(connection));
            logger.LogTrace("KnownFirst database transaction committed.");
            return result!;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "KnownFirst database transaction failed.");
            throw;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    public async Task ResetAsync()
    {
        logger.LogInformation("KnownFirst database reset started.");
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
            logger.LogInformation("KnownFirst database reset completed.");
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
        await DatabaseSchema.InitializeAsync(_connection);
        _initialized = true;
        logger.LogInformation(
            "KnownFirst database opened and schema initialization completed. SchemaVersion = {SchemaVersion}",
            DatabaseSchema.CurrentVersion);
    }
}
