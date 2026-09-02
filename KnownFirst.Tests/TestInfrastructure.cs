using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Migrations.Schema10;
using KnownFirst.Data.Migrations.Schema11;
using KnownFirst.Data.Migrations.Schema12;
using KnownFirst.Data.Migrations.Schema13;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Explicit test-only construction of historical database fixtures. Production initialization must never
/// call this ladder; tests that characterize old schemas opt into it at the exact fixture boundary.
/// </summary>
internal static class HistoricalMigrationFixture
{
    public static Task UpgradeToSchema8Async(SQLiteAsyncConnection connection) =>
        Schema8DormantMigration.ApplyAsync(connection);

    public static async Task UpgradeToSchema9Async(SQLiteAsyncConnection connection)
    {
        if (await ReadVersionAsync(connection) < Schema8DormantMigration.TargetVersion)
        {
            await UpgradeToSchema8Async(connection);
        }

        await Schema9DormantMigration.ApplyAsync(connection);
    }

    public static async Task UpgradeToSchema10Async(SQLiteAsyncConnection connection)
    {
        if (await ReadVersionAsync(connection) < Schema9DormantMigration.TargetVersion)
        {
            await UpgradeToSchema9Async(connection);
        }

        await Schema10DormantMigration.ApplyAsync(connection);
    }

    public static async Task UpgradeToSchema11Async(SQLiteAsyncConnection connection)
    {
        if (await ReadVersionAsync(connection) < Schema10DormantMigration.TargetVersion)
        {
            await UpgradeToSchema10Async(connection);
        }

        await Schema11DormantMigration.ApplyAsync(connection);
    }

    public static async Task UpgradeToSchema12Async(SQLiteAsyncConnection connection)
    {
        if (await ReadVersionAsync(connection) < Schema11DormantMigration.TargetVersion)
        {
            await UpgradeToSchema11Async(connection);
        }

        await Schema12DormantMigration.ApplyAsync(connection);
    }

    public static async Task UpgradeToSchema13Async(SQLiteAsyncConnection connection)
    {
        if (await ReadVersionAsync(connection) < Schema12DormantMigration.TargetVersion)
        {
            await UpgradeToSchema12Async(connection);
        }

        await Schema13DormantMigration.ApplyAsync(connection);
    }

    private static Task<int> ReadVersionAsync(SQLiteAsyncConnection connection) =>
        connection.ExecuteScalarAsync<int>("PRAGMA user_version");
}

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
    private bool _initialized;

    public TemporaryKnownFirstDatabase(string prefix = "knownfirst-mvp")
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db3");
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync()
    {
        _connection ??= new SQLiteAsyncConnection(DatabasePath);

        // The Schema-7 fixture shape is built exactly once — rebuilding it on every gated operation would
        // be destructive for a populated fixture. `_initialized` is only set after a successful build, so a
        // failed initialization stays retryable.
        if (!_initialized)
        {
            await Schema7Fixture.InitializeEmptyAsync(_connection);
            _initialized = true;
        }

        await EnsureSupportedSchemaVersionAsync(_connection);
    }

    /// <summary>
    /// Re-applies the future-version gate <see cref="DatabaseSchema.InitializeAsync"/> enforces before it
    /// touches any table, using the production constant and the production exception type. Building the
    /// fixture once is what makes the fixture non-destructive, but every gated operation must still refuse a
    /// database whose <c>PRAGMA user_version</c> is newer than the supported schema — otherwise a test that
    /// writes a future version observes a downstream subsystem's error instead of the schema-compatibility
    /// contract the application actually guarantees.
    /// </summary>
    private static async Task EnsureSupportedSchemaVersionAsync(SQLiteAsyncConnection connection)
    {
        var existingVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        if (existingVersion > DatabaseSchema.CurrentVersion)
        {
            throw new DatabaseSchemaCompatibilityException(existingVersion, DatabaseSchema.CurrentVersion);
        }
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
        TemporaryDatabaseFiles.Delete(DatabasePath);
        _initialized = false;
        await InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisposeConnectionAsync();
            TemporaryDatabaseFiles.Delete(DatabasePath);
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        // Closes and removes this connection string's pooled entry, so the file is released before the
        // caller deletes it. Deliberately scoped — see TemporaryDatabaseFiles for why a global pool reset
        // must not be used here.
        await _connection.CloseAsync();
        _connection = null;
    }
}

