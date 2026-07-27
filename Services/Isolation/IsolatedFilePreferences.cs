using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnownFirst.Services.Isolation;

/// <summary>
/// A JSON-file-backed <see cref="Microsoft.Maui.Storage.IPreferences"/> used only while a GUI
/// test profile is active, so test runs never read or write the real device preferences store.
/// Values are serialized via a source-generated context because reflection-based
/// System.Text.Json is disabled for this application.
/// </summary>
public sealed class IsolatedFilePreferences : Microsoft.Maui.Storage.IPreferences
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private Dictionary<string, string> _values;

    public IsolatedFilePreferences(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _filePath = Path.Combine(rootDirectory, "preferences.json");
        _values = Load(_filePath);
    }

    public bool ContainsKey(string key, string? sharedName = null)
    {
        lock (_sync)
        {
            return _values.ContainsKey(key);
        }
    }

    public void Remove(string key, string? sharedName = null)
    {
        lock (_sync)
        {
            if (_values.Remove(key))
            {
                Save();
            }
        }
    }

    public void Clear(string? sharedName = null)
    {
        lock (_sync)
        {
            if (_values.Count == 0)
            {
                return;
            }

            _values.Clear();
            Save();
        }
    }

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        lock (_sync)
        {
            _values[key] = SerializeValue(value);
            Save();
        }
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        lock (_sync)
        {
            if (!_values.TryGetValue(key, out var json))
            {
                return defaultValue;
            }

            try
            {
                var deserialized = DeserializeValue<T>(json);
                return deserialized is null ? defaultValue : deserialized;
            }
            catch (JsonException)
            {
                return defaultValue;
            }
            catch (NotSupportedException)
            {
                return defaultValue;
            }
        }
    }

    private static string SerializeValue<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, typeof(T), IsolatedPreferencesJsonContext.Default);
        return json;
    }

    private static T? DeserializeValue<T>(string json)
    {
        var result = JsonSerializer.Deserialize(json, typeof(T), IsolatedPreferencesJsonContext.Default);
        return result is T typed ? typed : default;
    }

    private static Dictionary<string, string> Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize(json, IsolatedPreferencesJsonContext.Default.DictionaryStringString)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_values, IsolatedPreferencesJsonContext.Default.DictionaryStringString);
        File.WriteAllText(_filePath, json);
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class IsolatedPreferencesJsonContext : JsonSerializerContext
{
}
