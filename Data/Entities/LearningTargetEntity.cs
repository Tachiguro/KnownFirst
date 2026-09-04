namespace KnownFirst.Data.Entities;

using KnownFirst.Core.Learning;
using SQLite;

[Table("LearningTargets")]
public sealed class LearningTargetEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_LearningTargets_StableId", Unique = true)]
    public string StableId { get; set; } = string.Empty;

    [Indexed(Name = "IX_LearningTargets_SenseId", Order = 1)]
    [Indexed(Name = "IX_LearningTargets_Sense_Kind_Languages", Order = 1, Unique = true)]
    public int SenseId { get; set; }

    [Indexed(Name = "IX_LearningTargets_Sense_Kind_Languages", Order = 2, Unique = true)]
    public LearningTargetKind TargetKind { get; set; }

    [Indexed(Name = "IX_LearningTargets_Sense_Kind_Languages", Order = 3, Unique = true)]
    public string SourceLanguage { get; set; } = string.Empty;

    [Indexed(Name = "IX_LearningTargets_Sense_Kind_Languages", Order = 4, Unique = true)]
    public string TargetLanguage { get; set; } = string.Empty;

    public bool TypingOptOut { get; set; }

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;
}
