namespace KnownFirst.Core.Learning;

public sealed class SimpleSpacedRepetitionScheduler : ISpacedRepetitionScheduler
{
    public const double DefaultEaseFactor = 2.5;
    public const double MinimumEaseFactor = 1.3;

    public CardSchedule Schedule(CardSchedule current, ReviewRating rating, DateTime reviewedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State is CardState.Suspended or CardState.Retired)
        {
            throw new InvalidOperationException("Suspended or retired cards cannot be scheduled.");
        }

        var now = reviewedAtUtc.Kind == DateTimeKind.Utc
            ? reviewedAtUtc
            : reviewedAtUtc.ToUniversalTime();

        return current.State == CardState.Review
            ? ScheduleReview(current, rating, now)
            : ScheduleNewOrLearning(current, rating, now);
    }

    private static CardSchedule ScheduleNewOrLearning(
        CardSchedule current,
        ReviewRating rating,
        DateTime now) => rating switch
    {
        ReviewRating.Again => current with
        {
            State = current.State == CardState.Relearning ? CardState.Relearning : CardState.Learning,
            DueAtUtc = now.AddMinutes(10),
            IntervalDays = 0,
            LastReviewedAtUtc = now,
            LastRating = rating
        },
        ReviewRating.Hard => Promote(current, rating, now, 1),
        ReviewRating.Good => Promote(current, rating, now, 3),
        ReviewRating.Easy => Promote(current, rating, now, 7),
        _ => throw new ArgumentOutOfRangeException(nameof(rating))
    };

    private static CardSchedule ScheduleReview(
        CardSchedule current,
        ReviewRating rating,
        DateTime now)
    {
        switch (rating)
        {
            case ReviewRating.Again:
                return current with
                {
                    State = CardState.Relearning,
                    DueAtUtc = now.AddMinutes(10),
                    IntervalDays = 0,
                    EaseFactor = Math.Max(MinimumEaseFactor, current.EaseFactor - 0.20),
                    SuccessfulReviewCount = 0,
                    LapseCount = current.LapseCount + 1,
                    LastReviewedAtUtc = now,
                    LastRating = rating
                };
            case ReviewRating.Hard:
            {
                var interval = Math.Max(1, RoundDays(current.IntervalDays * 1.2));
                return ReviewSuccess(
                    current,
                    rating,
                    now,
                    interval,
                    Math.Max(MinimumEaseFactor, current.EaseFactor - 0.15));
            }
            case ReviewRating.Good:
            {
                var interval = Math.Max(
                    current.IntervalDays + 1,
                    RoundDays(current.IntervalDays * current.EaseFactor));
                return ReviewSuccess(current, rating, now, interval, current.EaseFactor);
            }
            case ReviewRating.Easy:
            {
                var ease = current.EaseFactor + 0.15;
                var interval = Math.Max(
                    current.IntervalDays + 2,
                    RoundDays(current.IntervalDays * ease * 1.3));
                return ReviewSuccess(current, rating, now, interval, ease);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(rating));
        }
    }

    private static CardSchedule Promote(
        CardSchedule current,
        ReviewRating rating,
        DateTime now,
        int intervalDays) => current with
    {
        State = CardState.Review,
        DueAtUtc = now.AddDays(intervalDays),
        IntervalDays = intervalDays,
        SuccessfulReviewCount = current.SuccessfulReviewCount + 1,
        LastReviewedAtUtc = now,
        LastRating = rating
    };

    private static CardSchedule ReviewSuccess(
        CardSchedule current,
        ReviewRating rating,
        DateTime now,
        int intervalDays,
        double easeFactor) => current with
    {
        State = CardState.Review,
        DueAtUtc = now.AddDays(intervalDays),
        IntervalDays = intervalDays,
        EaseFactor = easeFactor,
        SuccessfulReviewCount = current.SuccessfulReviewCount + 1,
        LastReviewedAtUtc = now,
        LastRating = rating
    };

    private static int RoundDays(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
