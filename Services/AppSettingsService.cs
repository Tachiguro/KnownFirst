using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    public const int DefaultPreparationLimit = PreparationLimitPolicy.DefaultLimit;

    private const string PreparationLimitPreferenceKey = "preparation_limit";
    private const string CardDirectionPreferenceKey = "card_direction";
    private const string OnlineLookupConsentPreferenceKey = "online_lookup_consent";
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(ILogger<AppSettingsService> logger)
    {
        _logger = logger;
        PreparationLimit = ReadPreparationLimit();
        CardDirection = ReadCardDirection();
        HasOnlineLookupConsent = Preferences.Default.Get(OnlineLookupConsentPreferenceKey, false);
        _logger.LogDebug(
            "Application settings loaded. PreparationLimit = {PreparationLimit}, card direction = {CardDirection}, online lookup consent = {HasOnlineLookupConsent}",
            PreparationLimit,
            CardDirection,
            HasOnlineLookupConsent);
    }

    public int PreparationLimit { get; private set; }

    public IReadOnlyList<int> SupportedPreparationLimits => PreparationLimitPolicy.SupportedLimits;

    public CardDirectionPreference CardDirection { get; private set; }

    public bool HasOnlineLookupConsent { get; private set; }

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
        _logger.LogInformation(
            "Preparation limit saved. PreparationLimit = {PreparationLimit}",
            normalizedLimit);
    }

    public void SetCardDirection(CardDirectionPreference preference)
    {
        var normalized = CardDirectionPreferencePolicy.Normalize((int)preference);
        Preferences.Default.Set(CardDirectionPreferenceKey, (int)normalized);
        CardDirection = normalized;
        _logger.LogInformation("Card direction saved. CardDirection = {CardDirection}", normalized);
    }

    public void GrantOnlineLookupConsent()
    {
        Preferences.Default.Set(OnlineLookupConsentPreferenceKey, true);
        HasOnlineLookupConsent = true;
        _logger.LogInformation("Online dictionary lookup consent was granted.");
    }

    public void RevokeOnlineLookupConsent()
    {
        Preferences.Default.Remove(OnlineLookupConsentPreferenceKey);
        HasOnlineLookupConsent = false;
        _logger.LogInformation("Online dictionary lookup consent was revoked.");
    }

    public void Reset()
    {
        Preferences.Default.Remove(PreparationLimitPreferenceKey);
        Preferences.Default.Remove(CardDirectionPreferenceKey);
        Preferences.Default.Remove(OnlineLookupConsentPreferenceKey);
        PreparationLimit = DefaultPreparationLimit;
        CardDirection = CardDirectionPreferencePolicy.DefaultPreference;
        HasOnlineLookupConsent = false;
        _logger.LogInformation("Application settings were reset to defaults.");
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

    private CardDirectionPreference ReadCardDirection()
    {
        var saved = Preferences.Default.Get(
            CardDirectionPreferenceKey,
            (int)CardDirectionPreferencePolicy.DefaultPreference);
        var normalized = CardDirectionPreferencePolicy.Normalize(saved);
        if ((int)normalized != saved)
        {
            _logger.LogWarning(
                "The saved card direction value '{CardDirection}' is unsupported. Falling back to Both directions.",
                saved);
            Preferences.Default.Set(CardDirectionPreferenceKey, (int)normalized);
        }

        return normalized;
    }
}
