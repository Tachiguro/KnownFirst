namespace KnownFirst.Data.Targets;

using KnownFirst.Data.Migrations.Schema8;

public sealed record TargetAnswerVariantDraft(
    string AnswerLanguage,
    string DisplayText,
    AnswerVariantRequirement Requirement,
    bool IsPreferred,
    int? SourceMeaningId = null,
    string? NormalizedText = null);
