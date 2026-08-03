using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema9;

/// <summary>
/// Single source of truth for "what does a physically and logically valid Schema-9 shape look like".
/// Reuses <see cref="Schema8ShapeValidator"/> as the base physical/logical layer (Schema 9 changes only
/// the <c>ReviewSessions</c> index shape; every other table is byte-for-byte the same as Schema 8) and
/// adds only the Schema-9 <c>ReviewSessions</c> index requirements and the at-most-one-Active-session
/// invariant.
/// </summary>
internal static class Schema9ShapeValidator
{
    public static bool IsValidDatabase(SQLiteConnection connection, out string? failureDetail)
    {
        if (!Schema8ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        if (!HasNormalIndex(connection, out failureDetail))
        {
            return false;
        }

        if (!HasActiveIndex(connection, out failureDetail))
        {
            return false;
        }

        var strayLegacyIndexes = FindNonPartialUniqueSingleColumnIndexes(connection, "ReviewSessions", "DocumentId")
            .Where(name =>
                !string.Equals(name, Schema9Ddl.NormalIndexName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, Schema9Ddl.ActiveIndexName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (strayLegacyIndexes.Length > 0)
        {
            failureDetail = $"A legacy non-partial unique ReviewSessions(DocumentId) index still exists: {strayLegacyIndexes[0]}.";
            return false;
        }

        var duplicateActive = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT DocumentId FROM ReviewSessions WHERE Status = 0
                GROUP BY DocumentId HAVING COUNT(*) > 1
            )
            """);
        if (duplicateActive > 0)
        {
            failureDetail = "A DocumentId has more than one Active ReviewSession.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static bool HasNormalIndex(SQLiteConnection connection, out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>("PRAGMA index_list(\"ReviewSessions\")")
            .SingleOrDefault(item => string.Equals(item.Name, Schema9Ddl.NormalIndexName, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            failureDetail = $"Required index {Schema9Ddl.NormalIndexName} is missing from ReviewSessions.";
            return false;
        }

        if (index.Unique != 0)
        {
            failureDetail = $"Index {Schema9Ddl.NormalIndexName} must not be unique.";
            return false;
        }

        if (index.Partial != 0)
        {
            failureDetail = $"Index {Schema9Ddl.NormalIndexName} must not be partial.";
            return false;
        }

        if (!HasExactSingleColumn(connection, Schema9Ddl.NormalIndexName, "DocumentId"))
        {
            failureDetail = $"Index {Schema9Ddl.NormalIndexName} must be exactly (DocumentId), BINARY, ascending.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static bool HasActiveIndex(SQLiteConnection connection, out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>("PRAGMA index_list(\"ReviewSessions\")")
            .SingleOrDefault(item => string.Equals(item.Name, Schema9Ddl.ActiveIndexName, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            failureDetail = $"Required index {Schema9Ddl.ActiveIndexName} is missing from ReviewSessions.";
            return false;
        }

        if (index.Unique != 1)
        {
            failureDetail = $"Index {Schema9Ddl.ActiveIndexName} must be unique.";
            return false;
        }

        if (index.Partial != 1)
        {
            failureDetail = $"Index {Schema9Ddl.ActiveIndexName} must be partial.";
            return false;
        }

        if (!HasExactSingleColumn(connection, Schema9Ddl.ActiveIndexName, "DocumentId"))
        {
            failureDetail = $"Index {Schema9Ddl.ActiveIndexName} must be exactly (DocumentId), BINARY, ascending.";
            return false;
        }

        var createSql = connection.ExecuteScalar<string?>(
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = ?", Schema9Ddl.ActiveIndexName);
        var actualPredicate = ExtractNormalizedPredicate(createSql);
        var expectedPredicate = NormalizePredicate(Schema9Ddl.ActivePredicateSql);
        if (!string.Equals(actualPredicate, expectedPredicate, StringComparison.Ordinal))
        {
            failureDetail = $"Index {Schema9Ddl.ActiveIndexName} has the wrong partial-index predicate.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static bool HasExactSingleColumn(SQLiteConnection connection, string indexName, string columnName)
    {
        var columns = connection.Query<IndexXInfoRow>($"PRAGMA index_xinfo(\"{EscapeIdentifier(indexName)}\")")
            .Where(item => item.Key == 1)
            .OrderBy(item => item.SequenceNumber)
            .ToArray();

        return columns.Length == 1
            && columns[0].ColumnId >= 0
            && columns[0].Descending == 0
            && string.Equals(columns[0].Collation, "BINARY", StringComparison.OrdinalIgnoreCase)
            && string.Equals(columns[0].Name, columnName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Structurally finds every non-partial unique single-column index on <c>(table, column)</c> via
    /// <c>PRAGMA index_list</c>/<c>PRAGMA index_xinfo</c> — never by name. Shared by the migration (to
    /// discover the one legacy index it must replace) and this validator (to confirm none remain).
    /// </summary>
    public static string[] FindNonPartialUniqueSingleColumnIndexes(SQLiteConnection connection, string table, string column)
    {
        var matches = new List<string>();
        foreach (var index in connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(table)}\")"))
        {
            if (index.Unique != 1 || index.Partial != 0)
            {
                continue;
            }

            if (HasExactSingleColumn(connection, index.Name, column))
            {
                matches.Add(index.Name);
            }
        }

        return [.. matches];
    }

    private static string? ExtractNormalizedPredicate(string? createSql)
    {
        if (string.IsNullOrWhiteSpace(createSql))
        {
            return null;
        }

        var where = createSql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        return where < 0 ? null : NormalizePredicate(createSql[(where + "WHERE".Length)..]);
    }

    private static string NormalizePredicate(string predicate)
    {
        var normalized = new string(predicate
            .Where(character => !char.IsWhiteSpace(character)
                                && character is not '"' and not '`' and not '[' and not ']')
            .Select(char.ToLowerInvariant)
            .ToArray());

        while (HasRedundantOuterParentheses(normalized))
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    private static bool HasRedundantOuterParentheses(string value)
    {
        if (value.Length < 2 || value[0] != '(' || value[^1] != ')')
        {
            return false;
        }

        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            depth += value[index] == '(' ? 1 : value[index] == ')' ? -1 : 0;
            if (depth == 0 && index < value.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

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
