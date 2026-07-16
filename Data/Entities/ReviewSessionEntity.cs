using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("ReviewSessions")]
public sealed class ReviewSessionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Unique = true)]
    public int DocumentId { get; set; }

    [Indexed]
    public ReviewSessionStatus Status { get; set; } = ReviewSessionStatus.Active;

    public int TotalCandidates { get; set; }

    public int ReviewedCount { get; set; }

    public int KnownCount { get; set; }

    public int UnknownCount { get; set; }

    public int IgnoredCount { get; set; }

    public int DecisionSequence { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
