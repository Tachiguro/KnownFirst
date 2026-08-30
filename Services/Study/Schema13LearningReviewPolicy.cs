using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;

namespace KnownFirst.Services.Study;

/// <summary>
/// Preserves interaction and answer-variant progress for Schema 13 while excluding every legacy
/// schedule-derived mastery, 365-day extension, retirement, and queue-pruning side effect.
/// </summary>
internal static class Schema13LearningReviewPolicy
{
    public static Schema8ProgressReplacementPlan PlanCreditedProgress(
        Schema8AttributionCandidateRow creditedAssignment,
        Schema8ReplayVariantOutcome? currentOutcome,
        IReadOnlyList<AnswerVariantProgressRow> persistedProgress,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        DateTime reviewedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(creditedAssignment);
        ArgumentNullException.ThrowIfNull(persistedProgress);

        if (!creditedAssignment.IsRequired)
        {
            return Schema8ProgressReplacementPlan.Empty;
        }

        var outcome = currentOutcome
            ?? throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.ProgressRowInvalid,
                $"No replayed outcome exists for credited Required variant {creditedAssignment.AnswerVariantId}.");
        var nextInteraction = wasTypedAnswer
            ? AutomaticLearningPolicy.RecordTypingAssessment(outcome.State, wasCorrect)
            : AutomaticLearningPolicy.RecordRecallAssessment(outcome.State, rating != ReviewRating.Again);

        var expected = outcome.ToRow();
        expected.InteractionMode = nextInteraction.InteractionMode;
        expected.ConsecutiveReadingSuccessCount = nextInteraction.ConsecutiveRecallSuccesses;
        expected.ConsecutiveTypingSuccessCount = nextInteraction.ConsecutiveTypingSuccesses;
        expected.ConsecutiveTypingFailureCount = nextInteraction.ConsecutiveTypingFailures;
        expected.LastAssessedAtUtc = reviewedAtUtc;
        expected.UpdatedAtUtc = reviewedAtUtc;

        // Retain prior compatibility facts only. Schema 13 never derives mastery or an extension from the
        // legacy CardSchedule-shaped columns on LearningReviews.
        expected.MasteryReviewExtensionScheduled = outcome.State.MasteryReviewExtensionScheduled;
        expected.IsMastered = outcome.IsMastered;

        var persisted = persistedProgress.SingleOrDefault(
            row => row.AnswerVariantId == creditedAssignment.AnswerVariantId);
        if (persisted is null)
        {
            return new Schema8ProgressReplacementPlan([expected], [], []);
        }

        return Schema8LearningReviewReplayPolicy.AreReplayOwnedFieldsEqual(persisted, expected)
            ? Schema8ProgressReplacementPlan.Empty
            : new Schema8ProgressReplacementPlan([], [expected], []);
    }
}
