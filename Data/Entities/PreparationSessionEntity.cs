using KnownFirst.Core.Preparation;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("PreparationSessions")]
public sealed class PreparationSessionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public PreparationSessionStatus Status { get; set; } = PreparationSessionStatus.Active;

    public PreparationMethod Method { get; set; }

    public int TotalItems { get; set; }

    public int CompletedItems { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
