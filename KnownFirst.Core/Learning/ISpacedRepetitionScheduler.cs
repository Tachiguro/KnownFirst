namespace KnownFirst.Core.Learning;

public interface ISpacedRepetitionScheduler
{
    CardSchedule Schedule(CardSchedule current, ReviewRating rating, DateTime reviewedAtUtc);
}
