namespace KnownFirst.Data.Targets;

using KnownFirst.Core.Learning;

public sealed record PersistedLearningTarget(
    int Id,
    string StableId,
    int SenseId,
    LearningTargetKind TargetKind,
    string SourceLanguage,
    string TargetLanguage,
    bool TypingOptOut,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public LearningTargetIdentity Identity => new(TargetKind, SourceLanguage, TargetLanguage);
    public LearningTarget DomainTarget => new(Identity, TypingOptOut);
}
