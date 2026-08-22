using KnownFirst.Core.Learning;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningDayBoundaryPolicyTests
{
    [TestMethod]
    public void CalculateDayBoundariesUtc_WithMidnightCutoffUtc_ReturnsExactDayBoundaries()
    {
        var nowUtc = new DateTime(2026, 8, 15, 14, 30, 0, DateTimeKind.Utc);
        var tz = TimeZoneInfo.Utc;
        var cutoff = 0; // 00:00

        var (startUtc, endUtc, localDate) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, tz, cutoff);

        Assert.AreEqual(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.AreEqual(new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.AreEqual(new DateOnly(2026, 8, 15), localDate);
    }

    [TestMethod]
    public void CalculateDayBoundariesUtc_WithCutoff0400_BeforeCutoff_BelongsToPreviousCalendarDay()
    {
        var nowUtc = new DateTime(2026, 8, 15, 3, 30, 0, DateTimeKind.Utc);
        var tz = TimeZoneInfo.Utc;
        var cutoff = 240; // 04:00 (240 minutes)

        var (startUtc, endUtc, localDate) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, tz, cutoff);

        Assert.AreEqual(new DateTime(2026, 8, 14, 4, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.AreEqual(new DateTime(2026, 8, 15, 4, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.AreEqual(new DateOnly(2026, 8, 14), localDate);
    }

    [TestMethod]
    public void CalculateDayBoundariesUtc_WithCutoff0400_AfterCutoff_BelongsToCurrentCalendarDay()
    {
        var nowUtc = new DateTime(2026, 8, 15, 4, 30, 0, DateTimeKind.Utc);
        var tz = TimeZoneInfo.Utc;
        var cutoff = 240; // 04:00

        var (startUtc, endUtc, localDate) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, tz, cutoff);

        Assert.AreEqual(new DateTime(2026, 8, 15, 4, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.AreEqual(new DateTime(2026, 8, 16, 4, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.AreEqual(new DateOnly(2026, 8, 15), localDate);
    }

    [TestMethod]
    public void CalculateDayBoundariesUtc_WithNonUtcTimezone_ComputesCorrectUtcBoundaries()
    {
        // Tokyo is UTC+9 year-round (no DST)
        var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        // 2026-08-15 16:00 UTC = 2026-08-16 01:00 Tokyo
        var nowUtc = new DateTime(2026, 8, 15, 16, 0, 0, DateTimeKind.Utc);
        var cutoff = 0; // Midnight

        var (startUtc, endUtc, localDate) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, tokyoTz, cutoff);

        // Tokyo day start: 2026-08-16 00:00 Tokyo = 2026-08-15 15:00 UTC
        // Tokyo day end: 2026-08-17 00:00 Tokyo = 2026-08-16 15:00 UTC
        Assert.AreEqual(new DateTime(2026, 8, 15, 15, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.AreEqual(new DateTime(2026, 8, 16, 15, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.AreEqual(new DateOnly(2026, 8, 16), localDate);
    }

    [TestMethod]
    public void CalculateNextDayStartAtOrAfter_SameParameters_ReturnsExactTimestamp()
    {
        var tz = TimeZoneInfo.Utc;
        var cutoff = 0;
        var endUtc = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc);

        var nextStart = LearningDayBoundaryPolicy.CalculateNextDayStartAtOrAfter(endUtc, tz, cutoff);

        Assert.AreEqual(endUtc, nextStart);
    }

    [TestMethod]
    public void CalculateNextDayStartAtOrAfter_DifferentCutoff_ReturnsEarliestFutureBoundary()
    {
        var tz = TimeZoneInfo.Utc;
        var endUtc = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc);
        var targetCutoff = 240; // 04:00

        var nextStart = LearningDayBoundaryPolicy.CalculateNextDayStartAtOrAfter(endUtc, tz, targetCutoff);

        // At or after 2026-08-16 00:00 UTC, the first 04:00 UTC is 2026-08-16 04:00 UTC
        Assert.AreEqual(new DateTime(2026, 8, 16, 4, 0, 0, DateTimeKind.Utc), nextStart);
    }

    [TestMethod]
    public void AreTimeZonesEquivalent_SameId_ReturnsTrue()
    {
        var tz1 = TimeZoneInfo.Utc;
        var tz2 = TimeZoneInfo.Utc;
        var atUtc = DateTime.UtcNow;

        Assert.IsTrue(LearningDayBoundaryPolicy.AreTimeZonesEquivalent(tz1, tz2, atUtc));
    }

    [TestMethod]
    public void SystemBoundaryEquivalentDifferentIdentifiers_DoNotCreateArtificialTransition()
    {
        var tz1 = TimeZoneInfo.CreateCustomTimeZone("CustomTz_A", TimeSpan.FromHours(2), "Custom Zone A", "Standard Zone A");
        var tz2 = TimeZoneInfo.CreateCustomTimeZone("CustomTz_B", TimeSpan.FromHours(2), "Custom Zone B", "Standard Zone B");
        var checkUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.AreNotEqual(tz1.Id, tz2.Id);
        Assert.IsTrue(LearningDayBoundaryPolicy.AreTimeZonesEquivalent(tz1, tz2, checkUtc));

        var anchorUtc = new DateTime(2026, 8, 16, 22, 0, 0, DateTimeKind.Utc); // 00:00 local in +2
        var cutoff = 0;
        var nextStartA = LearningDayBoundaryPolicy.CalculateNextDayStartAtOrAfter(anchorUtc, tz1, cutoff);
        var nextStartB = LearningDayBoundaryPolicy.CalculateNextDayStartAtOrAfter(anchorUtc, tz2, cutoff);

        Assert.AreEqual(anchorUtc, nextStartA);
        Assert.AreEqual(anchorUtc, nextStartB);
        Assert.AreEqual(nextStartA, nextStartB);
    }

    [TestMethod]
    public void AreTimeZonesEquivalent_DifferentUpcomingTransitions_ReturnsFalse()
    {
        var tzConstant = TimeZoneInfo.CreateCustomTimeZone("Constant_Plus1", TimeSpan.FromHours(1), "Constant Plus1", "Standard Constant");

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 8, 16),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25));

        var tzTransitioning = TimeZoneInfo.CreateCustomTimeZone(
            "Transitioning_Plus1", TimeSpan.FromHours(1), "Transitioning Plus1", "Standard Transitioning", "Daylight Transitioning", [rule]);

        var checkUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.AreEqual(tzConstant.GetUtcOffset(checkUtc), tzTransitioning.GetUtcOffset(checkUtc));
        Assert.IsFalse(LearningDayBoundaryPolicy.AreTimeZonesEquivalent(tzConstant, tzTransitioning, checkUtc));
    }
}
