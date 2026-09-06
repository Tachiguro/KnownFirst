namespace KnownFirst.Core.Learning;

/// <summary>
/// Pure factual interaction event input for target progression replay.
/// Represents event timestamp ordering, rating, typing modality, correctness, and session-repeat classification.
/// </summary>
public readonly record struct TargetInteractionEvent
{
    public DateTimeOffset ReviewedAtUtc { get; }
    public ReviewRating Rating { get; }
    public bool WasTypedAnswer { get; }
    public bool WasCorrect { get; }
    public bool IsSessionRepeat { get; }

    public TargetInteractionEvent(
        DateTimeOffset reviewedAtUtc,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect,
        bool isSessionRepeat = false)
    {
        if (reviewedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Review timestamp must be in UTC (offset zero).", nameof(reviewedAtUtc));
        }

        if (!Enum.IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "Review rating is invalid.");
        }

        ReviewedAtUtc = reviewedAtUtc;
        Rating = rating;
        WasTypedAnswer = wasTypedAnswer;
        WasCorrect = wasCorrect;
        IsSessionRepeat = isSessionRepeat;
    }

    public static TargetInteractionEvent ScheduledRecall(
        DateTimeOffset reviewedAtUtc,
        ReviewRating rating) =>
        new(reviewedAtUtc, rating, wasTypedAnswer: false, wasCorrect: rating != ReviewRating.Again, isSessionRepeat: false);

    public static TargetInteractionEvent ScheduledTyping(
        DateTimeOffset reviewedAtUtc,
        ReviewRating rating,
        bool wasCorrect) =>
        new(reviewedAtUtc, rating, wasTypedAnswer: true, wasCorrect: wasCorrect, isSessionRepeat: false);

    public static TargetInteractionEvent SessionRepeat(
        DateTimeOffset reviewedAtUtc,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect) =>
        new(reviewedAtUtc, rating, wasTypedAnswer, wasCorrect, isSessionRepeat: true);
}
