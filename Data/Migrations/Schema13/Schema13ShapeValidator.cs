using KnownFirst.Data.Migrations.Schema12;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema13;

internal static class Schema13ShapeValidator
{
    public static bool IsValidDatabase(SQLiteConnection connection, out string? failureDetail)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Schema12ShapeValidator.IsValidDatabase(connection, out failureDetail))
        {
            return false;
        }

        if (!TableExists(connection, Schema13Ddl.FsrsCardStatesTableName))
        {
            failureDetail = $"Table '{Schema13Ddl.FsrsCardStatesTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, Schema13Ddl.FsrsReviewHistoryEntriesTableName))
        {
            failureDetail = $"Table '{Schema13Ddl.FsrsReviewHistoryEntriesTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, Schema13Ddl.WordLearningControlsTableName))
        {
            failureDetail = $"Table '{Schema13Ddl.WordLearningControlsTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, Schema13Ddl.SenseLearningControlsTableName))
        {
            failureDetail = $"Table '{Schema13Ddl.SenseLearningControlsTableName}' is missing.";
            return false;
        }

        if (!HasFsrsCardStatesColumns(connection, out failureDetail)
            || !HasFsrsReviewHistoryColumns(connection, out failureDetail)
            || !HasWordLearningControlsColumns(connection, out failureDetail)
            || !HasSenseLearningControlsColumns(connection, out failureDetail))
        {
            return false;
        }

        if (!HasForeignKeys(connection, out failureDetail))
        {
            return false;
        }

        if (!HasRequiredIndexes(connection, out failureDetail))
        {
            return false;
        }

        if (!HasRequiredConstraints(connection, out failureDetail))
        {
            return false;
        }

        failureDetail = null;
        return true;
    }

    public static bool TableExists(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;

    private static bool HasFsrsCardStatesColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("CardId", "INTEGER", false, true),
            ("State", "INTEGER", true, false),
            ("Stability", "REAL", false, false),
            ("Difficulty", "REAL", false, false),
            ("LastReviewedAtUtc", "TEXT", false, false),
            ("StepIndex", "INTEGER", false, false),
            ("DueAtUtc", "TEXT", false, false)
        };

        return ValidateColumns(connection, Schema13Ddl.FsrsCardStatesTableName, expected, out failureDetail);
    }

    private static bool HasFsrsReviewHistoryColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("StableId", "TEXT", true, false),
            ("CardId", "INTEGER", true, false),
            ("SequenceNumber", "INTEGER", true, false),
            ("Rating", "INTEGER", true, false),
            ("ReviewedAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, Schema13Ddl.FsrsReviewHistoryEntriesTableName, expected, out failureDetail);
    }

    private static bool HasWordLearningControlsColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("WordId", "INTEGER", false, true),
            ("DecidedAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, Schema13Ddl.WordLearningControlsTableName, expected, out failureDetail);
    }

    private static bool HasSenseLearningControlsColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("SenseId", "INTEGER", false, true),
            ("DecidedAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, Schema13Ddl.SenseLearningControlsTableName, expected, out failureDetail);
    }

    private static bool ValidateColumns(
        SQLiteConnection connection,
        string tableName,
        (string Name, string Type, bool NotNull, bool IsPk)[] expectedColumns,
        out string? failureDetail)
    {
        var columns = connection.Query<TableInfoRow>(
                $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\")")
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type, notNull, isPk) in expectedColumns)
        {
            if (!columns.TryGetValue(name, out var actual))
            {
                failureDetail = $"Table {tableName} is missing required column '{name}'.";
                return false;
            }

            if (!string.Equals(actual.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Column {tableName}.{name} must be of type {type} (found {actual.Type}).";
                return false;
            }

            if (notNull && actual.NotNull != 1)
            {
                failureDetail = $"Column {tableName}.{name} must be NOT NULL.";
                return false;
            }

            if (isPk && actual.Pk < 1)
            {
                failureDetail = $"Column {tableName}.{name} must be PRIMARY KEY.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool HasForeignKeys(SQLiteConnection connection, out string? failureDetail)
    {
        var checks = new (string Table, string Parent, string From, string To)[]
        {
            (Schema13Ddl.FsrsCardStatesTableName, "LearningCards", "CardId", "Id"),
            (Schema13Ddl.FsrsReviewHistoryEntriesTableName, "LearningCards", "CardId", "Id"),
            (Schema13Ddl.WordLearningControlsTableName, "Words", "WordId", "Id"),
            (Schema13Ddl.SenseLearningControlsTableName, "Senses", "SenseId", "Id")
        };

        foreach (var (table, parent, from, to) in checks)
        {
            var fks = connection.Query<ForeignKeyPragmaRow>($"PRAGMA foreign_key_list(\"{EscapeIdentifier(table)}\")");
            var match = fks.FirstOrDefault(f =>
                string.Equals(f.Table, parent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.From, from, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.To, to, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                failureDetail = $"Table '{table}' is missing required foreign key '{from}' -> '{parent}({to})'.";
                return false;
            }

            if (!string.Equals(match.On_delete, "CASCADE", StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Foreign key '{table}.{from}' -> '{parent}({to})' must declare ON DELETE CASCADE.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool HasRequiredIndexes(SQLiteConnection connection, out string? failureDetail)
    {
        var requiredIndexes = new (string Table, string IndexName, bool Unique, string[] Columns)[]
        {
            (Schema13Ddl.FsrsCardStatesTableName, Schema13Ddl.FsrsCardStatesDueIndexName, false, ["State", "DueAtUtc"]),
            (Schema13Ddl.FsrsReviewHistoryEntriesTableName, Schema13Ddl.FsrsReviewHistoryEntriesStableIdIndexName, true, ["StableId"]),
            (Schema13Ddl.FsrsReviewHistoryEntriesTableName, Schema13Ddl.FsrsReviewHistoryEntriesCardSequenceIndexName, true, ["CardId", "SequenceNumber"]),
            (Schema13Ddl.FsrsReviewHistoryEntriesTableName, Schema13Ddl.FsrsReviewHistoryEntriesReplayIndexName, false, ["CardId", "ReviewedAtUtc", "SequenceNumber"])
        };

        foreach (var (table, indexName, unique, columns) in requiredIndexes)
        {
            var index = connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(table)}\")")
                .FirstOrDefault(i => string.Equals(i.Name, indexName, StringComparison.OrdinalIgnoreCase));

            if (index is null)
            {
                failureDetail = $"Required index '{indexName}' is missing on table '{table}'.";
                return false;
            }

            if (unique && index.Unique != 1)
            {
                failureDetail = $"Index '{indexName}' on table '{table}' must be UNIQUE.";
                return false;
            }

            var indexColumns = connection.Query<IndexInfoRow>($"PRAGMA index_info(\"{EscapeIdentifier(indexName)}\")")
                .OrderBy(c => c.Seqno)
                .Select(c => c.Name)
                .ToArray();

            if (indexColumns.Length != columns.Length)
            {
                failureDetail = $"Index '{indexName}' column count mismatch: expected {columns.Length}, found {indexColumns.Length}.";
                return false;
            }

            for (var i = 0; i < columns.Length; i++)
            {
                if (!string.Equals(indexColumns[i], columns[i], StringComparison.OrdinalIgnoreCase))
                {
                    failureDetail = $"Index '{indexName}' column at position {i} must be '{columns[i]}' (found '{indexColumns[i]}').";
                    return false;
                }
            }
        }

        failureDetail = null;
        return true;
    }

    private static bool HasRequiredConstraints(SQLiteConnection connection, out string? failureDetail)
    {
        var tables = new[]
        {
            Schema13Ddl.FsrsCardStatesTableName,
            Schema13Ddl.FsrsReviewHistoryEntriesTableName,
            Schema13Ddl.WordLearningControlsTableName,
            Schema13Ddl.SenseLearningControlsTableName
        };

        var ddlByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var sql = connection.ExecuteScalar<string?>(
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?", table);

            if (string.IsNullOrWhiteSpace(sql))
            {
                failureDetail = $"Failed to retrieve table DDL for '{table}'.";
                return false;
            }

            ddlByTable[table] = NormalizeSql(sql);
        }

        // 1. FsrsCardStates semantic checks
        var cardStatesSql = ddlByTable[Schema13Ddl.FsrsCardStatesTableName];
        var cardStatesChecks = new (string Fragment, string Description)[]
        {
            ("STATEIN(0,1,2,3)", "state enumeration 0..3"),
            ("STABILITY>=0.001", "minimum stability >= 0.001"),
            ("DIFFICULTY>=1.0", "minimum difficulty >= 1.0"),
            ("DIFFICULTY<=10.0", "maximum difficulty <= 10.0"),
            ("STATE=0ANDSTABILITYISNULLANDDIFFICULTYISNULLANDLASTREVIEWEDATUTCISNULLANDSTEPINDEXISNULL", "State 0 (New) nullability invariant"),
            ("STATE=1ANDSTABILITYISNOTNULLANDDIFFICULTYISNOTNULLANDLASTREVIEWEDATUTCISNOTNULLANDSTEPINDEX=0", "State 1 (Learning) step index invariant"),
            ("STATE=2ANDSTABILITYISNOTNULLANDDIFFICULTYISNOTNULLANDLASTREVIEWEDATUTCISNOTNULLANDSTEPINDEXISNULL", "State 2 (Review) step index invariant"),
            ("STATE=3ANDSTABILITYISNOTNULLANDDIFFICULTYISNOTNULLANDLASTREVIEWEDATUTCISNOTNULLANDSTEPINDEX=0", "State 3 (Relearning) step index invariant")
        };

        foreach (var (fragment, desc) in cardStatesChecks)
        {
            if (!cardStatesSql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{Schema13Ddl.FsrsCardStatesTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
        }

        // 2. FsrsReviewHistoryEntries semantic checks
        var historySql = ddlByTable[Schema13Ddl.FsrsReviewHistoryEntriesTableName];
        var historyChecks = new (string Fragment, string Description)[]
        {
            ("LENGTH(TRIM(STABLEID))>0", "non-empty StableId constraint"),
            ("SEQUENCENUMBER>0", "positive SequenceNumber constraint"),
            ("RATINGIN(0,1,2,3)", "rating enumeration 0..3 constraint")
        };

        foreach (var (fragment, desc) in historyChecks)
        {
            if (!historySql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{Schema13Ddl.FsrsReviewHistoryEntriesTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
        }

        // 3. WordLearningControls semantic checks
        var wordControlSql = ddlByTable[Schema13Ddl.WordLearningControlsTableName];
        if (!wordControlSql.Contains("LENGTH(TRIM(DECIDEDATUTC))>0", StringComparison.OrdinalIgnoreCase))
        {
            failureDetail = $"Table '{Schema13Ddl.WordLearningControlsTableName}' is missing required CHECK constraint: non-empty DecidedAtUtc.";
            return false;
        }

        // 4. SenseLearningControls semantic checks
        var senseControlSql = ddlByTable[Schema13Ddl.SenseLearningControlsTableName];
        if (!senseControlSql.Contains("LENGTH(TRIM(DECIDEDATUTC))>0", StringComparison.OrdinalIgnoreCase))
        {
            failureDetail = $"Table '{Schema13Ddl.SenseLearningControlsTableName}' is missing required CHECK constraint: non-empty DecidedAtUtc.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    private static string NormalizeSql(string sql) =>
        string.Concat(sql.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

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

    private sealed class ForeignKeyPragmaRow
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string Table { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string? On_update { get; set; }
        public string? On_delete { get; set; }
        public string? Match { get; set; }
    }
}
