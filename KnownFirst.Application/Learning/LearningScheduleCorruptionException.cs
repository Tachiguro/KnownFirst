namespace KnownFirst.Application.Learning;

/// <summary>
/// Exception thrown when scheduling projection or replay data is corrupt or violates invariants.
/// </summary>
public sealed class LearningScheduleCorruptionException : Exception
{
    public LearningScheduleCorruptionException(string message)
        : base(message)
    {
    }

    public LearningScheduleCorruptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}