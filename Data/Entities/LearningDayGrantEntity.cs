using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LearningDayGrants")]
public sealed class LearningDayGrantEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int DayOrdinal { get; set; }

    public int WordId { get; set; }

    public int SlotOrdinal { get; set; }

    public DateTime GrantedAtUtc { get; set; }
}
