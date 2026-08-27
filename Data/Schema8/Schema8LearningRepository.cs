using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema10;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Schema8;

/// <summary>
/// Policy-free raw-SQL access to the Schema-8 learning graph (KF-MEANING-001 Slice 4).
/// <para>
/// Binding boundary rules: every method receives the caller-owned <see cref="SQLiteConnection"/>; this type
/// never opens a connection, never begins or commits a transaction, and contains no product-policy decision.
/// Every statement uses an explicit physical column list, so a shape drift surfaces as a missing-column
/// failure rather than as a silently reordered projection. No reflection and no dynamic JSON is used, so the
/// whole type stays AOT/trimming safe.
/// </para>
/// </summary>
public static class Schema8LearningRepository
{
    private const string QueueColumns =
        "Id, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, " +
        "SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId";

    private const string CardColumns =
        "Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, " +
        "SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc";

    private const string ReviewColumns =
        "Id, CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, " +
        "EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId";

    private const string SessionColumns =
        "Id, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, " +
        "UpdatedAtUtc, CompletedAtUtc";

    private const string ProgressColumns =
        "Id, CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount, " +
        "ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc, " +
        "MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc";

    private const string VariantColumns =
        "Id, StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc";

    // ---- Queue ----

    public static Schema8QueueTargetRow? LoadQueueRow(SQLiteConnection connection, int queueItemId) =>
        connection.Query<Schema8QueueTargetRow>(
            $"SELECT {QueueColumns} FROM LearningSessionCards WHERE Id = ?", queueItemId).FirstOrDefault();

    public static List<Schema8QueueTargetRow> LoadQueueRowsForSession(SQLiteConnection connection, int sessionId) =>
        connection.Query<Schema8QueueTargetRow>(
            $"SELECT {QueueColumns} FROM LearningSessionCards WHERE SessionId = ? ORDER BY QueueOrder, Id", sessionId);

    public static Schema8QueueTargetRow? LoadFirstIncompleteQueueRow(SQLiteConnection connection, int sessionId) =>
        connection.Query<Schema8QueueTargetRow>(
            $"SELECT {QueueColumns} FROM LearningSessionCards WHERE SessionId = ? AND IsCompleted = 0 ORDER BY QueueOrder, Id LIMIT 1",
            sessionId).FirstOrDefault();

    public static List<Schema8QueueTargetRow> LoadQueueRowsForCard(SQLiteConnection connection, int cardId) =>
        connection.Query<Schema8QueueTargetRow>(
            $"SELECT {QueueColumns} FROM LearningSessionCards WHERE CardId = ? ORDER BY Id", cardId);

    public static List<Schema8QueueTargetRow> LoadIncompleteQueueRowsForCard(SQLiteConnection connection, int cardId) =>
        connection.Query<Schema8QueueTargetRow>(
            $"SELECT {QueueColumns} FROM LearningSessionCards WHERE CardId = ? AND IsCompleted = 0 ORDER BY Id", cardId);

    /// <summary>
    /// Inserts one fresh queue row. On a Schema-10 database the statement additionally supplies a fresh
    /// <c>StableId</c>; on Schema 8/9 the identical Schema-8 column list is issued unchanged, because
    /// those databases have no such column. The branch is decided by
    /// <see cref="Schema10LearningIdentityWriter.HasLearningWorkflowIdentity"/> — i.e. by the physical
    /// shape actually present — never by appending a column to a query that still runs against Schema 8.
    /// </summary>
    public static void InsertQueueRow(
        SQLiteConnection connection, int sessionId, int cardId, int queueOrder, bool isDueCard,
        int targetAnswerVariantId)
    {
        if (Schema10LearningIdentityWriter.HasLearningWorkflowIdentity(connection))
        {
            connection.Execute(
                """
                INSERT INTO LearningSessionCards
                    (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                     SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId, StableId)
                VALUES (?, ?, ?, ?, 0, 0, 0, 0, 0, NULL, NULL, ?, ?)
                """,
                sessionId, cardId, queueOrder, isDueCard, targetAnswerVariantId,
                LearningWorkflowStableId.NewGuidForm());
            return;
        }

        connection.Execute(
            """
            INSERT INTO LearningSessionCards
                (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                 SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
            VALUES (?, ?, ?, ?, 0, 0, 0, 0, 0, NULL, NULL, ?)
            """,
            sessionId, cardId, queueOrder, isDueCard, targetAnswerVariantId);
    }

