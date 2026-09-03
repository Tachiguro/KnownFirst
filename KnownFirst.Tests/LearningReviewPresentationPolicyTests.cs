using System.Globalization;
using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningReviewPresentationPolicyTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("en-US", 1, ReviewPluralCategory.One)]
    [DataRow("en-US", 2, ReviewPluralCategory.Many)]
    [DataRow("de-DE", 0, ReviewPluralCategory.Many)]
    [DataRow("de-DE", 2, ReviewPluralCategory.Many)]
    public void Create_EnglishAndGermanPluralCategories_UseOneOnlyForOne(
        string cultureName,
        int count,
        ReviewPluralCategory expected)
    {
        var result = Create(count, null, CultureInfo.GetCultureInfo(cultureName));

        Assert.AreEqual(expected, result.PluralCategory);
    }

    [TestMethod]
    [DataRow(1, ReviewPluralCategory.One)]
    [DataRow(2, ReviewPluralCategory.Few)]
    [DataRow(5, ReviewPluralCategory.Many)]
    [DataRow(11, ReviewPluralCategory.Many)]
    [DataRow(21, ReviewPluralCategory.One)]
    [DataRow(22, ReviewPluralCategory.Few)]
    [DataRow(25, ReviewPluralCategory.Many)]
    public void Create_RussianPluralCategories_FollowLastDigitAndTeenRules(
        int count,
        ReviewPluralCategory expected)
    {
        var result = Create(count, null, CultureInfo.GetCultureInfo("ru-RU"));

        Assert.AreEqual(expected, result.PluralCategory);
    }

    [TestMethod]
    public void Create_LaterTodayInExplicitTimezone_ReturnsTodayAndShortTime()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Presentation_Plus0530",
            TimeSpan.FromMinutes(330),
            "Presentation Plus 05:30",
            "Presentation Plus 05:30");
        var nowUtc = new DateTimeOffset(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);
        var nextDueUtc = new DateTimeOffset(2026, 9, 3, 22, 5, 0, TimeSpan.Zero);

        var result = LearningReviewPresentationPolicy.Create(
            nowUtc, 0, nextDueUtc, timeZone, CultureInfo.GetCultureInfo("en-GB"), null);

        Assert.AreEqual(ReviewAvailabilityKind.Scheduled, result.Availability);
        Assert.AreEqual(ReviewDateClassification.Today, result.DateClassification);
        Assert.AreEqual("03:35", result.FormattedTime);
        Assert.IsNull(result.FormattedDate);
    }

    [TestMethod]
    public void Create_TomorrowInsideCurrentLogicalDay_RemainsTomorrowWithoutCompletionClaim()
    {
        var nowUtc = new DateTimeOffset(2026, 12, 1, 23, 0, 0, TimeSpan.Zero);
        var nextDueUtc = new DateTimeOffset(2026, 12, 2, 2, 0, 0, TimeSpan.Zero);
        var activeDayEndUtc = new DateTimeOffset(2026, 12, 2, 4, 0, 0, TimeSpan.Zero);

        var result = LearningReviewPresentationPolicy.Create(
            nowUtc,
            0,
            nextDueUtc,
            TimeZoneInfo.Utc,
            CultureInfo.GetCultureInfo("en-US"),
            activeDayEndUtc);

        Assert.AreEqual(ReviewDateClassification.Tomorrow, result.DateClassification);
        Assert.IsFalse(result.NothingElseDueToday);
    }

    [TestMethod]
    public void Create_ExplicitDatesAndTimes_UseSuppliedEnglishGermanAndRussianCultures()
    {
        var nextDueUtc = new DateTimeOffset(2026, 12, 15, 13, 5, 0, TimeSpan.Zero);

        var english = Create(0, nextDueUtc, CultureInfo.GetCultureInfo("en-US"));
        var german = Create(0, nextDueUtc, CultureInfo.GetCultureInfo("de-DE"));
        var russian = Create(0, nextDueUtc, CultureInfo.GetCultureInfo("ru-RU"));

        Assert.AreEqual(ReviewDateClassification.Date, english.DateClassification);
        Assert.AreEqual("1:05 PM", english.FormattedTime);
        Assert.AreEqual("December 15", english.FormattedDate);
        Assert.AreEqual("13:05", german.FormattedTime);
        Assert.AreEqual("15. Dezember", german.FormattedDate);
        Assert.AreEqual("13:05", russian.FormattedTime);
        Assert.AreEqual("15 \u0434\u0435\u043a\u0430\u0431\u0440\u044f", russian.FormattedDate);
        Assert.IsFalse(english.IncludesYear);
        Assert.IsFalse(german.IncludesYear);
        Assert.IsFalse(russian.IncludesYear);
    }

    [TestMethod]
    public void Create_DifferentYear_UsesCultureShortDateIncludingYear()
    {
        var result = Create(
            0,
            new DateTimeOffset(2027, 1, 2, 13, 5, 0, TimeSpan.Zero),
            CultureInfo.GetCultureInfo("de-DE"));

        Assert.AreEqual(ReviewDateClassification.Date, result.DateClassification);
        Assert.AreEqual("02.01.2027", result.FormattedDate);
        Assert.IsTrue(result.IncludesYear);
    }

    [TestMethod]
    public void Create_UtcDatesDiffer_UsesEffectiveTimezoneCalendarDate()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Presentation_Plus2",
            TimeSpan.FromHours(2),
            "Presentation Plus 2",
            "Presentation Plus 2");
        var nowUtc = new DateTimeOffset(2026, 9, 3, 23, 30, 0, TimeSpan.Zero);
        var nextDueUtc = new DateTimeOffset(2026, 9, 4, 0, 30, 0, TimeSpan.Zero);

        var result = LearningReviewPresentationPolicy.Create(
            nowUtc, 0, nextDueUtc, timeZone, CultureInfo.GetCultureInfo("en-US"), null);

        Assert.AreNotEqual(nowUtc.UtcDateTime.Date, nextDueUtc.UtcDateTime.Date);
        Assert.AreEqual(ReviewDateClassification.Today, result.DateClassification);
    }

    [TestMethod]
    public void Create_DstInstant_UsesOffsetActiveAtConcreteInstant()
    {
        var timeZone = CreateDeterministicDstTimeZone();
        var result = LearningReviewPresentationPolicy.Create(
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            0,
            new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            timeZone,
            CultureInfo.GetCultureInfo("en-GB"),
            null);

        Assert.AreEqual(ReviewDateClassification.Today, result.DateClassification);
        Assert.AreEqual("14:00", result.FormattedTime);
    }

    [TestMethod]
    public void Create_DueNow_SuppressesFutureReviewPresentation()
    {
        var result = LearningReviewPresentationPolicy.Create(
            NowUtc,
            2,
            NowUtc.AddHours(1),
            TimeZoneInfo.Utc,
            CultureInfo.GetCultureInfo("en-US"),
            NowUtc.AddHours(4));

        Assert.AreEqual(ReviewAvailabilityKind.DueNow, result.Availability);
        Assert.AreEqual(ReviewPluralCategory.Many, result.PluralCategory);
        Assert.IsNull(result.NextDueAtUtc);
        Assert.AreEqual(ReviewDateClassification.None, result.DateClassification);
        Assert.IsNull(result.FormattedTime);
        Assert.IsNull(result.FormattedDate);
        Assert.IsFalse(result.NothingElseDueToday);
    }

    [TestMethod]
    [DataRow(-1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public void Create_NextDueRelativeToHalfOpenActiveDayEnd_ControlsCompletionClaim(
        int minuteOffset,
        bool expected)
    {
        var activeDayEndUtc = NowUtc.AddHours(4);
        var result = LearningReviewPresentationPolicy.Create(
            NowUtc,
            0,
            activeDayEndUtc.AddMinutes(minuteOffset),
            TimeZoneInfo.Utc,
            CultureInfo.GetCultureInfo("en-US"),
            activeDayEndUtc);

        Assert.AreEqual(expected, result.NothingElseDueToday);
    }

    [TestMethod]
    public void Create_NoNextDueWithAuthoritativeEnd_IsCompleteButUnavailableEndIsNeutral()
    {
        var complete = LearningReviewPresentationPolicy.Create(
            NowUtc,
            0,
            null,
            TimeZoneInfo.Utc,
            CultureInfo.GetCultureInfo("en-US"),
            NowUtc.AddHours(4));
        var neutral = LearningReviewPresentationPolicy.Create(
            NowUtc,
            0,
            null,
            TimeZoneInfo.Utc,
            CultureInfo.GetCultureInfo("en-US"),
            null);

        Assert.IsTrue(complete.NothingElseDueToday);
        Assert.IsFalse(neutral.NothingElseDueToday);
    }

    private static LearningReviewPresentation Create(
        int dueCardCount,
        DateTimeOffset? nextDueAtUtc,
        CultureInfo culture) =>
        LearningReviewPresentationPolicy.Create(
            NowUtc,
            dueCardCount,
            nextDueAtUtc,
            TimeZoneInfo.Utc,
            culture,
            null);

    private static TimeZoneInfo CreateDeterministicDstTimeZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25));
        return TimeZoneInfo.CreateCustomTimeZone(
            "Presentation_Dst",
            TimeSpan.FromHours(1),
            "Presentation DST",
            "Presentation Standard",
            "Presentation Daylight",
            [rule]);
    }
}
