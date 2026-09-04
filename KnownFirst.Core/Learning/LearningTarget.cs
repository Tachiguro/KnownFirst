namespace KnownFirst.Core.Learning;

/// <summary>
/// Pure domain representation of an explicitly requested learning target under a semantic Sense.
/// A Word is not duplicated for additional targets; one real Sense remains one Sense.
/// Each LearningTarget maps to exactly one LearningCard and one FSRS-6 schedule in downstream cutover.
/// Per-target typing opt-out prevents Automatic mode from entering typing qualification without
/// altering FSRS identity.
/// </summary>
public sealed record LearningTarget
{
    public LearningTargetIdentity Identity { get; }
    public bool TypingOptOut { get; }

    public LearningTargetKind Kind => Identity.Kind;
    public string SourceLanguage => Identity.SourceLanguage;
    public string TargetLanguage => Identity.TargetLanguage;

    public LearningTarget(LearningTargetIdentity identity, bool typingOptOut = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
        TypingOptOut = typingOptOut;
    }

    public static LearningTarget CreateDefinition(
        string sourceLanguage,
        string definitionLanguage,
        bool typingOptOut = false) =>
        new(LearningTargetIdentity.Definition(sourceLanguage, definitionLanguage), typingOptOut);

    public static LearningTarget CreateTranslation(
        string sourceLanguage,
        string targetLanguage,
        bool typingOptOut = false) =>
        new(LearningTargetIdentity.Translation(sourceLanguage, targetLanguage), typingOptOut);
}
