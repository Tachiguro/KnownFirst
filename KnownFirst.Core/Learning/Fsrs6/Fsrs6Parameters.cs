namespace KnownFirst.Core.Learning.Fsrs6;

using System.Collections.Immutable;

/// <summary>
/// Platform-neutral immutable FSRS-6 scheduler parameters.
/// </summary>
public sealed record Fsrs6Parameters
{
    public const string AlgorithmName = "FSRS-6";
    public const double DefaultDesiredRetention = 0.90;
    public const int DefaultMaximumIntervalDays = 36500;
    public const int ParameterCount = 21;

    public static readonly ImmutableArray<double> DefaultWeights = ImmutableArray.Create<double>(
        0.212,
        1.2931,
        2.3065,
        8.2956,
        6.4133,
        0.8334,
        3.0194,
        0.001,
        1.8722,
        0.1666,
        0.796,
        1.4835,
        0.0614,
        0.2629,
        1.6483,
        0.6014,
        1.8729,
        0.5425,
        0.0912,
        0.0658,
        0.1542);

    public static Fsrs6Parameters Default { get; } = new(
        DefaultWeights,
        DefaultDesiredRetention,
        DefaultMaximumIntervalDays,
        enableFuzz: false);

    public string Algorithm => AlgorithmName;
    public ImmutableArray<double> Weights { get; }
    public double DesiredRetention { get; }
    public int MaximumIntervalDays { get; }
    public bool EnableFuzz { get; }

    public Fsrs6Parameters(
        IEnumerable<double> weights,
        double desiredRetention = DefaultDesiredRetention,
        int maximumIntervalDays = DefaultMaximumIntervalDays,
        bool enableFuzz = false)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var weightsArray = weights.ToImmutableArray();
        if (weightsArray.Length != ParameterCount)
        {
            throw new ArgumentException($"Weights must contain exactly {ParameterCount} parameters, but contained {weightsArray.Length}.", nameof(weights));
        }

        for (int i = 0; i < weightsArray.Length; i++)
        {
            if (!double.IsFinite(weightsArray[i]))
            {
                throw new ArgumentException($"Weight at index {i} must be a finite number (got {weightsArray[i]}).", nameof(weights));
            }
        }

        if (!double.IsFinite(desiredRetention) || desiredRetention <= 0.0 || desiredRetention >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredRetention), desiredRetention, "Desired retention must be a finite number strictly between 0.0 and 1.0.");
        }

        if (maximumIntervalDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumIntervalDays), maximumIntervalDays, "Maximum interval must be a positive integer.");
        }

        if (enableFuzz)
        {
            throw new ArgumentException("Fuzzing is disabled and not supported in production configuration.", nameof(enableFuzz));
        }

        Weights = weightsArray;
        DesiredRetention = desiredRetention;
        MaximumIntervalDays = maximumIntervalDays;
        EnableFuzz = false;
    }
}
