namespace KnownFirst.Core.Learning;

public sealed record CardSchedule(
    CardState State,
    DateTime DueAtUtc,
    int IntervalDays,
    double EaseFactor,
    int SuccessfulReviewCount,
    int LapseCount,
    DateTime? LastReviewedAtUtc,
    ReviewRating? LastRating)
{
    public static CardSchedule New(DateTime createdAtUtc) => new(
        CardState.New,
        createdAtUtc,
        0,
        SimpleSpacedRepetitionScheduler.DefaultEaseFactor,
        0,
        0,
        null,
        null);
}
