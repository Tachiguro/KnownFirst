using KnownFirst.Core.Learning;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LearningReviews")]
public sealed class LearningReviewEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CardId { get; set; }

    [Indexed]
    public int SessionId { get; set; }

    public ReviewRating Rating { get; set; }

    public bool WasTypedAnswer { get; set; }

    public bool WasCorrect { get; set; }

    public DateTime ReviewedAtUtc { get; set; }

    public DateTime DueAtUtc { get; set; }

    public int IntervalDays { get; set; }

    public double EaseFactor { get; set; }
}
