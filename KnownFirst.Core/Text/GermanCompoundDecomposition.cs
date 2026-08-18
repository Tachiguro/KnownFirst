namespace KnownFirst.Core.Text;

/// <summary>
/// The single unambiguous, fully lexicon-backed two-component decomposition of a German source
/// compound produced by <see cref="ConservativeGermanCompoundDecomposer"/>.
/// </summary>
public sealed record GermanCompoundDecomposition(
    GermanCompoundComponent LeftComponent,
    GermanCompoundComponent RightComponent);
