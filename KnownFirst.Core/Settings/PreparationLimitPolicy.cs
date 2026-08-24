namespace KnownFirst.Core.Settings;

public static class PreparationLimitPolicy
{
    public const int MinimumLimit = 1;

    public const int MaximumLimit = 50;

    public const int DefaultLimit = 5;

    public const int RecommendedLimit = 5;

    public const int HighBudgetWarningThreshold = 15;

    private static readonly IReadOnlyList<int> PresetLimits = Array.AsReadOnly([1, 5, 10]);

    public static IReadOnlyList<int> Presets => PresetLimits;

    public static IReadOnlyList<int> SupportedLimits => PresetLimits;

    public static bool IsValid(int value) => value is >= MinimumLimit and <= MaximumLimit;

    public static bool IsPreset(int value) => value is 1 or 5 or 10;

    public static bool RequiresHighBudgetWarning(int value) =>
        value is > HighBudgetWarningThreshold and <= MaximumLimit;

    public static int Normalize(int value) =>
        value is >= MinimumLimit and <= MaximumLimit ? value : DefaultLimit;
}
