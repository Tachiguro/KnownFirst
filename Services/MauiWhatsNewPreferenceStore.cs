namespace KnownFirst.Services;

public sealed class MauiWhatsNewPreferenceStore : IWhatsNewPreferenceStore
{
    private const string SeenVersionPreferenceKey = "whats_new_seen_version";

    public string GetSeenVersion() => Preferences.Default.Get(SeenVersionPreferenceKey, string.Empty);

    public void SetSeenVersion(string version) => Preferences.Default.Set(SeenVersionPreferenceKey, version);
}
