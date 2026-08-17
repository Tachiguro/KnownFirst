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
    public void EnhancedTermRecognitionEnabled_DefaultsToFalse()
    {
        var preferences = new InMemoryPreferences();
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

        Assert.IsFalse(service.EnhancedTermRecognitionEnabled);

        service.SetEnhancedTermRecognitionEnabled(true);

        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
        Assert.IsTrue(preferences.Get("enhanced_term_recognition_enabled", false));

        service.SetEnhancedTermRecognitionEnabled(false);

        Assert.IsFalse(service.EnhancedTermRecognitionEnabled);
        Assert.IsFalse(preferences.Get("enhanced_term_recognition_enabled", true));
    }

    [TestMethod]
    public void Reset_ClearsEnhancedTermRecognitionAndRestoresFalse()
    {
        var preferences = new InMemoryPreferences();
        var service = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);

        service.SetEnhancedTermRecognitionEnabled(true);
        Assert.IsTrue(service.EnhancedTermRecognitionEnabled);
        Assert.IsTrue(preferences.ContainsKey("enhanced_term_recognition_enabled"));

        service.Reset();

        Assert.IsFalse(service.EnhancedTermRecognitionEnabled);
        Assert.IsFalse(preferences.ContainsKey("enhanced_term_recognition_enabled"));
    }
}
