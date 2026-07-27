using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeVocabularyAndMeaningIdentityTests
{
    private static BackupVocabularyItem CreateVocabulary(
        string id = "vocabulary-1",
        string language = "en",
        string identityKey = "W:security") =>
        new(
            id,
            language,
            "security",
            identityKey,
            BackupTokenKind.Word,
            BackupKnowledgeState.Learning,
            BackupPreparationState.Prepared,
            1,
            1,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            [],
            new BackupAutomaticLearningState(BackupLearningInteractionMode.Typing, 0, 0, 0, false),
            []);

    private static BackupSourceReference CreateSourceReference(
        string providerName = "Wiktionary",
        string sourceProject = "English Wiktionary",
        string pageTitle = "security",
        long? revisionId = 111) =>
        new(providerName, sourceProject, pageTitle, revisionId, "CC BY-SA");

    private static BackupPreparedItem CreatePreparedItem(
        string id = "prepared-1",
        string vocabularyId = "vocabulary-1",
        string sourceLanguage = "en",
        string explanationLanguage = "de",
        string? definition = "Protection from danger.",
        string? translation = null,
        BackupSourceReference? source = null) =>
        new(
            id,
            vocabularyId,
            sourceLanguage,
            explanationLanguage,
            "security",
            "Security",
            null,
            BackupTokenKind.Word,
            "meaning-1",
            null,
            translation,
            definition,
            null,
            null,
            null,
            [],
            true,
            source ?? CreateSourceReference(),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            []);

    // --- Vocabulary identity ---

    [TestMethod]
    public void SameLanguageAndIdentityKey_ProducesSameVocabularyIdentity()
    {
        var v1 = CreateVocabulary(id: "a");
        var v2 = CreateVocabulary(id: "b");

        Assert.AreEqual(VocabularyMergeIdentityPolicy.Compute(v1), VocabularyMergeIdentityPolicy.Compute(v2));
    }

    [TestMethod]
    public void DifferentIdentityKey_ProducesDifferentVocabularyIdentity()
    {
        var security = CreateVocabulary(identityKey: "W:security");
        var safety = CreateVocabulary(identityKey: "W:safety");

        Assert.AreNotEqual(VocabularyMergeIdentityPolicy.Compute(security), VocabularyMergeIdentityPolicy.Compute(safety));
    }

    [TestMethod]
    public void DifferentLanguage_SameIdentityKey_ProducesDifferentVocabularyIdentity()
    {
        var english = CreateVocabulary(language: "en", identityKey: "W:gift");
        var german = CreateVocabulary(language: "de", identityKey: "W:gift");

        Assert.AreNotEqual(VocabularyMergeIdentityPolicy.Compute(english), VocabularyMergeIdentityPolicy.Compute(german));
    }

    [TestMethod]
    public void LanguageCase_DoesNotAffectVocabularyIdentity()
    {
        var lower = CreateVocabulary(language: "en");
        var upper = CreateVocabulary(language: "EN");

        Assert.AreEqual(VocabularyMergeIdentityPolicy.Compute(lower), VocabularyMergeIdentityPolicy.Compute(upper));
    }

    // --- Meaning identity ---

    [TestMethod]
    public void IdenticalMeanings_Deduplicate()
    {
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary());
        var meaning1 = CreatePreparedItem(id: "a");
        var meaning2 = CreatePreparedItem(id: "b");

        Assert.AreEqual(
            MeaningIdentityPolicy.Compute(meaning1, vocabularyIdentity),
            MeaningIdentityPolicy.Compute(meaning2, vocabularyIdentity));
    }

    [TestMethod]
    public void DistinctNonEmptyDefinitions_RemainDistinct()
    {
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary());
        var definitionA = CreatePreparedItem(definition: "Definition A");
        var definitionB = CreatePreparedItem(definition: "Definition B");

        Assert.AreNotEqual(
            MeaningIdentityPolicy.Compute(definitionA, vocabularyIdentity),
            MeaningIdentityPolicy.Compute(definitionB, vocabularyIdentity));
    }

    [TestMethod]
    public void DistinctNonEmptyTranslations_RemainDistinct()
    {
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary());
        var translationA = CreatePreparedItem(definition: null, translation: "Sicherheit");
        var translationB = CreatePreparedItem(definition: null, translation: "Absicherung");

        Assert.AreNotEqual(
            MeaningIdentityPolicy.Compute(translationA, vocabularyIdentity),
            MeaningIdentityPolicy.Compute(translationB, vocabularyIdentity));
    }

    [TestMethod]
    public void ProviderRevisionDifference_AffectsMeaningIdentity()
    {
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary());
        var revision1 = CreatePreparedItem(source: CreateSourceReference(revisionId: 111));
        var revision2 = CreatePreparedItem(source: CreateSourceReference(revisionId: 222));

        Assert.AreNotEqual(
            MeaningIdentityPolicy.Compute(revision1, vocabularyIdentity),
            MeaningIdentityPolicy.Compute(revision2, vocabularyIdentity));
    }

    [TestMethod]
    public void NullAndEmptyOptionalDefinition_AreTreatedAsEquivalent()
    {
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary());
        var nullDefinition = CreatePreparedItem(definition: null);
        var emptyDefinition = CreatePreparedItem(definition: string.Empty);
        var whitespaceDefinition = CreatePreparedItem(definition: "   ");

        var nullIdentity = MeaningIdentityPolicy.Compute(nullDefinition, vocabularyIdentity);
        var emptyIdentity = MeaningIdentityPolicy.Compute(emptyDefinition, vocabularyIdentity);
        var whitespaceIdentity = MeaningIdentityPolicy.Compute(whitespaceDefinition, vocabularyIdentity);

        Assert.AreEqual(nullIdentity, emptyIdentity);
        Assert.AreEqual(nullIdentity, whitespaceIdentity);
    }

    [TestMethod]
    public void DifferentVocabulary_ProducesDifferentMeaningIdentityEvenWithIdenticalContent()
    {
        var securityIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary(identityKey: "W:security"));
        var safetyIdentity = VocabularyMergeIdentityPolicy.Compute(CreateVocabulary(identityKey: "W:safety"));
        var preparedItem = CreatePreparedItem();

        Assert.AreNotEqual(
            MeaningIdentityPolicy.Compute(preparedItem, securityIdentity),
            MeaningIdentityPolicy.Compute(preparedItem, safetyIdentity));
    }

    [TestMethod]
    public void ArchiveIdMapOverload_ThrowsWhenVocabularyIdUnmapped()
    {
        var preparedItem = CreatePreparedItem(vocabularyId: "unmapped-vocabulary");
        var map = new Dictionary<string, VocabularyIdentity>();

        Assert.ThrowsExactly<KeyNotFoundException>(() => MeaningIdentityPolicy.Compute(preparedItem, map));
    }

    [TestMethod]
    public void ArchiveIdMapOverload_ResolvesThroughSuppliedMap()
    {
        var vocabulary = CreateVocabulary(id: "vocabulary-1");
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(vocabulary);
        var map = new Dictionary<string, VocabularyIdentity> { ["vocabulary-1"] = vocabularyIdentity };
        var preparedItem = CreatePreparedItem(vocabularyId: "vocabulary-1");

        var viaMap = MeaningIdentityPolicy.Compute(preparedItem, map);
        var direct = MeaningIdentityPolicy.Compute(preparedItem, vocabularyIdentity);

        Assert.AreEqual(direct, viaMap);
    }
}
