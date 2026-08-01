using SQLite;

namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// Single source of truth for "what does a physically valid Schema-8 shape look like" — extracted
/// from <see cref="Schema8DormantMigration"/>'s already-tested already-applied-shape check
/// (KF-MEANING-001 Slice 2) so <c>Services/DataSafety/BackupSchemaCapability</c> can reuse the exact
/// same checks instead of re-deriving them. Normal database initialization uses the combined physical
/// and logical validation before exposing an initialized Schema-8 database to services.
/// </summary>
internal static class Schema8ShapeValidator
{
    /// <summary>
    /// Non-throwing shape check. Returns <see langword="false"/> with a human-readable
    /// <paramref name="failureDetail"/> on the first violation found, rather than throwing, so callers
    /// with different failure-reporting needs (a migration exception vs. a capability-resolution
    /// exception) can each wrap the result in their own error type.
    /// </summary>
    public static bool IsValidShape(SQLiteConnection connection, out string? failureDetail)
    {
        foreach (var (table, _) in RequiredColumns)
        {
            if (!TableExists(connection, table))
            {
                failureDetail = $"Required table '{table}' is missing.";
                return false;
            }
        }

        if (HasColumn(connection, "LearningCards", "MeaningId"))
        {
            failureDetail = "LearningCards still has a legacy MeaningId column.";
            return false;
        }

        foreach (var (table, columns) in RequiredColumns)
        {
            var declared = connection
                .Query<TableColumnDefinition>($"PRAGMA table_info(\"{EscapeIdentifier(table)}\")")
                .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                if (!declared.TryGetValue(column.Name, out var actual))
                {
                    failureDetail = $"{table} is missing the required {column.Name} column.";
                    return false;
                }

                if (!IsDeclarationValid(column, actual, out var violation))
                {
                    failureDetail = $"{table}.{column.Name} {violation}";
                    return false;
                }
            }
        }

        if (IndexExists(connection, Schema8Ddl.OldCardIndexName))
        {
            failureDetail = $"Legacy index {Schema8Ddl.OldCardIndexName} still exists.";
            return false;
        }

