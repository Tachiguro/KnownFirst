using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("ReviewCandidates")]
public sealed class ReviewCandidateEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_ReviewCandidates_Session_Order", 1, Unique = true)]
    public int SessionId { get; set; }

    [Indexed]
    public int WordId { get; set; }

    [Indexed("IX_ReviewCandidates_Session_Order", 2, Unique = true)]
    public int Order { get; set; }

    [Indexed]
    public WordStatus Status { get; set; } = WordStatus.Unreviewed;

    public WordStatus PreviousWordStatus { get; set; } = WordStatus.Unreviewed;

    public int PreviousTotalOccurrenceCount { get; set; }

    public int PreviousDocumentCount { get; set; }

    public DateTime PreviousUpdatedAt { get; set; }

    public int DecisionSequence { get; set; }

    public bool WasWordCreatedForSession { get; set; }

    public DateTime? DecidedAt { get; set; }
}
