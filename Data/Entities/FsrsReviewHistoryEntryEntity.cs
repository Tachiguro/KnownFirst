namespace KnownFirst.Data.Entities;

using KnownFirst.Core.Learning;
using SQLite;

[Table("FsrsReviewHistoryEntries")]
public sealed class FsrsReviewHistoryEntryEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_FsrsReviewHistoryEntries_StableId", 1, Unique = true)]
    public string StableId { get; set; } = string.Empty;

    [Indexed("IX_FsrsReviewHistoryEntries_Card_Sequence", 1, Unique = true)]
    [Indexed("IX_FsrsReviewHistoryEntries_Card_Replay", 1)]
    public int CardId { get; set; }

    [Indexed("IX_FsrsReviewHistoryEntries_Card_Sequence", 2, Unique = true)]
    [Indexed("IX_FsrsReviewHistoryEntries_Card_Replay", 3)]
    public int SequenceNumber { get; set; }

    public ReviewRating Rating { get; set; }

    [Indexed("IX_FsrsReviewHistoryEntries_Card_Replay", 2)]
    public string ReviewedAtUtc { get; set; } = string.Empty;
}
