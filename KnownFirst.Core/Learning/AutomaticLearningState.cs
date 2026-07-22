namespace KnownFirst.Core.Learning;

public sealed record AutomaticLearningState(
    LearningInteractionMode InteractionMode,
    int ConsecutiveRecallSuccesses,
    int ConsecutiveTypingSuccesses,
    int ConsecutiveTypingFailures,
    bool MasteryReviewExtensionScheduled)
{
    public static AutomaticLearningState Initial { get; } = new(
        LearningInteractionMode.Reading,
        0,
        0,
        0,
        false);
}
