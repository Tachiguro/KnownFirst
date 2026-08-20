using KnownFirst.Tools.GermanLexicon;

namespace KnownFirst.Tests;

[TestClass]
public sealed class GermanMorphDictionaryParserTests
{
    [TestMethod]
    public void Parse_SingleWordSingleAnalysis_YieldsOneAnalysis()
    {
        using var reader = new StringReader("Arbeit\nArbeit NN,fem,nom,sing\n");

        var analyses = GermanMorphDictionaryParser.Parse(reader).ToList();

        Assert.AreEqual(1, analyses.Count);
        Assert.AreEqual("Arbeit", analyses[0].Word);
        Assert.AreEqual("Arbeit", analyses[0].Lemma);
        Assert.AreEqual("NN", analyses[0].RawCategory);
        Assert.AreEqual("NN,fem,nom,sing", analyses[0].Tags);
    }

    [TestMethod]
    public void Parse_MultipleAnalysesForOneWord_YieldsAllInFileOrderDeterministically()
    {
        using var reader = new StringReader(
            "schreiben\n" +
            "schreiben V,inf\n" +
            "schreiben V,1per,plu,pres,ind\n" +
            "schreiben V,3per,plu,pres,ind\n");

        var analyses = GermanMorphDictionaryParser.Parse(reader).ToList();

        Assert.AreEqual(3, analyses.Count);
        CollectionAssert.AllItemsAreNotNull(analyses.Select(a => (object)a.Word).ToList());
        Assert.IsTrue(analyses.All(a => a.Word == "schreiben" && a.Lemma == "schreiben" && a.RawCategory == "V"));
        Assert.AreEqual("V,inf", analyses[0].Tags);
        Assert.AreEqual("V,1per,plu,pres,ind", analyses[1].Tags);
        Assert.AreEqual("V,3per,plu,pres,ind", analyses[2].Tags);
    }

    [TestMethod]
    public void Parse_TwoWordBlocks_AssignsEachAnalysisToItsOwnWord()
    {
        using var reader = new StringReader(
            "bin\n" +
            "sein V,1per,sing,pres,ind,<aux>\n" +
            "bist\n" +
            "sein V,2per,sing,pres,ind,<aux>\n");

        var analyses = GermanMorphDictionaryParser.Parse(reader).ToList();

        Assert.AreEqual(2, analyses.Count);
        Assert.AreEqual("bin", analyses[0].Word);
        Assert.AreEqual("bist", analyses[1].Word);
        Assert.IsTrue(analyses.All(a => a.Lemma == "sein" && a.RawCategory == "V"));
    }

    [TestMethod]
    public void Parse_AmbiguousWordAcrossCategories_YieldsBothCompetingAnalyses()
    {
        using var reader = new StringReader(
            "gehabt\n" +
            "haben V,ppast,<aux>\n" +
            "gehabt ADJ,pos,<pred>\n");

        var analyses = GermanMorphDictionaryParser.Parse(reader).ToList();

        Assert.AreEqual(2, analyses.Count);
        Assert.AreEqual("haben", analyses[0].Lemma);
        Assert.AreEqual("V", analyses[0].RawCategory);
        Assert.AreEqual("gehabt", analyses[1].Lemma);
        Assert.AreEqual("ADJ", analyses[1].RawCategory);
    }

    [TestMethod]
    public void Parse_AnalysisLineBeforeAnyWordHeader_FailsClosedWithClearError()
    {
        using var reader = new StringReader("schreiben V,inf\n");

        var ex = Assert.ThrowsExactly<GermanMorphDictionaryFormatException>(
            () => GermanMorphDictionaryParser.Parse(reader).ToList());

        StringAssert.Contains(ex.Message, "Line 1");
    }

    [TestMethod]
    public void Parse_BlankLine_FailsClosedWithClearError()
    {
        using var reader = new StringReader("Arbeit\n\nArbeit NN,fem,nom,sing\n");

        var ex = Assert.ThrowsExactly<GermanMorphDictionaryFormatException>(
            () => GermanMorphDictionaryParser.Parse(reader).ToList());

        StringAssert.Contains(ex.Message, "Line 2");
    }

    [TestMethod]
    public void Parse_SameFixtureTwice_ProducesByteIdenticalDeterministicSequence()
    {
        const string fixture = "Arbeit\nArbeit NN,fem,nom,sing\nZimmer\nZimmer NN,neut,nom,sing\n";

        using var readerA = new StringReader(fixture);
        using var readerB = new StringReader(fixture);

        var first = GermanMorphDictionaryParser.Parse(readerA).ToList();
        var second = GermanMorphDictionaryParser.Parse(readerB).ToList();

        CollectionAssert.AreEqual(first, second);
    }
}
