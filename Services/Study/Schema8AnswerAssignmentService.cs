using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>The observable outcome of one assignment mutation.</summary>
public sealed record Schema8AssignmentMutationResult(
    int AssignmentId,
    string StableId,
    AnswerVariantRequirement Requirement,
    bool IsPreferred,
    DateTime? RequiredSinceUtc,
    bool Inserted,
    bool Promoted,
    bool Demoted,
    int AffectedCardId,
    CardState CardStateAfter,
    SenseStatus SenseStatusAfter,
    int RemovedIncompleteQueueRows);

/// <summary>
/// The single Schema-8 write path for direction-specific answer assignments (KF-MEANING-001 Slice 4).
/// <para>
/// Deliberately not a UI service and deliberately not registered in production DI: Slice 4 owns the assignment
/// contract, while any editing surface belongs to Slice 9. It creates no queue row, selects no target and
/// implements no Slice-5 scheduling — the only scheduling-adjacent effects are the binding reactivation and
/// immediate-retirement rules below, both of which preserve every card scheduling field.
/// </para>
/// </summary>
public sealed class Schema8AnswerAssignmentService
{
    private readonly IKnownFirstDatabase _database;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public Schema8AnswerAssignmentService(IKnownFirstDatabase database, IClock clock)
    {
        _database = database;
        _clock = clock;
    }

