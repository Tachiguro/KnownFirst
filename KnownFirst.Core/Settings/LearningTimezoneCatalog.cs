namespace KnownFirst.Core.Settings;

/// <summary>
/// One curated learning-timezone choice. <paramref name="TimezoneId"/> is the canonical IANA
/// identity that is persisted; <paramref name="CityResourceKey"/> names the localized city label
/// used to build the user-facing option text. A UTC offset is never part of the stored identity.
/// </summary>
public sealed record LearningTimezoneOption(string TimezoneId, string CityResourceKey);

/// <summary>
/// Bounded, curated world-timezone catalog for the learning-timezone setting.
/// <para>
/// The catalog is deliberately hand-maintained instead of enumerating
/// <see cref="TimeZoneInfo.GetSystemTimeZones"/>: the operating system decides both the ordering
/// and the display names of its zones, which would make the Settings list non-deterministic and
/// unlocalizable across Windows and Android. Every entry uses a canonical IANA identifier, which
/// .NET resolves directly through <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> on
/// both supported platforms, so no IANA/Windows identifier conversion layer is required.
/// </para>
/// <para>
/// Entries are ordered west to east by their standard (non-daylight-saving) UTC offset. The
/// displayed offset itself is never stored here; it is computed for the relevant instant by
/// <see cref="LearningTimezoneLabelFormatter"/> so the label follows daylight saving time.
/// </para>
/// </summary>
public static class LearningTimezoneCatalog
{
    public const string CityResourceKeyPrefix = "Timezone_City_";

    private static readonly IReadOnlyList<LearningTimezoneOption> Entries = Array.AsReadOnly(
    [
        Create("Pacific/Honolulu"),
        Create("America/Anchorage"),
        Create("America/Los_Angeles"),
        Create("America/Denver"),
        Create("America/Chicago"),
        Create("America/Mexico_City"),
        Create("America/New_York"),
        Create("America/Bogota"),
        Create("America/Halifax"),
        Create("America/Santiago"),
        Create("America/Sao_Paulo"),
        Create("America/Argentina/Buenos_Aires"),
        Create("Atlantic/Azores"),
        Create("UTC"),
        Create("Atlantic/Reykjavik"),
        Create("Europe/London"),
        Create("Europe/Lisbon"),
        Create("Africa/Lagos"),
        Create("Europe/Berlin"),
        Create("Europe/Paris"),
        Create("Europe/Madrid"),
        Create("Europe/Rome"),
        Create("Europe/Warsaw"),
        Create("Europe/Athens"),
        Create("Europe/Helsinki"),
        Create("Africa/Cairo"),
        Create("Africa/Johannesburg"),
        Create("Asia/Jerusalem"),
        Create("Europe/Moscow"),
        Create("Europe/Istanbul"),
        Create("Asia/Dubai"),
        Create("Asia/Karachi"),
        Create("Asia/Yekaterinburg"),
        Create("Asia/Kolkata"),
        Create("Asia/Dhaka"),
        Create("Asia/Bangkok"),
        Create("Asia/Jakarta"),
        Create("Asia/Novosibirsk"),
        Create("Asia/Shanghai"),
        Create("Asia/Singapore"),
        Create("Asia/Tokyo"),
        Create("Asia/Seoul"),
        Create("Australia/Adelaide"),
        Create("Australia/Sydney"),
        Create("Asia/Vladivostok"),
        Create("Pacific/Auckland")
    ]);

    private static readonly HashSet<string> EntryIds =
        new(Entries.Select(entry => entry.TimezoneId), StringComparer.Ordinal);

    public static IReadOnlyList<LearningTimezoneOption> Options => Entries;

    public static bool ContainsTimezoneId(string? timezoneId) =>
        !string.IsNullOrWhiteSpace(timezoneId) && EntryIds.Contains(timezoneId.Trim());

    public static LearningTimezoneOption? Find(string? timezoneId) =>
        string.IsNullOrWhiteSpace(timezoneId)
            ? null
            : Entries.FirstOrDefault(entry =>
                string.Equals(entry.TimezoneId, timezoneId.Trim(), StringComparison.Ordinal));

    private static LearningTimezoneOption Create(string timezoneId) =>
        new(timezoneId, CityResourceKeyPrefix + timezoneId.Replace('/', '_'));
}
