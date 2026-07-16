using SQLite;

namespace KnownFirst.Data.Entities;

[Table("ReviewStates")]
public sealed class ReviewStateEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int WordId { get; set; }

    public int ReviewCount { get; set; }

    public int ForgotCount { get; set; }

    public int PartialCount { get; set; }

    public int KnownCount { get; set; }

    public DateTime? LastReviewedAt { get; set; }
}
