using KnownFirst.Core.Text;

namespace KnownFirst.Tools.GermanLexicon;

/// <summary>The deterministic, sorted runtime-index contents produced by <see cref="GermanLexiconIndexBuilder"/>.</summary>
public sealed record GermanLexiconIndexBuildResult(
    IReadOnlyList<GermanLexemeEntry> Lemmas,
    IReadOnlyList<GermanCompoundStemEntry> Stems,
    GermanLexiconIndexBuildStatistics Statistics);
