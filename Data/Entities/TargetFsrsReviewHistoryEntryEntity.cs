namespace KnownFirst.Data.Entities;

using KnownFirst.Core.Learning;
using SQLite;

[Table("TargetFsrsReviewHistoryEntries")]
public sealed class TargetFsrsReviewHistoryEntryEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_TargetFsrsReviewHistoryEntries_StableId", 1, Unique = true)]
    public string StableId { get; set; } = string.Empty;

    [Indexed("IX_TargetFsrsReviewHistoryEntries_Target_Sequence", 1, Unique = true)]
    [Indexed("IX_TargetFsrsReviewHistoryEntries_Target_Replay", 1)]
    public int TargetId { get; set; }

    [Indexed("IX_TargetFsrsReviewHistoryEntries_Target_Sequence", 2, Unique = true)]
    [Indexed("IX_TargetFsrsReviewHistoryEntries_Target_Replay", 3)]
    public int SequenceNumber { get; set; }

    public ReviewRating Rating { get; set; }

    [Indexed("IX_TargetFsrsReviewHistoryEntries_Target_Replay", 2)]
    public string ReviewedAtUtc { get; set; } = string.Empty;
}
