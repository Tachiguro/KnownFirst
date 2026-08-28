namespace KnownFirst.Core.Learning;

public sealed record AlreadyKnownDecision
{
    public DateTime DecidedAtUtc { get; }

    public AlreadyKnownDecision(DateTime decidedAtUtc)
    {
        if (decidedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Decision timestamp must have DateTimeKind.Utc.", nameof(decidedAtUtc));
        }

        DecidedAtUtc = decidedAtUtc;
    }
}
