namespace KnownFirst.Data.Migrations.Schema10;

/// <summary>
/// Raw DDL for the Schema-10 learning-workflow identity shape. Issued only from inside
/// <see cref="Schema10DormantMigration"/> — never from <c>DatabaseSchema.InitializeAsync</c>, and never
/// from the Schema-7 baseline entity classes. <c>LearningSessionEntity</c> and
/// <c>LearningSessionCardEntity</c> deliberately do not declare <c>StableId</c>: if they did,
/// <c>CreateTableAsync</c> would pre-create the column on a fresh database and the
/// <c>ALTER TABLE ... ADD COLUMN</c> below would fail with a duplicate-column error instead of running.
///
/// <para>The columns are added nullable and are backfilled inside the same transaction, because SQLite's
/// <c>ADD COLUMN</c> cannot express "NOT NULL with no default" against an existing populated table. The
/// non-null/canonical-shape guarantee is therefore enforced by <see cref="Schema10ShapeValidator"/> as an
/// integrity invariant — the same way <c>Schema8ShapeValidator</c> already enforces relationship
/// invariants that no column declaration can express — and a unique index is created on each column once
/// every row carries a value.</para>
/// </summary>
internal static class Schema10Ddl
{
    public const string SessionTable = "LearningSessions";

    public const string QueueTable = "LearningSessionCards";

    public const string StableIdColumn = "StableId";

    public const string SessionStableIdIndexName = "IX_LearningSessions_StableId";

    public const string QueueStableIdIndexName = "IX_LearningSessionCards_StableId";

    public const string AddSessionStableIdColumn =
        $"ALTER TABLE {SessionTable} ADD COLUMN {StableIdColumn} TEXT NULL";

    public const string AddQueueStableIdColumn =
        $"ALTER TABLE {QueueTable} ADD COLUMN {StableIdColumn} TEXT NULL";

    public const string CreateSessionStableIdIndex =
        $"CREATE UNIQUE INDEX {SessionStableIdIndexName} ON {SessionTable} ({StableIdColumn})";

    public const string CreateQueueStableIdIndex =
        $"CREATE UNIQUE INDEX {QueueStableIdIndexName} ON {QueueTable} ({StableIdColumn})";

    /// <summary>
    /// Counts rows whose <c>StableId</c> is absent or not canonical. The <c>GLOB</c> class already excludes
    /// uppercase <c>A-F</c>, so it enforces the lowercase rule too — no separate <c>LOWER()</c> comparison
    /// is needed. Kept as SQL rather than a C# scan so the check costs one statement regardless of table
    /// size.
    /// </summary>
    public static string CountInvalidStableIds(string table) =>
        $"""
        SELECT COUNT(*) FROM {table}
        WHERE {StableIdColumn} IS NULL
           OR LENGTH({StableIdColumn}) NOT IN (32, 64)
           OR {StableIdColumn} GLOB '*[^0-9a-f]*'
        """;
}