    public static int CountQueueRows(SQLiteConnection connection, int sessionId) =>
        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessionCards WHERE SessionId = ?", sessionId);

    public static int CountIncompleteQueueRows(SQLiteConnection connection, int sessionId) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE SessionId = ? AND IsCompleted = 0", sessionId);

    public static int MaxQueueOrder(SQLiteConnection connection, int sessionId) =>
        connection.ExecuteScalar<int?>(
            "SELECT MAX(QueueOrder) FROM LearningSessionCards WHERE SessionId = ?", sessionId) ?? -1;

    public static void SetQueueAnswerRevealed(SQLiteConnection connection, int queueItemId) =>
        connection.Execute("UPDATE LearningSessionCards SET AnswerRevealed = 1 WHERE Id = ?", queueItemId);

    public static void SetQueueSpellingResult(
        SQLiteConnection connection, int queueItemId, bool spellingCorrect) =>
        connection.Execute(
            "UPDATE LearningSessionCards SET SpellingChecked = 1, SpellingCorrect = ?, AnswerRevealed = 1 WHERE Id = ?",
            spellingCorrect, queueItemId);

    public static void CompleteQueueRow(
        SQLiteConnection connection, int queueItemId, ReviewRating rating, DateTime completedAtUtc) =>
        connection.Execute(
            "UPDATE LearningSessionCards SET IsCompleted = 1, Rating = ?, CompletedAtUtc = ? WHERE Id = ?",
            (int)rating, completedAtUtc, queueItemId);

    /// <summary>
    /// Inserts the Again repeat of an existing queue row. The <c>INSERT ... SELECT</c> copies only the
    /// source row's session, card and answer-variant target — on Schema 10 the new row's <c>StableId</c>
    /// is a freshly generated parameter, never <c>SELECT StableId</c>. An Again repeat is a new queue
    /// position: copying the source identity would collide on the unique index and, worse, would assert
    /// that the two attempts are one.
    /// </summary>
    public static void InsertAgainRepeatQueueRow(
        SQLiteConnection connection, int sourceQueueItemId, int queueOrder)
    {
        if (Schema10LearningIdentityWriter.HasLearningWorkflowIdentity(connection))
        {
            connection.Execute(
                """
                INSERT INTO LearningSessionCards
                    (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                     SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId, StableId)
                SELECT SessionId, CardId, ?, 0, 1, 0, 0, 0, 0, NULL, NULL, TargetAnswerVariantId, ?
                FROM LearningSessionCards WHERE Id = ?
                """,
                queueOrder, LearningWorkflowStableId.NewGuidForm(), sourceQueueItemId);
            return;
        }

        connection.Execute(
            """
            INSERT INTO LearningSessionCards
                (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                 SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
            SELECT SessionId, CardId, ?, 0, 1, 0, 0, 0, 0, NULL, NULL, TargetAnswerVariantId
            FROM LearningSessionCards WHERE Id = ?
            """,
            queueOrder, sourceQueueItemId);
    }

    public static void DeleteQueueRow(SQLiteConnection connection, int queueItemId) =>
        connection.Execute("DELETE FROM LearningSessionCards WHERE Id = ?", queueItemId);

    // ---- Cards ----

    public static Schema8CardRow? LoadCard(SQLiteConnection connection, int cardId) =>
        connection.Query<Schema8CardRow>(
            $"SELECT {CardColumns} FROM LearningCards WHERE Id = ?", cardId).FirstOrDefault();

    public static List<Schema8CardRow> LoadAllCards(SQLiteConnection connection) =>
        connection.Query<Schema8CardRow>($"SELECT {CardColumns} FROM LearningCards ORDER BY Id");

