using SQLite;

namespace KnownFirst.Tests;

/// <summary>Shared low-level SQLite assertions used across the KF-MEANING-001 Slice 1 migration tests.</summary>
internal static class Schema8MigrationAssertHelpers
{
    public static Task<int> GetUserVersionAsync(SQLiteAsyncConnection connection) =>
        connection.ExecuteScalarAsync<int>("PRAGMA user_version");

    public static async Task<bool> TableExistsAsync(SQLiteAsyncConnection connection, string table) =>
        await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;

    public static async Task<bool> IndexExistsAsync(SQLiteAsyncConnection connection, string index) =>
        await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", index) > 0;

    public static async Task<bool> ColumnExistsAsync(SQLiteAsyncConnection connection, string table, string column)
    {
        var rows = await connection.QueryAsync<ColumnInfoRow>($"PRAGMA table_info({table})");
        return rows.Any(r => string.Equals(r.Name, column, StringComparison.OrdinalIgnoreCase));
    }

    public static Task<int> CountAsync(SQLiteAsyncConnection connection, string sql, params object[] args) =>
        connection.ExecuteScalarAsync<int>(sql, args);

    public static async Task<List<string>> GetTableNamesAsync(SQLiteAsyncConnection connection)
    {
        var rows = await connection.QueryAsync<NameRow>(
            "SELECT name AS Name FROM sqlite_master WHERE type = 'table' ORDER BY name");
        return rows.Select(r => r.Name).ToList();
    }

    public static async Task<List<string>> GetIndexNamesAsync(SQLiteAsyncConnection connection)
    {
        var rows = await connection.QueryAsync<NameRow>(
            "SELECT name AS Name FROM sqlite_master WHERE type = 'index' ORDER BY name");
        return rows.Select(r => r.Name).ToList();
    }

    private sealed class ColumnInfoRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NameRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
