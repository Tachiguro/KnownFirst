using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Services;

public sealed class ReleaseNotesService : IReleaseNotesService
{
    // Plain C# data, not JSON: each entry is a version plus the localization resource keys for
    // its title and bullets. The actual text lives in the .resx files, keeping this catalog
    // free of reflection-based (de)serialization and easy to extend for future releases.
    internal static readonly IReadOnlyList<ReleaseNoteEntry> DefaultCatalog =
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

    private readonly IBuildIdentityService _buildIdentityService;
    private readonly IWhatsNewPreferenceStore _preferenceStore;
    private readonly ILogger<ReleaseNotesService> _logger;
    private readonly IReadOnlyList<ReleaseNoteEntry> _catalog;

    public ReleaseNotesService(
        IBuildIdentityService buildIdentityService,
        IWhatsNewPreferenceStore preferenceStore,
        ILogger<ReleaseNotesService> logger)
        : this(buildIdentityService, preferenceStore, logger, DefaultCatalog)
    {
    }

    // Internal-only seam so tests can exercise "a later version with its own entry" scenarios
    // without mutating the shared static catalog (which would race under parallel test
    // execution). Production always uses the public constructor above, i.e. DefaultCatalog.
    internal ReleaseNotesService(
        IBuildIdentityService buildIdentityService,
        IWhatsNewPreferenceStore preferenceStore,
        ILogger<ReleaseNotesService> logger,
        IReadOnlyList<ReleaseNoteEntry> catalog)
    {
        _buildIdentityService = buildIdentityService;
        _preferenceStore = preferenceStore;
        _logger = logger;
        _catalog = catalog;
    }

    public ReleaseNoteEntry? GetUnseenReleaseNotes()
    {
        try
        {
            var currentVersion = _buildIdentityService.Identity.Version;
            var entry = _catalog.FirstOrDefault(
                candidate => string.Equals(candidate.Version, currentVersion, StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            var seenVersion = _preferenceStore.GetSeenVersion();
            return string.Equals(seenVersion, currentVersion, StringComparison.Ordinal) ? null : entry;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The What's New seen-version preference could not be read. The notice will not be shown this run.");
            return null;
        }
    }

    public void MarkSeen(string version)
    {
        try
        {
            _preferenceStore.SetSeenVersion(version);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The What's New seen-version preference could not be saved.");
        }
    }
}
