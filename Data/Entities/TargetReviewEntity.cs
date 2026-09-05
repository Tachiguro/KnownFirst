namespace KnownFirst.Data.Entities;

using KnownFirst.Core.Learning;
using SQLite;

[Table("TargetReviews")]
public sealed class TargetReviewEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_TargetReviews_StableId", 1, Unique = true)]
    public string StableId { get; set; } = string.Empty;

    [Indexed("IX_TargetReviews_TargetId", 1)]
    [Indexed("IX_TargetReviews_Target_ReviewedAt", 1)]
    public int TargetId { get; set; }

    [Indexed("IX_TargetReviews_SessionId", 1)]
    public int SessionId { get; set; }

    public ReviewRating Rating { get; set; }

    public bool WasTypedAnswer { get; set; }

    public bool WasCorrect { get; set; }

    public bool IsSessionRepeat { get; set; }

    public int? TargetAnswerVariantId { get; set; }

    public int? MatchedAnswerVariantId { get; set; }

    [Indexed("IX_TargetReviews_Target_ReviewedAt", 2)]
    public string ReviewedAtUtc { get; set; } = string.Empty;

    public string DueAtUtc { get; set; } = string.Empty;
}
