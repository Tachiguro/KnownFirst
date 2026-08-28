using KnownFirst.Core.Settings;

namespace KnownFirst.Core.Learning;

public static class LearningInteractionPolicy
{
    public const int RequiredConsecutiveAssessments = 2;

    public static LearningInteractionMode ResolveInteraction(
        LearningMode learningMode,
        LearningInteractionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return learningMode switch
        {
            LearningMode.Reading => LearningInteractionMode.Reading,
            LearningMode.Typing => LearningInteractionMode.Typing,
            LearningMode.Automatic => progress.InteractionMode,
            _ => LearningInteractionMode.Reading
        };
    }

    public static LearningInteractionProgress RecordRecallAssessment(
        LearningInteractionProgress progress,
        bool successful)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!successful)
        {
            return progress with { ConsecutiveRecallSuccesses = 0 };
        }

        var successes = Math.Min(
            RequiredConsecutiveAssessments,
            progress.ConsecutiveRecallSuccesses + 1);

        return successes < RequiredConsecutiveAssessments
            ? progress with { ConsecutiveRecallSuccesses = successes }
            : progress with
            {
                InteractionMode = LearningInteractionMode.Typing,
                ConsecutiveRecallSuccesses = successes,
                ConsecutiveTypingFailures = 0
            };
    }

    public static LearningInteractionProgress RecordTypingAssessment(
        LearningInteractionProgress progress,
        bool correct)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (correct)
        {
            return progress with
            {
                ConsecutiveTypingFailures = 0
            };
        }

        var failures = Math.Min(
            RequiredConsecutiveAssessments,
            progress.ConsecutiveTypingFailures + 1);

        return failures < RequiredConsecutiveAssessments
            ? progress with
            {
                ConsecutiveTypingFailures = failures
            }
            : progress with
            {
                InteractionMode = LearningInteractionMode.Reading,
                ConsecutiveRecallSuccesses = 0,
                ConsecutiveTypingFailures = 0
            };
    }
}
