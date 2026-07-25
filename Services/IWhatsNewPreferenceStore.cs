namespace KnownFirst.Services;

public interface IWhatsNewPreferenceStore
{
    string GetSeenVersion();

    void SetSeenVersion(string version);
}
