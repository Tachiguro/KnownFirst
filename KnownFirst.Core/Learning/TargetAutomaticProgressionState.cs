namespace KnownFirst.Core.Learning;

/// <summary>
/// Target-centric progression state for Automatic interaction qualification.
/// Reflects recall qualification, typing qualification, qualification maintenance, and typing re-checks.
/// </summary>
public sealed record TargetAutomaticProgressionState(
    LearningInteractionMode InteractionMode,
    int ConsecutiveRecallSuccesses,
    int ConsecutiveTypingSuccesses,
    bool IsTypingQualified,
    bool IsRecheckArmed,
    bool TypingOptOut)
{
    public static TargetAutomaticProgressionState Initial { get; } = new(
        LearningInteractionMode.Reading,
        ConsecutiveRecallSuccesses: 0,
        ConsecutiveTypingSuccesses: 0,
        IsTypingQualified: false,
        IsRecheckArmed: false,
        TypingOptOut: false);

    public static TargetAutomaticProgressionState InitialFor(LearningTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(
            LearningInteractionMode.Reading,
            ConsecutiveRecallSuccesses: 0,
            ConsecutiveTypingSuccesses: 0,
            IsTypingQualified: false,
            IsRecheckArmed: false,
            TypingOptOut: target.TypingOptOut);
    }
}
