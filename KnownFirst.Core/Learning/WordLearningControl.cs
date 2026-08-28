namespace KnownFirst.Core.Learning;

public sealed record WordLearningControl(AlreadyKnownDecision? AlreadyKnown = null)
{
    public static WordLearningControl Default { get; } = new();

    public bool IsAlreadyKnown => AlreadyKnown is not null;

    public WordLearningControl MarkAlreadyKnown(DateTime decidedAtUtc)
    {
        if (AlreadyKnown is not null)
        {
            return this;
        }

        return new WordLearningControl(new AlreadyKnownDecision(decidedAtUtc));
    }

    public WordLearningControl MarkAlreadyKnown(AlreadyKnownDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (AlreadyKnown is not null)
        {
            return this;
        }

        return new WordLearningControl(decision);
    }

    public WordLearningControl ClearAlreadyKnown()
    {
        return AlreadyKnown is null ? this : Default;
    }
}
