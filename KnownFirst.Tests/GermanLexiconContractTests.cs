using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class GermanLexiconContractTests
{
    private readonly IGermanLexicon _lexicon = new FixtureGermanLexicon();

    [TestMethod]
    public void TryLookupLemma_KnownFixtureLexeme_ReturnsCanonicalEntry()
    {
        Assert.IsTrue(_lexicon.TryLookupLemma("schreiben", out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual("schreiben", entry.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, entry.Category);

        Assert.IsTrue(_lexicon.TryLookupLemma("Maschine", out var noun));
        Assert.IsNotNull(noun);
        Assert.AreEqual("Maschine", noun.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, noun.Category);
    }

    [TestMethod]
    public void TryLookupStem_KnownVerbStem_ReturnsCanonicalLemma()
    {
        Assert.IsTrue(_lexicon.TryLookupStem("Schreib", out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual("Schreib", entry.ComponentForm);
        Assert.AreEqual("schreiben", entry.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, entry.Category);

        Assert.IsTrue(_lexicon.TryLookupStem("Wasch", out var wash));
        Assert.IsNotNull(wash);
        Assert.AreEqual("waschen", wash.Lemma);
    }

    [TestMethod]
    public void TryLookupStem_KnownNounStem_ReturnsCanonicalLemma()
    {
        Assert.IsTrue(_lexicon.TryLookupStem("Arbeit", out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual("Arbeit", entry.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, entry.Category);

        Assert.IsTrue(_lexicon.TryLookupStem("Sicherheit", out var safety));
        Assert.IsNotNull(safety);
        Assert.AreEqual("Sicherheit", safety.Lemma);
    }

    [TestMethod]
    public void TryLookupLemma_UnknownFixtureLexeme_ReturnsFalse()
    {
        Assert.IsFalse(_lexicon.TryLookupLemma("Zeppelinwerft", out var entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void TryLookupStem_UnknownFixtureStem_ReturnsFalse()
    {
        Assert.IsFalse(_lexicon.TryLookupStem("Zeppelin", out var entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void FixtureLexicon_RepeatedLookup_IsDeterministic()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.IsTrue(_lexicon.TryLookupStem("Schreib", out var stem));
            Assert.IsNotNull(stem);
            Assert.AreEqual("schreiben", stem.Lemma);

            Assert.IsTrue(_lexicon.TryLookupLemma("Management", out var lexeme));
            Assert.IsNotNull(lexeme);
            Assert.AreEqual("Management", lexeme.Lemma);
            Assert.AreEqual(GermanLexemeCategory.Noun, lexeme.Category);

            Assert.IsFalse(_lexicon.TryLookupStem("Zeppelin", out var unknown));
            Assert.IsNull(unknown);
        }
    }
}
