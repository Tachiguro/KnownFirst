using KnownFirst.Core.Text;
using KnownFirst.Core.Text.German;

namespace KnownFirst.Tests;

/// <summary>
/// Production-data contract tests for <see cref="GeneratedGermanLexicon"/>, loaded from the
/// real runtime asset generated from the pinned DuyguA/german-morph-dictionaries commit
/// <c>1780890c0fd25a989201c96000af323cd201fa5c</c> (CC BY-SA 4.0 data). Unlike
/// <see cref="GermanLexiconContractTests"/> (which exercises the hand-written
/// <c>FixtureGermanLexicon</c>), this class proves the actual generated production asset
/// behaves per the <see cref="IGermanLexicon"/> contract on real upstream evidence.
/// </summary>
[TestClass]
public sealed class ProductionGermanLexiconTests
{
    private static GeneratedGermanLexicon Lexicon { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "German", "german-lexicon.v2.kfgl");
        Lexicon = GeneratedGermanLexicon.LoadFromFile(path);
    }

    [TestMethod]
    public void Provenance_MatchesThePinnedUpstreamCommitAndLicense()
    {
        Assert.AreEqual("german-morph-dictionaries", Lexicon.Provenance.UpstreamProjectName);
        Assert.AreEqual("https://github.com/DuyguA/german-morph-dictionaries", Lexicon.Provenance.UpstreamRepositoryUrl);
        Assert.AreEqual("1780890c0fd25a989201c96000af323cd201fa5c", Lexicon.Provenance.UpstreamCommit);
        Assert.AreEqual("morf_dict.zip", Lexicon.Provenance.UpstreamSourceAssetPath);
        StringAssert.Contains(Lexicon.Provenance.DataLicenseIdentifier, "CC BY-SA 4.0");
    }

    [TestMethod]
    public void Counts_ReportNonZeroAcceptedLemmaAndStemEntries()
    {
        Assert.IsTrue(Lexicon.Counts.UnambiguousBaseFormLemmaEntries > 0);
        Assert.IsTrue(Lexicon.Counts.DerivedVerbStemEntries > 0);
    }

    [TestMethod]
    [DataRow("Arbeit")]
    [DataRow("Zimmer")]
    [DataRow("Sicherheit")]
    [DataRow("Management")]
    [DataRow("Haus")]
    [DataRow("Maus")]
    [DataRow("Maschine")]
    [DataRow("Test")]
    [DataRow("Auto")]
    [DataRow("Anlage")]
    public void TryLookupLemma_RealUpstreamBaseFormNouns_ReturnCanonicalNounEntry(string noun)
    {
        Assert.IsTrue(Lexicon.TryLookupLemma(noun, out var entry), $"'{noun}' should resolve as a base-form noun.");
        Assert.AreEqual(noun, entry!.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, entry.Category);
    }

    [TestMethod]
    [DataRow("schreiben")]
    [DataRow("waschen")]
    [DataRow("schrauben")]
    public void TryLookupLemma_RealUpstreamInfinitiveVerbs_ReturnCanonicalVerbEntry(string verb)
    {
        Assert.IsTrue(Lexicon.TryLookupLemma(verb, out var entry), $"'{verb}' should resolve as a base-form verb.");
        Assert.AreEqual(verb, entry!.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, entry.Category);
    }

    [TestMethod]
    [DataRow("Schreib", "schreiben")]
    [DataRow("Wasch", "waschen")]
    [DataRow("Schraub", "schrauben")]
    public void TryLookupStem_RealUpstreamImperativeDerivedVerbStems_ReturnCanonicalLemma(
        string componentForm, string expectedLemma)
    {
        Assert.IsTrue(
            Lexicon.TryLookupStem(componentForm, out var entry),
            $"'{componentForm}' should resolve as a derived compound-initial verb stem.");
        Assert.AreEqual(componentForm, entry!.ComponentForm);
        Assert.AreEqual(expectedLemma, entry.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, entry.Category);
    }

    [TestMethod]
    [DataRow("Arbeit")]
    [DataRow("Zimmer")]
    [DataRow("Sicherheit")]
    [DataRow("Maschine")]
    public void TryLookupStem_RealUpstreamNouns_ResolveAsTheirOwnCompoundStem(string noun)
    {
        Assert.IsTrue(Lexicon.TryLookupStem(noun, out var entry));
        Assert.AreEqual(noun, entry!.ComponentForm);
        Assert.AreEqual(noun, entry.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, entry.Category);
    }

    [TestMethod]
    public void TryLookupLemma_GenuineUpstreamAmbiguity_FailsClosedRatherThanGuessing()
    {
        // Real upstream evidence: "gehabt" analyzes both as "haben V,ppast" and as
        // "gehabt ADJ,pos" (two distinct lemma/category pairs). Neither wins arbitrarily.
        Assert.IsFalse(Lexicon.TryLookupLemma("gehabt", out var entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void TryLookupLemma_InflectedNonBaseForm_FailsClosedInThisPackagesRepresentation()
    {
        // Real, honest upstream-evidence gap: "Griffe" (plural of "Griff") is unambiguously
        // analyzed by the upstream data as lemma "Griff", but this package's production lemma
        // table is deliberately restricted to forms that are themselves base/citation forms
        // (word == lemma). "Griffe" != "Griff", so it is excluded rather than fabricated as a
        // second, broader inflected-form index. Its base form resolves normally.
        Assert.IsFalse(Lexicon.TryLookupLemma("Griffe", out var griffeEntry));
        Assert.IsNull(griffeEntry);

        Assert.IsTrue(Lexicon.TryLookupLemma("Griff", out var griffEntry));
        Assert.AreEqual("Griff", griffEntry!.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, griffEntry.Category);
    }

    [TestMethod]
    public void TryLookupLemma_UnsupportedUpstreamCategory_FailsClosed()
    {
        // "der" is only ever analyzed as ART (article), an upstream category this package does
        // not support; it must not be force-mapped to Noun/Verb/Adjective.
        Assert.IsFalse(Lexicon.TryLookupLemma("der", out var entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void TryLookupLemma_UnknownForm_FailsClosed()
    {
        Assert.IsFalse(Lexicon.TryLookupLemma("Zeppelinwerft", out var entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void RepeatedLookups_AreDeterministic()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.IsTrue(Lexicon.TryLookupLemma("Maschine", out var noun));
            Assert.AreEqual(GermanLexemeCategory.Noun, noun!.Category);

            Assert.IsTrue(Lexicon.TryLookupStem("Schreib", out var stem));
            Assert.AreEqual("schreiben", stem!.Lemma);

            Assert.IsFalse(Lexicon.TryLookupLemma("gehabt", out _));
        }
    }

    [TestMethod]
    public void ConservativeGermanCompoundDecomposer_RealCompoundBackedByProductionLexicon_Decomposes()
    {
        Assert.IsTrue(
            ConservativeGermanCompoundDecomposer.TryDecompose("Schreibmaschine", Lexicon, out var decomposition));
        Assert.AreEqual("schreiben", decomposition!.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, decomposition.Components[0].Category);
        Assert.AreEqual("Maschine", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }

    [TestMethod]
    public void ConservativeGermanCompoundDecomposer_SecondRealCompound_Decomposes()
    {
        Assert.IsTrue(
            ConservativeGermanCompoundDecomposer.TryDecompose("Waschmaschine", Lexicon, out var decomposition));
        Assert.AreEqual("waschen", decomposition!.Components[0].Lemma);
        Assert.AreEqual("Maschine", decomposition.Components[1].Lemma);
    }

    [TestMethod]
    public void ConservativeGermanCompoundDecomposer_Arbeitszimmer_DecomposesToArbeitAndZimmerAgainstProductionLexicon()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Arbeitszimmer", Lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Arbeit", decomposition.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[0].Category);
        Assert.AreEqual("Zimmer", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }

    [TestMethod]
    public void ConservativeGermanCompoundDecomposer_Sicherheitsmanagement_DecomposesToSicherheitAndManagementAgainstProductionLexicon()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Sicherheitsmanagement", Lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Sicherheit", decomposition.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[0].Category);
        Assert.AreEqual("Management", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }
}
