using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MeaningRankerHybridTests
{
    private static LexicalMeaning CreateMeaning(
        string id,
        string definition,
        string partOfSpeech = "noun",
        string? example = null,
        params string[] usageLabels) =>
        new(
            MeaningId: id,
            PartOfSpeech: partOfSpeech,
            Definition: definition,
            Translation: null,
            Example: example,
            UsageLabels: usageLabels);

    [TestMethod]
    public void Rank_StandardTierBeatsDemotedTier_EvenWhenDemotedHasHigherContextOverlap()
    {
        // A. Strong demotion vs context (Genuine RED)
        var ranker = new MeaningRanker();
        var demotedWithHighOverlap = CreateMeaning(
            "demoted",
            "Authentication risk is reduced by strong encryption architecture.",
            "noun",
            example: "Risk mitigation is key.",
            "archaic");
        var standardWithZeroOverlap = CreateMeaning(
            "standard",
            "A basic physical shelter or house.",
            "noun",
            example: null);

        var ranked = ranker.Rank(
            [demotedWithHighOverlap, standardWithZeroOverlap],
            TokenKind.Word,
            "Authentication risk is reduced by strong encryption architecture.");

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("standard", ranked[0].MeaningId, "Standard tier must sort before Demoted tier despite lower context overlap.");
        Assert.AreEqual("demoted", ranked[1].MeaningId, "Demoted tier must sort after Standard tier despite higher context overlap.");
    }

    [TestMethod]
    public void Rank_StandardTierBeatsDemotedTier_EvenWhenDemotedHasStrongerTokenKindMatch()
    {
        // B. Strong demotion vs token kind (Genuine RED)
        var ranker = new MeaningRanker();
        var demotedAcronym = CreateMeaning(
            "demoted-acronym",
            "An archaic security acronym.",
            partOfSpeech: "initialism",
            example: null,
            "obsolete");
        var standardWord = CreateMeaning(
            "standard-word",
            "A standard word definition.",
            partOfSpeech: "noun",
            example: null);

        var ranked = ranker.Rank(
            [demotedAcronym, standardWord],
            TokenKind.Acronym,
            context: null);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("standard-word", ranked[0].MeaningId, "Standard tier must sort before Demoted tier despite weaker TokenKindMatch.");
        Assert.AreEqual("demoted-acronym", ranked[1].MeaningId, "Demoted tier must sort after Standard tier despite stronger TokenKindMatch.");
    }

    [TestMethod]
    public void Rank_ContextOverlapRanksFirst_BetweenMeaningsInSameStandardTier()
    {
        // C. Context remains effective inside Standard tier
        var ranker = new MeaningRanker();
        var standardLowOverlap = CreateMeaning("m1", "A financial institution banking establishment.", "noun");
        var standardHighOverlap = CreateMeaning("m2", "A small wooden cabin in the forest.", "noun");

        var ranked = ranker.Rank(
            [standardLowOverlap, standardHighOverlap],
            TokenKind.Word,
            "They stayed in a small wooden cabin deep in the green forest.");

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId, "Higher context overlap ranks first within Standard tier.");
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_ContextOverlapRanksFirst_BetweenMeaningsInSameDemotedTier()
    {
        // D. Context remains effective inside Demoted tier
        var ranker = new MeaningRanker();
        var demotedLowOverlap = CreateMeaning("m1", "An archaic financial ledger entry.", "noun", null, "archaic");
        var demotedHighOverlap = CreateMeaning("m2", "An obsolete wooden fortress structure.", "noun", null, "obsolete");

        var ranked = ranker.Rank(
            [demotedLowOverlap, demotedHighOverlap],
            TokenKind.Word,
            "The ancient wooden fortress structure stood on the hill.");

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId, "Higher context overlap ranks first within Demoted tier.");
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_TokenKindMatchRanksFirst_BetweenMeaningsInSameStandardTier()
    {
        // E. TokenKindMatch remains effective inside the same tier
        var ranker = new MeaningRanker();
        var generalWord = CreateMeaning("m1", "A general word definition.", "noun");
        var acronymMatch = CreateMeaning("m2", "A specific technological initialism.", "initialism");

        var ranked = ranker.Rank(
            [generalWord, acronymMatch],
            TokenKind.Acronym,
            context: null);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId, "TokenKindMatch ranks first within same tier.");
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_PreservesOriginalProviderOrder_WhenRelevanceTokenKindAndContextOverlapAreEqual()
    {
        // F. Provider-order fallback
        var ranker = new MeaningRanker();
        var m1 = CreateMeaning("m1", "First equal standard meaning.", "noun");
        var m2 = CreateMeaning("m2", "Second equal standard meaning.", "noun");
        var m3 = CreateMeaning("m3", "Third equal standard meaning.", "noun");

        var ranked = ranker.Rank([m1, m2, m3], TokenKind.Word, context: null);

        Assert.AreEqual(3, ranked.Count);
        Assert.AreEqual("m1", ranked[0].MeaningId);
        Assert.AreEqual("m2", ranked[1].MeaningId);
        Assert.AreEqual("m3", ranked[2].MeaningId);
    }

    [TestMethod]
    public void Rank_DeterministicWhenContextIsNull()
    {
        // G. No-context fallback
        var ranker = new MeaningRanker();
        var demoted = CreateMeaning("m1", "Archaic meaning.", "noun", null, "archaic");
        var standard = CreateMeaning("m2", "Modern meaning.", "noun");

        var ranked = ranker.Rank([demoted, standard], TokenKind.Word, context: null);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId);
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_TreatsRareSeltenAndUnknownLabelsAsNeutralStandardTier()
    {
        // H. Neutral labels
        var ranker = new MeaningRanker();
        var m1 = CreateMeaning("m1", "Rare term.", "noun", null, "rare");
        var m2 = CreateMeaning("m2", "Seltene Bedeutung.", "noun", null, "selten");
        var m3 = CreateMeaning("m3", "Unknown category.", "noun", null, "custom-domain");
        var m4 = CreateMeaning("m4", "Archaic term.", "noun", null, "archaic");

        var ranked = ranker.Rank([m1, m2, m3, m4], TokenKind.Word, context: null);

        Assert.AreEqual(4, ranked.Count);
        Assert.AreEqual("m1", ranked[0].MeaningId, "'rare' remains standard in provider order.");
        Assert.AreEqual("m2", ranked[1].MeaningId, "'selten' remains standard in provider order.");
        Assert.AreEqual("m3", ranked[2].MeaningId, "unknown label remains standard in provider order.");
        Assert.AreEqual("m4", ranked[3].MeaningId, "'archaic' is demoted to tail.");
    }

    [TestMethod]
    public void Rank_PreservesAllInputMeaningsWithoutLossOrDuplication()
    {
        // I & J. Losslessness and identity
        var ranker = new MeaningRanker();
        var m1 = CreateMeaning("m1", "Def 1", "noun", null, "veraltet");
        var m2 = CreateMeaning("m2", "Def 2", "verb");
        var m3 = CreateMeaning("m3", "Def 3", "adjective");
        var m4 = CreateMeaning("m4", "Def 4", "noun", null, "historisch");

        var ranked = ranker.Rank([m1, m2, m3, m4], TokenKind.Word, "Def 1 context match");

        Assert.AreEqual(4, ranked.Count);
        var ids = ranked.Select(m => m.MeaningId).ToArray();
        CollectionAssert.AreEquivalent(new[] { "m1", "m2", "m3", "m4" }, ids);

        // Standard meanings (m2, m3) precede demoted meanings (m1, m4)
        Assert.AreEqual("m2", ranked[0].MeaningId);
        Assert.AreEqual("m3", ranked[1].MeaningId);
        Assert.AreEqual("m1", ranked[2].MeaningId, "Demoted m1 with context overlap precedes demoted m4 without overlap.");
        Assert.AreEqual("m4", ranked[3].MeaningId);
    }
}