    public static List<Schema8CardRow> LoadCardsForWord(SQLiteConnection connection, int wordId) =>
        connection.Query<Schema8CardRow>(
            $"SELECT {CardColumns} FROM LearningCards WHERE WordId = ? ORDER BY Id", wordId);

    public static List<Schema8QueueWordRow> LoadQueueWords(SQLiteConnection connection) =>
        connection.Query<Schema8QueueWordRow>(
            "SELECT Id, CanonicalTerm, TotalOccurrenceCount, CreatedAt FROM Words ORDER BY Id");

    public static List<Schema8CardRow> LoadCardsForSense(SQLiteConnection connection, int senseId) =>
        connection.Query<Schema8CardRow>(
            $"SELECT {CardColumns} FROM LearningCards WHERE SenseId = ? ORDER BY Id", senseId);

    public static int CountCardsForSenseDirection(SQLiteConnection connection, int senseId, CardDirection direction) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningCards WHERE SenseId = ? AND Direction = ?", senseId, (int)direction);

    public static void UpdateCardSchedule(
        SQLiteConnection connection, int cardId, CardSchedule schedule, DateTime updatedAtUtc) =>
        connection.Execute(
            """
            UPDATE LearningCards
            SET State = ?, DueAtUtc = ?, IntervalDays = ?, EaseFactor = ?, SuccessfulReviewCount = ?,
                LapseCount = ?, LastReviewedAtUtc = ?, LastRating = ?, UpdatedAtUtc = ?
            WHERE Id = ?
            """,
            (int)schedule.State, schedule.DueAtUtc, schedule.IntervalDays, schedule.EaseFactor,
            schedule.SuccessfulReviewCount, schedule.LapseCount, schedule.LastReviewedAtUtc,
            schedule.LastRating.HasValue ? (int?)schedule.LastRating.Value : null, updatedAtUtc, cardId);

    /// <summary>Changes only <c>State</c> and <c>UpdatedAtUtc</c>; every scheduling field is preserved.</summary>
    public static void UpdateCardState(
        SQLiteConnection connection, int cardId, CardState state, DateTime updatedAtUtc) =>
        connection.Execute(
            "UPDATE LearningCards SET State = ?, UpdatedAtUtc = ? WHERE Id = ?", (int)state, updatedAtUtc, cardId);

    // ---- Senses ----

    public static Schema8SenseStatusRow? LoadSense(SQLiteConnection connection, int senseId) =>
        connection.Query<Schema8SenseStatusRow>(
            "SELECT Id, WordId, Status FROM Senses WHERE Id = ?", senseId).FirstOrDefault();

    public static List<Schema8SenseStatusRow> LoadSensesForWord(SQLiteConnection connection, int wordId) =>
        connection.Query<Schema8SenseStatusRow>(
            "SELECT Id, WordId, Status FROM Senses WHERE WordId = ? ORDER BY Id", wordId);

    // ---- Meanings and contexts ----

    public static Schema8MeaningRow? LoadMeaning(SQLiteConnection connection, int meaningId) =>
        connection.Query<Schema8MeaningRow>(
            "SELECT * FROM Meanings WHERE Id = ?", meaningId).FirstOrDefault();

    public static List<Schema8MeaningRow> LoadMeaningsForWord(SQLiteConnection connection, int wordId) =>
        connection.Query<Schema8MeaningRow>(
            "SELECT * FROM Meanings WHERE WordId = ? ORDER BY Id", wordId);

    public static List<Schema8ContextRow> LoadContextsForMeaning(SQLiteConnection connection, int meaningId) =>
        connection.Query<Schema8ContextRow>(
            "SELECT * FROM ContextSnapshots WHERE MeaningId = ? ORDER BY Id", meaningId);

    public static void UpdateSenseStatus(
        SQLiteConnection connection, int senseId, SenseStatus status, DateTime updatedAtUtc) =>
        connection.Execute(
            "UPDATE Senses SET Status = ?, UpdatedAtUtc = ? WHERE Id = ?", (int)status, updatedAtUtc, senseId);

