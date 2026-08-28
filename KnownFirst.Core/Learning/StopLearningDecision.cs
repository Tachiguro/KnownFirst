namespace KnownFirst.Core.Learning;

public sealed record StopLearningDecision
{
    public DateTime DecidedAtUtc { get; }

    public StopLearningDecision(DateTime decidedAtUtc)
    {
        if (decidedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Decision timestamp must have DateTimeKind.Utc.", nameof(decidedAtUtc));
        }

        DecidedAtUtc = decidedAtUtc;
    }
}
