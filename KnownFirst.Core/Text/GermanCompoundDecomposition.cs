namespace KnownFirst.Core.Text;

/// <summary>
/// The single unambiguous, fully lexicon-backed decomposition of a German source compound into an
/// ordered sequence of 2 to <see cref="ConservativeGermanCompoundDecomposer.MaxComponents"/>
/// components, produced by <see cref="ConservativeGermanCompoundDecomposer"/>.
/// </summary>
public sealed record GermanCompoundDecomposition(IReadOnlyList<GermanCompoundComponent> Components);