    // ---- Answer variants and assignments ----

    public static AnswerVariantRow? LoadAnswerVariant(SQLiteConnection connection, int answerVariantId) =>
        connection.Query<AnswerVariantRow>(
            $"SELECT {VariantColumns} FROM AnswerVariants WHERE Id = ?", answerVariantId).FirstOrDefault();

    /// <summary>
    /// Every assignment of one <c>(SenseId, CardDirection)</c> joined with its variant text, ordered
    /// deterministically by assignment Id. This is the complete attribution surface of one card direction.
    /// </summary>
    public static List<Schema8AttributionCandidateRow> LoadAssignmentsForSenseDirection(
        SQLiteConnection connection, int senseId, CardDirection direction) =>
        connection.Query<Schema8AttributionCandidateRow>(
            """
            SELECT a.Id AS AssignmentId, a.StableId AS AssignmentStableId, a.SenseId AS SenseId,
                   a.CardDirection AS CardDirection, a.AnswerVariantId AS AnswerVariantId,
                   a.Requirement AS Requirement, a.IsPreferred AS IsPreferred,
                   a.RequiredSinceUtc AS RequiredSinceUtc, v.AnswerLanguage AS AnswerLanguage,
                   v.DisplayText AS DisplayText, v.NormalizedText AS NormalizedText
            FROM SenseAnswerVariantAssignments a
            JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            ORDER BY a.Id
            """,
            senseId, (int)direction);

