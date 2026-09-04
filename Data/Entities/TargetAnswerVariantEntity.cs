namespace KnownFirst.Data.Entities;

using KnownFirst.Data.Migrations.Schema8;
using SQLite;

[Table("TargetAnswerVariants")]
public sealed class TargetAnswerVariantEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_TargetAnswerVariants_StableId", Unique = true)]
    public string StableId { get; set; } = string.Empty;

    [Indexed(Name = "IX_TargetAnswerVariants_TargetId", Order = 1)]
    [Indexed(Name = "IX_TargetAnswerVariants_Target_NormalizedText", Order = 1, Unique = true)]
    public int TargetId { get; set; }

    public string AnswerLanguage { get; set; } = string.Empty;

    public string DisplayText { get; set; } = string.Empty;

    [Indexed(Name = "IX_TargetAnswerVariants_Target_NormalizedText", Order = 2, Unique = true)]
    public string NormalizedText { get; set; } = string.Empty;

    public AnswerVariantRequirement Requirement { get; set; }

    public bool IsPreferred { get; set; }

    public string? RequiredSinceUtc { get; set; }

    public int? SourceMeaningId { get; set; }

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;
}
