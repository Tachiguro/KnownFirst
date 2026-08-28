namespace KnownFirst.Core.Learning;

public sealed record SenseLearningControl(StopLearningDecision? StopLearning = null)
{
    public static SenseLearningControl Default { get; } = new();

    public bool IsStopped => StopLearning is not null;

    public SenseLearningControl Stop(DateTime decidedAtUtc)
    {
        if (StopLearning is not null)
        {
            return this;
        }

        return new SenseLearningControl(new StopLearningDecision(decidedAtUtc));
    }

    public SenseLearningControl Stop(StopLearningDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (StopLearning is not null)
        {
            return this;
        }

        return new SenseLearningControl(decision);
    }

    public SenseLearningControl Resume()
    {
        return StopLearning is null ? this : Default;
    }
}
