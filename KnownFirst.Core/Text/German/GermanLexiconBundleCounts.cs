namespace KnownFirst.Core.Text.German;

/// <summary>Deterministic generation-time acceptance/exclusion counts embedded in a German lexicon runtime bundle.</summary>
public sealed record GermanLexiconBundleCounts(
    int TotalDistinctWordForms,
    int AmbiguousWordForms,
    int UnsupportedCategoryWordForms,
    int InflectedFormWordFormsExcluded,
    int UnambiguousBaseFormLemmaEntries,
    int ImperativeSingularCandidateForms,
    int AmbiguousImperativeSingularForms,
    int DerivedVerbStemCollisionExclusions,
    int DerivedVerbStemEntries);
