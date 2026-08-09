using KnownFirst.Data.Migrations.Schema9;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema10;

/// <summary>
/// Single source of truth for "what does a physically and logically valid Schema-10 shape look like".
/// Reuses <see cref="Schema9ShapeValidator"/> as the base layer (Schema 10 changes nothing Schema 9
/// already guarantees) and adds only the learning-workflow identity requirements: both <c>StableId</c>
/// columns exist, both carry a non-partial unique single-column index, and every persisted value is a
/// canonical lowercase 32- or 64-character hex identity.
///
/// <para>The value check is what makes the physical shape honest. A SQLite unique index permits any
/// number of NULLs, so the index alone would not notice a row inserted through a path that forgot to
/// supply an identity — which is exactly the failure mode Schema 10 must not have. Because every
/// capability resolver validates this shape before handing out a Schema-10 proof object, such a row makes
/// the database fail closed at the next capability check rather than silently propagating a null identity
/// into an archive or a merge.</para>
/// </summary>
internal static class Schema10ShapeValidator
{
    public static bool IsValidDatabase(SQLiteConnection connection, out string? failureDetail)
    {
        if (!Schema9ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        foreach (var (table, indexName) in TablesWithStableIdIndex)
        {
            if (!HasStableIdColumn(connection, table))
            {
                failureDetail = $"Table {table} is missing the required Schema-10 {Schema10Ddl.StableIdColumn} column.";
                return false;
            }

            if (!HasUniqueStableIdIndex(connection, table, indexName, out failureDetail))
            {
                return false;
            }

            var invalidCount = connection.ExecuteScalar<int>(Schema10Ddl.CountInvalidStableIds(table));
            if (invalidCount > 0)
            {
                failureDetail =
                    $"{invalidCount} {table} row(s) carry a missing or non-canonical {Schema10Ddl.StableIdColumn} " +
                    "(expected 32 or 64 lowercase hex characters).";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    public static bool HasStableIdColumn(SQLiteConnection connection, string table) =>
        connection.Query<TableInfoRow>($"PRAGMA table_info(\"{EscapeIdentifier(table)}\")")
            .Any(column => string.Equals(column.Name, Schema10Ddl.StableIdColumn, StringComparison.OrdinalIgnoreCase));

    private static bool HasUniqueStableIdIndex(
        SQLiteConnection connection, string table, string indexName, out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(table)}\")")
            .SingleOrDefault(item => string.Equals(item.Name, indexName, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            failureDetail = $"Required index {indexName} is missing from {table}.";
            return false;
        }

        if (index.Unique != 1)
        {
            failureDetail = $"Index {indexName} must be unique.";
            return false;
        }

        if (index.Partial != 0)
        {
            failureDetail = $"Index {indexName} must not be partial.";
            return false;
        }

        var columns = connection.Query<IndexXInfoRow>($"PRAGMA index_xinfo(\"{EscapeIdentifier(indexName)}\")")
            .Where(item => item.Key == 1)
            .OrderBy(item => item.SequenceNumber)
            .ToArray();
        if (columns.Length != 1
            || columns[0].ColumnId < 0
            || columns[0].Descending != 0
            || !string.Equals(columns[0].Collation, "BINARY", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(columns[0].Name, Schema10Ddl.StableIdColumn, StringComparison.OrdinalIgnoreCase))
        {
            failureDetail = $"Index {indexName} must be exactly ({Schema10Ddl.StableIdColumn}), BINARY, ascending.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static readonly (string Table, string IndexName)[] TablesWithStableIdIndex =
    [
        (Schema10Ddl.SessionTable, Schema10Ddl.SessionStableIdIndexName),
        (Schema10Ddl.QueueTable, Schema10Ddl.QueueStableIdIndexName)
    ];

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    private sealed class TableInfoRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
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
