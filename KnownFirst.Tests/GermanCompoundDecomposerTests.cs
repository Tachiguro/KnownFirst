using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class GermanCompoundDecomposerTests
{
    private readonly IGermanLexicon _lexicon = new FixtureGermanLexicon();

    [TestMethod]
    public void TryDecompose_Schreibmaschine_UniquelyDecomposesToSchreibenAndMaschine()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Schreibmaschine", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.AreEqual("Schreib", decomposition.LeftComponent.ComponentForm);
        Assert.AreEqual("schreiben", decomposition.LeftComponent.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, decomposition.LeftComponent.Category);
        Assert.AreEqual("maschine", decomposition.RightComponent.ComponentForm);
        Assert.AreEqual("Maschine", decomposition.RightComponent.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.RightComponent.Category);
    }

    [TestMethod]
    public void TryDecompose_Waschmaschine_UniquelyDecomposesToWaschenAndMaschine()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Waschmaschine", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.AreEqual("Wasch", decomposition.LeftComponent.ComponentForm);
        Assert.AreEqual("waschen", decomposition.LeftComponent.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, decomposition.LeftComponent.Category);
        Assert.AreEqual("maschine", decomposition.RightComponent.ComponentForm);
        Assert.AreEqual("Maschine", decomposition.RightComponent.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.RightComponent.Category);
    }

    [TestMethod]
    public void TryDecompose_Arbeitszimmer_FailsClosedBecauseLinkingSIsNeverStripped()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Arbeitszimmer", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_Sicherheitsmanagement_FailsClosedBecauseLinkingSIsNeverStripped()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Sicherheitsmanagement", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_UnknownRightComponent_PreventsDecomposition()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Schreibpferd", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_UnknownLeftComponent_PreventsDecomposition()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Katzenmaschine", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_MultipleValidTwoComponentSplits_FailsClosed()
    {
        var lexicon = new AmbiguousFixtureLexicon();

        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Xyzabc", lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_RightComponentInitialUppercaseProbe_MatchesExactCaseAfterFirstLetterOnly()
    {
        var lexicon = new UppercaseProbeFixtureLexicon(includeProperlyCasedNoun: true);

        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Hausmaus", lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.AreEqual("Haus", decomposition.LeftComponent.ComponentForm);
        Assert.AreEqual("Haus", decomposition.LeftComponent.Lemma);
        Assert.AreEqual("maus", decomposition.RightComponent.ComponentForm);
        Assert.AreEqual("Maus", decomposition.RightComponent.Lemma);
    }

    [TestMethod]
    public void TryDecompose_RightComponentUppercaseProbe_DoesNotBroadenIntoFullCaseInsensitiveMatch()
    {
        var lexicon = new UppercaseProbeFixtureLexicon(includeProperlyCasedNoun: false);

        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Hausmaus", lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_RepeatedCalls_AreDeterministic()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
                "Schreibmaschine", _lexicon, out var decomposition);

            Assert.IsTrue(succeeded);
            Assert.IsNotNull(decomposition);
            Assert.AreEqual("schreiben", decomposition.LeftComponent.Lemma);
            Assert.AreEqual("Maschine", decomposition.RightComponent.Lemma);
        }
    }

    /// <summary>
    /// Test-local fake lexicon that deliberately creates two distinct, equally valid two-component
    /// splits of the word "Xyzabc" ("Xyz"+"Abc" and "Xyza"+"Bc") so the decomposer's fail-closed
    /// ambiguity rule can be exercised without broadening the production <see cref="FixtureGermanLexicon"/>.
    /// </summary>
    private sealed class AmbiguousFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["Xyz"] = new("Xyz", GermanLexemeCategory.Noun),
                ["Xyza"] = new("Xyza", GermanLexemeCategory.Noun),
                ["Abc"] = new("Abc", GermanLexemeCategory.Noun),
                ["Bc"] = new("Bc", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon isolating the right-hand initial-uppercase lookup probe: with
    /// <paramref name="includeProperlyCasedNoun"/> true it exposes exactly the properly-cased noun
    /// "Maus" the probe is expected to find; with it false it exposes only an all-uppercase "MAUS"
    /// key that a correct minimal (first-letter-only) probe must never match.
    /// </summary>
    private sealed class UppercaseProbeFixtureLexicon(bool includeProperlyCasedNoun) : IGermanLexicon
    {
        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry)
        {
            if (string.Equals(form, "Haus", StringComparison.Ordinal))
            {
                entry = new GermanLexemeEntry("Haus", GermanLexemeCategory.Noun);
                return true;
            }

            if (includeProperlyCasedNoun && string.Equals(form, "Maus", StringComparison.Ordinal))
            {
                entry = new GermanLexemeEntry("Maus", GermanLexemeCategory.Noun);
                return true;
            }

            if (!includeProperlyCasedNoun && string.Equals(form, "MAUS", StringComparison.Ordinal))
            {
                entry = new GermanLexemeEntry("MAUS", GermanLexemeCategory.Noun);
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }
}
