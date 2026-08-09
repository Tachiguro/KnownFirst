using SQLite;

namespace KnownFirst.Data.Schema10;

/// <summary>
/// The single decision point for "may this insert supply a learning-workflow <c>StableId</c>?", plus the
/// identity-bearing insert statements the restore and merge writers share (KF-BACKUP-005A).
///
/// <para><b>Why a physical-shape probe rather than a capability object.</b> The Schema-8 learning
/// repository, the restore-into-empty importer and the merge writer all legitimately run against a
/// Schema-8, Schema-9 <em>or</em> Schema-10 database, and their existing signatures carry a Schema-8
/// capability proof by design. Appending <c>StableId</c> to their shared statements unconditionally would
/// break every genuine Schema-8/9 execution with a missing-column error, and threading a fourth
/// capability through them would change contracts KF-BACKUP-005A is not chartered to change. Probing the
/// physical column instead keeps each schema's statement exactly as valid as it was, and is truthful:
/// the column's presence is the very thing that decides whether a value can be stored.</para>
///
/// <para>The probe is deliberately <b>not</b> cached. The same connection can cross the Schema-10
/// boundary mid-life — <c>DatabaseSchema.InitializeAsync</c> migrates on exactly the connection the app
/// then keeps using — so any cache would have to be invalidated by the migration, and a missed
/// invalidation would silently produce identity-less rows. Two <c>PRAGMA table_info</c> reads are
/// negligible next to the insert they guard, and being always-correct is worth more here than being
/// marginally faster.</para>
/// </summary>
public static class Schema10LearningIdentityWriter
{
    /// <summary>True when the connected database physically carries the Schema-10 learning-workflow identity columns.</summary>
    public static bool HasLearningWorkflowIdentity(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return HasStableIdColumn(connection, "LearningSessions")
            && HasStableIdColumn(connection, "LearningSessionCards");
    }

    /// <summary>One parameterized statement, ready to hand to whichever mutation helper the caller
    /// already uses (both the restore importer and the merge writer wrap every write in their own
    /// cancellation/failure-injection bookkeeping, so this type builds the statement and never issues
    /// it).</summary>
    public readonly record struct IdentityAwareInsert(string Sql, object?[] Arguments);

    /// <summary>
    /// Builds the session insert, appending <c>StableId</c> only when the target database has the column.
    /// A supplied identity is used verbatim; a missing or malformed one becomes a fresh GUID-form id
    /// rather than a NULL, so no Schema-10 path can produce an identity-less row.
    /// </summary>
    public static IdentityAwareInsert BuildSessionInsert(
        SQLiteConnection connection,
        int status,
        int totalCards,
        int completedCards,
        int againCount,
        int hardCount,
        int goodCount,
        int easyCount,
        DateTime startedAtUtc,
        DateTime updatedAtUtc,
        DateTime? completedAtUtc,
        string? stableId) =>
        HasLearningWorkflowIdentity(connection)
            ? new IdentityAwareInsert(
                """
                INSERT INTO LearningSessions
                    (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                     StartedAtUtc, UpdatedAtUtc, CompletedAtUtc, StableId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [
                    status, totalCards, completedCards, againCount, hardCount, goodCount, easyCount,
                    startedAtUtc, updatedAtUtc, completedAtUtc, OrFresh(stableId)
                ])
            : new IdentityAwareInsert(
                """
                INSERT INTO LearningSessions
                    (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                     StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [
                    status, totalCards, completedCards, againCount, hardCount, goodCount, easyCount,
                    startedAtUtc, updatedAtUtc, completedAtUtc
                ]);

    /// <summary>Queue-row counterpart of <see cref="BuildSessionInsert"/>, following the identical rule.</summary>
    public static IdentityAwareInsert BuildQueueInsert(
        SQLiteConnection connection,
        int sessionId,
        int cardId,
        int queueOrder,
        bool isDueCard,
        bool isAgainRepeat,
        bool answerRevealed,
        bool spellingChecked,
        bool spellingCorrect,
        bool isCompleted,
        int? rating,
        DateTime? completedAtUtc,
        int? targetAnswerVariantId,
        string? stableId) =>
        HasLearningWorkflowIdentity(connection)
            ? new IdentityAwareInsert(
                """
                INSERT INTO LearningSessionCards
                    (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                     SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId, StableId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [
                    sessionId, cardId, queueOrder, isDueCard, isAgainRepeat, answerRevealed, spellingChecked,
                    spellingCorrect, isCompleted, rating, completedAtUtc, targetAnswerVariantId, OrFresh(stableId)
                ])
            : new IdentityAwareInsert(
                """
                INSERT INTO LearningSessionCards
                    (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                     SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [
                    sessionId, cardId, queueOrder, isDueCard, isAgainRepeat, answerRevealed, spellingChecked,
                    spellingCorrect, isCompleted, rating, completedAtUtc, targetAnswerVariantId
                ]);

    private static string OrFresh(string? stableId) =>
        LearningWorkflowStableId.IsValid(stableId) ? stableId! : LearningWorkflowStableId.NewGuidForm();

    private static bool HasStableIdColumn(SQLiteConnection connection, string table) =>
        connection.Query<TableInfoRow>($"PRAGMA table_info(\"{table}\")")
            .Any(column => string.Equals(column.Name, "StableId", StringComparison.OrdinalIgnoreCase));

    private sealed class TableInfoRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
