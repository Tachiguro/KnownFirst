using KnownFirst.Core.Settings;

namespace KnownFirst.Core.Learning;

public static class LearningInteractionPolicy
{
    public const int RequiredConsecutiveAssessments = 2;

    public static LearningInteractionMode ResolveInteraction(
        LearningMode learningMode,
        LearningInteractionProgress progress,
        CardDirection direction = CardDirection.MeaningToTerm)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (direction == CardDirection.TermToMeaning)
        {
            return LearningInteractionMode.Reading;
        }

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
        bool successful) =>
        RecordRecallAssessment(
            progress,
            successful ? RecallProgressionAssessment.Advance : RecallProgressionAssessment.Reset);

    public static LearningInteractionProgress RecordRecallAssessment(
        LearningInteractionProgress progress,
        ReviewRating rating) =>
        RecordRecallAssessment(progress, AutomaticLearningPolicy.ToProgressionAssessment(rating));

    public static LearningInteractionProgress RecordRecallAssessment(
        LearningInteractionProgress progress,
        RecallProgressionAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(progress);

        switch (assessment)
        {
            case RecallProgressionAssessment.Advance:
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

            case RecallProgressionAssessment.Hold:
                return progress;

            case RecallProgressionAssessment.Reset:
                return progress with { ConsecutiveRecallSuccesses = 0 };

            default:
                throw new ArgumentOutOfRangeException(nameof(assessment), assessment, "Unknown recall progression assessment.");
        }
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