/// <summary>
/// Shared teardown for every temporary test database.
/// <para>
/// Release is always <em>scoped to the owned connection</em>: <c>SQLiteAsyncConnection.CloseAsync</c> closes
/// and removes exactly that connection string's pooled entry, which is all a fixture needs before deleting
/// its own file. The process-wide <c>SQLiteAsyncConnection.ResetPool()</c> must never be used for ordinary
/// setup, reset, teardown, or migration-fixture flows: the assembly runs tests with
/// <c>ExecutionScope.MethodLevel</c> parallelization, so a global drain on one test thread closes native
/// handles that a concurrently running test is still using. That is a use-after-free, and it faults the whole
/// test host inside <c>sqlite3_changes</c> instead of failing cleanly. The behaviour was reproduced directly:
/// adding a single global drain to this teardown made <c>BackupCreationTests</c> crash as a class while every
/// one of its tests still passed in isolation.
/// </para>
/// </summary>
internal static class TemporaryDatabaseFiles
{
    /// <summary>
    /// Closes the supplied connection (releasing its pooled entry) and then deletes its exact files. The
    /// single entry point every fixture teardown should use.
    /// </summary>
    public static async Task CloseAndDeleteAsync(SQLiteAsyncConnection? connection, string databasePath)
    {
        if (connection is not null)
        {
            await connection.CloseAsync();
        }

        Delete(databasePath);
    }

    /// <summary>
    /// Removes the database together with its write-ahead-log and shared-memory sidecars — leaving those
    /// behind lets a recreated database at the same path observe stale state from the previous cycle. Only
    /// the fixture's own unique temporary path is touched. Failures are deliberately not swallowed: a delete
    /// that fails means a handle is still open, which is exactly the defect this helper exists to surface.
    /// </summary>
    public static void Delete(string databasePath)
    {
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}

/// <summary>
/// A <see cref="TemporaryKnownFirstDatabase"/>-equivalent Schema-8 fixture that can additionally inject
/// migration options for direct migration tests. Normal initialization already activates Schema 8.
/// Everything else about normal-use import/preparation flows works identically
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
        await Schema7Fixture.InitializeEmptyAsync(_connection);
        await Schema8DormantMigration.ApplyAsync(_connection, _migrationOptions);
        _migrated = true;
    }

    /// <summary>
    /// Test-only opt-in transition from this fixture's frozen Schema-8 baseline to historical Schema 12.
    /// Call this only at the exact point a legacy test begins exercising Schema-12 behavior (e.g.
    /// <c>TextReviewService</c>'s <c>DerivedTermEvidenceEntries</c>-dependent methods) — never implicitly
    /// from <see cref="InitializeAsync"/> itself, so tests that intentionally characterize the frozen
    /// Schema-8 shape, capability resolution, or lazy-upgrade behavior remain unaffected unless they opt
    /// in explicitly. Operates only on this fixture's own isolated temporary connection/file. Does not
    /// hide the resulting <c>PRAGMA user_version</c>: callers can read it back through <see cref="ReadAsync{T}"/>
    /// exactly as with any other operation.
    /// </summary>
    public async Task UpgradeToHistoricalSchema12Async()
    {
        await InitializeAsync();
        await Schema9DormantMigration.ApplyAsync(_connection!);
        await Schema10DormantMigration.ApplyAsync(_connection!);
        await Schema11DormantMigration.ApplyAsync(_connection!);
        await Schema12DormantMigration.ApplyAsync(_connection!);
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
        TemporaryDatabaseFiles.Delete(DatabasePath);
        _migrated = false;
        await InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisposeConnectionAsync();
            TemporaryDatabaseFiles.Delete(DatabasePath);
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        // Closes and removes this connection string's pooled entry, so the file is released before the
        // caller deletes it. Deliberately scoped — see TemporaryDatabaseFiles for why a global pool reset
        // must not be used here.
        await _connection.CloseAsync();
        _connection = null;
    }
}

/// <summary>
/// One exact <c>(table, column)</c> pair a snapshot comparison is explicitly allowed to ignore, declared by
/// the calling test. Used only for a column a deliberate additive migration introduces between the "before"
/// and "after" capture, so the comparison can still prove that every pre-existing column value, and the row
/// cardinality, are unchanged. Never a wildcard: any column not named here still alters the snapshot.
/// </summary>
internal sealed record SnapshotToleratedAdditiveColumn(string Table, string Column);

