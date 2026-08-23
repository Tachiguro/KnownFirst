using KnownFirst.Core.Learning;

namespace KnownFirst.Tests;

/// <summary>
/// Focused characterization and contract tests proving that the learning-day cutoff remains a
/// minute-precision local wall-clock value. Slice 2A must not narrow the stored domain to whole
/// hours and must not introduce a second, competing cutoff policy.
/// </summary>
[TestClass]
public sealed class LearningDayCutoffPrecisionTests
{
    [TestMethod]
    public void DefaultCutoff_IsMidnight()
    {
        Assert.AreEqual(0, LearningDayConfiguration.Default.CutoffMinutes);
        Assert.AreEqual(
            LearningDayConfiguration.DefaultCutoffMinutes,
            LearningDayConfiguration.Default.CutoffMinutes);
        Assert.AreEqual(
            "00:00",
            LearningDayCutoffFormatter.ToWallClockText(LearningDayConfiguration.DefaultCutoffMinutes));
    }

    [TestMethod]
    public void NormalizeCutoffMinutes_AcceptsArbitraryValidMinuteValues()
    {
        Assert.AreEqual(457, LearningDayConfiguration.NormalizeCutoffMinutes(457)); // 07:37
        Assert.AreEqual(1, LearningDayConfiguration.NormalizeCutoffMinutes(1)); // 00:01
        Assert.AreEqual(1439, LearningDayConfiguration.NormalizeCutoffMinutes(1439)); // 23:59
        Assert.AreEqual(233, LearningDayConfiguration.NormalizeCutoffMinutes(233)); // 03:53
    }

    [TestMethod]
    public void NormalizeCutoffMinutes_AcceptsEveryMinuteOfTheDay()
    {
        for (var minute = 0; minute < 1440; minute++)
        {
            Assert.AreEqual(minute, LearningDayConfiguration.NormalizeCutoffMinutes(minute));
        }
    }

    [TestMethod]
    public void NormalizeCutoffMinutes_RejectsOnlyOutOfRangeValues()
    {
        Assert.AreEqual(0, LearningDayConfiguration.NormalizeCutoffMinutes(-1));
        Assert.AreEqual(0, LearningDayConfiguration.NormalizeCutoffMinutes(1440));
        Assert.AreEqual(0, LearningDayConfiguration.NormalizeCutoffMinutes(int.MaxValue));
    }

    [TestMethod]
    public void CutoffMinutes_RoundTripThroughTheWallClockTextUsedByTheSettingsControl()
    {
        Assert.AreEqual("00:00", LearningDayCutoffFormatter.ToWallClockText(0));
        Assert.AreEqual("07:37", LearningDayCutoffFormatter.ToWallClockText(457));
        Assert.AreEqual("23:59", LearningDayCutoffFormatter.ToWallClockText(1439));

        Assert.AreEqual(0, LearningDayCutoffFormatter.ParseWallClockText("00:00"));
        Assert.AreEqual(457, LearningDayCutoffFormatter.ParseWallClockText("07:37"));
        Assert.AreEqual(1439, LearningDayCutoffFormatter.ParseWallClockText("23:59"));
    }

    [TestMethod]
    public void CutoffWallClockParsing_FallsBackToTheDefaultForUnusableInput()
    {
        Assert.AreEqual(0, LearningDayCutoffFormatter.ParseWallClockText(null));
        Assert.AreEqual(0, LearningDayCutoffFormatter.ParseWallClockText(string.Empty));
        Assert.AreEqual(0, LearningDayCutoffFormatter.ParseWallClockText("not-a-time"));
        Assert.AreEqual(0, LearningDayCutoffFormatter.ParseWallClockText("24:00"));
        Assert.AreEqual(0, LearningDayCutoffFormatter.ParseWallClockText("07:60"));
    }

    [TestMethod]
    public void CutoffWallClockParsing_KeepsSecondsPrecisionOutOfTheStoredValue()
    {
        Assert.AreEqual(457, LearningDayCutoffFormatter.ParseWallClockText("07:37:45"));
    }

    [TestMethod]
    public void LearningDayBoundaries_HonourMinutePrecisionCutoffsAtFixedInstants()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // 2026-07-01 06:00 UTC is 08:00 local in Berlin (UTC+02:00 in summer), which is after a
        // 07:37 cutoff, so the logical learning day is 2026-07-01.
        var afterCutoff = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(
            new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc),
            berlin,
            457);
        Assert.AreEqual(new DateOnly(2026, 7, 1), afterCutoff.LogicalDate);
        Assert.AreEqual(new DateTime(2026, 7, 1, 5, 37, 0, DateTimeKind.Utc), afterCutoff.StartUtc);

        // 2026-07-01 05:00 UTC is 07:00 local, which is before the same 07:37 cutoff, so the
        // logical learning day is still 2026-06-30.
        var beforeCutoff = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(
            new DateTime(2026, 7, 1, 5, 0, 0, DateTimeKind.Utc),
            berlin,
            457);
        Assert.AreEqual(new DateOnly(2026, 6, 30), beforeCutoff.LogicalDate);
        Assert.AreEqual(new DateTime(2026, 6, 30, 5, 37, 0, DateTimeKind.Utc), beforeCutoff.StartUtc);
    }
}
