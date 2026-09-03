using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningReviewStatusTextPolicyTests
{
    [TestMethod]
    public void Create_SelectsPluralizedDueNowResourceKeys()
    {
        AssertStatus(
            Presentation(ReviewAvailabilityKind.DueNow, ReviewPluralCategory.One, dueCardCount: 1),
            "Learning_DueNowOne",
            1);
        AssertStatus(
            Presentation(ReviewAvailabilityKind.DueNow, ReviewPluralCategory.Few, dueCardCount: 3),
            "Learning_DueNowFew",
            3);
        AssertStatus(
            Presentation(ReviewAvailabilityKind.DueNow, ReviewPluralCategory.Many, dueCardCount: 5),
            "Learning_DueNowMany",
            5);
    }

    [TestMethod]
    public void Create_SelectsTodayTomorrowAndDateResourceKeys()
    {
        AssertStatus(
            Presentation(
                ReviewAvailabilityKind.Scheduled,
                ReviewPluralCategory.Many,
                dateClassification: ReviewDateClassification.Today,
                formattedTime: "21:30"),
            "Learning_NextReviewToday",
            "21:30");
        AssertStatus(
            Presentation(
                ReviewAvailabilityKind.Scheduled,
                ReviewPluralCategory.Many,
                dateClassification: ReviewDateClassification.Tomorrow,
                formattedTime: "09:30"),
            "Learning_NextReviewTomorrow",
            "09:30");
        AssertStatus(
            Presentation(
                ReviewAvailabilityKind.Scheduled,
                ReviewPluralCategory.Many,
                dateClassification: ReviewDateClassification.Date,
                formattedTime: "14:30",
                formattedDate: "6. September",
                nothingElseDueToday: true),
            "Learning_NextReviewDate",
            "6. September",
            "14:30");
    }

    [TestMethod]
    public void Create_SelectsStrongCompleteOnlyWhenNoScheduledReviewOtherwiseNeutralFallback()
    {
        AssertStatus(
            Presentation(
                ReviewAvailabilityKind.None,
                ReviewPluralCategory.Many,
                nothingElseDueToday: true),
            "Learning_NothingElseDueToday");
        AssertStatus(
            Presentation(ReviewAvailabilityKind.None, ReviewPluralCategory.Many),
            "Learning_NoReviewsDueNow");
    }

    private static LearningReviewPresentation Presentation(
        ReviewAvailabilityKind availability,
        ReviewPluralCategory pluralCategory,
        int dueCardCount = 0,
        ReviewDateClassification dateClassification = ReviewDateClassification.None,
        string? formattedTime = null,
        string? formattedDate = null,
        bool nothingElseDueToday = false) =>
        new(
            availability,
            dueCardCount,
            pluralCategory,
            availability == ReviewAvailabilityKind.Scheduled
                ? new DateTimeOffset(2026, 9, 6, 12, 30, 0, TimeSpan.Zero)
                : null,
            dateClassification,
            formattedTime,
            formattedDate,
            false,
            nothingElseDueToday);

    private static void AssertStatus(
        LearningReviewPresentation presentation,
        string expectedResourceKey,
        params object[] expectedArguments)
    {
        var result = LearningReviewStatusTextPolicy.Create(presentation);

        Assert.AreEqual(expectedResourceKey, result.ResourceKey);
        CollectionAssert.AreEqual(expectedArguments, result.Arguments);
    }
}
