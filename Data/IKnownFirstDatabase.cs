using SQLite;

namespace KnownFirst.Data;

public interface IKnownFirstDatabase
{
    string DatabasePath { get; }

    Task InitializeAsync();

    Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation);

    Task ResetAsync();
}
