using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>
/// Rebuilds Schema-13 interaction progress from physical factual compatibility reviews without consulting
/// any legacy scheduling snapshot. Every physical row is an event, ordered by ReviewedAtUtc and Id.
/// </summary>
internal static class Schema13LearningReviewPolicy
{
    public const int ReplayVersion = 1;

    public static Schema13InteractionProjection Project(
        int cardId,
        IReadOnlyList<Schema8AttributionCandidateRow> assignments,
        IReadOnlyList<Schema8ReviewRow> reviews,
        IReadOnlyList<AnswerVariantProgressRow> persistedProgress)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cardId);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(persistedProgress);

        var events = reviews
            .Select(ToInteractionEvent)
            .OrderBy(review => Schema8Utc.Normalize(review.ReviewedAtUtc).Ticks)
            .ThenBy(review => review.Id)
            .ToList();
        var outcomes = new List<Schema13InteractionOutcome>();

        foreach (var assignment in assignments.Where(row => row.IsRequired).OrderBy(row => row.AnswerVariantId))
        {
            if (!assignment.RequiredSinceUtc.HasValue)
            {
                throw Schema8LearningDataException.Create(
                    Schema8LearningDataErrorCode.RequirementBoundaryViolation,
                    $"Assignment {assignment.AssignmentId} is Required but carries no RequiredSinceUtc.");
            }

            var boundary = Schema8Utc.Normalize(assignment.RequiredSinceUtc.Value);
            var persisted = persistedProgress.FirstOrDefault(
                row => row.AnswerVariantId == assignment.AnswerVariantId);
            if (persisted is not null && persisted.ReplayVersion > ReplayVersion)
            {
                throw Schema8LearningDataException.Create(
                    Schema8LearningDataErrorCode.ReplayVersionUnsupported,
                    $"Progress row for card {cardId}/variant {assignment.AnswerVariantId} declares ReplayVersion " +
                    $"{persisted.ReplayVersion}, which is newer than this build's {ReplayVersion}.");
            }

            var state = AutomaticLearningState.Initial;
            DateTime? lastAssessedAtUtc = null;
            var consumed = 0;
            foreach (var interactionEvent in events)
            {
                var attribution = Classify(interactionEvent, assignment.AnswerVariantId);
                if (!attribution.HasValue
                    || Schema8Utc.Normalize(interactionEvent.ReviewedAtUtc).Ticks < boundary.Ticks)
                {
                    continue;
                }

                state = interactionEvent.WasTypedAnswer
                    ? AutomaticLearningPolicy.RecordTypingAssessment(state, attribution.Value)
                    : AutomaticLearningPolicy.RecordRecallAssessment(
                        state, interactionEvent.Rating != ReviewRating.Again);
                lastAssessedAtUtc = Schema8Utc.Normalize(interactionEvent.ReviewedAtUtc);
                consumed++;
            }

            outcomes.Add(new Schema13InteractionOutcome(
                cardId,
                assignment.AnswerVariantId,
                boundary,
                state,
                lastAssessedAtUtc,
                consumed));
        }

        return new Schema13InteractionProjection(events, outcomes);
    }

    public static Schema8ProgressReplacementPlan PlanProgressReplacement(
        IReadOnlyList<Schema8AttributionCandidateRow> assignments,
        IReadOnlyList<AnswerVariantProgressRow> persistedProgress,
        Schema13InteractionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(persistedProgress);
        ArgumentNullException.ThrowIfNull(projection);

        var inserts = new List<AnswerVariantProgressRow>();
        var updates = new List<AnswerVariantProgressRow>();
        var deletes = new List<int>();
        var assignedVariantIds = assignments.Select(row => row.AnswerVariantId).ToHashSet();
        var acceptedOnlyVariantIds = assignments
            .Where(row => !row.IsRequired)
            .Select(row => row.AnswerVariantId)
            .ToHashSet();

        foreach (var outcome in projection.Outcomes.OrderBy(row => row.AnswerVariantId))
        {
            var expected = outcome.ToRow();
            var persisted = persistedProgress.FirstOrDefault(
                row => row.AnswerVariantId == outcome.AnswerVariantId);
            if (persisted is null)
            {
                inserts.Add(expected);
            }
            else if (!AreProjectionOwnedFieldsEqual(persisted, expected))
            {
                updates.Add(expected);
            }
        }

        foreach (var persisted in persistedProgress.OrderBy(row => row.AnswerVariantId))
        {
            if (!acceptedOnlyVariantIds.Contains(persisted.AnswerVariantId)
                && !assignedVariantIds.Contains(persisted.AnswerVariantId))
            {
                deletes.Add(persisted.AnswerVariantId);
            }
        }

        return new Schema8ProgressReplacementPlan(inserts, updates, deletes);
    }

    public static void ApplyProgressPlan(
        SQLiteConnection connection,
        int cardId,
        Schema8ProgressReplacementPlan plan)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var variantId in plan.DeletedAnswerVariantIds)
        {
            Schema8LearningRepository.DeleteProgress(connection, cardId, variantId);
        }
        foreach (var row in plan.Inserts)
        {
            Schema8LearningRepository.InsertProgress(connection, row);
        }
        foreach (var row in plan.Updates)
        {
            Schema8LearningRepository.UpdateProgress(connection, row);
        }
    }

    public static LearningInteractionMode ResolveInteraction(
        LearningMode? learningMode,
        Schema13InteractionOutcome targetOutcome)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);
        return AutomaticLearningPolicy.ResolveInteraction(
            learningMode ?? LearningMode.Automatic,
            targetOutcome.State);
    }

    private static Schema13InteractionEvent ToInteractionEvent(Schema8ReviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new Schema13InteractionEvent(
            row.Id,
            row.CardId,
            row.Rating,
            row.WasTypedAnswer,
            row.WasCorrect,
            Schema8Utc.Normalize(row.ReviewedAtUtc),
            row.TargetAnswerVariantId,
            row.MatchedAnswerVariantId);
    }

    private static bool? Classify(Schema13InteractionEvent interactionEvent, int answerVariantId)
    {
        if (interactionEvent.MatchedAnswerVariantId == answerVariantId)
        {
            return true;
        }
        if (interactionEvent.TargetAnswerVariantId != answerVariantId
            || interactionEvent.MatchedAnswerVariantId.HasValue)
        {
            return null;
        }
        return interactionEvent.WasCorrect;
    }

    private static bool AreProjectionOwnedFieldsEqual(
        AnswerVariantProgressRow persisted,
        AnswerVariantProgressRow expected) =>
        persisted.InteractionMode == expected.InteractionMode
        && persisted.ConsecutiveReadingSuccessCount == expected.ConsecutiveReadingSuccessCount
        && persisted.ConsecutiveTypingSuccessCount == expected.ConsecutiveTypingSuccessCount
        && persisted.ConsecutiveTypingFailureCount == expected.ConsecutiveTypingFailureCount
        && Schema8Utc.AreSameInstant(persisted.LastAssessedAtUtc, expected.LastAssessedAtUtc)
        && persisted.MasteryReviewExtensionScheduled == expected.MasteryReviewExtensionScheduled
        && persisted.IsMastered == expected.IsMastered
        && persisted.ReplayVersion == expected.ReplayVersion
        && Schema8Utc.AreSameInstant(persisted.CreatedAtUtc, expected.CreatedAtUtc)
        && Schema8Utc.AreSameInstant(persisted.UpdatedAtUtc, expected.UpdatedAtUtc);
}