    public static int CountAssignmentRowsForSenseDirection(
        SQLiteConnection connection, int senseId, CardDirection direction) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)direction);

    public static int CountInvalidVariantReferencesForSenseDirection(
        SQLiteConnection connection, int senseId, CardDirection direction) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM SenseAnswerVariantAssignments a
            LEFT JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
              AND (v.Id IS NULL OR v.SenseId <> a.SenseId)
            """,
            senseId, (int)direction);

    public static Schema8AttributionCandidateRow? LoadAssignment(
        SQLiteConnection connection, int senseId, CardDirection direction, int answerVariantId) =>
        LoadAssignmentsForSenseDirection(connection, senseId, direction)
            .SingleOrDefault(row => row.AnswerVariantId == answerVariantId);

    public static int CountAssignments(
        SQLiteConnection connection, int senseId, CardDirection direction, int answerVariantId) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments
            WHERE SenseId = ? AND CardDirection = ? AND AnswerVariantId = ?
            """,
            senseId, (int)direction, answerVariantId);

    public static int CountPreferredAssignments(
        SQLiteConnection connection, int senseId, CardDirection direction) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments
            WHERE SenseId = ? AND CardDirection = ? AND IsPreferred = 1
            """,
            senseId, (int)direction);

    public static void InsertAssignment(
        SQLiteConnection connection,
        string stableId,
        int senseId,
        CardDirection direction,
        int answerVariantId,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTime? requiredSinceUtc,
        DateTime timestampUtc) =>
        connection.Execute(
            """
            INSERT INTO SenseAnswerVariantAssignments
                (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId, senseId, (int)direction, answerVariantId, (int)requirement, isPreferred,
            requiredSinceUtc, timestampUtc, timestampUtc);

    /// <summary>Inserts an assignment and returns its new physical Id, so no caller has to issue raw SQL.</summary>
    public static int InsertAssignmentAndGetId(
        SQLiteConnection connection,
        string stableId,
        int senseId,
        CardDirection direction,
        int answerVariantId,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTime? requiredSinceUtc,
        DateTime timestampUtc)
    {
        InsertAssignment(
            connection, stableId, senseId, direction, answerVariantId, requirement, isPreferred,
            requiredSinceUtc, timestampUtc);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    /// <summary>Updates only requirement, preference, boundary and <c>UpdatedAtUtc</c>. <c>StableId</c> and <c>CreatedAtUtc</c> are never touched.</summary>
    public static void UpdateAssignment(
        SQLiteConnection connection,
        int assignmentId,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTime? requiredSinceUtc,
        DateTime updatedAtUtc) =>
        connection.Execute(
            """
            UPDATE SenseAnswerVariantAssignments
            SET Requirement = ?, IsPreferred = ?, RequiredSinceUtc = ?, UpdatedAtUtc = ?
            WHERE Id = ?
            """,
            (int)requirement, isPreferred, requiredSinceUtc, updatedAtUtc, assignmentId);

    /// <summary>
    /// Clears every preferred flag of one <c>(SenseId, CardDirection)</c> except <paramref name="exceptAssignmentId"/>.
    /// Must run before setting a new preferred row so the partial unique index is never transiently violated.
    /// </summary>
    public static void ClearPreferredForSenseDirection(
        SQLiteConnection connection, int senseId, CardDirection direction, int exceptAssignmentId, DateTime updatedAtUtc) =>
        connection.Execute(
            """
            UPDATE SenseAnswerVariantAssignments
            SET IsPreferred = 0, UpdatedAtUtc = ?
            WHERE SenseId = ? AND CardDirection = ? AND IsPreferred = 1 AND Id <> ?
            """,
            updatedAtUtc, senseId, (int)direction, exceptAssignmentId);

    // ---- Reviews ----

    public static List<Schema8ReviewRow> LoadReviewsForCard(SQLiteConnection connection, int cardId) =>
        connection.Query<Schema8ReviewRow>(
            $"SELECT {ReviewColumns} FROM LearningReviews WHERE CardId = ? ORDER BY ReviewedAtUtc, Id", cardId);

    public static int InsertReview(
        SQLiteConnection connection,
        int cardId,
        int sessionId,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        DateTime reviewedAtUtc,
        CardSchedule postReviewSchedule)
    {
        connection.Execute(
            """
            INSERT INTO LearningReviews
                (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays,
                 EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, NULL)
            """,
            cardId, sessionId, (int)rating, wasTypedAnswer, wasCorrect, reviewedAtUtc,
            postReviewSchedule.DueAtUtc, postReviewSchedule.IntervalDays, postReviewSchedule.EaseFactor);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    public static void UpdateReviewAttribution(
        SQLiteConnection connection, int reviewId, int? targetAnswerVariantId, int? matchedAnswerVariantId) =>
        connection.Execute(
            "UPDATE LearningReviews SET TargetAnswerVariantId = ?, MatchedAnswerVariantId = ? WHERE Id = ?",
            targetAnswerVariantId, matchedAnswerVariantId, reviewId);

    // ---- Progress ----

    public static List<AnswerVariantProgressRow> LoadProgressForCard(SQLiteConnection connection, int cardId) =>
        connection.Query<AnswerVariantProgressRow>(
            $"SELECT {ProgressColumns} FROM AnswerVariantProgress WHERE CardId = ? ORDER BY AnswerVariantId", cardId);

    public static int CountInvalidProgressRowsForCard(SQLiteConnection connection, int cardId) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM AnswerVariantProgress p
            JOIN LearningCards c ON c.Id = p.CardId
            LEFT JOIN AnswerVariants v ON v.Id = p.AnswerVariantId
            LEFT JOIN SenseAnswerVariantAssignments a
              ON a.SenseId = c.SenseId
             AND a.CardDirection = c.Direction
             AND a.AnswerVariantId = p.AnswerVariantId
            WHERE p.CardId = ? AND (v.Id IS NULL OR v.SenseId <> c.SenseId OR a.Id IS NULL)
            """,
            cardId);

    public static void InsertProgress(SQLiteConnection connection, AnswerVariantProgressRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        connection.Execute(
            """
            INSERT INTO AnswerVariantProgress
                (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                 MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            row.CardId, row.AnswerVariantId, (int)row.InteractionMode, row.ConsecutiveReadingSuccessCount,
            row.ConsecutiveTypingSuccessCount, row.ConsecutiveTypingFailureCount, row.LastAssessedAtUtc,
            row.MasteryReviewExtensionScheduled, row.IsMastered, row.ReplayVersion, row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    public static void UpdateProgress(SQLiteConnection connection, AnswerVariantProgressRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        connection.Execute(
            """
            UPDATE AnswerVariantProgress
            SET InteractionMode = ?, ConsecutiveReadingSuccessCount = ?, ConsecutiveTypingSuccessCount = ?,
                ConsecutiveTypingFailureCount = ?, LastAssessedAtUtc = ?, MasteryReviewExtensionScheduled = ?,
                IsMastered = ?, ReplayVersion = ?, CreatedAtUtc = ?, UpdatedAtUtc = ?
            WHERE CardId = ? AND AnswerVariantId = ?
            """,
            (int)row.InteractionMode, row.ConsecutiveReadingSuccessCount, row.ConsecutiveTypingSuccessCount,
            row.ConsecutiveTypingFailureCount, row.LastAssessedAtUtc, row.MasteryReviewExtensionScheduled,
            row.IsMastered, row.ReplayVersion, row.CreatedAtUtc, row.UpdatedAtUtc,
            row.CardId, row.AnswerVariantId);
    }

    public static void DeleteProgress(SQLiteConnection connection, int cardId, int answerVariantId) =>
        connection.Execute(
            "DELETE FROM AnswerVariantProgress WHERE CardId = ? AND AnswerVariantId = ?", cardId, answerVariantId);

    // ---- Sessions ----

    public static Schema8SessionCounterRow? LoadSession(SQLiteConnection connection, int sessionId) =>
        connection.Query<Schema8SessionCounterRow>(
            $"SELECT {SessionColumns} FROM LearningSessions WHERE Id = ?", sessionId).FirstOrDefault();

    /// <summary>
    /// Starts a new Active session. On Schema 10 it receives a fresh <c>StableId</c> at creation — not at
    /// completion — so the identity exists for the whole life of the workflow and is already immutable
    /// before the session ever transitions to Completed.
    /// </summary>
    public static int InsertSession(SQLiteConnection connection, DateTime startedAtUtc, int totalCards)
    {
        if (Schema10LearningIdentityWriter.HasLearningWorkflowIdentity(connection))
        {
            connection.Execute(
                """
                INSERT INTO LearningSessions
                    (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                     StartedAtUtc, UpdatedAtUtc, CompletedAtUtc, StableId)
                VALUES (?, ?, 0, 0, 0, 0, 0, ?, ?, NULL, ?)
                """,
                (int)LearningSessionStatus.Active, totalCards, startedAtUtc, startedAtUtc,
                LearningWorkflowStableId.NewGuidForm());
            return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
        }

        connection.Execute(
            """
            INSERT INTO LearningSessions
                (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                 StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, ?, 0, 0, 0, 0, 0, ?, ?, NULL)
            """,
            (int)LearningSessionStatus.Active, totalCards, startedAtUtc, startedAtUtc);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    public static List<Schema8SessionCounterRow> LoadActiveSessions(SQLiteConnection connection) =>
        connection.Query<Schema8SessionCounterRow>(
            $"SELECT {SessionColumns} FROM LearningSessions WHERE Status = ? ORDER BY Id",
            (int)LearningSessionStatus.Active);

    public static Schema8SessionCounterRow? LoadLatestCompletedSession(SQLiteConnection connection) =>
        connection.Query<Schema8SessionCounterRow>(
            $"SELECT {SessionColumns} FROM LearningSessions WHERE Status = ? ORDER BY Id DESC LIMIT 1",
            (int)LearningSessionStatus.Completed).FirstOrDefault();

    public static void UpdateSessionCounters(SQLiteConnection connection, Schema8SessionCounterRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        connection.Execute(
            """
            UPDATE LearningSessions
            SET Status = ?, TotalCards = ?, CompletedCards = ?, AgainCount = ?, HardCount = ?, GoodCount = ?,
                EasyCount = ?, UpdatedAtUtc = ?, CompletedAtUtc = ?
            WHERE Id = ?
            """,
            (int)row.Status, row.TotalCards, row.CompletedCards, row.AgainCount, row.HardCount, row.GoodCount,
            row.EasyCount, row.UpdatedAtUtc, row.CompletedAtUtc, row.Id);
    }

    public static void DeleteSession(SQLiteConnection connection, int sessionId) =>
        connection.Execute("DELETE FROM LearningSessions WHERE Id = ?", sessionId);

    public static int CountReviewsWithRating(SQLiteConnection connection, int sessionId, ReviewRating rating) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningReviews WHERE SessionId = ? AND Rating = ?", sessionId, (int)rating);

    public static int CountReviewsForSession(SQLiteConnection connection, int sessionId) =>
        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews WHERE SessionId = ?", sessionId);

    public static List<int> LoadAffectedSessionIdsForWord(SQLiteConnection connection, int wordId) =>
        connection.Query<Schema8IdRow>(
            """
            SELECT DISTINCT SessionId AS Id FROM LearningSessionCards
            WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?)
            UNION
            SELECT DISTINCT SessionId AS Id FROM LearningReviews
            WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?)
            ORDER BY Id
            """,
            wordId, wordId).Select(row => row.Id).ToList();

    public static void DeleteWordLearningGraph(SQLiteConnection connection, int wordId)
    {
        connection.Execute(
            "DELETE FROM ContextSnapshots WHERE MeaningId IN (SELECT Id FROM Meanings WHERE WordId = ?)", wordId);
        connection.Execute(
            "DELETE FROM LearningSessionCards WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?)", wordId);
        connection.Execute(
            "DELETE FROM LearningReviews WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?)", wordId);
        connection.Execute(
            "DELETE FROM AnswerVariantProgress WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?)", wordId);
        connection.Execute("DELETE FROM LearningCards WHERE WordId = ?", wordId);
        connection.Execute(
            "DELETE FROM SenseAnswerVariantAssignments WHERE SenseId IN (SELECT Id FROM Senses WHERE WordId = ?)", wordId);
        connection.Execute(
            "DELETE FROM AnswerVariants WHERE SenseId IN (SELECT Id FROM Senses WHERE WordId = ?)", wordId);
        connection.Execute("DELETE FROM Meanings WHERE WordId = ?", wordId);
        connection.Execute("DELETE FROM Senses WHERE WordId = ?", wordId);
    }

    // ---- Summary projections ----

    /// <summary>
    /// The approved Schema-8 meaning of <c>RemainingUnpreparedCount</c>: distinct Words in the Unknown
    /// backlog that carry no Sense at all. Deliberately a stable lower bound — it never reads
    /// <c>PreparationCandidates.ResultJson</c>, never scans evidence and never counts provider-meaning
    /// indexes.
    /// </summary>
    public static int CountRemainingUnprepared(SQLiteConnection connection) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM Words w
            WHERE w.Status = ?
              AND w.PreparationState <> ?
              AND NOT EXISTS (SELECT 1 FROM Senses s WHERE s.WordId = w.Id)
            """,
            (int)WordStatus.UnknownBacklog, (int)PreparationState.Prepared);

    /// <summary>Earliest due date across scheduled cards (Learning, Review, Relearning); <see langword="null"/> when none exist.</summary>
    public static DateTime? SelectNextDueAtUtc(SQLiteConnection connection) =>
        Schema8Utc.Normalize(connection.Query<Schema8NullableTimestampRow>(
            """
            SELECT MIN(DueAtUtc) AS Value FROM LearningCards
            WHERE State IN (?, ?, ?)
            """,
            (int)CardState.Learning, (int)CardState.Review, (int)CardState.Relearning).FirstOrDefault()?.Value);

    // ---- Schema 12 Learning Day & Daily Limit ----

    public static bool HasEverBeenLearned(SQLiteConnection connection, int wordId) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM LearningReviews r
            JOIN LearningCards c ON r.CardId = c.Id
            WHERE c.WordId = ?
            """,
            wordId) > 0;

    public static HashSet<int> LoadEverLearnedWordIds(SQLiteConnection connection) =>
        connection.Query<Schema8IdRow>(
                """
                SELECT DISTINCT c.WordId AS Id
                FROM LearningReviews r
                JOIN LearningCards c ON r.CardId = c.Id
                ORDER BY c.WordId
                """)
            .Select(row => row.Id)
            .ToHashSet();

    public static List<Schema8QueueTargetRow> LoadIncompleteQueueRowsForSession(SQLiteConnection connection, int sessionId) =>
        connection.Query<Schema8QueueTargetRow>(
            $"SELECT {QueueColumns} FROM LearningSessionCards WHERE SessionId = ? AND IsCompleted = 0 ORDER BY QueueOrder, Id",
            sessionId);

    public static Schema12LearningDayStateRow? LoadLearningDayState(SQLiteConnection connection)
    {
        if (!Schema12DdlTableExists(connection, "LearningDayState"))
        {
            return null;
        }

        return connection.Query<Schema12LearningDayStateRow>(
            """
            SELECT Id, Phase, DayOrdinal, ActiveDayStartUtc, ActiveDayEndUtc, FrozenTimeZoneId,
                   FrozenCutoffMinutes, BridgeStartedUtc, BridgeTargetTimeZoneId,
                   BridgeTargetCutoffMinutes, BridgeTargetUtc, UpdatedAtUtc
            FROM LearningDayState
            WHERE Id = 1
            """).FirstOrDefault();
    }

    public static void UpsertLearningDayState(SQLiteConnection connection, Schema12LearningDayStateRow state)
    {
        connection.Execute(
            """
            INSERT OR REPLACE INTO LearningDayState
                (Id, Phase, DayOrdinal, ActiveDayStartUtc, ActiveDayEndUtc, FrozenTimeZoneId,
                 FrozenCutoffMinutes, BridgeStartedUtc, BridgeTargetTimeZoneId,
                 BridgeTargetCutoffMinutes, BridgeTargetUtc, UpdatedAtUtc)
            VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (int)state.Phase, state.DayOrdinal, state.ActiveDayStartUtc, state.ActiveDayEndUtc,
            state.FrozenTimeZoneId, state.FrozenCutoffMinutes, state.BridgeStartedUtc,
            state.BridgeTargetTimeZoneId, state.BridgeTargetCutoffMinutes,
            state.BridgeTargetUtc, state.UpdatedAtUtc);
    }

    public static List<Schema12LearningDayGrantRow> LoadGrantsForDay(SQLiteConnection connection, int dayOrdinal)
    {
        if (!Schema12DdlTableExists(connection, "LearningDayGrants"))
        {
            return [];
        }

        return connection.Query<Schema12LearningDayGrantRow>(
            """
            SELECT Id, DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc
            FROM LearningDayGrants
            WHERE DayOrdinal = ?
            ORDER BY SlotOrdinal, Id
            """,
            dayOrdinal);
    }

    public static Schema12LearningDayGrantRow? LoadGrantForDayAndWord(SQLiteConnection connection, int dayOrdinal, int wordId)
    {
        if (!Schema12DdlTableExists(connection, "LearningDayGrants"))
        {
            return null;
        }

        return connection.Query<Schema12LearningDayGrantRow>(
            """
            SELECT Id, DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc
            FROM LearningDayGrants
            WHERE DayOrdinal = ? AND WordId = ?
            LIMIT 1
            """,
            dayOrdinal, wordId).FirstOrDefault();
    }

    public static int CountGrantsForDay(SQLiteConnection connection, int dayOrdinal)
    {
        if (!Schema12DdlTableExists(connection, "LearningDayGrants"))
        {
            return 0;
        }

        return connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = ?", dayOrdinal);
    }

    public static void InsertDayGrant(
        SQLiteConnection connection, int dayOrdinal, int wordId, int slotOrdinal, DateTime grantedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc)
            VALUES (?, ?, ?, ?)
            """,
            dayOrdinal, wordId, slotOrdinal, grantedAtUtc);
    }

    private static bool Schema12DdlTableExists(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;
}
