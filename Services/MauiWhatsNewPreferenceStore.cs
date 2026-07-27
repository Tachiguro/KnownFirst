using Microsoft.Maui.Storage;

namespace KnownFirst.Services;

public sealed class MauiWhatsNewPreferenceStore(IPreferences preferences) : IWhatsNewPreferenceStore
{
    private const string SeenVersionPreferenceKey = "whats_new_seen_version";

    public string GetSeenVersion() => preferences.Get(SeenVersionPreferenceKey, string.Empty);

    public void SetSeenVersion(string version) => preferences.Set(SeenVersionPreferenceKey, version);
}
