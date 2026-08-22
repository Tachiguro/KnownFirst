using KnownFirst.Data.Migrations.Schema11;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema12;

internal static class Schema12ShapeValidator
{
    public static bool IsValidDatabase(SQLiteConnection connection, out string? failureDetail)
    {
        if (!Schema11ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        if (!TableExists(connection, Schema12Ddl.StateTableName))
        {
            failureDetail = $"Table '{Schema12Ddl.StateTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, Schema12Ddl.GrantsTableName))
        {
            failureDetail = $"Table '{Schema12Ddl.GrantsTableName}' is missing.";
            return false;
        }

        if (!HasStateRequiredColumns(connection, out failureDetail))
        {
            return false;
        }

        if (!HasGrantsRequiredColumns(connection, out failureDetail))
        {
            return false;
        }

        if (!HasGrantsDayOrdinalIndex(connection, out failureDetail))
        {
            return false;
        }

        failureDetail = null;
        return true;
    }

    public static bool TableExists(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;

    private static bool HasStateRequiredColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var columns = connection.Query<TableInfoRow>(
                $"PRAGMA table_info(\"{EscapeIdentifier(Schema12Ddl.StateTableName)}\")")
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var expectedColumns = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("Phase", "INTEGER", true, false),
            ("DayOrdinal", "INTEGER", true, false),
            ("ActiveDayStartUtc", "TEXT", true, false),
            ("ActiveDayEndUtc", "TEXT", true, false),
            ("FrozenTimeZoneId", "TEXT", true, false),
            ("FrozenCutoffMinutes", "INTEGER", true, false),
            ("BridgeStartedUtc", "TEXT", false, false),
            ("BridgeTargetTimeZoneId", "TEXT", false, false),
            ("BridgeTargetCutoffMinutes", "INTEGER", false, false),
            ("BridgeTargetUtc", "TEXT", false, false),
            ("UpdatedAtUtc", "TEXT", true, false)
        };

        foreach (var (name, type, notNull, isPk) in expectedColumns)
        {
            if (!columns.TryGetValue(name, out var actual))
            {
                failureDetail = $"Table {Schema12Ddl.StateTableName} is missing required column '{name}'.";
                return false;
            }

            if (!string.Equals(actual.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Column {Schema12Ddl.StateTableName}.{name} must be of type {type} (found {actual.Type}).";
                return false;
            }

            if (notNull && actual.NotNull != 1)
            {
                failureDetail = $"Column {Schema12Ddl.StateTableName}.{name} must be NOT NULL.";
                return false;
            }

            if (isPk && actual.Pk != 1)
            {
                failureDetail = $"Column {Schema12Ddl.StateTableName}.{name} must be PRIMARY KEY.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool HasGrantsRequiredColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var columns = connection.Query<TableInfoRow>(
                $"PRAGMA table_info(\"{EscapeIdentifier(Schema12Ddl.GrantsTableName)}\")")
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var expectedColumns = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("DayOrdinal", "INTEGER", true, false),
            ("WordId", "INTEGER", true, false),
            ("SlotOrdinal", "INTEGER", true, false),
            ("GrantedAtUtc", "TEXT", true, false)
        };

        foreach (var (name, type, notNull, isPk) in expectedColumns)
        {
            if (!columns.TryGetValue(name, out var actual))
            {
                failureDetail = $"Table {Schema12Ddl.GrantsTableName} is missing required column '{name}'.";
                return false;
            }

            if (!string.Equals(actual.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Column {Schema12Ddl.GrantsTableName}.{name} must be of type {type} (found {actual.Type}).";
                return false;
            }

            if (notNull && actual.NotNull != 1)
            {
                failureDetail = $"Column {Schema12Ddl.GrantsTableName}.{name} must be NOT NULL.";
                return false;
            }

            if (isPk && actual.Pk != 1)
            {
                failureDetail = $"Column {Schema12Ddl.GrantsTableName}.{name} must be PRIMARY KEY.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool HasGrantsDayOrdinalIndex(SQLiteConnection connection, out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(Schema12Ddl.GrantsTableName)}\")")
            .FirstOrDefault(i => string.Equals(i.Name, Schema12Ddl.GrantsDayOrdinalIndexName, StringComparison.OrdinalIgnoreCase));

        if (index is null)
        {
            failureDetail = $"Index '{Schema12Ddl.GrantsDayOrdinalIndexName}' is missing on table {Schema12Ddl.GrantsTableName}.";
            return false;
        }

        var indexColumns = connection.Query<IndexInfoRow>(
                $"PRAGMA index_info(\"{EscapeIdentifier(Schema12Ddl.GrantsDayOrdinalIndexName)}\")")
            .OrderBy(c => c.Seqno)
            .Select(c => c.Name)
            .ToArray();

        if (indexColumns.Length != 1 || !string.Equals(indexColumns[0], "DayOrdinal", StringComparison.OrdinalIgnoreCase))
        {
            failureDetail = $"Index '{Schema12Ddl.GrantsDayOrdinalIndexName}' must be on column DayOrdinal.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    private sealed class TableInfoRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNull { get; set; }
        public string? Dflt_value { get; set; }
        public int Pk { get; set; }
    }

    private sealed class IndexListRow
    {
        public int Seq { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Unique { get; set; }
        public string Origin { get; set; } = string.Empty;
        public int Partial { get; set; }
    }

    private sealed class IndexInfoRow
    {
        public int Seqno { get; set; }
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
