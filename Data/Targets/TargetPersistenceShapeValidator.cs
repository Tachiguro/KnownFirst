namespace KnownFirst.Data.Targets;

using SQLite;

internal static class TargetPersistenceShapeValidator
{
    public static bool Validate(SQLiteConnection connection, out string? failureDetail)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!TableExists(connection, TargetPersistenceDdl.LearningTargetsTableName))
        {
            failureDetail = $"Table '{TargetPersistenceDdl.LearningTargetsTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, TargetPersistenceDdl.TargetAnswerVariantsTableName))
        {
            failureDetail = $"Table '{TargetPersistenceDdl.TargetAnswerVariantsTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, TargetPersistenceDdl.TargetFsrsStatesTableName))
        {
            failureDetail = $"Table '{TargetPersistenceDdl.TargetFsrsStatesTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName))
        {
            failureDetail = $"Table '{TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName}' is missing.";
            return false;
        }

        if (!TableExists(connection, TargetPersistenceDdl.TargetReviewsTableName))
        {
            failureDetail = $"Table '{TargetPersistenceDdl.TargetReviewsTableName}' is missing.";
            return false;
        }

        if (!HasLearningTargetsColumns(connection, out failureDetail)
            || !HasTargetAnswerVariantsColumns(connection, out failureDetail)
            || !HasTargetFsrsStatesColumns(connection, out failureDetail)
            || !HasTargetFsrsReviewHistoryEntriesColumns(connection, out failureDetail)
            || !HasTargetReviewsColumns(connection, out failureDetail))
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

    private static bool HasLearningTargetsColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("StableId", "TEXT", true, false),
            ("SenseId", "INTEGER", true, false),
            ("TargetKind", "INTEGER", true, false),
            ("SourceLanguage", "TEXT", true, false),
            ("TargetLanguage", "TEXT", true, false),
            ("TypingOptOut", "INTEGER", true, false),
            ("CreatedAtUtc", "TEXT", true, false),
            ("UpdatedAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, TargetPersistenceDdl.LearningTargetsTableName, expected, out failureDetail);
    }

    private static bool HasTargetAnswerVariantsColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("StableId", "TEXT", true, false),
            ("TargetId", "INTEGER", true, false),
            ("AnswerLanguage", "TEXT", true, false),
            ("DisplayText", "TEXT", true, false),
            ("NormalizedText", "TEXT", true, false),
            ("Requirement", "INTEGER", true, false),
            ("IsPreferred", "INTEGER", true, false),
            ("RequiredSinceUtc", "TEXT", false, false),
            ("SourceMeaningId", "INTEGER", false, false),
            ("CreatedAtUtc", "TEXT", true, false),
            ("UpdatedAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, TargetPersistenceDdl.TargetAnswerVariantsTableName, expected, out failureDetail);
    }

    private static bool HasTargetFsrsStatesColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("TargetId", "INTEGER", false, true),
            ("State", "INTEGER", true, false),
            ("Stability", "REAL", false, false),
            ("Difficulty", "REAL", false, false),
            ("LastReviewedAtUtc", "TEXT", false, false),
            ("StepIndex", "INTEGER", false, false),
            ("DueAtUtc", "TEXT", false, false)
        };

        return ValidateColumns(connection, TargetPersistenceDdl.TargetFsrsStatesTableName, expected, out failureDetail);
    }

    private static bool HasTargetFsrsReviewHistoryEntriesColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("StableId", "TEXT", true, false),
            ("TargetId", "INTEGER", true, false),
            ("SequenceNumber", "INTEGER", true, false),
            ("Rating", "INTEGER", true, false),
            ("ReviewedAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName, expected, out failureDetail);
    }

    private static bool HasTargetReviewsColumns(SQLiteConnection connection, out string? failureDetail)
    {
        var expected = new (string Name, string Type, bool NotNull, bool IsPk)[]
        {
            ("Id", "INTEGER", false, true),
            ("StableId", "TEXT", true, false),
            ("TargetId", "INTEGER", true, false),
            ("SessionId", "INTEGER", true, false),
            ("Rating", "INTEGER", true, false),
            ("WasTypedAnswer", "INTEGER", true, false),
            ("WasCorrect", "INTEGER", true, false),
            ("IsSessionRepeat", "INTEGER", true, false),
            ("TargetAnswerVariantId", "INTEGER", false, false),
            ("MatchedAnswerVariantId", "INTEGER", false, false),
            ("ReviewedAtUtc", "TEXT", true, false),
            ("DueAtUtc", "TEXT", true, false)
        };

        return ValidateColumns(connection, TargetPersistenceDdl.TargetReviewsTableName, expected, out failureDetail);
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
        var checks = new (string Table, string Parent, string From, string To, string OnDelete)[]
        {
            (TargetPersistenceDdl.LearningTargetsTableName, "Senses", "SenseId", "Id", "CASCADE"),
            (TargetPersistenceDdl.TargetAnswerVariantsTableName, TargetPersistenceDdl.LearningTargetsTableName, "TargetId", "Id", "CASCADE"),
            (TargetPersistenceDdl.TargetAnswerVariantsTableName, "Meanings", "SourceMeaningId", "Id", "SET NULL"),
            (TargetPersistenceDdl.TargetFsrsStatesTableName, TargetPersistenceDdl.LearningTargetsTableName, "TargetId", "Id", "CASCADE"),
            (TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName, TargetPersistenceDdl.LearningTargetsTableName, "TargetId", "Id", "CASCADE"),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.LearningTargetsTableName, "TargetId", "Id", "CASCADE"),
            (TargetPersistenceDdl.TargetReviewsTableName, "LearningSessions", "SessionId", "Id", "CASCADE"),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.TargetAnswerVariantsTableName, "TargetAnswerVariantId", "Id", "SET NULL"),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.TargetAnswerVariantsTableName, "MatchedAnswerVariantId", "Id", "SET NULL")
        };

        foreach (var (table, parent, from, to, onDelete) in checks)
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

            if (!string.Equals(match.On_delete, onDelete, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Foreign key '{table}.{from}' -> '{parent}({to})' must declare ON DELETE {onDelete} (found {match.On_delete}).";
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
            (TargetPersistenceDdl.LearningTargetsTableName, TargetPersistenceDdl.LearningTargetsStableIdIndexName, true, ["StableId"]),
            (TargetPersistenceDdl.LearningTargetsTableName, TargetPersistenceDdl.LearningTargetsSenseIdIndexName, false, ["SenseId"]),
            (TargetPersistenceDdl.LearningTargetsTableName, TargetPersistenceDdl.LearningTargetsSemanticIndexName, true, ["SenseId", "TargetKind", "SourceLanguage", "TargetLanguage"]),
            (TargetPersistenceDdl.TargetAnswerVariantsTableName, TargetPersistenceDdl.TargetAnswerVariantsStableIdIndexName, true, ["StableId"]),
            (TargetPersistenceDdl.TargetAnswerVariantsTableName, TargetPersistenceDdl.TargetAnswerVariantsTargetIdIndexName, false, ["TargetId"]),
            (TargetPersistenceDdl.TargetAnswerVariantsTableName, TargetPersistenceDdl.TargetAnswerVariantsNormalizedTextIndexName, true, ["TargetId", "NormalizedText"]),
            (TargetPersistenceDdl.TargetAnswerVariantsTableName, TargetPersistenceDdl.TargetAnswerVariantsPreferredIndexName, true, ["TargetId"]),
            (TargetPersistenceDdl.TargetFsrsStatesTableName, TargetPersistenceDdl.TargetFsrsStatesDueIndexName, false, ["State", "DueAtUtc"]),
            (TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName, TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesStableIdIndexName, true, ["StableId"]),
            (TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName, TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTargetSequenceIndexName, true, ["TargetId", "SequenceNumber"]),
            (TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName, TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesReplayIndexName, false, ["TargetId", "ReviewedAtUtc", "SequenceNumber"]),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.TargetReviewsStableIdIndexName, true, ["StableId"]),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.TargetReviewsTargetIdIndexName, false, ["TargetId"]),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.TargetReviewsSessionIdIndexName, false, ["SessionId"]),
            (TargetPersistenceDdl.TargetReviewsTableName, TargetPersistenceDdl.TargetReviewsTargetReviewedAtIndexName, false, ["TargetId", "ReviewedAtUtc"])
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
            TargetPersistenceDdl.LearningTargetsTableName,
            TargetPersistenceDdl.TargetAnswerVariantsTableName,
            TargetPersistenceDdl.TargetFsrsStatesTableName,
            TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName,
            TargetPersistenceDdl.TargetReviewsTableName
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

        // LearningTargets semantic checks
        var targetsSql = ddlByTable[TargetPersistenceDdl.LearningTargetsTableName];
        var targetsChecks = new (string Fragment, string Description)[]
        {
            ("LENGTH(TRIM(STABLEID))>0", "non-empty StableId"),
            ("TARGETKINDIN(0,1)", "TargetKind in (0, 1)"),
            ("LENGTH(TRIM(SOURCELANGUAGE))>0", "non-empty SourceLanguage"),
            ("LENGTH(TRIM(TARGETLANGUAGE))>0", "non-empty TargetLanguage"),
            ("TYPINGOPTOUTIN(0,1)", "TypingOptOut in (0, 1)"),
            ("LENGTH(TRIM(CREATEDATUTC))>0", "non-empty CreatedAtUtc"),
            ("LENGTH(TRIM(UPDATEDATUTC))>0", "non-empty UpdatedAtUtc")
        };

        foreach (var (fragment, desc) in targetsChecks)
        {
            if (!targetsSql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{TargetPersistenceDdl.LearningTargetsTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
        }

        // TargetAnswerVariants semantic checks
        var variantsSql = ddlByTable[TargetPersistenceDdl.TargetAnswerVariantsTableName];
        var variantsChecks = new (string Fragment, string Description)[]
        {
            ("LENGTH(TRIM(STABLEID))>0", "non-empty StableId"),
            ("LENGTH(TRIM(ANSWERLANGUAGE))>0", "non-empty AnswerLanguage"),
            ("LENGTH(TRIM(DISPLAYTEXT))>0", "non-empty DisplayText"),
            ("LENGTH(TRIM(NORMALIZEDTEXT))>0", "non-empty NormalizedText"),
            ("REQUIREMENTIN(0,1)", "Requirement in (0, 1)"),
            ("ISPREFERREDIN(0,1)", "IsPreferred in (0, 1)"),
            ("REQUIREMENT=0ANDREQUIREDSINCEUTCISNOTNULL", "Required variant must have RequiredSinceUtc"),
            ("REQUIREMENT=1ANDREQUIREDSINCEUTCISNULL", "AcceptedOnly variant must have null RequiredSinceUtc"),
            ("LENGTH(TRIM(CREATEDATUTC))>0", "non-empty CreatedAtUtc"),
            ("LENGTH(TRIM(UPDATEDATUTC))>0", "non-empty UpdatedAtUtc")
        };

        foreach (var (fragment, desc) in variantsChecks)
        {
            if (!variantsSql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{TargetPersistenceDdl.TargetAnswerVariantsTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
        }

        // TargetFsrsStates checks
        var statesSql = ddlByTable[TargetPersistenceDdl.TargetFsrsStatesTableName];
        var statesChecks = new (string Fragment, string Description)[]
        {
            ("STATEIN(0,1,2,3)", "State in (0, 1, 2, 3)"),
            ("STABILITYISNULLORSTABILITY>=0.001", "Stability validity"),
            ("DIFFICULTY>=1.0ANDDIFFICULTY<=10.0", "Difficulty validity"),
            ("STATE=0ANDSTABILITYISNULLANDDIFFICULTYISNULLANDLASTREVIEWEDATUTCISNULLANDSTEPINDEXISNULL", "State 0 invariants"),
            ("STATE=1ANDSTABILITYISNOTNULLANDDIFFICULTYISNOTNULLANDLASTREVIEWEDATUTCISNOTNULLANDSTEPINDEX=0", "State 1 invariants"),
            ("STATE=2ANDSTABILITYISNOTNULLANDDIFFICULTYISNOTNULLANDLASTREVIEWEDATUTCISNOTNULLANDSTEPINDEXISNULL", "State 2 invariants"),
            ("STATE=3ANDSTABILITYISNOTNULLANDDIFFICULTYISNOTNULLANDLASTREVIEWEDATUTCISNOTNULLANDSTEPINDEX=0", "State 3 invariants")
        };

        foreach (var (fragment, desc) in statesChecks)
        {
            if (!statesSql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{TargetPersistenceDdl.TargetFsrsStatesTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
        }

        // TargetFsrsReviewHistoryEntries checks
        var historySql = ddlByTable[TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName];
        var historyChecks = new (string Fragment, string Description)[]
        {
            ("LENGTH(TRIM(STABLEID))>0", "non-empty StableId"),
            ("SEQUENCENUMBER>0", "SequenceNumber > 0"),
            ("RATINGIN(0,1,2,3)", "Rating in (0, 1, 2, 3)")
        };

        foreach (var (fragment, desc) in historyChecks)
        {
            if (!historySql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{TargetPersistenceDdl.TargetFsrsReviewHistoryEntriesTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
        }

        // TargetReviews checks
        var reviewsSql = ddlByTable[TargetPersistenceDdl.TargetReviewsTableName];
        var reviewsChecks = new (string Fragment, string Description)[]
        {
            ("LENGTH(TRIM(STABLEID))>0", "non-empty StableId"),
            ("RATINGIN(0,1,2,3)", "Rating in (0, 1, 2, 3)"),
            ("WASTYPEDANSWERIN(0,1)", "WasTypedAnswer in (0, 1)"),
            ("WASCORRECTIN(0,1)", "WasCorrect in (0, 1)"),
            ("ISSESSIONREPEATIN(0,1)", "IsSessionRepeat in (0, 1)"),
            ("LENGTH(TRIM(REVIEWEDATUTC))>0", "non-empty ReviewedAtUtc"),
            ("LENGTH(TRIM(DUEATUTC))>0", "non-empty DueAtUtc")
        };

        foreach (var (fragment, desc) in reviewsChecks)
        {
            if (!reviewsSql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                failureDetail = $"Table '{TargetPersistenceDdl.TargetReviewsTableName}' is missing required CHECK constraint: {desc}.";
                return false;
            }
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
