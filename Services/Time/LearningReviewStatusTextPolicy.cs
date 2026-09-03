namespace KnownFirst.Services.Time;

public sealed record LearningReviewStatusText(
    string ResourceKey,
    object[] Arguments);

public static class LearningReviewStatusTextPolicy
{
    public static LearningReviewStatusText Create(LearningReviewPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (presentation.Availability == ReviewAvailabilityKind.DueNow)
        {
            var resourceKey = presentation.PluralCategory switch
            {
                ReviewPluralCategory.One => "Learning_DueNowOne",
                ReviewPluralCategory.Few => "Learning_DueNowFew",
                _ => "Learning_DueNowMany"
            };
            return new LearningReviewStatusText(resourceKey, [presentation.DueCardCount]);
        }

        if (presentation.Availability == ReviewAvailabilityKind.Scheduled)
        {
            return presentation.DateClassification switch
            {
                ReviewDateClassification.Today => new LearningReviewStatusText(
                    "Learning_NextReviewToday",
                    [presentation.FormattedTime ?? string.Empty]),
                ReviewDateClassification.Tomorrow => new LearningReviewStatusText(
                    "Learning_NextReviewTomorrow",
                    [presentation.FormattedTime ?? string.Empty]),
                ReviewDateClassification.Date => new LearningReviewStatusText(
                    "Learning_NextReviewDate",
                    [presentation.FormattedDate ?? string.Empty, presentation.FormattedTime ?? string.Empty]),
                _ => new LearningReviewStatusText("Learning_NoReviewsDueNow", [])
            };
        }

        return presentation.NothingElseDueToday
            ? new LearningReviewStatusText("Learning_NothingElseDueToday", [])
            : new LearningReviewStatusText("Learning_NoReviewsDueNow", []);
    }
}
