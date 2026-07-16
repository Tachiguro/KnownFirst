using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LearningSessions")]
public sealed class LearningSessionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public LearningSessionStatus Status { get; set; } = LearningSessionStatus.Active;

    public int TotalCards { get; set; }

    public int CompletedCards { get; set; }

    public int AgainCount { get; set; }

    public int HardCount { get; set; }

    public int GoodCount { get; set; }

    public int EasyCount { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
