using System.Globalization;

namespace KnownFirst.Services.Time;

public enum ReviewAvailabilityKind
{
    None = 0,
    DueNow = 1,
    Scheduled = 2
}

public enum ReviewPluralCategory
{
    One = 0,
    Few = 1,
    Many = 2
}

public enum ReviewDateClassification
{
    None = 0,
    Today = 1,
    Tomorrow = 2,
    Date = 3
}

public sealed record LearningReviewPresentation(
    ReviewAvailabilityKind Availability,
    int DueCardCount,
    ReviewPluralCategory PluralCategory,
    DateTimeOffset? NextDueAtUtc,
    ReviewDateClassification DateClassification,
    string? FormattedTime,
    string? FormattedDate,
    bool IncludesYear,
    bool NothingElseDueToday);

public static class LearningReviewPresentationPolicy
{
    public static LearningReviewPresentation Create(
        DateTimeOffset nowUtc,
        int dueCardCount,
        DateTimeOffset? nextDueAtUtc,
        TimeZoneInfo effectiveTimeZone,
        CultureInfo culture,
        DateTimeOffset? activeLearningDayEndUtc)
    {
        ArgumentNullException.ThrowIfNull(effectiveTimeZone);
        ArgumentNullException.ThrowIfNull(culture);
        if (dueCardCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dueCardCount));
        }

        RequireUtc(nowUtc, nameof(nowUtc));
        if (nextDueAtUtc is { } nextDue)
        {
            RequireUtc(nextDue, nameof(nextDueAtUtc));
        }

        if (activeLearningDayEndUtc is { } activeDayEnd)
        {
            RequireUtc(activeDayEnd, nameof(activeLearningDayEndUtc));
        }

        var pluralCategory = SelectPluralCategory(dueCardCount, culture);
        if (dueCardCount > 0)
        {
            return new LearningReviewPresentation(
                ReviewAvailabilityKind.DueNow,
                dueCardCount,
                pluralCategory,
                null,
                ReviewDateClassification.None,
                null,
                null,
                false,
                false);
        }

        var nothingElseDueToday = activeLearningDayEndUtc.HasValue
            && (!nextDueAtUtc.HasValue || nextDueAtUtc.Value >= activeLearningDayEndUtc.Value);
        if (!nextDueAtUtc.HasValue)
        {
            return new LearningReviewPresentation(
                ReviewAvailabilityKind.None,
                0,
                pluralCategory,
                null,
                ReviewDateClassification.None,
                null,
                null,
                false,
                nothingElseDueToday);
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, effectiveTimeZone);
        var localDue = TimeZoneInfo.ConvertTime(nextDueAtUtc.Value, effectiveTimeZone);
        var classification = localDue.Date == localNow.Date
            ? ReviewDateClassification.Today
            : localDue.Date == localNow.Date.AddDays(1)
                ? ReviewDateClassification.Tomorrow
                : ReviewDateClassification.Date;
        var includesYear = classification == ReviewDateClassification.Date
            && localDue.Year != localNow.Year;
        var formattedDate = classification == ReviewDateClassification.Date
            ? localDue.ToString(
                includesYear
                    ? culture.DateTimeFormat.ShortDatePattern
                    : culture.DateTimeFormat.MonthDayPattern,
                culture)
            : null;

        return new LearningReviewPresentation(
            ReviewAvailabilityKind.Scheduled,
            0,
            pluralCategory,
            nextDueAtUtc,
            classification,
            localDue.ToString(culture.DateTimeFormat.ShortTimePattern, culture),
            formattedDate,
            includesYear,
            nothingElseDueToday);
    }

    private static ReviewPluralCategory SelectPluralCategory(int count, CultureInfo culture)
    {
        if (!string.Equals(culture.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase))
        {
            return count == 1 ? ReviewPluralCategory.One : ReviewPluralCategory.Many;
        }

        var lastTwoDigits = count % 100;
        var lastDigit = count % 10;
        if (lastDigit == 1 && lastTwoDigits != 11)
        {
            return ReviewPluralCategory.One;
        }

        return lastDigit is >= 2 and <= 4 && lastTwoDigits is not (>= 12 and <= 14)
            ? ReviewPluralCategory.Few
            : ReviewPluralCategory.Many;
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use UTC offset zero.", parameterName);
        }
    }
}
