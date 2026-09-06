namespace KnownFirst.Core.Learning;

/// <summary>
/// Declares the semantic category of an explicitly requested learning target.
/// </summary>
public enum LearningTargetKind
{
    /// <summary>
    /// Learning intention targeting a natural-language definition of the word's sense.
    /// </summary>
    Definition = 0,

    /// <summary>
    /// Learning intention targeting a translation from a source language to a target language.
    /// </summary>
    Translation = 1
}
