using KnownFirst.Core.Preparation;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MeaningPreviewPolicyTests
{
    private static LexicalMeaning CreateMeaning(
        string id,
        string? definition,
        string? translation = null,
        string? partOfSpeech = "noun",
        params string[] usageLabels) =>
        new(
            MeaningId: id,
            PartOfSpeech: partOfSpeech,
            Definition: definition,
            Translation: translation,
            Example: null,
            UsageLabels: usageLabels);

    [TestMethod]
    public void GetSelectableMeanings_RanksNeutralModernMeaningAheadOfArchaicMeaning_AndPreservesOriginalIndex()
    {
        // A. Basic integration & B. OriginalIndex integrity
        var archaic = CreateMeaning("m0", "Old historic sense", null, "noun", "archaic");
        var modern = CreateMeaning("m1", "Modern everyday sense", null, "noun");

        var result = MeaningPreviewPolicy.GetSelectableMeanings([archaic, modern], LexicalLookupMode.Definition);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Modern everyday sense", result[0].PrimaryText);
        Assert.AreEqual(1, result[0].OriginalIndex, "First returned entry must retain its original input index (1).");

        Assert.AreEqual("Old historic sense", result[1].PrimaryText);
        Assert.AreEqual(0, result[1].OriginalIndex, "Second returned entry must retain its original input index (0).");
    }

    [TestMethod]
    public void GetSelectableMeanings_PreservesAllEligibleMeaningsWithoutLoss()
    {
        // C. Lossless selectability
        var m0 = CreateMeaning("m0", "Def 0", null, "noun", "obsolete");
        var m1 = CreateMeaning("m1", "Def 1", null, "noun");
        var m2 = CreateMeaning("m2", "Def 2", null, "verb");

        var result = MeaningPreviewPolicy.GetSelectableMeanings([m0, m1, m2], LexicalLookupMode.Definition);

        Assert.AreEqual(3, result.Count);
        var returnedIndices = result.Select(r => r.OriginalIndex).ToList();
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, returnedIndices);
        Assert.AreEqual(1, result[0].OriginalIndex);
        Assert.AreEqual(2, result[1].OriginalIndex);
        Assert.AreEqual(0, result[2].OriginalIndex);
    }

    [TestMethod]
    public void GetSelectableMeanings_PreservesStableProviderOrderForMeaningsInSameRelevanceTier()
    {
        // D. Stable provider-order fallback
        var std0 = CreateMeaning("m0", "Standard A", null, "noun");
        var dem0 = CreateMeaning("m1", "Demoted A", null, "noun", "archaic");
        var std1 = CreateMeaning("m2", "Standard B", null, "verb");
        var dem1 = CreateMeaning("m3", "Demoted B", null, "noun", "veraltet");

        var result = MeaningPreviewPolicy.GetSelectableMeanings([std0, dem0, std1, dem1], LexicalLookupMode.Definition);

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(0, result[0].OriginalIndex, "First standard meaning retains relative provider order.");
        Assert.AreEqual(2, result[1].OriginalIndex, "Second standard meaning retains relative provider order.");
        Assert.AreEqual(1, result[2].OriginalIndex, "First demoted meaning retains relative provider order.");
        Assert.AreEqual(3, result[3].OriginalIndex, "Second demoted meaning retains relative provider order.");
    }

    [TestMethod]
    public void GetSelectableMeanings_TreatsRareSeltenAndUnknownLabelsAsNeutralStandardTier()
    {
        // E. Neutral labels
        var m0 = CreateMeaning("m0", "Rare sense", null, "noun", "rare");
        var m1 = CreateMeaning("m1", "Seltene Bedeutung", null, "noun", "selten");
        var m2 = CreateMeaning("m2", "Unknown domain sense", null, "noun", "custom-domain");
        var m3 = CreateMeaning("m3", "Archaic sense", null, "noun", "archaic");

        var result = MeaningPreviewPolicy.GetSelectableMeanings([m0, m1, m2, m3], LexicalLookupMode.Definition);

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(0, result[0].OriginalIndex, "'rare' remains neutral in initial provider order.");
        Assert.AreEqual(1, result[1].OriginalIndex, "'selten' remains neutral in initial provider order.");
        Assert.AreEqual(2, result[2].OriginalIndex, "unknown label remains neutral in initial provider order.");
        Assert.AreEqual(3, result[3].OriginalIndex, "'archaic' is demoted to tail.");
    }

    [TestMethod]
    public void GetSelectableMeanings_PreservesLookupModeTextSelectionAcrossTiers()
    {
        // F. Lookup-mode preservation
        var archaic = CreateMeaning("m0", "Def Archaic", "Trans Archaic", "noun", "archaic");
        var modern = CreateMeaning("m1", "Def Modern", "Trans Modern", "noun");

        // Definition mode
        var defResult = MeaningPreviewPolicy.GetSelectableMeanings([archaic, modern], LexicalLookupMode.Definition);
        Assert.AreEqual("Def Modern", defResult[0].PrimaryText);
        Assert.AreEqual("Def Archaic", defResult[1].PrimaryText);

        // Translation mode
        var transResult = MeaningPreviewPolicy.GetSelectableMeanings([archaic, modern], LexicalLookupMode.Translation);
        Assert.AreEqual("Trans Modern", transResult[0].PrimaryText);
        Assert.AreEqual("Trans Archaic", transResult[1].PrimaryText);

        // DefinitionAndTranslation mode
        var combinedResult = MeaningPreviewPolicy.GetSelectableMeanings([archaic, modern], LexicalLookupMode.DefinitionAndTranslation);
        Assert.AreEqual("Def Modern (Trans Modern)", combinedResult[0].PrimaryText);
        Assert.AreEqual("Def Archaic (Trans Archaic)", combinedResult[1].PrimaryText);

        // null mode defaults to combined semantics
        var nullResult = MeaningPreviewPolicy.GetSelectableMeanings([archaic, modern], null);
        Assert.AreEqual("Def Modern (Trans Modern)", nullResult[0].PrimaryText);
        Assert.AreEqual("Def Archaic (Trans Archaic)", nullResult[1].PrimaryText);
    }

    [TestMethod]
    public void GetSelectableMeanings_PreservesFirstOccurrenceDeduplicationSemanticsBeforeRanking()
    {
        // G. Deduplication preservation
        var m0 = CreateMeaning("m0", "Repeated text", null, "noun", "archaic");
        var m1 = CreateMeaning("m1", "Modern distinct text", null, "noun");
        var m2 = CreateMeaning("m2", "Repeated text", null, "noun", "archaic"); // exact signature duplicate of m0

        var result = MeaningPreviewPolicy.GetSelectableMeanings([m0, m1, m2], LexicalLookupMode.Definition);

        Assert.AreEqual(2, result.Count, "Duplicate signature must be deduplicated exactly once.");
        Assert.AreEqual("Modern distinct text", result[0].PrimaryText);
        Assert.AreEqual(1, result[0].OriginalIndex);

        Assert.AreEqual("Repeated text", result[1].PrimaryText);
        Assert.AreEqual(0, result[1].OriginalIndex, "Deduplication must keep first provider occurrence (0), not later occurrence (2).");
    }

    [TestMethod]
    public void GetSelectableMeanings_PreservesTruncationAndFullTextForReorderedMeanings()
    {
        // H. Preview behavior
        var longModern = new string('A', 300);
        var longArchaic = new string('B', 300);

        var archaic = CreateMeaning("m0", longArchaic, null, "noun", "archaic");
        var modern = CreateMeaning("m1", longModern, null, "noun");

        var result = MeaningPreviewPolicy.GetSelectableMeanings([archaic, modern], LexicalLookupMode.Definition);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].IsTruncated);
        Assert.AreEqual(longModern, result[0].FullText);
        Assert.IsTrue(result[0].PrimaryText.EndsWith('…'));
        Assert.AreEqual(1, result[0].OriginalIndex);

        Assert.IsTrue(result[1].IsTruncated);
        Assert.AreEqual(longArchaic, result[1].FullText);
        Assert.IsTrue(result[1].PrimaryText.EndsWith('…'));
        Assert.AreEqual(0, result[1].OriginalIndex);
    }

    [TestMethod]
    public void GetSelectableMeanings_SkipsEmptyOrNonDisplayableMeaningsRegardlessOfUsageLabels()
    {
        // I. Empty/non-displayable meanings
        var emptyArchaic = CreateMeaning("m0", "", "", "noun", "archaic");
        var whitespaceArchaic = CreateMeaning("m1", "   ", "   ", "noun", "archaic");
        var modern = CreateMeaning("m2", "Valid modern", null, "noun");

        var result = MeaningPreviewPolicy.GetSelectableMeanings([emptyArchaic, whitespaceArchaic, modern], LexicalLookupMode.Definition);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Valid modern", result[0].PrimaryText);
        Assert.AreEqual(2, result[0].OriginalIndex);
    }
}
