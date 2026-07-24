using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services;

public sealed class ReleaseNotesService(
    IBuildIdentityService buildIdentityService,
    ILogger<ReleaseNotesService> logger) : IReleaseNotesService
{
    private const string SeenVersionPreferenceKey = "whats_new_seen_version";

    // Plain C# data, not JSON: each entry is a version plus the localization resource keys for
    // its title and bullets. The actual text lives in the .resx files, keeping this catalog
    // free of reflection-based (de)serialization and easy to extend for future releases.
    private static readonly IReadOnlyList<ReleaseNoteEntry> Catalog =
    [
        new ReleaseNoteEntry(
            "1.0.0-beta.10",
            "WhatsNew_Title",
            [
                "WhatsNew_Beta10_Bullet1",
                "WhatsNew_Beta10_Bullet2",
                "WhatsNew_Beta10_Bullet3",
                "WhatsNew_Beta10_Bullet4"
            ])
    ];

    public ReleaseNoteEntry? GetUnseenReleaseNotes()
    {
        try
        {
            var currentVersion = buildIdentityService.Identity.Version;
            var entry = Catalog.FirstOrDefault(
                candidate => string.Equals(candidate.Version, currentVersion, StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            var seenVersion = Preferences.Default.Get(SeenVersionPreferenceKey, string.Empty);
            return string.Equals(seenVersion, currentVersion, StringComparison.Ordinal) ? null : entry;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The What's New seen-version preference could not be read. The notice will not be shown this run.");
            return null;
        }
    }

    public void MarkSeen(string version)
    {
        try
        {
            Preferences.Default.Set(SeenVersionPreferenceKey, version);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The What's New seen-version preference could not be saved.");
        }
    }
}
