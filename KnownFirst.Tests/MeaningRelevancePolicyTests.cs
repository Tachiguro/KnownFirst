using KnownFirst.Core.Preparation;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MeaningRelevancePolicyTests
{
    private static LexicalMeaning CreateMeaning(
        string id,
        string definition,
        params string[] usageLabels) =>
        new(
            MeaningId: id,
            PartOfSpeech: "noun",
            Definition: definition,
            Translation: null,
            Example: null,
            UsageLabels: usageLabels);

    [TestMethod]
    public void Rank_DemotesArchaicMeaningBelowNeutral_EvenWhenArchaicIsFirstInProviderOrder()
    {
        var archaic = CreateMeaning("m1", "First archaic definition", "archaic");
        var modern = CreateMeaning("m2", "Second modern definition");

        var ranked = MeaningRelevancePolicy.Rank([archaic, modern]);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId, "Neutral modern meaning must sort before archaic.");
        Assert.AreEqual("m1", ranked[1].MeaningId, "Archaic meaning must sort after neutral modern meaning.");
    }

    [TestMethod]
    public void Rank_DemotesObsoleteMeaningBelowNeutral()
    {
        var obsolete = CreateMeaning("m1", "Obsolete definition", "obsolete");
        var modern = CreateMeaning("m2", "Current definition");

        var ranked = MeaningRelevancePolicy.Rank([obsolete, modern]);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId);
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_DemotesHistoricalMeaningBelowNeutral()
    {
        var historical = CreateMeaning("m1", "Historical definition", "historical");
        var modern = CreateMeaning("m2", "Current definition");

        var ranked = MeaningRelevancePolicy.Rank([historical, modern]);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId);
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_DemotesDatedMeaningBelowNeutral()
    {
        var dated = CreateMeaning("m1", "Dated definition", "dated");
        var modern = CreateMeaning("m2", "Current definition");

        var ranked = MeaningRelevancePolicy.Rank([dated, modern]);

        Assert.AreEqual(2, ranked.Count);
        Assert.AreEqual("m2", ranked[0].MeaningId);
        Assert.AreEqual("m1", ranked[1].MeaningId);
    }

    [TestMethod]
    public void Rank_DemotesGermanDemotionLabelsBelowNeutral()
    {
        var veraltet = CreateMeaning("m1", "Veraltete Bedeutung", "veraltet");
        var historisch = CreateMeaning("m2", "Historische Bedeutung", "historisch");
        var obsolet = CreateMeaning("m3", "Obsolete Bedeutung", "obsolet");
        var veraltend = CreateMeaning("m4", "Veraltende Bedeutung", "veraltend");
        var modern = CreateMeaning("m5", "Gegenwärtige Bedeutung");

        var ranked = MeaningRelevancePolicy.Rank([veraltet, historisch, obsolet, veraltend, modern]);

        Assert.AreEqual(5, ranked.Count);
        Assert.AreEqual("m5", ranked[0].MeaningId, "Modern German meaning must rank first.");
        Assert.AreEqual("m1", ranked[1].MeaningId, "Veraltet preserved in provider relative order.");
        Assert.AreEqual("m2", ranked[2].MeaningId, "Historisch preserved in provider relative order.");
        Assert.AreEqual("m3", ranked[3].MeaningId, "Obsolet preserved in provider relative order.");
        Assert.AreEqual("m4", ranked[4].MeaningId, "Veraltend preserved in provider relative order.");
    }

    [TestMethod]
    public void Rank_NormalizesCaseWhitespaceAndPunctuation()
    {
        var m1 = CreateMeaning("m1", "Def 1", "  (Archaic.)  ");
        var m2 = CreateMeaning("m2", "Def 2", "OBSOLETE");
        var m3 = CreateMeaning("m3", "Def 3", "[historisch]");
        var modern = CreateMeaning("m4", "Modern def");

        var ranked = MeaningRelevancePolicy.Rank([m1, m2, m3, modern]);

        Assert.AreEqual(4, ranked.Count);
        Assert.AreEqual("m4", ranked[0].MeaningId, "Modern definition must rank ahead of punctuation/case-wrapped demotion labels.");
        Assert.AreEqual("m1", ranked[1].MeaningId);
        Assert.AreEqual("m2", ranked[2].MeaningId);
        Assert.AreEqual("m3", ranked[3].MeaningId);
    }

    [TestMethod]
    public void Rank_TreatsUnknownAndStyleLabelsAsNeutral()
    {
        var m1 = CreateMeaning("m1", "Colloquial sense", "colloquial");
        var m2 = CreateMeaning("m2", "Technical sense", "computing", "technical");
        var m3 = CreateMeaning("m3", "Slang sense", "slang");
        var m4 = CreateMeaning("m4", "Formal sense", "formal");
        var m5 = CreateMeaning("m5", "Obsolete sense", "obsolete");

        var ranked = MeaningRelevancePolicy.Rank([m1, m2, m3, m4, m5]);

        Assert.AreEqual(5, ranked.Count);
        Assert.AreEqual("m1", ranked[0].MeaningId, "Colloquial remains in original order.");
        Assert.AreEqual("m2", ranked[1].MeaningId, "Technical remains in original order.");
        Assert.AreEqual("m3", ranked[2].MeaningId, "Slang remains in original order.");
        Assert.AreEqual("m4", ranked[3].MeaningId, "Formal remains in original order.");
        Assert.AreEqual("m5", ranked[4].MeaningId, "Obsolete is demoted to tail.");
    }

    [TestMethod]
    public void Rank_TreatsRareAndSeltenAsNeutral()
    {
        var m1 = CreateMeaning("m1", "Rare sense", "rare");
        var m2 = CreateMeaning("m2", "Seltene Bedeutung", "selten");
        var m3 = CreateMeaning("m3", "Archaic sense", "archaic");

        var ranked = MeaningRelevancePolicy.Rank([m1, m2, m3]);

        Assert.AreEqual(3, ranked.Count);
        Assert.AreEqual("m1", ranked[0].MeaningId, "'rare' is not strongly demoted.");
        Assert.AreEqual("m2", ranked[1].MeaningId, "'selten' is not strongly demoted.");
        Assert.AreEqual("m3", ranked[2].MeaningId, "'archaic' is demoted.");
    }

    [TestMethod]
    public void Rank_PreservesOriginalProviderOrderForEqualTiers()
    {
        var m1 = CreateMeaning("m1", "First modern");
        var m2 = CreateMeaning("m2", "Second modern", "computing");
        var m3 = CreateMeaning("m3", "Third modern");

        var ranked = MeaningRelevancePolicy.Rank([m1, m2, m3]);

        Assert.AreEqual(3, ranked.Count);
        Assert.AreEqual("m1", ranked[0].MeaningId);
        Assert.AreEqual("m2", ranked[1].MeaningId);
        Assert.AreEqual("m3", ranked[2].MeaningId);
    }

    [TestMethod]
    public void Rank_PreservesOriginalProviderOrderWhenUsageLabelsAreEmptyOrNull()
    {
        var m1 = new LexicalMeaning("m1", null, "Def 1", null, null, []);
        var m2 = new LexicalMeaning("m2", null, "Def 2", null, null, null!);
        var m3 = new LexicalMeaning("m3", null, "Def 3", null, null, ["   "]);

        var ranked = MeaningRelevancePolicy.Rank([m1, m2, m3]);

        Assert.AreEqual(3, ranked.Count);
        Assert.AreEqual("m1", ranked[0].MeaningId);
        Assert.AreEqual("m2", ranked[1].MeaningId);
        Assert.AreEqual("m3", ranked[2].MeaningId);
    }

    [TestMethod]
    public void RankIndices_ReturnsOriginalZeroBasedIndicesInRankedOrder()
    {
        var m0 = CreateMeaning("m0", "Obsolete", "obsolete");
        var m1 = CreateMeaning("m1", "Modern 1");
        var m2 = CreateMeaning("m2", "Archaic", "archaic");
        var m3 = CreateMeaning("m3", "Modern 2");

        var rankedIndices = MeaningRelevancePolicy.RankIndices([m0, m1, m2, m3]);

        CollectionAssert.AreEqual(new[] { 1, 3, 0, 2 }, rankedIndices.ToArray());
    }

    [TestMethod]
    public void Classify_ReturnsExpectedTierAndTriggeringLabel()
    {
        var obsolete = CreateMeaning("m1", "Def", "obsolete");
        var modern = CreateMeaning("m2", "Def", "botany");

        var c1 = MeaningRelevancePolicy.Classify(obsolete);
        var c2 = MeaningRelevancePolicy.Classify(modern);

        Assert.AreEqual(MeaningRelevanceTier.Demoted, c1.Tier);
        Assert.AreEqual("obsolete", c1.TriggeringLabel);

        Assert.AreEqual(MeaningRelevanceTier.Standard, c2.Tier);
        Assert.IsNull(c2.TriggeringLabel);
    }
}
