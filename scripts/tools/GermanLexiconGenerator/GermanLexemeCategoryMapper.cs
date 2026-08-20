using KnownFirst.Core.Text;

namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Maps an upstream RFTagger-style leading tag token to the <see cref="GermanLexemeCategory"/>
/// values <see cref="IGermanLexicon"/> supports. Every other upstream category (ART, ADV, CARD,
/// CIRCP, CONJ, DEMO, INDEF, INTJ, NNP, ORD, POSS, POSTP, PREP, PREPART, PROADV, PRP, PRTKL,
/// REL, VPART, WPADV, WPRO, ZU, ...) is intentionally unsupported in this package and fails
/// closed (returns <c>null</c>) rather than being force-mapped to a nearest category.
/// </summary>
public static class GermanLexemeCategoryMapper
{
    public static GermanLexemeCategory? Map(string rawCategory) => rawCategory switch
    {
        "NN" => GermanLexemeCategory.Noun,
        "V" => GermanLexemeCategory.Verb,
        "ADJ" => GermanLexemeCategory.Adjective,
        _ => null,
    };
}
