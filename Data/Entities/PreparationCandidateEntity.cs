using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("PreparationCandidates")]
public sealed class PreparationCandidateEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_PreparationCandidates_Session_Order", 1, Unique = true)]
    public int SessionId { get; set; }

    [Indexed]
    public int WordId { get; set; }

    [Indexed("IX_PreparationCandidates_Session_Order", 2, Unique = true)]
    public int Order { get; set; }

    [Indexed]
    public PreparationCandidateStatus Status { get; set; } = PreparationCandidateStatus.Pending;

    public string ResultJson { get; set; } = string.Empty;

    public int SelectedMeaningIndex { get; set; }

    public string LastErrorCode { get; set; } = string.Empty;

    public int LookupAttemptCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
