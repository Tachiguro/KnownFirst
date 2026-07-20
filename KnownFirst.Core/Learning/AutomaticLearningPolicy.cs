using KnownFirst.Core.Settings;

namespace KnownFirst.Core.Learning;

public static class AutomaticLearningPolicy
{
    public const int RequiredConsecutiveAssessments = 2;
    public const int MaximumReviewIntervalDays = 365;

    public static LearningInteractionMode ResolveInteraction(
        LearningMode learningMode,
        AutomaticLearningState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return learningMode switch
        {
            LearningMode.Reading => LearningInteractionMode.Reading,
            LearningMode.Typing => LearningInteractionMode.Typing,
            LearningMode.Automatic => state.InteractionMode,
            _ => LearningInteractionMode.Reading
        };
    }

    public static AutomaticLearningState RecordRecallAssessment(
        AutomaticLearningState state,
        bool successful)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!successful)
        {
            return state with { ConsecutiveRecallSuccesses = 0 };
        }

        var successes = Math.Min(
            RequiredConsecutiveAssessments,
            state.ConsecutiveRecallSuccesses + 1);
        return successes < RequiredConsecutiveAssessments
            ? state with { ConsecutiveRecallSuccesses = successes }
            : state with
            {
                InteractionMode = LearningInteractionMode.Typing,
                ConsecutiveRecallSuccesses = successes,
                ConsecutiveTypingFailures = 0
            };
    }

    public static AutomaticLearningState RecordTypingAssessment(
        AutomaticLearningState state,
        bool correct)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (correct)
        {
            return state with
            {
                ConsecutiveTypingSuccesses = Math.Min(
                    RequiredConsecutiveAssessments,
                    state.ConsecutiveTypingSuccesses + 1),
                ConsecutiveTypingFailures = 0
            };
        }

        var failures = Math.Min(
            RequiredConsecutiveAssessments,
            state.ConsecutiveTypingFailures + 1);
        return failures < RequiredConsecutiveAssessments
            ? state with
            {
                ConsecutiveTypingSuccesses = 0,
                ConsecutiveTypingFailures = failures
            }
            : state with
            {
                InteractionMode = LearningInteractionMode.Reading,
                ConsecutiveRecallSuccesses = 0,
                ConsecutiveTypingSuccesses = 0,
                ConsecutiveTypingFailures = 0
            };
    }

    public static bool IsMasteryReview(CardSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return schedule.State == CardState.Review
            && schedule.IntervalDays >= MaximumReviewIntervalDays;
    }

    public static bool HasTypingMastery(AutomaticLearningState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ConsecutiveTypingSuccesses >= RequiredConsecutiveAssessments;
    }
}
