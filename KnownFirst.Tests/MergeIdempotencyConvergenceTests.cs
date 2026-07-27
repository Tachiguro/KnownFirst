using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

/// <summary>
/// Executable proofs for design §11's six idempotency/convergence scenarios, plus the general
/// commutativity/preservation properties §11 relies on, using MergeFixtureSet's pure set utilities
/// over identity keys produced by the merge-contract policies. No database is involved.
/// </summary>
[TestClass]
public sealed class MergeIdempotencyConvergenceTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BackupSourceMaterial CreateMaterial(
        string id,
        string contentSha256,
        string title = "Untitled",
        BackupLexicalLookupMode lookupMode = BackupLexicalLookupMode.Translation,
        string? targetLanguage = "de") =>
        new(id, title, "en", "de", lookupMode, targetLanguage, "text-" + id, contentSha256, BaseTime, 1, [], []);

    private static string MaterialKey(BackupSourceMaterial material) => SourceMaterialIdentityPolicy.Compute(material).Value;

    private static void AssertSameIdentitySet(
        IReadOnlyCollection<BackupSourceMaterial> left, IReadOnlyCollection<BackupSourceMaterial> right)
    {
        var leftKeys = new HashSet<string>(left.Select(MaterialKey), StringComparer.Ordinal);
        var rightKeys = new HashSet<string>(right.Select(MaterialKey), StringComparer.Ordinal);
        Assert.IsTrue(leftKeys.SetEquals(rightKeys));
    }

    /// <summary>
    /// Stronger than <see cref="AssertSameIdentitySet"/>: DeduplicatedUnion's contract is a
    /// deterministic ordinal sort by identity key, so two convergent results must match not just as an
    /// unordered set but as the exact same ordered sequence — this is what "byte-for-byte identical
    /// canonical sorted output" means concretely for a list of identity-key strings.
    /// </summary>
    private static void AssertSameOrderedIdentitySequence(
        IReadOnlyList<BackupSourceMaterial> left, IReadOnlyList<BackupSourceMaterial> right)
    {
        var leftKeys = left.Select(MaterialKey).ToList();
        var rightKeys = right.Select(MaterialKey).ToList();
        CollectionAssert.AreEqual(leftKeys, rightKeys);
    }

    // --- §11 Scenario 1: Merge A applied twice ---

    [TestMethod]
    public void Scenario1_MergeAAppliedTwice_SecondMergeIsAllExactDuplicates()
    {
        var archiveA = new[] { CreateMaterial("a1", "hash-1"), CreateMaterial("a2", "hash-2") };

        var afterFirstMerge = MergeFixtureSet.DeduplicatedUnion(
            Array.Empty<BackupSourceMaterial>(), archiveA, MaterialKey);
        // Independently hand-computed expectation: two archive-only items with distinct content
        // hashes, merged into an empty target, must produce exactly 2 surviving items — this is not
        // derived from a second call to the production helper.
        Assert.AreEqual(2, afterFirstMerge.Count);

        var classificationsOnFirstMerge = MergeFixtureSet.Classify(
            Array.Empty<BackupSourceMaterial>(), archiveA, MaterialKey);
        Assert.IsTrue(classificationsOnFirstMerge.All(c => c.Classification == FixtureRecordClassification.New));

        var afterSecondMerge = MergeFixtureSet.DeduplicatedUnion(afterFirstMerge, archiveA, MaterialKey);
        var classificationsOnSecondMerge = MergeFixtureSet.Classify(afterFirstMerge, archiveA, MaterialKey);

        Assert.IsTrue(classificationsOnSecondMerge.All(c => c.Classification == FixtureRecordClassification.ExactDuplicate));
        AssertSameOrderedIdentitySequence(afterFirstMerge, afterSecondMerge);
        Assert.AreEqual(2, afterSecondMerge.Count);
    }

    // --- §11 Scenario 2: Merge A into B, then merge A again ---

    [TestMethod]
    public void Scenario2_MergeAIntoB_ThenMergeAAgain_IsNoOp()
    {
        var targetB = new[] { CreateMaterial("b1", "hash-b1") };
        var archiveA = new[] { CreateMaterial("a1", "hash-a1"), CreateMaterial("a2", "hash-a2") };

        var afterFirstMerge = MergeFixtureSet.DeduplicatedUnion(targetB, archiveA, MaterialKey);
        Assert.AreEqual(3, afterFirstMerge.Count);

        var afterSecondMerge = MergeFixtureSet.DeduplicatedUnion(afterFirstMerge, archiveA, MaterialKey);

        AssertSameIdentitySet(afterFirstMerge, afterSecondMerge);
        var classifications = MergeFixtureSet.Classify(afterFirstMerge, archiveA, MaterialKey);
        Assert.IsTrue(classifications.All(c => c.Classification == FixtureRecordClassification.ExactDuplicate));
    }

    // --- §11 Scenario 3: Two exports derived from the same older baseline ---

    [TestMethod]
    public void Scenario3_TwoExportsFromSharedBaseline_ConvergeWithoutDuplicatingBaseline()
    {
        var baseline = CreateMaterial("baseline", "hash-baseline");
        var exportB1 = new[] { baseline, CreateMaterial("b1-only", "hash-b1-only") };
        var exportB2 = new[] { baseline, CreateMaterial("b2-only", "hash-b2-only") };

        var mergedForward = MergeFixtureSet.DeduplicatedUnion(exportB1, exportB2, MaterialKey);
        var mergedBackward = MergeFixtureSet.DeduplicatedUnion(exportB2, exportB1, MaterialKey);

        Assert.AreEqual(3, mergedForward.Count, "Baseline must not be double-counted.");
        AssertSameIdentitySet(mergedForward, mergedBackward);
    }

    // --- §11 Scenario 4: Duplicate review events ---

    [TestMethod]
    public void Scenario4_DuplicateReviewEvents_CollapseAcrossArchivesAndWithinOneArchive()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");
        var map = new Dictionary<string, LearningCardMatchIdentity> { ["card-1"] = cardIdentity };

        // Two separately-constructed object instances with identical field values, not the same
        // reference reused twice — proves the collapse is content-based (via the fingerprint policy
        // reading fields), not an artifact of reference/object identity.
        static BackupLearningReview CreateReview() =>
            new("card-1", "session-1", BackupReviewRating.Good, true, true, BaseTime, BaseTime.AddDays(3), 3, 2.5);

        string Key(BackupLearningReview r) => LearningReviewFingerprintPolicy.Compute(r, map).Value;

        // Same real-world event appearing twice within a single archive's export.
        var oneArchiveWithDuplicate = new[] { CreateReview(), CreateReview() };
        var dedupedWithinArchive = MergeFixtureSet.DeduplicatedUnion(
            Array.Empty<BackupLearningReview>(), oneArchiveWithDuplicate, Key);
        Assert.AreEqual(1, dedupedWithinArchive.Count, "Independently constructed, content-identical events must still collapse to one.");

        // Same real-world event re-appearing when merging archive A (already applied) with archive B.
        var target = new[] { CreateReview() };
        var archiveB = new[] { CreateReview() };
        var merged = MergeFixtureSet.DeduplicatedUnion(target, archiveB, Key);
        Assert.AreEqual(1, merged.Count);
    }

    [TestMethod]
    public void SameTimestampDifferentReviewEvents_BothSurviveAUnion()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");
        var map = new Dictionary<string, LearningCardMatchIdentity> { ["card-1"] = cardIdentity };
        string Key(BackupLearningReview r) => LearningReviewFingerprintPolicy.Compute(r, map).Value;

        var goodAtNine = new BackupLearningReview("card-1", "session-1", BackupReviewRating.Good, true, true, BaseTime, BaseTime.AddDays(3), 3, 2.5);
        var hardAtNine = new BackupLearningReview("card-1", "session-1", BackupReviewRating.Hard, true, false, BaseTime, BaseTime.AddDays(1), 1, 2.0);

        var merged = MergeFixtureSet.DeduplicatedUnion(Array.Empty<BackupLearningReview>(), [goodAtNine, hardAtNine], Key);

        Assert.AreEqual(2, merged.Count, "Two textually different events sharing a ReviewedAtUtc must both survive, not collapse.");
    }

    // --- §11 Scenario 5: Duplicate documents with different titles ---

    [TestMethod]
    public void Scenario5_DuplicateDocumentsWithDifferentTitles_CollapseToOne_KeepingTargetsTitle()
    {
        var target = new[] { CreateMaterial("target-doc", "shared-hash", title: "Target's Title") };
        var archive = new[] { CreateMaterial("archive-doc", "shared-hash", title: "Archive's Title") };

        var classifications = MergeFixtureSet.Classify(target, archive, MaterialKey);
        Assert.AreEqual(FixtureRecordClassification.ExactDuplicate, classifications.Single().Classification);

        var merged = MergeFixtureSet.DeduplicatedUnion(target, archive, MaterialKey);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("Target's Title", merged.Single().Title, "Existing target content must never be silently replaced.");
    }

    // --- §11 Scenario 6: Same text with different lookup modes ---

    [TestMethod]
    public void Scenario6_SameTextDifferentLookupModes_BothPreserved()
    {
        var target = new[] { CreateMaterial("def-doc", "shared-hash", lookupMode: BackupLexicalLookupMode.Definition, targetLanguage: null) };
        var archive = new[] { CreateMaterial("trans-doc", "shared-hash", lookupMode: BackupLexicalLookupMode.Translation, targetLanguage: "de") };

        var classifications = MergeFixtureSet.Classify(target, archive, MaterialKey);
        Assert.AreEqual(FixtureRecordClassification.New, classifications.Single().Classification);

        var merged = MergeFixtureSet.DeduplicatedUnion(target, archive, MaterialKey);
        Assert.AreEqual(2, merged.Count);
    }

    // --- General commutativity / preservation proofs ---

    [TestMethod]
    public void Union_IsCommutative_ForDisjointOrIdenticalEntitySets()
    {
        var shared = CreateMaterial("shared", "shared-hash");
        var setA = new[] { shared, CreateMaterial("a-only", "hash-a") };
        var setB = new[] { shared, CreateMaterial("b-only", "hash-b") };

        var aThenB = MergeFixtureSet.DeduplicatedUnion(setA, setB, MaterialKey);
        var bThenA = MergeFixtureSet.DeduplicatedUnion(setB, setA, MaterialKey);

        // Independently hand-computed: exactly 3 distinct-hash materials (shared, a-only, b-only).
        Assert.AreEqual(3, aThenB.Count);
        // The deterministic ordinal identity-key sort means commutativity here is a stronger claim
        // than "same set" — it must be the exact same ordered sequence regardless of argument order.
        AssertSameOrderedIdentitySequence(aThenB, bThenA);
    }

    [TestMethod]
    public void RepeatedDeduplication_DoesNotChangeOutput()
    {
        var items = new[] { CreateMaterial("x1", "hash-x1"), CreateMaterial("x2", "hash-x2") };

        var once = MergeFixtureSet.DeduplicatedUnion(Array.Empty<BackupSourceMaterial>(), items, MaterialKey);
        Assert.AreEqual(2, once.Count, "Independently hand-computed: 2 distinct-hash items merged into an empty target.");

        var twice = MergeFixtureSet.DeduplicatedUnion(once, items, MaterialKey);
        var thrice = MergeFixtureSet.DeduplicatedUnion(twice, items, MaterialKey);

        // Byte-for-byte identical canonical sorted output across repeated applications, not just the
        // same set: the exact ordered sequence of identity keys must be unchanged.
        AssertSameOrderedIdentitySequence(once, twice);
        AssertSameOrderedIdentitySequence(twice, thrice);
    }

    [TestMethod]
    public void TargetOnlyRecords_SurviveMerge()
    {
        var targetOnly = CreateMaterial("target-only", "hash-target-only");
        var target = new[] { targetOnly };
        var archive = new[] { CreateMaterial("archive-item", "hash-archive-item") };

        var merged = MergeFixtureSet.DeduplicatedUnion(target, archive, MaterialKey);

        Assert.IsTrue(merged.Any(m => m.Id == "target-only"));
    }

    [TestMethod]
    public void ArchiveOnlyRecords_SurviveMerge()
    {
        var target = new[] { CreateMaterial("target-item", "hash-target-item") };
        var archiveOnly = CreateMaterial("archive-only", "hash-archive-only");
        var archive = new[] { archiveOnly };

        var merged = MergeFixtureSet.DeduplicatedUnion(target, archive, MaterialKey);

        Assert.IsTrue(merged.Any(m => m.Id == "archive-only"));
    }

    [TestMethod]
    public void NoDistinctNonEmptyMeaningVariant_IsLost()
    {
        var vocabularyIdentity = new VocabularyIdentity("VOCAB-HASH");
        var sourceReference = new BackupSourceReference("Wiktionary", "English Wiktionary", "security", 1, "CC BY-SA");

        BackupPreparedItem CreatePrepared(string id, string? definition, string? translation) => new(
            id, "vocabulary-1", "en", "de", "security", null, null, BackupTokenKind.Word, null, null,
            translation, definition, null, null, null, [], true, sourceReference, BaseTime, BaseTime, BaseTime, []);

        var germanDefinition = CreatePrepared("p1", "Schutz vor Gefahr.", null);
        var russianTranslation = CreatePrepared("p2", null, "Bezopasnost");
        var target = new[] { germanDefinition };
        var archive = new[] { russianTranslation };

        string Key(BackupPreparedItem item) => MeaningIdentityPolicy.Compute(item, vocabularyIdentity).Value;

        var merged = MergeFixtureSet.DeduplicatedUnion(target, archive, Key);

        Assert.AreEqual(2, merged.Count, "Both the target's existing Meaning and the archive's distinct Meaning must survive.");
        Assert.IsTrue(merged.Any(m => m.Definition == "Schutz vor Gefahr."));
        Assert.IsTrue(merged.Any(m => m.Translation == "Bezopasnost"));
    }

    [TestMethod]
    public void Classify_IsIdempotent_RepeatedCallsProduceSameResult()
    {
        var target = new[] { CreateMaterial("t1", "hash-t1") };
        var archive = new[] { CreateMaterial("a1", "hash-a1") };

        var first = MergeFixtureSet.Classify(target, archive, MaterialKey);
        var second = MergeFixtureSet.Classify(target, archive, MaterialKey);

        Assert.AreEqual(first.Single().Classification, second.Single().Classification);
    }
}
