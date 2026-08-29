namespace KnownFirst.Data.Entities;

using SQLite;

[Table("SenseLearningControls")]
public sealed class SenseLearningControlEntity
{
    [PrimaryKey]
    public int SenseId { get; set; }

    public string DecidedAtUtc { get; set; } = string.Empty;
}
