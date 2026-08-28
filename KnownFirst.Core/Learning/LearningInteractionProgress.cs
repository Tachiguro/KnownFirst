namespace KnownFirst.Core.Learning;

public sealed record LearningInteractionProgress(
    LearningInteractionMode InteractionMode = LearningInteractionMode.Reading,
    int ConsecutiveRecallSuccesses = 0,
    int ConsecutiveTypingFailures = 0)
{
    public static LearningInteractionProgress Initial { get; } = new();

    public LearningInteractionMode CurrentInteractionMode => InteractionMode;
}
