using System.Globalization;

namespace KnownFirst.Core.Learning;

/// <summary>
/// Converts between the stored learning-day cutoff (minutes after local midnight, 0..1439) and the
/// <c>HH:mm</c> wall-clock text used by the minute-precision Settings control.
/// <para>
/// This is a presentation helper only. It never narrows the valid cutoff domain: normalization
/// remains owned by <see cref="LearningDayConfiguration.NormalizeCutoffMinutes(int)"/>, and any
/// minute of the day stays selectable.
/// </para>
/// </summary>
public static class LearningDayCutoffFormatter
{
    public static string ToWallClockText(int cutoffMinutes)
    {
        var normalized = LearningDayConfiguration.NormalizeCutoffMinutes(cutoffMinutes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{normalized / 60:D2}:{normalized % 60:D2}");
    }

    public static int ParseWallClockText(string? wallClockText)
    {
        if (string.IsNullOrWhiteSpace(wallClockText))
        {
            return LearningDayConfiguration.DefaultCutoffMinutes;
        }

        var candidate = wallClockText.Trim();
        var parts = candidate.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return LearningDayConfiguration.DefaultCutoffMinutes;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            return LearningDayConfiguration.DefaultCutoffMinutes;
        }

        if (hours is < 0 or > 23 || minutes is < 0 or > 59)
        {
            return LearningDayConfiguration.DefaultCutoffMinutes;
        }

        // Seconds are accepted but intentionally discarded: the stored cutoff is minute precise.
        if (parts.Length == 3
            && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds is < 0 or > 59))
        {
            return LearningDayConfiguration.DefaultCutoffMinutes;
        }

        return LearningDayConfiguration.NormalizeCutoffMinutes((hours * 60) + minutes);
    }
}