    /// <summary>
    /// Creates or updates the assignment of one <c>(SenseId, CardDirection, AnswerVariantId)</c> triple.
    /// <para>
    /// <c>StableId</c> and <c>CreatedAtUtc</c> are never replaced. <c>Requirement</c> and
    /// <c>IsPreferred</c> are independent. A promotion opens a new Required epoch at
    /// <c>clock.UtcNow</c>, so neither preserved AcceptedOnly progress nor previous-epoch mastery can carry
    /// over; a demotion clears the boundary and preserves the progress row byte-for-byte.
    /// <c>AnswerLanguage</c> is never consulted to infer or reject the direction.
    /// </para>
    /// </summary>
    public async Task<Schema8AssignmentMutationResult> SetAssignmentAsync(
        int senseId,
        CardDirection direction,
        int answerVariantId,
        AnswerVariantRequirement requirement,
        bool isPreferred)
    {
        await _operationGate.WaitAsync();
        try
        {
            return await _database.RunInTransactionAsync(connection =>
                Mutate(connection, senseId, direction, answerVariantId, requirement, isPreferred));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Schema8AssignmentMutationResult Mutate(
        SQLiteConnection connection,
        int senseId,
        CardDirection direction,
        int answerVariantId,
        AnswerVariantRequirement requirement,
        bool isPreferred)
    {
        // ---- 1. Capability and physical shape (fail-closed before anything is read semantically) ----
        if (LearningSchemaCapability.Resolve(connection) is not LearningSchema8CapabilityResult)
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidCardGraph,
                "Answer assignments can only be mutated against a validated Schema-8 database.");
        }

        // ---- 2. Direction and referenced rows ----
        if (direction is not (CardDirection.TermToMeaning or CardDirection.MeaningToTerm))
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidCardGraph, $"Undefined CardDirection value {(int)direction}.");
        }

        var sense = Schema8LearningRepository.LoadSense(connection, senseId)
            ?? throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.SenseNotFound, $"Sense {senseId} does not exist.");

        var variant = Schema8LearningRepository.LoadAnswerVariant(connection, answerVariantId)
            ?? throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidTarget, $"AnswerVariant {answerVariantId} does not exist.");

        if (variant.SenseId != senseId)
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidTarget,
                $"AnswerVariant {answerVariantId} belongs to Sense {variant.SenseId}, not to Sense {senseId}.");
        }

        // ---- 3. Exactly one card for this (SenseId, Direction) ----
        var cardCount = Schema8LearningRepository.CountCardsForSenseDirection(connection, senseId, direction);
        if (cardCount != 1)
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Sense {senseId} has {cardCount} cards for direction {direction}; exactly one is required.");
        }

        var card = Schema8LearningRepository.LoadCardsForSense(connection, senseId)
            .Single(row => row.Direction == direction);

        // ---- 4. Existing assignment graph must be sound before it is changed ----
        var assignments = Schema8LearningRepository.LoadAssignmentsForSenseDirection(connection, senseId, direction);
        ValidateAssignmentGraph(assignments, senseId, direction);

        var existing = assignments.SingleOrDefault(row => row.AnswerVariantId == answerVariantId);
        var now = Schema8Utc.Normalize(_clock.UtcNow);

        var wasRequired = existing?.IsRequired ?? false;
        var willBeRequired = requirement == AnswerVariantRequirement.Required;
        var requiredAdded = willBeRequired && !wasRequired;
        var promoted = existing is not null && !wasRequired && willBeRequired;
        var demoted = existing is not null && wasRequired && !willBeRequired;

        // ---- 5. Boundary transition ----
        DateTime? boundary;
        if (existing is null)
        {
            // A direct Required insert uses its own CreatedAtUtc, which is `now`.
            boundary = willBeRequired ? now : null;
        }
        else if (promoted)
        {
            boundary = now;
        }
        else if (demoted)
        {
            boundary = null;
        }
        else
        {
            // Requirement unchanged: an IsPreferred-only (or no-op) update preserves the boundary byte-for-byte.
            boundary = existing.RequiredSinceUtc;
        }

        // ---- 6. Write the assignment; the partial unique index is never transiently violated ----
        int assignmentId;
        string stableId;
        bool inserted;
        if (existing is null)
        {
            if (isPreferred)
            {
                Schema8LearningRepository.ClearPreferredForSenseDirection(
                    connection, senseId, direction, exceptAssignmentId: 0, now);
            }

            stableId = Guid.NewGuid().ToString("N");
            assignmentId = Schema8LearningRepository.InsertAssignmentAndGetId(
                connection, stableId, senseId, direction, answerVariantId, requirement, isPreferred, boundary, now);
            inserted = true;
        }
        else
        {
            if (isPreferred)
            {
                Schema8LearningRepository.ClearPreferredForSenseDirection(
                    connection, senseId, direction, existing.AssignmentId, now);
            }

            Schema8LearningRepository.UpdateAssignment(
                connection, existing.AssignmentId, requirement, isPreferred, boundary, now);
            assignmentId = existing.AssignmentId;
            stableId = existing.AssignmentStableId;
            inserted = false;
        }

        // ---- 7. Post-mutation graph must still be sound ----
        var updatedAssignments =
            Schema8LearningRepository.LoadAssignmentsForSenseDirection(connection, senseId, direction);
        ValidateAssignmentGraph(updatedAssignments, senseId, direction);

        // ---- 8. Epoch-aware progress rebuild against the NEW assignment set ----
        var events = Schema8LearningRepository.LoadReviewsForCard(connection, card.Id)
            .Select(Schema8LearningReviewReplayPolicy.ToReplayEvent)
            .ToList();
        var persistedProgress = Schema8LearningRepository.LoadProgressForCard(connection, card.Id);
        var replayResult = Schema8LearningReviewReplayPolicy.Replay(
            card, updatedAssignments, events, persistedProgress);
        var plan = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            updatedAssignments, persistedProgress, replayResult);
        Schema8LearningReviewReplayPolicy.ApplyProgressPlan(connection, card.Id, plan);

        // ---- 9. Reactivation / immediate retirement, always preserving every scheduling field ----
        var requiredCount = updatedAssignments.Count(row => row.IsRequired);
        var allRequiredMastered = Schema8CardRetirementPolicy.AllRequiredMastered(replayResult);
        var cardStateAfter = card.State;
        var removedQueueRows = 0;

        if (requiredAdded)
        {
            var requiredOutcome = replayResult.FindOutcome(answerVariantId);
            var prerequisitePending = requiredOutcome is null || !requiredOutcome.IsMastered;
            if (prerequisitePending && card.State == CardState.Retired)
            {
                Schema8LearningRepository.UpdateCardState(connection, card.Id, CardState.Learning, now);
                cardStateAfter = CardState.Learning;
            }
        }
        else if (demoted
            && requiredCount > 0
            && allRequiredMastered
            && card.State != CardState.Retired)
        {
            Schema8LearningRepository.UpdateCardState(connection, card.Id, CardState.Retired, now);
            cardStateAfter = CardState.Retired;
            removedQueueRows = Schema8CardRetirementPolicy
                .PruneIncompleteQueueRowsForCard(connection, card.Id, now)
                .RemovedIncompleteQueueRows;
        }

        // A demotion that leaves zero Required assignments never newly retires a card, and never revives an
        // already-retired one.

        // ---- 10. Affected Sense only ----
        var senseStatusAfter = Schema8CardRetirementPolicy.RecomputeSenseStatus(
            connection, senseId, sense.Status, now);

        return new Schema8AssignmentMutationResult(
            assignmentId, stableId, requirement, isPreferred, boundary, inserted, promoted, demoted,
            card.Id, cardStateAfter, senseStatusAfter, removedQueueRows);
    }

    /// <summary>
    /// Fail-closed structural checks: no duplicated triple, at most one preferred row, and invariant I1
    /// (<c>Requirement = Required</c> if and only if <c>RequiredSinceUtc</c> is non-null) for every row.
    /// </summary>
    private static void ValidateAssignmentGraph(
        IReadOnlyList<Schema8AttributionCandidateRow> assignments, int senseId, CardDirection direction)
    {
        var duplicated = assignments
            .GroupBy(row => row.AnswerVariantId)
            .Any(group => group.Count() > 1);
        if (duplicated)
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Sense {senseId}/{direction} has a duplicated (SenseId, CardDirection, AnswerVariantId) triple.");
        }

        if (assignments.Count(row => row.IsPreferred) > 1)
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Sense {senseId}/{direction} has more than one preferred assignment.");
        }

        foreach (var row in assignments)
        {
            if (row.IsRequired != row.RequiredSinceUtc.HasValue)
            {
                throw Schema8LearningDataException.Create(
                    Schema8LearningDataErrorCode.RequirementBoundaryViolation,
                    $"Assignment {row.AssignmentId} violates 'Requirement = Required if and only if RequiredSinceUtc is not null'.");
            }
        }
    }
}
