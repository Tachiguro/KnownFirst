namespace KnownFirst.Data.Entities;

using KnownFirst.Core.Learning.Fsrs6;
using SQLite;

[Table("FsrsCardStates")]
public sealed class FsrsCardStateEntity
{
    [PrimaryKey]
    public int CardId { get; set; }

    [Indexed("IX_FsrsCardStates_State_DueAtUtc", 1)]
    public Fsrs6CardState State { get; set; }

    public double? Stability { get; set; }

    public double? Difficulty { get; set; }

    public string? LastReviewedAtUtc { get; set; }

    public int? StepIndex { get; set; }

    [Indexed("IX_FsrsCardStates_State_DueAtUtc", 2)]
    public string? DueAtUtc { get; set; }
}
