using KnownFirst.Core.Learning;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema10;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema10;

/// <summary>
/// The one-time Schema-10 identity backfill (KF-BACKUP-005A). Runs inside
/// <see cref="Schema10DormantMigration"/>'s transaction, after the <c>StableId</c> columns exist and
/// before their unique indexes are created, and assigns exactly one identity to every existing
/// <c>LearningSessions</c> and <c>LearningSessionCards</c> row.
///
/// <para>The split is by portability, not by convenience:</para>
/// <list type="bullet">
/// <item><description><b>Completed</b> sessions were already exported by ordinary portable export, so two
/// installations can genuinely hold the same completed history. They receive the deterministic
/// <see cref="LearningWorkflowStableIdBootstrapPolicy"/> identity, which both installations compute
/// independently and agree on. Their queue rows cascade from that identity.</description></item>
/// <item><description><b>Active</b> sessions were never portable through any supported path, so there is
/// no counterpart anywhere to agree with. They — and each of their surviving queue rows independently —
/// receive a fresh GUID-form identity that is then immutable forever.</description></item>
/// </list>
///
/// <para>Reads use the Schema-9 physical column lists only. The <c>StableId</c> column exists by now but
/// is still empty, so nothing here may read it; this is also why the backfill can never depend on a value
/// it is itself producing.</para>
/// </summary>
internal static class Schema10LearningIdentityBootstrap
{
    public static void Apply(SQLiteConnection connection)
    {
        var sessions = connection.Query<BootstrapSessionRow>(
            """
            SELECT Id, Status, StartedAtUtc, CompletedAtUtc
            FROM LearningSessions
            ORDER BY Id
            """);
        if (sessions.Count == 0 && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessionCards") == 0)
        {
            return;
        }

        var queueRows = connection.Query<BootstrapQueueRow>(
            """
            SELECT Id, SessionId, CardId, QueueOrder, IsAgainRepeat, Rating
            FROM LearningSessionCards
            ORDER BY SessionId, QueueOrder, Id
            """);

        var futureCardIdentityByCardId = BuildFutureCardIdentities(connection, sessions, queueRows);

        var assignedSessionStableIds = new HashSet<string>(StringComparer.Ordinal);
        var assignedQueueStableIds = new HashSet<string>(StringComparer.Ordinal);
        var queueRowsBySessionId = queueRows.GroupBy(row => row.SessionId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var session in sessions)
        {
            var ownedQueueRows = queueRowsBySessionId.TryGetValue(session.Id, out var owned) ? owned : [];
            var isCompleted = session.Status == LearningSessionStatus.Completed;

            var sessionStableId = isCompleted
                ? LearningWorkflowStableIdBootstrapPolicy.ComputeCompletedSessionStableId(
                    session.StartedAtUtc,
                    session.CompletedAtUtc,
                    [.. ownedQueueRows.Select(row => (
                        futureCardIdentityByCardId[row.CardId],
                        row.Rating is null ? (BackupReviewRating?)null : BackupEnumMappings.ToBackup(row.Rating.Value)))])
                : LearningWorkflowStableId.NewGuidForm();

            if (!assignedSessionStableIds.Add(sessionStableId))
            {
                throw Schema10MigrationException.DuplicateBootstrapIdentity(Schema10Ddl.SessionTable, sessionStableId);
            }

            connection.Execute(
                "UPDATE LearningSessions SET StableId = ? WHERE Id = ?", sessionStableId, session.Id);

            foreach (var queueRow in ownedQueueRows)
            {
                var queueStableId = isCompleted
                    ? LearningWorkflowStableIdBootstrapPolicy.ComputeCompletedQueueItemStableId(
                        sessionStableId,
                        futureCardIdentityByCardId[queueRow.CardId],
                        queueRow.QueueOrder,
                        queueRow.IsAgainRepeat)
                    : LearningWorkflowStableId.NewGuidForm();

                if (!assignedQueueStableIds.Add(queueStableId))
                {
                    throw Schema10MigrationException.DuplicateBootstrapIdentity(Schema10Ddl.QueueTable, queueStableId);
                }

                connection.Execute(
                    "UPDATE LearningSessionCards SET StableId = ? WHERE Id = ?", queueStableId, queueRow.Id);
            }
        }

        // A queue row whose SessionId matches no session cannot exist in a shape-valid database, but the
        // backfill must still leave no row without an identity — an orphan would otherwise survive as a
        // permanent NULL and fail every later capability check with no way to explain where it came from.
        var orphanCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE StableId IS NULL");
        if (orphanCount > 0)
        {
            throw Schema10MigrationException.InvariantViolation(
                $"{orphanCount} LearningSessionCards row(s) reference no existing LearningSessions row and could not be given an identity.");
        }
    }

    /// <summary>
    /// The <see cref="FutureCardIdentity"/> of every card any queue row references. Computed only when a
    /// Completed session actually needs one: a database holding nothing but Active sessions gets fresh
    /// GUIDs and must not be blocked by unrelated semantic-graph state.
    /// </summary>
    private static Dictionary<int, FutureCardIdentity> BuildFutureCardIdentities(
        SQLiteConnection connection,
        IReadOnlyList<BootstrapSessionRow> sessions,
        IReadOnlyList<BootstrapQueueRow> queueRows)
    {
        var completedSessionIds = sessions
            .Where(session => session.Status == LearningSessionStatus.Completed)
            .Select(session => session.Id)
            .ToHashSet();
        if (completedSessionIds.Count == 0 || !queueRows.Any(row => completedSessionIds.Contains(row.SessionId)))
        {
            return [];
        }

        var words = connection.Query<WordEntity>("SELECT * FROM Words");
        var senses = connection.Query<SenseRow>("SELECT * FROM Senses");
        var cards = connection.Query<Schema8CardRow>(
            """
            SELECT Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                   SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc
            FROM LearningCards
            """);

        var identities = Schema8RowSemanticIdentities.ComputeFutureCardIdentitiesByCardId(
            words, senses, cards, Schema10MigrationException.UnresolvableCardIdentity);

        foreach (var queueRow in queueRows.Where(row => completedSessionIds.Contains(row.SessionId)))
        {
            if (!identities.ContainsKey(queueRow.CardId))
            {
                throw Schema10MigrationException.UnresolvableCardIdentity(queueRow.CardId);
            }
        }

        return identities;
    }

    private sealed class BootstrapSessionRow
    {
        public int Id { get; set; }

        public LearningSessionStatus Status { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }

    private sealed class BootstrapQueueRow
    {
        public int Id { get; set; }

        public int SessionId { get; set; }

        public int CardId { get; set; }

        public int QueueOrder { get; set; }

        public bool IsAgainRepeat { get; set; }

        public ReviewRating? Rating { get; set; }
    }
}
