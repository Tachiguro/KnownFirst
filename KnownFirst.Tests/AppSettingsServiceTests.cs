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
        Assert.AreEqual(5, service.PreparationLimit);
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
    public void ReadPreparationLimit_WhenPreferenceIsAbsent_ReturnsProductDefaultFive()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.AreEqual(5, service.PreparationLimit);
        Assert.IsFalse(preferences.ContainsKey("preparation_limit"));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(10)]
    [DataRow(20)]
    [DataRow(25)]
    [DataRow(30)]
    [DataRow(50)]
    public void ReadPreparationLimit_WhenStoredValueIsWithinRange_ReturnsValueWithoutRewrite(int storedLimit)
    {
        var preferences = new InMemoryPreferences();
        preferences.Set("preparation_limit", storedLimit);
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.AreEqual(storedLimit, service.PreparationLimit);
        Assert.AreEqual(storedLimit, preferences.Get("preparation_limit", -1));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    [DataRow(51)]
    [DataRow(99)]
    public void ReadPreparationLimit_WhenStoredValueIsOutsideRange_NormalizesToFiveAndRewrites(int invalidStoredLimit)
    {
        var preferences = new InMemoryPreferences();
        preferences.Set("preparation_limit", invalidStoredLimit);
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        Assert.AreEqual(5, service.PreparationLimit);
        Assert.AreEqual(5, preferences.Get("preparation_limit", -1));
    }

    [TestMethod]
    public void SupportedPreparationLimits_ExposesPresetsOneFiveTen()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        CollectionAssert.AreEqual(new[] { 1, 5, 10 }, service.SupportedPreparationLimits.ToArray());
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

    [TestMethod]
    public void OnlineLookupConsentChanged_FiresWhenConsentIsGranted()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var notifications = new List<bool>();
        service.OnlineLookupConsentChanged += value => notifications.Add(value);

        service.GrantOnlineLookupConsent();

        Assert.AreEqual(1, notifications.Count);
        Assert.IsTrue(notifications[0]);
    }

    [TestMethod]
    public void OnlineLookupConsentChanged_DoesNotFireWhenAlreadyGranted()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        service.GrantOnlineLookupConsent();

        var notifications = new List<bool>();
        service.OnlineLookupConsentChanged += value => notifications.Add(value);

        service.GrantOnlineLookupConsent();

        Assert.AreEqual(0, notifications.Count);
    }

    [TestMethod]
    public void OnlineLookupConsentChanged_FiresWhenConsentIsRevoked()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        service.GrantOnlineLookupConsent();

        var notifications = new List<bool>();
        service.OnlineLookupConsentChanged += value => notifications.Add(value);

        service.RevokeOnlineLookupConsent();

        Assert.AreEqual(1, notifications.Count);
        Assert.IsFalse(notifications[0]);
    }

    [TestMethod]
    public void OnlineLookupConsentChanged_DoesNotFireWhenAlreadyRevoked()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        var notifications = new List<bool>();
        service.OnlineLookupConsentChanged += value => notifications.Add(value);

        service.RevokeOnlineLookupConsent();

        Assert.AreEqual(0, notifications.Count);
    }

    [TestMethod]
    public void OnlineLookupConsentChanged_FiresOnResetWhenConsentWasGranted()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        service.GrantOnlineLookupConsent();

        var notifications = new List<bool>();
        service.OnlineLookupConsentChanged += value => notifications.Add(value);

        service.Reset();

        Assert.AreEqual(1, notifications.Count);
        Assert.IsFalse(notifications[0]);
    }

    [TestMethod]
    public void OnlineLookupConsentChanged_DoesNotFireOnResetWhenConsentWasAlreadyFalse()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        var notifications = new List<bool>();
        service.OnlineLookupConsentChanged += value => notifications.Add(value);

        service.Reset();

        Assert.AreEqual(0, notifications.Count);
    }
}
