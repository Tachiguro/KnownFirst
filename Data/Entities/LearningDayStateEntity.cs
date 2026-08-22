using KnownFirst.Core.Learning;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LearningDayState")]
public sealed class LearningDayStateEntity
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    public LearningDayPhase Phase { get; set; } = LearningDayPhase.ActiveBudgetDay;

    public int DayOrdinal { get; set; }

    public DateTime ActiveDayStartUtc { get; set; }

    public DateTime ActiveDayEndUtc { get; set; }

    public string FrozenTimeZoneId { get; set; } = string.Empty;

    public int FrozenCutoffMinutes { get; set; }

    public DateTime? BridgeStartedUtc { get; set; }

    public string? BridgeTargetTimeZoneId { get; set; }

    public int? BridgeTargetCutoffMinutes { get; set; }

    public DateTime? BridgeTargetUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
