using KnownFirst.Core.Text;
using KnownFirst.Tools.GermanLexicon;

namespace KnownFirst.Tests;

[TestClass]
public sealed class GermanLexiconIndexBuilderTests
{
    private const string Fixture =
        "Arbeit\n" +
        "Arbeit NN,fem,nom,sing\n" +
        "Arbeit NN,fem,dat,sing\n" +
        "Zimmer\n" +
        "Zimmer NN,neut,nom,sing\n" +
        "schreiben\n" +
        "schreiben V,inf\n" +
        "schreib\n" +
        "schreiben V,imp,sing\n" +
        "wasch\n" +
        "waschen V,imp,sing\n" +
        "Griffe\n" +
        "Griff NN,acc,plu\n" +
        "gehabt\n" +
        "haben V,ppast,<aux>\n" +
        "gehabt ADJ,pos,<pred>\n" +
        "der\n" +
        "der ART,def,masc,nom,sing\n" +
        "Aal\n" +
        "Aal NN,masc,nom,sing\n" +
        "aal\n" +
        "aalen V,imp,sing\n";

    private static GermanLexiconIndexBuildResult BuildFixture()
    {
        using var reader = new StringReader(Fixture);
        return GermanLexiconIndexBuilder.Build(GermanMorphDictionaryParser.Parse(reader));
    }

    [TestMethod]
    public void Build_UnambiguousBaseForms_AreIncludedAsLemmaEntries()
    {
        var result = BuildFixture();

        CollectionAssert.Contains(result.Lemmas.ToList(), new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun));
        CollectionAssert.Contains(result.Lemmas.ToList(), new GermanLexemeEntry("Zimmer", GermanLexemeCategory.Noun));
        CollectionAssert.Contains(result.Lemmas.ToList(), new GermanLexemeEntry("schreiben", GermanLexemeCategory.Verb));
        CollectionAssert.Contains(result.Lemmas.ToList(), new GermanLexemeEntry("Aal", GermanLexemeCategory.Noun));
        Assert.AreEqual(4, result.Lemmas.Count);
    }

    [TestMethod]
    public void Build_LemmaEntries_AreSortedByOrdinalForm()
    {
        var result = BuildFixture();

        var sorted = result.Lemmas.OrderBy(e => e.Lemma, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(sorted, result.Lemmas.ToList());
    }

    [TestMethod]
    public void Build_ImperativeSingularVerbForms_AreCapitalizedAndDerivedAsStems()
    {
        var result = BuildFixture();

        CollectionAssert.Contains(
            result.Stems.ToList(),
            new GermanCompoundStemEntry("Schreib", "schreiben", GermanLexemeCategory.Verb));
        CollectionAssert.Contains(
            result.Stems.ToList(),
            new GermanCompoundStemEntry("Wasch", "waschen", GermanLexemeCategory.Verb));
    }

    [TestMethod]
    public void Build_StemCandidateCollidingWithExistingNounLemma_IsExcludedFailClosed()
    {
        var result = BuildFixture();

        Assert.IsFalse(result.Stems.Any(s => s.ComponentForm == "Aal"));
        Assert.AreEqual(1, result.Statistics.DerivedVerbStemCollisionExclusions);
        Assert.AreEqual(2, result.Stems.Count);
    }

    [TestMethod]
    public void Build_TwoDistinctImperativeSingularFormsCollideAfterCapitalization_ExcludesBothFailClosed()
    {
        // "Schreib" (already-capitalized raw form, lemma "schreien") and "schreib" (lowercase
        // raw form, lemma "schreiben") are two distinct upstream word forms, but
        // CapitalizeFirstLetter maps both to the same component key "Schreib". Neither lemma may
        // win arbitrarily: the ambiguous component key must be entirely absent from the final
        // stem index, not silently resolved to whichever candidate was processed first.
        const string fixture =
            "Schreib\n" +
            "schreien V,imp,sing\n" +
            "schreib\n" +
            "schreiben V,imp,sing\n";

        using var reader = new StringReader(fixture);
        var result = GermanLexiconIndexBuilder.Build(GermanMorphDictionaryParser.Parse(reader));

        Assert.IsFalse(result.Stems.Any(s => s.ComponentForm == "Schreib"));
        Assert.AreEqual(0, result.Stems.Count);
        Assert.AreEqual(2, result.Statistics.DerivedVerbStemCollisionExclusions);
        Assert.AreEqual(0, result.Statistics.AmbiguousImperativeSingularForms);
    }

    [TestMethod]
    public void Build_AmbiguousWordAcrossCategories_IsExcludedFromLemmaTable()
    {
        var result = BuildFixture();

        Assert.IsFalse(result.Lemmas.Any(e => e.Lemma == "gehabt"));
        Assert.IsFalse(result.Lemmas.Any(e => e.Lemma == "haben"));
        Assert.AreEqual(1, result.Statistics.AmbiguousWordForms);
    }

    [TestMethod]
    public void Build_UnsupportedCategoryWord_IsExcludedFailClosed()
    {
        var result = BuildFixture();

        Assert.IsFalse(result.Lemmas.Any(e => e.Lemma == "der"));
        Assert.AreEqual(1, result.Statistics.UnsupportedCategoryWordForms);
    }

    [TestMethod]
    public void Build_InflectedNonBaseForm_IsExcludedFromLemmaTable()
    {
        var result = BuildFixture();

        Assert.IsFalse(result.Lemmas.Any(e => e.Lemma == "Griffe"));
        Assert.IsFalse(result.Lemmas.Any(e => e.Lemma == "Griff"));

        // "Griffe"->Griff plus the three imperative-singular forms ("schreib", "wasch", "aal")
        // are each single-analysis, supported-category, but word != lemma: all four fail closed
        // out of the base-form lemma table here, independently of whether "schreib"/"wasch"
        // separately succeed as *stems* via the imperative-singular derivation path below.
        Assert.AreEqual(4, result.Statistics.InflectedFormWordFormsExcluded);
    }

    [TestMethod]
    public void Build_ExactCountsMatchFixtureShape()
    {
        var result = BuildFixture();

        Assert.AreEqual(10, result.Statistics.TotalDistinctWordForms);
        Assert.AreEqual(4, result.Statistics.UnambiguousBaseFormLemmaEntries);
        Assert.AreEqual(3, result.Statistics.ImperativeSingularCandidateForms);
        Assert.AreEqual(0, result.Statistics.AmbiguousImperativeSingularForms);
        Assert.AreEqual(2, result.Statistics.DerivedVerbStemEntries);
    }

    [TestMethod]
    public void Build_SameFixtureTwice_ProducesEquivalentDeterministicOutput()
    {
        var first = BuildFixture();
        var second = BuildFixture();

        CollectionAssert.AreEqual(first.Lemmas.ToList(), second.Lemmas.ToList());
        CollectionAssert.AreEqual(first.Stems.ToList(), second.Stems.ToList());
        Assert.AreEqual(first.Statistics, second.Statistics);
    }
}
