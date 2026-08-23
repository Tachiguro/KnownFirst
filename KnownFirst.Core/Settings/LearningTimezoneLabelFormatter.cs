using System.Globalization;

namespace KnownFirst.Core.Settings;

/// <summary>
/// Deterministic, culture-independent formatting for learning-timezone labels.
/// <para>
/// The rendered offset is always computed from <see cref="TimeZoneInfo"/> for a concrete instant,
/// never read from a stored value, so a label such as <c>(UTC+02:00) Berlin</c> becomes
/// <c>(UTC+01:00) Berlin</c> once daylight saving time ends. Formatting uses the invariant culture
/// so the digits and separators do not depend on the active UI language.
/// </para>
/// </summary>
public static class LearningTimezoneLabelFormatter
{
    public static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset < TimeSpan.Zero ? offset.Negate() : offset;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"UTC{sign}{(int)absolute.TotalHours:D2}:{absolute.Minutes:D2}");
    }

    public static string FormatUtcOffset(TimeZoneInfo timeZone, DateTime instantUtc)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return FormatUtcOffset(timeZone.GetUtcOffset(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc)));
    }

    public static string FormatOptionLabel(TimeZoneInfo timeZone, DateTime instantUtc, string cityName)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return "(" + FormatUtcOffset(timeZone, instantUtc) + ") " + cityName;
    }
}
