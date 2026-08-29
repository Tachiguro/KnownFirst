namespace KnownFirst.Data.Entities;

using SQLite;

[Table("WordLearningControls")]
public sealed class WordLearningControlEntity
{
    [PrimaryKey]
    public int WordId { get; set; }

    public string DecidedAtUtc { get; set; } = string.Empty;
}
