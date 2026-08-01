using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>One durable review event, projected for local replay (KF-MEANING-001 Slice 4).</summary>
public sealed record Schema8ReplayReviewEvent(
    int ReviewId,
    int CardId,
    ReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor,
    int? TargetAnswerVariantId,
    int? MatchedAnswerVariantId);

/// <summary>A deduplicated review event together with its logical fingerprint.</summary>
public sealed record Schema8ReplaySurvivor(Schema8ReplayReviewEvent Event, string Fingerprint);

/// <summary>
/// How one event relates to one answer variant. <see cref="NotAttributed"/> covers the binding
/// synonym-credit rule: when a different variant was matched, the <em>targeted</em> variant is neither
/// advanced, reset, nor failed.
/// </summary>
public enum Schema8EventAttribution
{
    NotAttributed,
    Success,
    Failure
}

/// <summary>
/// The replayed outcome for one <c>(CardId, AnswerVariantId)</c> pair inside one Required epoch.
/// </summary>
public sealed record Schema8ReplayVariantOutcome(
    int CardId,
    int AnswerVariantId,
    DateTime RequiredSinceUtc,
    AutomaticLearningState State,
    bool IsMastered,
    DateTime? LastAssessedAtUtc,
    int ConsumedEventCount)
{
    /// <summary>The complete expected persisted row for this outcome (deterministic, wall-clock free).</summary>
    public AnswerVariantProgressRow ToRow() => new()
    {
        CardId = CardId,
        AnswerVariantId = AnswerVariantId,
        InteractionMode = State.InteractionMode,
        ConsecutiveReadingSuccessCount = State.ConsecutiveRecallSuccesses,
        ConsecutiveTypingSuccessCount = State.ConsecutiveTypingSuccesses,
        ConsecutiveTypingFailureCount = State.ConsecutiveTypingFailures,
        LastAssessedAtUtc = LastAssessedAtUtc,
        MasteryReviewExtensionScheduled = State.MasteryReviewExtensionScheduled,
        IsMastered = IsMastered,
        ReplayVersion = Schema8LearningReviewReplayPolicy.ReplayVersion,
        CreatedAtUtc = RequiredSinceUtc,
        UpdatedAtUtc = LastAssessedAtUtc ?? RequiredSinceUtc
    };
}

/// <summary>A non-fatal replay observation, surfaced instead of being silently dropped.</summary>
public sealed record Schema8ReplayDiagnostic(string Code, string Detail);

/// <summary>The complete replay result for one card.</summary>
public sealed record Schema8ReplayResult(
    IReadOnlyList<Schema8ReplaySurvivor> Survivors,
    IReadOnlyList<Schema8ReplayVariantOutcome> Outcomes,
    IReadOnlyList<Schema8ReplayDiagnostic> Diagnostics)
{
    public Schema8ReplayVariantOutcome? FindOutcome(int answerVariantId) =>
        Outcomes.FirstOrDefault(outcome => outcome.AnswerVariantId == answerVariantId);
}

/// <summary>
/// The complete, pre-computed progress replacement for one card. Constructed in full before the first
/// mutation, so a fault-injection checkpoint can abort between planning and applying, and so an unchanged
/// second replay is provably a zero-write operation (<see cref="IsEmpty"/>).
/// </summary>
public sealed record Schema8ProgressReplacementPlan(
    IReadOnlyList<AnswerVariantProgressRow> Inserts,
    IReadOnlyList<AnswerVariantProgressRow> Updates,
    IReadOnlyList<int> DeletedAnswerVariantIds)
{
    public static Schema8ProgressReplacementPlan Empty { get; } = new([], [], []);

    public bool IsEmpty => Inserts.Count == 0 && Updates.Count == 0 && DeletedAnswerVariantIds.Count == 0;

    public int MutationCount => Inserts.Count + Updates.Count + DeletedAnswerVariantIds.Count;
}

