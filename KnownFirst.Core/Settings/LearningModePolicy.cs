namespace KnownFirst.Core.Settings;

public static class LearningModePolicy
{
    public const LearningMode DefaultMode = LearningMode.Automatic;

    public static LearningMode Normalize(int value) => value switch
    {
        (int)LearningMode.Reading => LearningMode.Reading,
        (int)LearningMode.Typing => LearningMode.Typing,
        (int)LearningMode.Automatic => LearningMode.Automatic,
        _ => DefaultMode
    };
}
