using KnownFirst.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    public const int DefaultPreparationLimit = PreparationLimitPolicy.DefaultLimit;

    private const string PreparationLimitPreferenceKey = "preparation_limit";
    private const string CardDirectionPreferenceKey = "card_direction";
    private const string LearningModePreferenceKey = "learning_mode";
    private const string OnlineLookupConsentPreferenceKey = "online_lookup_consent";
    private const string EnhancedTermRecognitionPreferenceKey = "enhanced_term_recognition_enabled";
    private readonly IPreferences _preferences;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(IPreferences preferences, ILogger<AppSettingsService> logger)
    {
        _preferences = preferences;
        _logger = logger;
        PreparationLimit = ReadPreparationLimit();
        CardDirection = ReadCardDirection();
        LearningMode = ReadLearningMode();
        HasOnlineLookupConsent = _preferences.Get(OnlineLookupConsentPreferenceKey, false);
        EnhancedTermRecognitionEnabled = _preferences.Get(EnhancedTermRecognitionPreferenceKey, false);
        _logger.LogDebug(
            "Application settings loaded. PreparationLimit = {PreparationLimit}, card direction = {CardDirection}, learning mode = {LearningMode}, online lookup consent = {HasOnlineLookupConsent}, enhanced term recognition = {EnhancedTermRecognitionEnabled}",
            PreparationLimit,
            CardDirection,
            LearningMode,
            HasOnlineLookupConsent,
            EnhancedTermRecognitionEnabled);
    }

    public int PreparationLimit { get; private set; }

    public IReadOnlyList<int> SupportedPreparationLimits => PreparationLimitPolicy.SupportedLimits;

    public CardDirectionPreference CardDirection { get; private set; }

    public LearningMode LearningMode { get; private set; }

    public bool HasOnlineLookupConsent { get; private set; }

    public bool EnhancedTermRecognitionEnabled { get; private set; }

    public void SetPreparationLimit(int preparationLimit)
    {
        var normalizedLimit = PreparationLimitPolicy.Normalize(preparationLimit);
        if (normalizedLimit != preparationLimit)
        {
            _logger.LogWarning(
                "The requested preparation limit '{PreparationLimit}' is unsupported. Falling back to the default.",
                preparationLimit);
        }

        _preferences.Set(PreparationLimitPreferenceKey, normalizedLimit);
        PreparationLimit = normalizedLimit;
        _logger.LogInformation(
            "Preparation limit saved. PreparationLimit = {PreparationLimit}",
            normalizedLimit);
    }

    public void SetCardDirection(CardDirectionPreference preference)
    {
        var normalized = CardDirectionPreferencePolicy.Normalize((int)preference);
        _preferences.Set(CardDirectionPreferenceKey, (int)normalized);
        CardDirection = normalized;
        _logger.LogInformation("Card direction saved. CardDirection = {CardDirection}", normalized);
    }

    public void SetLearningMode(LearningMode mode)
    {
        var normalized = LearningModePolicy.Normalize((int)mode);
        _preferences.Set(LearningModePreferenceKey, (int)normalized);
        LearningMode = normalized;
        _logger.LogInformation("Learning mode saved. LearningMode = {LearningMode}", normalized);
    }

    public void GrantOnlineLookupConsent()
    {
        _preferences.Set(OnlineLookupConsentPreferenceKey, true);
        HasOnlineLookupConsent = true;
        _logger.LogInformation("Online dictionary lookup consent was granted.");
    }

    public void RevokeOnlineLookupConsent()
    {
        _preferences.Remove(OnlineLookupConsentPreferenceKey);
        HasOnlineLookupConsent = false;
        _logger.LogInformation("Online dictionary lookup consent was revoked.");
    }

    public void SetEnhancedTermRecognitionEnabled(bool enabled)
    {
        _preferences.Set(EnhancedTermRecognitionPreferenceKey, enabled);
        EnhancedTermRecognitionEnabled = enabled;
        _logger.LogInformation(
            "Enhanced term recognition setting saved. EnhancedTermRecognitionEnabled = {EnhancedTermRecognitionEnabled}",
            enabled);
    }

    public void Reset()
    {
        _preferences.Remove(PreparationLimitPreferenceKey);
        _preferences.Remove(CardDirectionPreferenceKey);
        _preferences.Remove(LearningModePreferenceKey);
        _preferences.Remove(OnlineLookupConsentPreferenceKey);
        _preferences.Remove(EnhancedTermRecognitionPreferenceKey);
        PreparationLimit = DefaultPreparationLimit;
        CardDirection = CardDirectionPreferencePolicy.DefaultPreference;
        LearningMode = LearningModePolicy.DefaultMode;
        HasOnlineLookupConsent = false;
        EnhancedTermRecognitionEnabled = false;
        _logger.LogInformation("Application settings were reset to defaults.");
    }

    private int ReadPreparationLimit()
    {
        var savedLimit = _preferences.Get(PreparationLimitPreferenceKey, DefaultPreparationLimit);
        var normalizedLimit = PreparationLimitPolicy.Normalize(savedLimit);
        if (normalizedLimit == savedLimit)
        {
            return normalizedLimit;
        }

        _logger.LogWarning(
            "The saved preparation limit '{PreparationLimit}' is unsupported. Falling back to the default.",
            savedLimit);
        _preferences.Set(PreparationLimitPreferenceKey, normalizedLimit);
        return normalizedLimit;
    }

    private CardDirectionPreference ReadCardDirection()
    {
        var saved = _preferences.Get(
            CardDirectionPreferenceKey,
            (int)CardDirectionPreferencePolicy.DefaultPreference);
        var normalized = CardDirectionPreferencePolicy.Normalize(saved);
        if ((int)normalized != saved)
        {
            _logger.LogWarning(
                "The saved card direction value '{CardDirection}' is unsupported. Falling back to Both directions.",
                saved);
            _preferences.Set(CardDirectionPreferenceKey, (int)normalized);
        }

        return normalized;
    }

    private LearningMode ReadLearningMode()
    {
        var saved = _preferences.Get(
            LearningModePreferenceKey,
            (int)LearningModePolicy.DefaultMode);
        var normalized = LearningModePolicy.Normalize(saved);
        if ((int)normalized != saved)
        {
            _logger.LogWarning(
                "The saved learning mode value '{LearningMode}' is unsupported. Falling back to Automatic.",
                saved);
            _preferences.Set(LearningModePreferenceKey, (int)normalized);
        }

        return normalized;
    }
}
