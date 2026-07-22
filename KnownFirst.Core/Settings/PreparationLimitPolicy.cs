namespace KnownFirst.Core.Settings;

public static class PreparationLimitPolicy
{
    public const int DefaultLimit = 10;

    public const int MaximumLimit = 50;

    private static readonly IReadOnlyList<int> Limits = Array.AsReadOnly([5, 10, 20, 30, 50]);

    public static IReadOnlyList<int> SupportedLimits => Limits;

    public static int Normalize(int value) => Limits.Contains(value) ? value : DefaultLimit;
}