/// <summary>What a card-retirement cleanup actually removed and finalized.</summary>
public sealed record Schema8RetirementCleanupResult(
    int RemovedIncompleteQueueRows,
    IReadOnlyList<int> FinalizedSessionIds);

/// <summary>
/// Shared per-card retirement, queue-pruning and affected-Sense rollup policy (KF-MEANING-001 Slice 4), used
/// identically by the Schema-8 rating transaction and by <see cref="Schema8AnswerAssignmentService"/> so the
/// two never grow divergent copies. Pure policy over the caller-owned connection: it opens no connection and
/// begins no transaction.
/// </summary>
public static class Schema8CardRetirementPolicy
{
    /// <summary>
    /// A card is retirement-eligible only when it has at least one currently Required assignment and every
    /// one of them is mastered in its current epoch. A card with zero Required assignments never
    /// auto-masters and never newly retires.
    /// </summary>
    public static bool AllRequiredMastered(Schema8ReplayResult replayResult)
    {
        ArgumentNullException.ThrowIfNull(replayResult);
        return replayResult.Outcomes.Count > 0 && replayResult.Outcomes.All(outcome => outcome.IsMastered);
    }

    /// <summary>
    /// Deletes only the <em>incomplete</em> queue rows of one card, decrements each affected session's
    /// <c>TotalCards</c> by exactly the number of rows removed from it, and finalizes an active session that
    /// is left with queue history but no incomplete rows. Completed queue history is never deleted.
    /// </summary>
    public static Schema8RetirementCleanupResult PruneIncompleteQueueRowsForCard(
        SQLiteConnection connection, int cardId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var removedBySession = new Dictionary<int, int>();
        foreach (var row in Schema8LearningRepository.LoadIncompleteQueueRowsForCard(connection, cardId))
        {
            Schema8LearningRepository.DeleteQueueRow(connection, row.Id);
            removedBySession[row.SessionId] = removedBySession.GetValueOrDefault(row.SessionId) + 1;
        }

        var finalized = new List<int>();
        foreach (var sessionId in removedBySession.Keys.OrderBy(id => id))
        {
            var session = Schema8LearningRepository.LoadSession(connection, sessionId);
            if (session is null)
            {
                continue;
            }

            session.TotalCards = Math.Max(0, session.TotalCards - removedBySession[sessionId]);
            session.UpdatedAtUtc = nowUtc;
            if (session.Status == LearningSessionStatus.Active
                && Schema8LearningRepository.CountQueueRows(connection, sessionId) > 0
                && Schema8LearningRepository.CountIncompleteQueueRows(connection, sessionId) == 0)
            {
                session.Status = LearningSessionStatus.Completed;
                session.CompletedAtUtc ??= nowUtc;
                finalized.Add(sessionId);
            }

            Schema8LearningRepository.UpdateSessionCounters(connection, session);
        }

        return new Schema8RetirementCleanupResult(removedBySession.Values.Sum(), finalized);
    }

    /// <summary>
    /// Recomputes only the supplied Sense: Mastered when every existing card of that Sense is Retired,
    /// otherwise rolled back from Mastered to Learning. A Suspended Sense is never overridden, and no other
    /// Sense and no <c>WordEntity</c> column is touched.
    /// </summary>
    public static SenseStatus RecomputeSenseStatus(
        SQLiteConnection connection, int senseId, SenseStatus currentStatus, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (currentStatus == SenseStatus.Suspended)
        {
            return currentStatus;
        }

        var cards = Schema8LearningRepository.LoadCardsForSense(connection, senseId);
        var allRetired = cards.Count > 0 && cards.All(card => card.State == CardState.Retired);
        var target = allRetired
            ? SenseStatus.Mastered
            : currentStatus == SenseStatus.Mastered
                ? SenseStatus.Learning
                : currentStatus;

        if (target != currentStatus)
        {
            Schema8LearningRepository.UpdateSenseStatus(connection, senseId, target, nowUtc);
        }

        return target;
    }
}
