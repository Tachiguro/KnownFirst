using System;

namespace KnownFirst.Core.Learning;

public static class LearningDayBoundaryPolicy
{
    public static (DateTime StartUtc, DateTime EndUtc, DateOnly LogicalDate) CalculateDayBoundariesUtc(
        DateTime utcInstant,
        TimeZoneInfo timeZone,
        int cutoffMinutes)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        cutoffMinutes = LearningDayConfiguration.NormalizeCutoffMinutes(cutoffMinutes);
        utcInstant = DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);

        var localInstant = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, timeZone);
        var localDate = DateOnly.FromDateTime(localInstant.Date);
        var todayCutoff = localDate.ToDateTime(TimeOnly.MinValue).AddMinutes(cutoffMinutes);

        DateOnly logicalDate;
        if (localInstant < todayCutoff)
        {
            logicalDate = localDate.AddDays(-1);
        }
        else
        {
            logicalDate = localDate;
        }

        var localStart = logicalDate.ToDateTime(TimeOnly.MinValue).AddMinutes(cutoffMinutes);
        var localEnd = logicalDate.AddDays(1).ToDateTime(TimeOnly.MinValue).AddMinutes(cutoffMinutes);

        var startUtc = ConvertToUtcSafe(localStart, timeZone);
        var endUtc = ConvertToUtcSafe(localEnd, timeZone);

        return (startUtc, endUtc, logicalDate);
    }

    public static DateTime CalculateNextDayStartAtOrAfter(
        DateTime anchorUtc,
        TimeZoneInfo targetTimeZone,
        int cutoffMinutes)
    {
        ArgumentNullException.ThrowIfNull(targetTimeZone);
        cutoffMinutes = LearningDayConfiguration.NormalizeCutoffMinutes(cutoffMinutes);
        anchorUtc = DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc);

        var (startUtc, endUtc, _) = CalculateDayBoundariesUtc(anchorUtc, targetTimeZone, cutoffMinutes);
        if (startUtc == anchorUtc)
        {
            return anchorUtc;
        }

        return endUtc;
    }

    public static DateTime ConvertToUtcSafe(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(localDateTime))
        {
            // Spring-forward gap: advance past gap to valid local time
            var testTime = localDateTime.AddHours(1);
            while (timeZone.IsInvalidTime(testTime))
            {
                testTime = testTime.AddMinutes(15);
            }
            return TimeZoneInfo.ConvertTimeToUtc(testTime, timeZone);
        }

        if (timeZone.IsAmbiguousTime(localDateTime))
        {
            // Fall-back overlap: pick earlier UTC instant (daylight saving time offset)
            var offsets = timeZone.GetAmbiguousTimeOffsets(localDateTime);
            var maxOffset = offsets[0];
            for (var i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] > maxOffset)
                {
                    maxOffset = offsets[i];
                }
            }
            var offsetDateTime = new DateTimeOffset(localDateTime, maxOffset);
            return offsetDateTime.UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
    }

    public static bool AreTimeZonesEquivalent(
        TimeZoneInfo zoneA,
        TimeZoneInfo zoneB,
        DateTime checkUtc)
    {
        if (ReferenceEquals(zoneA, zoneB))
        {
            return true;
        }

        if (string.Equals(zoneA.Id, zoneB.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        checkUtc = DateTime.SpecifyKind(checkUtc, DateTimeKind.Utc);
        var offsetA = zoneA.GetUtcOffset(checkUtc);
        var offsetB = zoneB.GetUtcOffset(checkUtc);
        if (offsetA != offsetB)
        {
            return false;
        }

        // Check if next boundary transition in 48 hours is identical
        var checkPlus24 = checkUtc.AddHours(24);
        var checkPlus48 = checkUtc.AddHours(48);
        return zoneA.GetUtcOffset(checkPlus24) == zoneB.GetUtcOffset(checkPlus24)
            && zoneA.GetUtcOffset(checkPlus48) == zoneB.GetUtcOffset(checkPlus48);
    }
}
