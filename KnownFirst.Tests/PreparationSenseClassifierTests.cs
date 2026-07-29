using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3: the three-valued Sense classifier (Equal/Conflict/Unknown), reusing the exact
/// same discriminator policy the dormant Schema 7→8 migration already uses for grouping.
/// </summary>
[TestClass]
public sealed class PreparationSenseClassifierTests
{
    [TestMethod]
    public void Classify_SameProviderSenseId_ReturnsEqual()
    {
        var candidate = Facts(providerSenseId: "wikt-financial-institution");
        var existing = Facts(providerSenseId: "wikt-financial-institution");

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Equal, outcome);
    }

    [TestMethod]
    public void Classify_DifferentProviderSenseId_ReturnsConflict()
    {
        var candidate = Facts(providerSenseId: "wikt-financial-institution");
        var existing = Facts(providerSenseId: "wikt-river-edge");

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Conflict, outcome);
    }

    [TestMethod]
    public void Classify_CandidateHasNoDiscriminator_ReturnsUnknown()
    {
        var candidate = Facts();
        var existing = Facts(providerSenseId: "wikt-financial-institution");

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Unknown, outcome);
    }

    [TestMethod]
    public void Classify_ExistingHasNoDiscriminator_ReturnsUnknown()
    {
        var candidate = Facts(providerSenseId: "wikt-financial-institution");
        var existing = Facts();

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Unknown, outcome);
    }

    [TestMethod]
    public void Classify_NeitherSideHasDiscriminator_ReturnsUnknown()
    {
        var candidate = Facts();
        var existing = Facts();

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Unknown, outcome);
    }

    [TestMethod]
    public void Classify_SameGrammaticalRelationshipOnly_ReturnsEqual()
    {
        var candidate = Facts(grammaticalRelationship: "plural of bank");
        var existing = Facts(grammaticalRelationship: "plural of bank");

        var outcome = PreparationSenseClassifier.Classify("en", "W:banks", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Equal, outcome);
    }

    [TestMethod]
    public void Classify_DifferentTopicOrDomain_ReturnsConflict()
    {
        var candidate = Facts(topicOrDomain: "finance");
        var existing = Facts(topicOrDomain: "geography");

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Conflict, outcome);
    }

    [TestMethod]
    public void Classify_SameProviderSenseIdButDifferentExplanationLanguage_ReturnsConflict()
    {
        var candidate = Facts(providerSenseId: "wikt-financial-institution", explanationLanguage: "de");
        var existing = Facts(providerSenseId: "wikt-financial-institution", explanationLanguage: "ru");

        var outcome = PreparationSenseClassifier.Classify("en", "W:bank", candidate, existing);

        Assert.AreEqual(SenseMatchOutcome.Conflict, outcome);
    }

    [TestMethod]
    public void ClassifyAgainstExisting_NoExistingSenses_ReturnsUnknown()
    {
        var (match, outcome) = PreparationSenseClassifier.ClassifyAgainstExisting(
            "en", "W:bank", Facts(providerSenseId: "wikt-financial-institution"), []);

        Assert.IsNull(match);
        Assert.AreEqual(SenseMatchOutcome.Unknown, outcome);
    }

    [TestMethod]
    public void ClassifyAgainstExisting_OneMatchAmongSeveral_ReturnsThatSense()
    {
        var financial = SenseRow("wikt-financial-institution");
        var river = SenseRow("wikt-river-edge");
        var candidate = Facts(providerSenseId: "wikt-river-edge");

        var (match, outcome) = PreparationSenseClassifier.ClassifyAgainstExisting(
            "en", "W:bank", candidate, [financial, river]);

        Assert.AreEqual(SenseMatchOutcome.Equal, outcome);
        Assert.AreEqual(river.Id, match!.Id);
    }

    [TestMethod]
    public void ClassifyAgainstExisting_AllDiscriminatedButNoneMatch_ReturnsConflict()
    {
        var financial = SenseRow("wikt-financial-institution");
        var river = SenseRow("wikt-river-edge");
        var candidate = Facts(providerSenseId: "wikt-blood-bank");

        var (match, outcome) = PreparationSenseClassifier.ClassifyAgainstExisting(
            "en", "W:bank", candidate, [financial, river]);

        Assert.IsNull(match);
        Assert.AreEqual(SenseMatchOutcome.Conflict, outcome);
    }

    [TestMethod]
    public void ClassifyAgainstExisting_OneUndiscriminatedExistingAndNoMatch_ReturnsUnknown()
    {
        var financial = SenseRow("wikt-financial-institution");
        var undiscriminated = SenseRow(providerSenseId: "");
        var candidate = Facts(providerSenseId: "wikt-blood-bank");

        var (match, outcome) = PreparationSenseClassifier.ClassifyAgainstExisting(
            "en", "W:bank", candidate, [financial, undiscriminated]);

        Assert.IsNull(match);
        Assert.AreEqual(SenseMatchOutcome.Unknown, outcome);
    }

    private static SenseDiscriminatorFacts Facts(
        string sourceLanguage = "en",
        string explanationLanguage = "de",
        string providerSenseId = "",
        string topicOrDomain = "",
        string grammaticalRelationship = "",
        string acronymExpansion = "") => new(
        sourceLanguage, explanationLanguage, providerSenseId, topicOrDomain, grammaticalRelationship, acronymExpansion);

    private static int _nextSenseId = 1;

    private static SenseRow SenseRow(string providerSenseId) => new()
    {
        Id = _nextSenseId++,
        StableId = Guid.NewGuid().ToString("N"),
        WordId = 1,
        SourceLanguage = "en",
        ExplanationLanguage = "de",
        ProviderSenseId = providerSenseId,
        TopicOrDomain = string.Empty,
        PartOfSpeech = string.Empty,
        GrammaticalRelationship = string.Empty,
        AcronymExpansion = string.Empty,
        Status = SenseStatus.Prepared,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
