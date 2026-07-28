using SQLite;

namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// Single source of truth for "what does a physically valid Schema-8 shape look like" — extracted
/// from <see cref="Schema8DormantMigration"/>'s already-tested already-applied-shape check
/// (KF-MEANING-001 Slice 2) so <c>Services/DataSafety/BackupSchemaCapability</c> can reuse the exact
/// same checks instead of re-deriving them. Never referenced by <c>DatabaseSchema.InitializeAsync</c>.
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
        foreach (var table in new[] { "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress" })
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

        if (!HasColumn(connection, "LearningCards", "PreferredMeaningId"))
        {
            failureDetail = "LearningCards is missing the PreferredMeaningId column.";
            return false;
        }

        if (!IndexExists(connection, "IX_LearningCards_Sense_Direction"))
        {
            failureDetail = "IX_LearningCards_Sense_Direction is missing.";
            return false;
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
        connection.Query<TableColumnInfo>($"PRAGMA table_info({table})")
            .Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
}