/// <summary>
/// Deterministic test-only capture of persistent SQLite schema metadata and application rows. The
/// helper deliberately introspects the supplied temporary database rather than assuming a valid
/// KnownFirst shape, so malformed-schema tests can prove exact zero mutation without silently
/// skipping a missing table or column.
/// </summary>
internal static class PersistentDatabaseSnapshot
{
    public static async Task<string[]> CaptureCompleteAsync(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(databasePath);
            return await CaptureCompleteAsync(connection);
        }
        finally
        {
            // Scoped close only — this snapshot helper runs inside parallel tests, so it must never drain
            // the process-wide pool (see TemporaryDatabaseFiles).
            if (connection is not null)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static async Task<string[]> CaptureCompleteAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        string[]? snapshot = null;
        await connection.RunInTransactionAsync(sqlite => snapshot = CaptureComplete(sqlite));
        return snapshot!;
    }

    public static async Task<string[]> CaptureTableRowsAsync(
        SQLiteAsyncConnection connection,
        params string[] tableNames)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tableNames);

        string[]? snapshot = null;
        await connection.RunInTransactionAsync(sqlite => snapshot = CaptureTableRows(sqlite, tableNames, []));
        return snapshot!;
    }

    /// <summary>
    /// Opt-in variant of <see cref="CaptureTableRowsAsync"/> for a comparison that spans a deliberate
    /// additive migration. Behaviour is identical except that the exact <c>(table, column)</c> pairs the
    /// caller declares in <paramref name="toleratedAdditiveColumns"/> are omitted from both the projection
    /// line and the row-value encoding — and only when the physical column actually exists, so the same
    /// tolerance set is valid before the column is added and after. Every other column keeps its original
    /// order and encoding, a missing requested table still fails exactly as before, and any column the
    /// caller did not name still changes the snapshot and fails the comparison.
    /// </summary>
    public static async Task<string[]> CaptureTableRowsIgnoringAdditiveColumnsAsync(
        SQLiteAsyncConnection connection,
        IReadOnlyCollection<SnapshotToleratedAdditiveColumn> toleratedAdditiveColumns,
        params string[] tableNames)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(toleratedAdditiveColumns);
        ArgumentNullException.ThrowIfNull(tableNames);

        string[]? snapshot = null;
        await connection.RunInTransactionAsync(sqlite =>
            snapshot = CaptureTableRows(sqlite, tableNames, toleratedAdditiveColumns));
        return snapshot!;
    }

    private static string[] CaptureComplete(SQLiteConnection connection)
    {
        var result = new List<string>
        {
            $"user_version|{connection.ExecuteScalar<int>("PRAGMA user_version")}",
        };
        var tables = connection.Query<SnapshotTableRow>(
            "SELECT name, COALESCE(sql, '') AS Sql FROM sqlite_master "
            + "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        foreach (var table in tables)
        {
            result.Add($"table|{Encode(table.Name)}|{Encode(table.Sql)}");
            var columns = GetColumns(connection, table.Name);
            result.AddRange(columns.Select(column =>
                $"column|{Encode(table.Name)}|{column.ColumnId}|{Encode(column.Name)}|{Encode(column.Type)}|"
                + $"{column.NotNull}|{Encode(column.DefaultValue)}|{column.PrimaryKey}"));
            AddRows(connection, table.Name, columns, result);
            AddIndexes(connection, table.Name, result);
        }

        return [.. result];
    }

    private static string[] CaptureTableRows(
        SQLiteConnection connection,
        IReadOnlyCollection<string> tableNames,
        IReadOnlyCollection<SnapshotToleratedAdditiveColumn> toleratedAdditiveColumns)
    {
        var requested = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        var existing = connection.Query<SnapshotTableRow>(
                "SELECT name, COALESCE(sql, '') AS Sql FROM sqlite_master "
                + "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
            .ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
        var missing = requested.Where(table => !existing.ContainsKey(table)).OrderBy(table => table).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException($"Snapshot table is missing: {string.Join(", ", missing)}.");
        }

        var result = new List<string>();
        foreach (var tableName in requested.OrderBy(table => table, StringComparer.Ordinal))
        {
            // Filtering (rather than substituting a placeholder) is what makes the same tolerance set valid
            // on both sides of the migration: before the column exists there is nothing to drop, and after
            // it exists it is dropped, so the two projections and row encodings line up exactly.
            var columns = GetColumns(connection, tableName)
                .Where(column => !IsTolerated(toleratedAdditiveColumns, tableName, column.Name))
                .ToArray();
            result.Add($"projection|{Encode(tableName)}|{string.Join(",", columns.Select(column => Encode(column.Name)))}");
            AddRows(connection, tableName, columns, result);
        }

        return [.. result];
    }

    private static bool IsTolerated(
        IReadOnlyCollection<SnapshotToleratedAdditiveColumn> toleratedAdditiveColumns,
        string tableName,
        string columnName) =>
        toleratedAdditiveColumns.Any(tolerated =>
            string.Equals(tolerated.Table, tableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(tolerated.Column, columnName, StringComparison.OrdinalIgnoreCase));

    private static SnapshotColumnRow[] GetColumns(SQLiteConnection connection, string tableName) =>
        connection.Query<SnapshotColumnRow>($"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\")")
            .OrderBy(column => column.ColumnId)
            .ToArray();

    private static void AddRows(
        SQLiteConnection connection,
        string tableName,
        IReadOnlyCollection<SnapshotColumnRow> columns,
        ICollection<string> result)
    {
        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Snapshot table '{tableName}' exposes no columns.");
        }

        // quote() distinguishes NULL, numeric, text, and blob storage classes. Hex-encoding the quoted
        // representation makes separators and control characters unambiguous. Sorting the serialized
        // values avoids depending on rowid or physical page order.
        var rowExpression = string.Join(
            " || ':' || ",
            columns.Select(column =>
                $"hex(CAST(quote(\"{EscapeIdentifier(column.Name)}\") AS BLOB))"));
        var rows = connection.Query<SnapshotValueRow>(
                $"SELECT {rowExpression} AS Value FROM \"{EscapeIdentifier(tableName)}\"")
            .Select(row => row.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        for (var ordinal = 0; ordinal < rows.Length; ordinal++)
        {
            result.Add($"row|{Encode(tableName)}|{ordinal}|{rows[ordinal]}");
        }
    }

    private static void AddIndexes(SQLiteConnection connection, string tableName, ICollection<string> result)
    {
        var indexes = connection.Query<SnapshotIndexListRow>(
                $"PRAGMA index_list(\"{EscapeIdentifier(tableName)}\")")
            .OrderBy(index => index.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var index in indexes)
        {
            var sql = connection.ExecuteScalar<string?>(
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = ?", index.Name);
            result.Add(
                $"index|{Encode(tableName)}|{Encode(index.Name)}|{index.Unique}|{Encode(index.Origin)}|"
                + $"{index.Partial}|{Encode(sql)}");
            var indexColumns = connection.Query<SnapshotIndexColumnRow>(
                    $"PRAGMA index_xinfo(\"{EscapeIdentifier(index.Name)}\")")
                .OrderBy(column => column.SequenceNumber);
            foreach (var column in indexColumns)
            {
                result.Add(
                    $"index-column|{Encode(index.Name)}|{column.SequenceNumber}|{column.ColumnId}|"
                    + $"{Encode(column.Name)}|{column.Descending}|{Encode(column.Collation)}|{column.Key}");
            }
        }
    }

    private static string Encode(string? value) =>
        value is null ? "-1:" : $"{value.Length}:{value}";

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    private sealed class SnapshotTableRow
    {
        public string Name { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }

    private sealed class SnapshotColumnRow
    {
        [Column("cid")]
        public int ColumnId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;

        [Column("notnull")]
        public int NotNull { get; set; }

        [Column("dflt_value")]
        public string? DefaultValue { get; set; }

        [Column("pk")]
        public int PrimaryKey { get; set; }
    }

    private sealed class SnapshotIndexListRow
    {
        public string Name { get; set; } = string.Empty;

        [Column("unique")]
        public int Unique { get; set; }

        [Column("origin")]
        public string Origin { get; set; } = string.Empty;

        [Column("partial")]
        public int Partial { get; set; }
    }

    private sealed class SnapshotIndexColumnRow
    {
        [Column("seqno")]
        public int SequenceNumber { get; set; }

        [Column("cid")]
        public int ColumnId { get; set; }

        public string? Name { get; set; }

        [Column("desc")]
        public int Descending { get; set; }

        [Column("coll")]
        public string? Collation { get; set; }

        [Column("key")]
        public int Key { get; set; }
    }

    private sealed class SnapshotValueRow
    {
        public string Value { get; set; } = string.Empty;
    }
}
