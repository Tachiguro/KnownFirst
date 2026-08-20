using KnownFirst.Data.Migrations.Schema10;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema11;

internal static class Schema11ShapeValidator
{
    public static bool IsValidDatabase(SQLiteConnection connection, out string? failureDetail)
    {
        if (!Schema10ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        if (!TableExists(connection, Schema11Ddl.TableName))
        {
            failureDetail = $"Table '{Schema11Ddl.TableName}' is missing.";
            return false;
        }

        if (!HasRequiredColumns(connection, out failureDetail))
        {
            return false;
        }

        if (!HasOwnerIndex(connection, out failureDetail))
        {
            return false;
        }

        if (!HasUniqueSemanticEvidenceIndex(connection, out failureDetail))
        {
            return false;
        }

        if (!Schema11EvidenceValidator.Validate(connection, out failureDetail))
        {
            return false;
        }

        failureDetail = null;
        return true;
    }

    public static bool TableExists(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;

    private static bool HasRequiredColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var columns = connection.Query<TableInfoRow>(
                $"PRAGMA table_info(\"{EscapeIdentifier(Schema11Ddl.TableName)}\")")
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var expectedColumns = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("ReviewCandidateId", "INTEGER", true, false),
            ("SourceIdentity", "TEXT", true, false),
            ("SourceSurfaceForm", "TEXT", true, false),
            ("SourceStartPosition", "INTEGER", true, false),
            ("SourceLength", "INTEGER", true, false),
            ("SourceSentenceOrder", "INTEGER", true, false),
            ("ComponentForm", "TEXT", true, false)
        };

        foreach (var (name, type, notNull, isPk) in expectedColumns)
        {
            if (!columns.TryGetValue(name, out var actual))
            {
                failureDetail = $"Table {Schema11Ddl.TableName} is missing required column '{name}'.";
                return false;
            }

            if (!string.Equals(actual.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Column {Schema11Ddl.TableName}.{name} must be of type {type} (found {actual.Type}).";
                return false;
            }

            if (notNull && actual.NotNull != 1)
            {
                failureDetail = $"Column {Schema11Ddl.TableName}.{name} must be NOT NULL.";
                return false;
            }

            if (isPk && actual.Pk != 1)
            {
                failureDetail = $"Column {Schema11Ddl.TableName}.{name} must be PRIMARY KEY.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool HasOwnerIndex(SQLiteConnection connection, out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(Schema11Ddl.TableName)}\")")
            .SingleOrDefault(item => string.Equals(item.Name, Schema11Ddl.OwnerIndexName, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            failureDetail = $"Required index {Schema11Ddl.OwnerIndexName} is missing from {Schema11Ddl.TableName}.";
            return false;
        }

        if (index.Unique != 0)
        {
            failureDetail = $"Index {Schema11Ddl.OwnerIndexName} must not be unique.";
            return false;
        }

        if (index.Partial != 0)
        {
            failureDetail = $"Index {Schema11Ddl.OwnerIndexName} must not be partial.";
            return false;
        }

        var columns = connection.Query<IndexXInfoRow>($"PRAGMA index_xinfo(\"{EscapeIdentifier(Schema11Ddl.OwnerIndexName)}\")")
            .Where(item => item.Key == 1)
            .OrderBy(item => item.SequenceNumber)
            .ToArray();
        if (columns.Length != 1
            || columns[0].ColumnId < 0
            || columns[0].Descending != 0
            || !string.Equals(columns[0].Collation, "BINARY", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(columns[0].Name, "ReviewCandidateId", StringComparison.OrdinalIgnoreCase))
        {
            failureDetail = $"Index {Schema11Ddl.OwnerIndexName} must be exactly (ReviewCandidateId), BINARY, ascending.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static bool HasUniqueSemanticEvidenceIndex(SQLiteConnection connection, out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(Schema11Ddl.TableName)}\")")
            .SingleOrDefault(item => string.Equals(item.Name, Schema11Ddl.UniqueEvidenceIndexName, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            failureDetail = $"Required index {Schema11Ddl.UniqueEvidenceIndexName} is missing from {Schema11Ddl.TableName}.";
            return false;
        }

        if (index.Unique != 1)
        {
            failureDetail = $"Index {Schema11Ddl.UniqueEvidenceIndexName} must be unique.";
            return false;
        }

        if (index.Partial != 0)
        {
            failureDetail = $"Index {Schema11Ddl.UniqueEvidenceIndexName} must not be partial.";
            return false;
        }

        var columns = connection.Query<IndexXInfoRow>($"PRAGMA index_xinfo(\"{EscapeIdentifier(Schema11Ddl.UniqueEvidenceIndexName)}\")")
            .Where(item => item.Key == 1)
            .OrderBy(item => item.SequenceNumber)
            .ToArray();

        var expectedColumns = new[] { "ReviewCandidateId", "SourceIdentity", "SourceStartPosition", "SourceLength", "ComponentForm" };
        if (columns.Length != expectedColumns.Length)
        {
            failureDetail = $"Index {Schema11Ddl.UniqueEvidenceIndexName} must contain exactly {expectedColumns.Length} columns.";
            return false;
        }

        for (var i = 0; i < expectedColumns.Length; i++)
        {
            if (columns[i].ColumnId < 0
                || columns[i].Descending != 0
                || !string.Equals(columns[i].Collation, "BINARY", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(columns[i].Name, expectedColumns[i], StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Index {Schema11Ddl.UniqueEvidenceIndexName} column at sequence {i} must be exactly '{expectedColumns[i]}', BINARY, ascending.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    private sealed class TableInfoRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("type")]
        public string Type { get; set; } = string.Empty;

        [Column("notnull")]
        public int NotNull { get; set; }

        [Column("pk")]
        public int Pk { get; set; }
    }

    private sealed class IndexListRow
    {
        public string Name { get; set; } = string.Empty;

        [Column("unique")]
        public int Unique { get; set; }

        [Column("partial")]
        public int Partial { get; set; }
    }

    private sealed class IndexXInfoRow
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
}
