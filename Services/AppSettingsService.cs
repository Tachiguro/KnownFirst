using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    public const int DefaultPreparationLimit = PreparationLimitPolicy.DefaultLimit;

    private const string PreparationLimitPreferenceKey = "preparation_limit";
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(ILogger<AppSettingsService> logger)
    {
        _logger = logger;
        PreparationLimit = ReadPreparationLimit();
    }

    public int PreparationLimit { get; private set; }

    public IReadOnlyList<int> SupportedPreparationLimits => PreparationLimitPolicy.SupportedLimits;

    public void SetPreparationLimit(int preparationLimit)
    {
        var normalizedLimit = PreparationLimitPolicy.Normalize(preparationLimit);
        if (normalizedLimit != preparationLimit)
        {
            _logger.LogWarning(
                "The requested preparation limit '{PreparationLimit}' is unsupported. Falling back to the default.",
                preparationLimit);
        }

        Preferences.Default.Set(PreparationLimitPreferenceKey, normalizedLimit);
        PreparationLimit = normalizedLimit;
    }

    public void Reset()
    {
        Preferences.Default.Remove(PreparationLimitPreferenceKey);
        PreparationLimit = DefaultPreparationLimit;
    }

    private int ReadPreparationLimit()
    {
        var savedLimit = Preferences.Default.Get(PreparationLimitPreferenceKey, DefaultPreparationLimit);
        var normalizedLimit = PreparationLimitPolicy.Normalize(savedLimit);
        if (normalizedLimit == savedLimit)
        {
            return normalizedLimit;
        }

        _logger.LogWarning(
            "The saved preparation limit '{PreparationLimit}' is unsupported. Falling back to the default.",
            savedLimit);
        Preferences.Default.Set(PreparationLimitPreferenceKey, normalizedLimit);
        return normalizedLimit;
    }
}
