namespace KnownFirst.Data.Targets;

using KnownFirst.Data.Migrations.Schema8;

public sealed record PersistedTargetAnswerVariant(
    int Id,
    string StableId,
    int TargetId,
    string AnswerLanguage,
    string DisplayText,
    string NormalizedText,
    AnswerVariantRequirement Requirement,
    bool IsPreferred,
    DateTime? RequiredSinceUtc,
    int? SourceMeaningId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public bool IsRequired => Requirement == AnswerVariantRequirement.Required;
}
