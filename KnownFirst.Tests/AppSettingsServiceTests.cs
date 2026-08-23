using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class AppSettingsServiceTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new();

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);

        public void Remove(string key, string? sharedName = null) => _values.Remove(key);

        public void Clear(string? sharedName = null) => _values.Clear();

        public void Set<T>(string key, T value, string? sharedName = null)
        {
            if (value is null)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
        }

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            if (_values.TryGetValue(key, out var val) && val is T typedVal)
            {
                return typedVal;
            }

            return defaultValue;
        }
    }

    [TestMethod]
    public void EnhancedTermRecognitionEnabled_DefaultsToTrue()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
    }

    [TestMethod]
    public void EnhancedTermRecognitionEnabled_WhenPreferenceIsAbsent_DefaultMatchesPolicy()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.AreEqual(
            EnhancedTermRecognitionPolicy.DefaultEnabled,
            service.EnhancedTermRecognitionEnabled);
    }

    [TestMethod]
    public void EnhancedTermRecognitionEnabled_PersistedFalseRemainsFalse()
    {
        var preferences = new InMemoryPreferences();
        preferences.Set("enhanced_term_recognition_enabled", false);
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.IsFalse(service.EnhancedTermRecognitionEnabled);
    }

    [TestMethod]
    public void EnhancedTermRecognitionEnabled_LoadsPersistedTrue()
    {
        var preferences = new InMemoryPreferences();
        preferences.Set("enhanced_term_recognition_enabled", true);
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
    }

    [TestMethod]
    public void SetEnhancedTermRecognitionEnabled_PersistsAndUpdatesValue()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        service.SetEnhancedTermRecognitionEnabled(false);

        Assert.IsFalse(service.EnhancedTermRecognitionEnabled);
        Assert.IsFalse(preferences.Get("enhanced_term_recognition_enabled", true));

        service.SetEnhancedTermRecognitionEnabled(true);

        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
        Assert.IsTrue(preferences.Get("enhanced_term_recognition_enabled", false));
    }

    [TestMethod]
    public void Reset_ClearsEnhancedTermRecognitionAndRestoresTrue()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        service.SetEnhancedTermRecognitionEnabled(false);
        Assert.IsFalse(service.EnhancedTermRecognitionEnabled);
        Assert.IsTrue(preferences.ContainsKey("enhanced_term_recognition_enabled"));

        service.Reset();

        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
        Assert.IsFalse(preferences.ContainsKey("enhanced_term_recognition_enabled"));
    }

    [TestMethod]
    public void Reset_RestoresTheCompleteTargetApplicationSettingsDefaults()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        service.SetPreparationLimit(50);
        service.SetCardDirection(CardDirectionPreference.MeaningToTerm);
        service.SetLearningMode(LearningMode.Typing);
        service.GrantOnlineLookupConsent();
        service.SetEnhancedTermRecognitionEnabled(false);
        service.SetLearningTimezoneMode(LearningTimezoneMode.Explicit);
        service.SetExplicitLearningTimezoneId("Asia/Tokyo");
        service.SetLearningDayCutoffMinutes(457);

        service.Reset();

        Assert.AreEqual(PreparationLimitPolicy.DefaultLimit, service.PreparationLimit);
        Assert.AreEqual(10, service.PreparationLimit);
        Assert.AreEqual(CardDirectionPreferencePolicy.DefaultPreference, service.CardDirection);
        Assert.AreEqual(CardDirectionPreference.Both, service.CardDirection);
        Assert.AreEqual(LearningModePolicy.DefaultMode, service.LearningMode);
        Assert.AreEqual(LearningMode.Automatic, service.LearningMode);
        Assert.IsFalse(service.HasOnlineLookupConsent);
        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
        Assert.AreEqual(LearningTimezoneMode.System, service.LearningTimezoneMode);
        Assert.IsNull(service.ExplicitLearningTimezoneId);
        Assert.AreEqual(LearningDayConfiguration.DefaultCutoffMinutes, service.LearningDayCutoffMinutes);
        Assert.AreEqual(0, service.LearningDayCutoffMinutes);
    }

    [TestMethod]
    public void SetLearningDayCutoffMinutes_AcceptsArbitraryMinutePrecisionValues()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        service.SetLearningDayCutoffMinutes(457);

        Assert.AreEqual(457, service.LearningDayCutoffMinutes);
        Assert.AreEqual(457, preferences.Get("learning_day_cutoff_minutes", -1));
    }

    [TestMethod]
    public void SetExplicitLearningTimezoneId_PersistsCanonicalIanaIdentity()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        service.SetLearningTimezoneMode(LearningTimezoneMode.Explicit);
        service.SetExplicitLearningTimezoneId("Europe/Berlin");

        Assert.AreEqual("Europe/Berlin", service.ExplicitLearningTimezoneId);
        Assert.AreEqual("Europe/Berlin", preferences.Get("explicit_learning_timezone_id", (string?)null));
    }
}