internal sealed record Schema13InteractionEvent(
    int Id,
    int CardId,
    ReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    DateTime ReviewedAtUtc,
    int? TargetAnswerVariantId,
    int? MatchedAnswerVariantId);

internal sealed record Schema13InteractionOutcome(
    int CardId,
    int AnswerVariantId,
    DateTime RequiredSinceUtc,
    AutomaticLearningState State,
    DateTime? LastAssessedAtUtc,
    int ConsumedEventCount)
{
    public AnswerVariantProgressRow ToRow() => new()
    {
        CardId = CardId,
        AnswerVariantId = AnswerVariantId,
        InteractionMode = State.InteractionMode,
        ConsecutiveReadingSuccessCount = State.ConsecutiveRecallSuccesses,
        ConsecutiveTypingSuccessCount = State.ConsecutiveTypingSuccesses,
        ConsecutiveTypingFailureCount = State.ConsecutiveTypingFailures,
        LastAssessedAtUtc = LastAssessedAtUtc,
        MasteryReviewExtensionScheduled = false,
        IsMastered = false,
        ReplayVersion = Schema13LearningReviewPolicy.ReplayVersion,
        CreatedAtUtc = RequiredSinceUtc,
        UpdatedAtUtc = LastAssessedAtUtc ?? RequiredSinceUtc
    };
}

internal sealed record Schema13InteractionProjection(
    IReadOnlyList<Schema13InteractionEvent> Events,
    IReadOnlyList<Schema13InteractionOutcome> Outcomes)
{
    public Schema13InteractionOutcome? FindOutcome(int answerVariantId) =>
        Outcomes.FirstOrDefault(outcome => outcome.AnswerVariantId == answerVariantId);
}
