namespace KnownFirst.Tools.GermanLexicon;

/// <summary>Deterministic counts describing how <see cref="GermanLexiconIndexBuilder"/> reduced parsed analyses to the runtime index.</summary>
public sealed record GermanLexiconIndexBuildStatistics(
    int TotalDistinctWordForms,
    int AmbiguousWordForms,
    int UnsupportedCategoryWordForms,
    int InflectedFormWordFormsExcluded,
    int UnambiguousBaseFormLemmaEntries,
    int ImperativeSingularCandidateForms,
    int AmbiguousImperativeSingularForms,
    int DerivedVerbStemCollisionExclusions,
    int DerivedVerbStemEntries);