        foreach (var index in RequiredIndexes)
        {
            if (!HasRequiredIndexDefinition(connection, index, out failureDetail))
            {
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    public static bool IsValidDatabase(SQLiteConnection connection, out string? failureDetail)
    {
        if (!IsValidShape(connection, out failureDetail))
        {
            return false;
        }

        foreach (var (sql, detail) in LogicalInvariantQueries)
        {
            if (connection.ExecuteScalar<int>(sql) != 0)
            {
                failureDetail = detail;
                return false;
            }
        }

        failureDetail = null;
        return true;
    }

    /// <summary>
    /// Non-throwing check that a database has none of the Schema-8-only tables and still carries the
    /// legacy <c>LearningCards.MeaningId</c> column — the Schema-7 counterpart to
    /// <see cref="IsValidShape"/>.
    /// </summary>
    public static bool IsValidSchema7Shape(SQLiteConnection connection, out string? failureDetail)
    {
        foreach (var table in new[] { "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress" })
        {
            if (TableExists(connection, table))
            {
                failureDetail = $"Table '{table}' exists but PRAGMA user_version reports Schema 7.";
                return false;
            }
        }

        if (!HasColumn(connection, "LearningCards", "MeaningId"))
        {
            failureDetail = "LearningCards is missing the legacy MeaningId column expected at Schema 7.";
            return false;
        }

        if (HasColumn(connection, "LearningCards", "PreferredMeaningId"))
        {
            failureDetail = "LearningCards already has PreferredMeaningId but PRAGMA user_version reports Schema 7.";
            return false;
        }

        failureDetail = null;
        return true;
    }

    public static bool TableExists(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;

    public static bool IndexExists(SQLiteConnection connection, string index) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", index) > 0;

    public static bool HasColumn(SQLiteConnection connection, string table, string column) =>
        connection.Query<TableColumnInfo>($"PRAGMA table_info(\"{EscapeIdentifier(table)}\")")
            .Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

    private static bool HasRequiredIndexDefinition(
        SQLiteConnection connection,
        RequiredIndex required,
        out string? failureDetail)
    {
        var index = connection.Query<IndexListRow>($"PRAGMA index_list(\"{EscapeIdentifier(required.Table)}\")")
            .SingleOrDefault(item => string.Equals(item.Name, required.Name, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            failureDetail = $"Required index {required.Name} is missing from {required.Table}.";
            return false;
        }

        if ((index.Unique == 1) != required.Unique)
        {
            failureDetail = $"Index {required.Name} has the wrong uniqueness.";
            return false;
        }

        var columns = connection.Query<IndexXInfoRow>($"PRAGMA index_xinfo(\"{EscapeIdentifier(required.Name)}\")")
            .Where(item => item.Key == 1)
            .OrderBy(item => item.SequenceNumber)
            .ToArray();
        if (columns.Length != required.Columns.Length
            || columns.Where((item, ordinal) =>
                    item.ColumnId < 0
                    || item.Descending != 0
                    || !string.Equals(item.Collation, "BINARY", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(item.Name, required.Columns[ordinal], StringComparison.OrdinalIgnoreCase))
                .Any())
        {
            failureDetail = $"Index {required.Name} has the wrong ordered columns, expression, collation, or sort metadata.";
            return false;
        }

        var expectsPartial = required.Predicate is not null;
        if ((index.Partial == 1) != expectsPartial)
        {
            failureDetail = $"Index {required.Name} has the wrong partial-index setting.";
            return false;
        }

        if (expectsPartial)
        {
            var createSql = connection.ExecuteScalar<string?>(
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = ?", required.Name);
            var actualPredicate = ExtractNormalizedPredicate(createSql);
            var requiredPredicate = NormalizePredicate(required.Predicate!);
            if (!string.Equals(actualPredicate, requiredPredicate, StringComparison.Ordinal))
            {
                failureDetail = $"Index {required.Name} has the wrong partial-index predicate.";
                return false;
            }
        }

        failureDetail = null;
        return true;
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

    /// <summary>
    /// Read-only comparison of one <c>PRAGMA table_info</c> row against the nullability the real Schema-8
    /// DDL declares for that column. Deliberately inspects only the schema declaration — never row
    /// contents — so an empty table is validated exactly like a populated one, and never repairs anything.
    /// </summary>
    private static bool IsDeclarationValid(
        RequiredColumn required,
        TableColumnDefinition actual,
        out string violation)
    {
        switch (required.Declaration)
        {
            case ColumnDeclaration.PrimaryKey:
                // SQLite reports `notnull` inconsistently for a primary key: the raw Schema-8 DDL
                // (`Id INTEGER PRIMARY KEY AUTOINCREMENT`) reports notnull = 0 while the sqlite-net baseline
                // DDL appends an explicit `not null` and reports 1. Both are rowid aliases and can never
                // hold NULL, so primary-key-ness plus INTEGER affinity — not the notnull flag — is what
                // must be validated here.
                if (actual.PrimaryKey != 1
                    || !string.Equals(actual.Type, "INTEGER", StringComparison.OrdinalIgnoreCase))
                {
                    violation = "must be the table's INTEGER PRIMARY KEY.";
                    return false;
                }

                break;

            case ColumnDeclaration.NotNull:
                if (actual.PrimaryKey != 0)
                {
                    violation = "must not be part of the primary key.";
                    return false;
                }

                if (actual.NotNull != 1)
                {
                    violation = "must be declared NOT NULL.";
                    return false;
                }

                break;

            case ColumnDeclaration.Nullable:
                if (actual.PrimaryKey != 0)
                {
                    violation = "must not be part of the primary key.";
                    return false;
                }

                if (actual.NotNull != 0)
                {
                    violation = "must remain declared nullable.";
                    return false;
                }

                break;

            default:
                if (actual.PrimaryKey != 0)
                {
                    violation = "must not be part of the primary key.";
                    return false;
                }

                break;
        }

        violation = string.Empty;
        return true;
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    /// <summary>How the real Schema-8 DDL declares one required column's nullability.</summary>
    private enum ColumnDeclaration
    {
        /// <summary>The table's <c>INTEGER PRIMARY KEY</c> rowid alias; the reported notnull flag is not meaningful.</summary>
        PrimaryKey,

        /// <summary>Declared <c>NOT NULL</c> and required to stay that way.</summary>
        NotNull,

        /// <summary>Intentionally nullable and required to stay that way.</summary>
        Nullable,

        /// <summary>
        /// A legacy <c>LearningCards</c> column whose declared nullability legitimately differs between the
        /// two supported Schema-7 -&gt; 8 card paths: the sqlite-net baseline table reached through
        /// <c>ALTER TABLE ... RENAME COLUMN</c> declares it nullable, while the forced column-rebuild
        /// fallback recreates it as <c>NOT NULL</c>. Both are valid Schema-8 shapes, so only the
        /// non-primary-key position is asserted.
        /// </summary>
        CardRebuildDependent
    }

    private readonly record struct RequiredColumn(string Name, ColumnDeclaration Declaration);

    private static RequiredColumn Key(string name) => new(name, ColumnDeclaration.PrimaryKey);

    private static RequiredColumn NonNull(string name) => new(name, ColumnDeclaration.NotNull);

    private static RequiredColumn Optional(string name) => new(name, ColumnDeclaration.Nullable);

    private static RequiredColumn CardColumn(string name) => new(name, ColumnDeclaration.CardRebuildDependent);

    /// <summary>
    /// Every required column of the 21 Schema-8 tables together with the nullability the real DDL declares
    /// for it — the sqlite-net baseline DDL for the seventeen Schema-7 tables (which marks only the primary
    /// key as not-null) plus the raw <see cref="Schema8Ddl"/> DDL for the four Schema-8 tables and the six
    /// migration-added nullable columns.
    /// </summary>
    private static readonly (string Table, RequiredColumn[] Columns)[] RequiredColumns =
    [
        ("Documents", [Key("Id"), Optional("Title"), Optional("TextLanguage"), Optional("ExplanationLanguage"), Optional("LookupMode"), Optional("TargetLanguage"), Optional("Content"), Optional("ContentFingerprint"), Optional("ImportedAt"), Optional("WordCount")]),
        ("Words", [Key("Id"), Optional("Language"), Optional("CanonicalTerm"), Optional("NormalizedTerm"), Optional("Status"), Optional("TokenKind"), Optional("PreparationState"), Optional("TotalOccurrenceCount"), Optional("DocumentCount"), Optional("AutomaticInteractionMode"), Optional("ConsecutiveRecallSuccessCount"), Optional("ConsecutiveTypingSuccessCount"), Optional("ConsecutiveTypingFailureCount"), Optional("MasteryReviewExtensionScheduled"), Optional("CreatedAt"), Optional("UpdatedAt")]),
        ("WordForms", [Key("Id"), Optional("WordId"), Optional("SurfaceForm"), Optional("OccurrenceCount")]),
        ("SentenceSpans", [Key("Id"), Optional("DocumentId"), Optional("StartPosition"), Optional("Length"), Optional("Order")]),
        ("WordOccurrences", [Key("Id"), Optional("WordId"), Optional("DocumentId"), Optional("SentenceSpanId"), Optional("StartPosition"), Optional("Length"), Optional("SurfaceForm"), Optional("TechnicalFamily"), Optional("TechnicalInstanceYear"), Optional("TechnicalInstanceIdentifier"), Optional("TechnicalVariant"), Optional("Order")]),
        ("Meanings", [Key("Id"), Optional("WordId"), Optional("ExplanationLanguage"), Optional("SourceLanguage"), Optional("DisplayTerm"), Optional("EncounteredSurfaceForm"), Optional("GrammaticalRelationship"), Optional("TokenKind"), Optional("SelectedMeaningId"), Optional("AcronymExpansion"), Optional("Translation"), Optional("Definition"), Optional("DictionaryExample"), Optional("AdditionalNote"), Optional("AcceptedAliasesJson"), Optional("TranslationOrDefinition"), Optional("Source"), Optional("SourceProject"), Optional("SourcePageTitle"), Optional("SourceRevisionId"), Optional("Attribution"), Optional("ConfirmedByUser"), Optional("CreatedAt"), Optional("UpdatedAt"), Optional("PreparedAt"), Optional("SenseId"), Optional("StableId")]),
        ("ReviewStates", [Key("Id"), Optional("WordId"), Optional("ReviewCount"), Optional("ForgotCount"), Optional("PartialCount"), Optional("KnownCount"), Optional("LastReviewedAt")]),
        ("ReviewSessions", [Key("Id"), Optional("DocumentId"), Optional("Status"), Optional("TotalCandidates"), Optional("ReviewedCount"), Optional("KnownCount"), Optional("UnknownCount"), Optional("IgnoredCount"), Optional("DecisionSequence"), Optional("StartedAt"), Optional("CompletedAt")]),
        ("ReviewCandidates", [Key("Id"), Optional("SessionId"), Optional("WordId"), Optional("Order"), Optional("Status"), Optional("PreviousWordStatus"), Optional("PreviousTotalOccurrenceCount"), Optional("PreviousDocumentCount"), Optional("PreviousUpdatedAt"), Optional("DecisionSequence"), Optional("WasWordCreatedForSession"), Optional("DecidedAt")]),
        ("LexicalCache", [Key("Id"), Optional("CacheKey"), Optional("SourceLanguage"), Optional("ExplanationLanguage"), Optional("NormalizedLemma"), Optional("LookupMode"), Optional("TargetLanguage"), Optional("CanonicalLookupTerm"), Optional("TokenKind"), Optional("Provider"), Optional("ProviderSchemaVersion"), Optional("ResultJson"), Optional("SourceProject"), Optional("PageTitle"), Optional("RevisionId"), Optional("Attribution"), Optional("FetchedAtUtc")]),
        ("PreparationSessions", [Key("Id"), Optional("Status"), Optional("Method"), Optional("TotalItems"), Optional("CompletedItems"), Optional("StartedAtUtc"), Optional("UpdatedAtUtc"), Optional("CompletedAtUtc")]),
        ("PreparationCandidates", [Key("Id"), Optional("SessionId"), Optional("WordId"), Optional("Order"), Optional("Status"), Optional("ResultJson"), Optional("SelectedMeaningIndex"), Optional("LastErrorCode"), Optional("LookupAttemptCount"), Optional("UpdatedAtUtc")]),
        ("ContextSnapshots", [Key("Id"), Optional("MeaningId"), Optional("WordId"), Optional("SourceDocumentId"), Optional("SourceDocumentTitle"), Optional("Text"), Optional("TargetStart"), Optional("TargetLength"), Optional("NormalizedFingerprint"), Optional("CreatedAtUtc"), Optional("SenseId")]),
        ("LearningCards", [Key("Id"), CardColumn("WordId"), Optional("SenseId"), CardColumn("PreferredMeaningId"), CardColumn("Direction"), CardColumn("State"), CardColumn("DueAtUtc"), CardColumn("IntervalDays"), CardColumn("EaseFactor"), CardColumn("SuccessfulReviewCount"), CardColumn("LapseCount"), Optional("LastReviewedAtUtc"), Optional("LastRating"), CardColumn("CreatedAtUtc"), CardColumn("UpdatedAtUtc")]),
        ("LearningReviews", [Key("Id"), Optional("CardId"), Optional("SessionId"), Optional("Rating"), Optional("WasTypedAnswer"), Optional("WasCorrect"), Optional("ReviewedAtUtc"), Optional("DueAtUtc"), Optional("IntervalDays"), Optional("EaseFactor"), Optional("TargetAnswerVariantId"), Optional("MatchedAnswerVariantId")]),
        ("LearningSessions", [Key("Id"), Optional("Status"), Optional("TotalCards"), Optional("CompletedCards"), Optional("AgainCount"), Optional("HardCount"), Optional("GoodCount"), Optional("EasyCount"), Optional("StartedAtUtc"), Optional("UpdatedAtUtc"), Optional("CompletedAtUtc")]),
        ("LearningSessionCards", [Key("Id"), Optional("SessionId"), Optional("CardId"), Optional("QueueOrder"), Optional("IsDueCard"), Optional("IsAgainRepeat"), Optional("AnswerRevealed"), Optional("SpellingChecked"), Optional("SpellingCorrect"), Optional("IsCompleted"), Optional("Rating"), Optional("CompletedAtUtc"), Optional("TargetAnswerVariantId")]),
        ("Senses", [Key("Id"), NonNull("StableId"), NonNull("WordId"), NonNull("SourceLanguage"), NonNull("ExplanationLanguage"), NonNull("ProviderSenseId"), NonNull("TopicOrDomain"), NonNull("PartOfSpeech"), NonNull("GrammaticalRelationship"), NonNull("AcronymExpansion"), Optional("DefaultMeaningId"), NonNull("Status"), NonNull("CreatedAtUtc"), NonNull("UpdatedAtUtc")]),
        ("AnswerVariants", [Key("Id"), NonNull("StableId"), NonNull("SenseId"), NonNull("AnswerLanguage"), NonNull("DisplayText"), NonNull("NormalizedText"), Optional("SourceMeaningId"), NonNull("CreatedAtUtc"), NonNull("UpdatedAtUtc")]),
        ("SenseAnswerVariantAssignments", [Key("Id"), NonNull("StableId"), NonNull("SenseId"), NonNull("CardDirection"), NonNull("AnswerVariantId"), NonNull("Requirement"), NonNull("IsPreferred"), Optional(Schema8Ddl.AssignmentRequiredSinceUtcColumn), NonNull("CreatedAtUtc"), NonNull("UpdatedAtUtc")]),
        ("AnswerVariantProgress", [Key("Id"), NonNull("CardId"), NonNull("AnswerVariantId"), NonNull("InteractionMode"), NonNull("ConsecutiveReadingSuccessCount"), NonNull("ConsecutiveTypingSuccessCount"), NonNull("ConsecutiveTypingFailureCount"), Optional("LastAssessedAtUtc"), NonNull("MasteryReviewExtensionScheduled"), NonNull("IsMastered"), NonNull("ReplayVersion"), NonNull("CreatedAtUtc"), NonNull("UpdatedAtUtc")])
    ];

    private static readonly RequiredIndex[] RequiredIndexes =
    [
        new("Senses", "IX_Senses_StableId", true, ["StableId"]),
        new("Senses", "IX_Senses_WordId", false, ["WordId"]),
        new("AnswerVariants", "IX_AnswerVariants_StableId", true, ["StableId"]),
        new("AnswerVariants", "IX_AnswerVariants_Sense_Language_Text", true, ["SenseId", "AnswerLanguage", "NormalizedText"]),
        new("SenseAnswerVariantAssignments", "IX_SenseAnswerVariantAssignments_StableId", true, ["StableId"]),
        new("SenseAnswerVariantAssignments", "IX_SenseAnswerVariantAssignments_Sense_Direction_Variant", true, ["SenseId", "CardDirection", "AnswerVariantId"]),
        new("SenseAnswerVariantAssignments", "IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred", true, ["SenseId", "CardDirection"], "IsPreferred = 1"),
        new("AnswerVariantProgress", "IX_AnswerVariantProgress_Card_Variant", true, ["CardId", "AnswerVariantId"]),
        new("LearningCards", "IX_LearningCards_Sense_Direction", true, ["SenseId", "Direction"])
    ];

    private static readonly (string Sql, string Detail)[] LogicalInvariantQueries =
    [
        ("SELECT COUNT(*) FROM Documents WHERE LookupMode IS NULL OR LookupMode NOT IN (0, 1, 2)", "A Document row has an invalid lookup mode."),
        ("SELECT COUNT(*) FROM Words WHERE Status IS NULL OR Status NOT IN (0, 1, 2, 6) OR TokenKind IS NULL OR TokenKind NOT IN (0, 1, 2, 3) OR PreparationState IS NULL OR PreparationState NOT IN (0, 1, 2, 3) OR AutomaticInteractionMode IS NULL OR AutomaticInteractionMode NOT IN (0, 1)", "A Word row has an invalid enum value or a legacy Schema-7 learning status."),
        ("SELECT COUNT(*) FROM WordOccurrences WHERE TechnicalFamily IS NULL OR TechnicalFamily NOT IN (0, 1, 2)", "A WordOccurrence row has an invalid technical-token family."),
        ("SELECT COUNT(*) FROM Meanings WHERE TokenKind IS NULL OR TokenKind NOT IN (0, 1, 2, 3)", "A Meaning row has an invalid token kind."),
        ("SELECT COUNT(*) FROM ReviewSessions WHERE Status IS NULL OR Status NOT IN (0, 1)", "A ReviewSession row has an invalid status."),
        ("SELECT COUNT(*) FROM ReviewCandidates WHERE Status IS NULL OR Status NOT IN (0, 1, 2, 3, 4, 5, 6) OR PreviousWordStatus IS NULL OR PreviousWordStatus NOT IN (0, 1, 2, 3, 4, 5, 6)", "A ReviewCandidate row has an invalid current or previous Word status."),
        ("SELECT COUNT(*) FROM LexicalCache WHERE LookupMode IS NULL OR LookupMode NOT IN (0, 1, 2) OR TokenKind IS NULL OR TokenKind NOT IN (0, 1, 2, 3)", "A LexicalCache row has an invalid lookup mode or token kind."),
        ("SELECT COUNT(*) FROM PreparationSessions WHERE Status IS NULL OR Status NOT IN (0, 1, 2) OR Method IS NULL OR Method NOT IN (0, 1)", "A PreparationSession row has an invalid status or method."),
        ("SELECT COUNT(*) FROM PreparationCandidates WHERE Status IS NULL OR Status NOT IN (0, 1, 2, 3, 4, 5, 6, 7)", "A PreparationCandidate row has an invalid status."),
        ("SELECT COUNT(*) FROM LearningCards WHERE Direction IS NULL OR Direction NOT IN (0, 1) OR State IS NULL OR State NOT IN (0, 1, 2, 3, 4, 5) OR (LastRating IS NOT NULL AND LastRating NOT IN (0, 1, 2, 3))", "A LearningCard row has an invalid direction, state, or last rating."),
        ("SELECT COUNT(*) FROM LearningReviews WHERE Rating IS NULL OR Rating NOT IN (0, 1, 2, 3)", "A LearningReview row has an invalid rating."),
        ("SELECT COUNT(*) FROM LearningSessions WHERE Status IS NULL OR Status NOT IN (0, 1)", "A LearningSession row has an invalid status."),
        ("SELECT COUNT(*) FROM LearningSessionCards WHERE Rating IS NOT NULL AND Rating NOT IN (0, 1, 2, 3)", "A LearningSessionCard row has an invalid optional rating."),
        ("SELECT COUNT(*) FROM Senses WHERE Status IS NULL OR Status NOT IN (0, 1, 2, 3)", "A Sense row has an invalid status."),
        ("SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE CardDirection IS NULL OR CardDirection NOT IN (0, 1) OR Requirement IS NULL OR Requirement NOT IN (0, 1) OR IsPreferred IS NULL OR IsPreferred NOT IN (0, 1)", "An assignment row has an invalid direction, requirement, or preferred flag."),
        ("SELECT COUNT(*) FROM AnswerVariantProgress WHERE InteractionMode IS NULL OR InteractionMode NOT IN (0, 1)", "A progress row has an invalid interaction mode."),
        ("SELECT COUNT(*) FROM WordForms f LEFT JOIN Words w ON w.Id = f.WordId WHERE w.Id IS NULL", "A WordForm row references a missing Word."),
        ("SELECT COUNT(*) FROM SentenceSpans s LEFT JOIN Documents d ON d.Id = s.DocumentId WHERE d.Id IS NULL", "A SentenceSpan row references a missing Document."),
        ("SELECT COUNT(*) FROM WordOccurrences o LEFT JOIN Words w ON w.Id = o.WordId WHERE w.Id IS NULL", "A WordOccurrence Word reference is invalid."),
        ("SELECT COUNT(*) FROM WordOccurrences o LEFT JOIN Documents d ON d.Id = o.DocumentId WHERE d.Id IS NULL", "A WordOccurrence Document reference is invalid."),
        ("SELECT COUNT(*) FROM WordOccurrences o LEFT JOIN SentenceSpans s ON s.Id = o.SentenceSpanId WHERE s.Id IS NULL OR s.DocumentId <> o.DocumentId", "A WordOccurrence SentenceSpan reference is missing or belongs to a different Document."),
        ("SELECT COUNT(*) FROM ReviewStates r LEFT JOIN Words w ON w.Id = r.WordId WHERE w.Id IS NULL", "A ReviewState row references a missing Word."),
        ("SELECT COUNT(*) FROM ReviewSessions r LEFT JOIN Documents d ON d.Id = r.DocumentId WHERE d.Id IS NULL", "A ReviewSession row references a missing Document."),
        ("SELECT COUNT(*) FROM ReviewCandidates r LEFT JOIN ReviewSessions s ON s.Id = r.SessionId WHERE s.Id IS NULL", "A ReviewCandidate Session reference is invalid."),
        ("SELECT COUNT(*) FROM ReviewCandidates r LEFT JOIN Words w ON w.Id = r.WordId WHERE w.Id IS NULL", "A ReviewCandidate Word reference is invalid."),
        ("SELECT COUNT(*) FROM PreparationCandidates p LEFT JOIN PreparationSessions s ON s.Id = p.SessionId WHERE s.Id IS NULL", "A PreparationCandidate Session reference is invalid."),
        ("SELECT COUNT(*) FROM PreparationCandidates p LEFT JOIN Words w ON w.Id = p.WordId WHERE w.Id IS NULL", "A PreparationCandidate Word reference is invalid."),
        ("SELECT COUNT(*) FROM Senses s LEFT JOIN Words w ON w.Id = s.WordId WHERE w.Id IS NULL", "A Sense row references a missing Word."),
        ("SELECT COUNT(*) FROM Meanings m LEFT JOIN Senses s ON s.Id = m.SenseId WHERE s.Id IS NULL OR s.WordId <> m.WordId", "A Meaning row is not owned by a Sense for the same Word."),
        ("SELECT COUNT(*) FROM Senses s LEFT JOIN Meanings m ON m.Id = s.DefaultMeaningId WHERE s.DefaultMeaningId IS NOT NULL AND (m.Id IS NULL OR m.SenseId <> s.Id)", "A Sense DefaultMeaning reference is invalid."),
        ("SELECT COUNT(*) FROM LearningCards c LEFT JOIN Senses s ON s.Id = c.SenseId LEFT JOIN Meanings m ON m.Id = c.PreferredMeaningId WHERE s.Id IS NULL OR s.WordId <> c.WordId OR m.Id IS NULL OR m.SenseId <> c.SenseId OR m.WordId <> c.WordId", "A LearningCard Sense, Word, or PreferredMeaning relationship is invalid."),
        ("SELECT COUNT(*) FROM AnswerVariants v LEFT JOIN Senses s ON s.Id = v.SenseId LEFT JOIN Meanings m ON m.Id = v.SourceMeaningId WHERE s.Id IS NULL OR (v.SourceMeaningId IS NOT NULL AND (m.Id IS NULL OR m.SenseId <> v.SenseId))", "An AnswerVariant ownership relationship is invalid."),
        ("SELECT COUNT(*) FROM SenseAnswerVariantAssignments a LEFT JOIN Senses s ON s.Id = a.SenseId LEFT JOIN AnswerVariants v ON v.Id = a.AnswerVariantId WHERE s.Id IS NULL OR v.Id IS NULL OR v.SenseId <> a.SenseId", "An assignment ownership relationship is invalid."),
        ("SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE (Requirement = 0 AND RequiredSinceUtc IS NULL) OR (Requirement <> 0 AND RequiredSinceUtc IS NOT NULL)", "An assignment violates the RequiredSinceUtc boundary invariant."),
        ("SELECT COUNT(*) FROM ContextSnapshots x LEFT JOIN Meanings m ON m.Id = x.MeaningId LEFT JOIN Senses s ON s.Id = x.SenseId WHERE m.Id IS NULL OR s.Id IS NULL OR m.SenseId <> x.SenseId OR m.WordId <> x.WordId OR s.WordId <> x.WordId", "A ContextSnapshot ownership relationship is invalid."),
        ("SELECT COUNT(*) FROM ContextSnapshots x LEFT JOIN Documents d ON d.Id = x.SourceDocumentId WHERE d.Id IS NULL", "A ContextSnapshot source Document reference is invalid."),
        ("SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN LearningSessions s ON s.Id = q.SessionId WHERE s.Id IS NULL", "A LearningSessionCard Session reference is invalid."),
        ("SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN LearningCards c ON c.Id = q.CardId LEFT JOIN SenseAnswerVariantAssignments a ON a.SenseId = c.SenseId AND a.CardDirection = c.Direction AND a.AnswerVariantId = q.TargetAnswerVariantId WHERE c.Id IS NULL OR (q.TargetAnswerVariantId IS NOT NULL AND a.Id IS NULL)", "A learning queue target is not assigned to its card."),
        ("SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningSessions s ON s.Id = r.SessionId WHERE s.Id IS NULL", "A LearningReview Session reference is invalid."),
        ("SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningCards c ON c.Id = r.CardId LEFT JOIN SenseAnswerVariantAssignments target ON target.SenseId = c.SenseId AND target.CardDirection = c.Direction AND target.AnswerVariantId = r.TargetAnswerVariantId LEFT JOIN SenseAnswerVariantAssignments matched ON matched.SenseId = c.SenseId AND matched.CardDirection = c.Direction AND matched.AnswerVariantId = r.MatchedAnswerVariantId WHERE c.Id IS NULL OR (r.TargetAnswerVariantId IS NOT NULL AND target.Id IS NULL) OR (r.MatchedAnswerVariantId IS NOT NULL AND matched.Id IS NULL)", "A LearningReview target or matched variant is invalid for its card."),
        ("SELECT COUNT(*) FROM AnswerVariantProgress p LEFT JOIN LearningCards c ON c.Id = p.CardId LEFT JOIN SenseAnswerVariantAssignments a ON a.SenseId = c.SenseId AND a.CardDirection = c.Direction AND a.AnswerVariantId = p.AnswerVariantId WHERE c.Id IS NULL OR a.Id IS NULL", "An AnswerVariantProgress row is not attributed to an assignment for its card."),
        ("SELECT COUNT(*) FROM Senses WHERE StableId IS NULL OR TRIM(StableId) = ''", "A Sense has an empty StableId."),
        ("SELECT COUNT(*) FROM Meanings WHERE StableId IS NULL OR TRIM(StableId) = ''", "A Meaning has an empty StableId."),
        ("SELECT COUNT(*) FROM AnswerVariants WHERE StableId IS NULL OR TRIM(StableId) = ''", "An AnswerVariant has an empty StableId."),
        ("SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE StableId IS NULL OR TRIM(StableId) = ''", "An assignment has an empty StableId.")
    ];

    /// <summary>One <c>PRAGMA table_info</c> row, including the schema-declared nullability metadata.</summary>
    private sealed class TableColumnDefinition
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        [Column("notnull")]
        public int NotNull { get; set; }

        [Column("pk")]
        public int PrimaryKey { get; set; }
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

    private sealed record RequiredIndex(
        string Table,
        string Name,
        bool Unique,
        string[] Columns,
        string? Predicate = null);
}
