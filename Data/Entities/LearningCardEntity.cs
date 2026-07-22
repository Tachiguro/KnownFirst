using KnownFirst.Core.Learning;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LearningCards")]
public sealed class LearningCardEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_LearningCards_Word_Direction", 1, Unique = true)]
    public int WordId { get; set; }

    [Indexed]
    public int MeaningId { get; set; }

    [Indexed("IX_LearningCards_Word_Direction", 2, Unique = true)]
    public CardDirection Direction { get; set; }

    [Indexed]
    public CardState State { get; set; } = CardState.New;

    [Indexed]
    public DateTime DueAtUtc { get; set; }

    public int IntervalDays { get; set; }

    public double EaseFactor { get; set; } = SimpleSpacedRepetitionScheduler.DefaultEaseFactor;

    public int SuccessfulReviewCount { get; set; }

    public int LapseCount { get; set; }

    public DateTime? LastReviewedAtUtc { get; set; }

    public ReviewRating? LastRating { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
