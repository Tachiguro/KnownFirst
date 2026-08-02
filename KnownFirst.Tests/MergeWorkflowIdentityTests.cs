using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeWorkflowIdentityTests
{
    private static readonly DateTime StartedAt = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    // --- ReviewSession / ReviewCandidate ---

    [TestMethod]
    public void ReviewSessionIdentity_EqualsAcrossArchives_WhenDocumentIdentityMatches()
    {
        var documentIdentity = new SourceMaterialIdentity("DOC-HASH");

        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(documentIdentity),
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(documentIdentity));
    }

    [TestMethod]
    public void ReviewSessionIdentity_ResolvesArchiveLocalSourceMaterialIdThroughMap()
    {
        var documentIdentity = new SourceMaterialIdentity("DOC-HASH");
        var map = new Dictionary<string, SourceMaterialIdentity> { ["source-1"] = documentIdentity };
        var workflow = new BackupVocabularyReviewWorkflow(
            "workflow-1", "source-1", BackupReviewSessionStatus.Completed, 1, 1, 1, 0, 0, 1,
            StartedAt, CompletedAt, []);

        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(documentIdentity),
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(workflow, map));
    }

    [TestMethod]
    public void ReviewCandidateIdentity_CombinesDocumentAndVocabularyIdentity()
    {
        var documentIdentity = new SourceMaterialIdentity("DOC-HASH");
        var vocabularyIdentity = new VocabularyIdentity("VOCAB-HASH");
        var otherVocabularyIdentity = new VocabularyIdentity("OTHER-VOCAB-HASH");

        var identity1 = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(documentIdentity, vocabularyIdentity);
        var identity2 = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(documentIdentity, vocabularyIdentity);
        var differentVocabulary = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(documentIdentity, otherVocabularyIdentity);

        Assert.AreEqual(identity1, identity2);
        Assert.AreNotEqual(identity1, differentVocabulary);
    }

    // --- PreparationSession / PreparationCandidate ---

    [TestMethod]
    public void PreparationSessionIdentity_SameStartCompletedAndItems_Deduplicates()
    {
        var items = new List<VocabularyIdentity> { new("A"), new("B") };

        var identity1 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);

        Assert.AreEqual(identity1, identity2);
    }

    [TestMethod]
    public void PreparationSessionIdentity_DifferentItemOrder_ProducesDifferentIdentity()
    {
        var forward = new List<VocabularyIdentity> { new("A"), new("B") };
        var reversed = new List<VocabularyIdentity> { new("B"), new("A") };

        var identity1 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, forward);
        var identity2 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, reversed);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void PreparationSessionIdentity_DifferentCompletedAt_ProducesDifferentIdentity()
    {
        var items = new List<VocabularyIdentity> { new("A") };

        var identity1 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt.AddMinutes(1), items);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void PreparationSessionIdentity_ResolvesItemsInOrderThroughMap()
    {
        var identityA = new VocabularyIdentity("A");
        var identityB = new VocabularyIdentity("B");
        var map = new Dictionary<string, VocabularyIdentity> { ["vocab-a"] = identityA, ["vocab-b"] = identityB };

        var workflow = new BackupPreparationWorkflow(
            "workflow-1", BackupPreparationSessionStatus.Completed, BackupPreparationMethod.Manual,
            2, 2, StartedAt, CompletedAt, CompletedAt,
            [
                new BackupPreparationItem("item-2", "vocab-b", 1, BackupPreparationCandidateStatus.Prepared, 0, null, 1, CompletedAt, null),
                new BackupPreparationItem("item-1", "vocab-a", 0, BackupPreparationCandidateStatus.Prepared, 0, null, 1, CompletedAt, null),
            ]);

        var expected = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, [identityA, identityB]);
        var actual = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(workflow, map);

        Assert.AreEqual(expected, actual, "Items must be ordered by their Order field, not archive declaration order.");
    }

    [TestMethod]
    public void PreparationCandidateIdentity_CascadesFromParentSession()
    {
        var sessionIdentity1 = new PreparationSessionIdentity("SESSION-A");
        var sessionIdentity2 = new PreparationSessionIdentity("SESSION-B");
        var vocabularyIdentity = new VocabularyIdentity("VOCAB");

        var underSessionA = PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(sessionIdentity1, vocabularyIdentity, 0);
        var underSessionB = PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(sessionIdentity2, vocabularyIdentity, 0);

        Assert.AreNotEqual(underSessionA, underSessionB, "Same vocabulary/order under a different parent session must be a distinct candidate identity.");
    }

    // --- LearningSession / LearningSessionCard ---

    [TestMethod]
    public void LearningSessionIdentity_SameQueueContent_Deduplicates()
    {
        var items = new List<(LearningCardMatchIdentity, BackupReviewRating?)>
        {
            (new LearningCardMatchIdentity("CARD-A"), BackupReviewRating.Good),
            (new LearningCardMatchIdentity("CARD-B"), null),
        };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);

        Assert.AreEqual(identity1, identity2);
    }

    [TestMethod]
    public void LearningSessionIdentity_DifferentRatingForSameCard_ProducesDifferentIdentity()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-A");

        var goodItems = new List<(LearningCardMatchIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Good) };
        var hardItems = new List<(LearningCardMatchIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Hard) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, goodItems);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, hardItems);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void LearningSessionCardIdentity_CascadesFromParentSession()
    {
        var sessionIdentity1 = new LearningSessionIdentity("SESSION-A");
        var sessionIdentity2 = new LearningSessionIdentity("SESSION-B");
        var cardIdentity = new LearningCardMatchIdentity("CARD");

        var underSessionA = LearningWorkflowIdentityPolicy.ComputeSessionCardIdentity(sessionIdentity1, cardIdentity, 0);
        var underSessionB = LearningWorkflowIdentityPolicy.ComputeSessionCardIdentity(sessionIdentity2, cardIdentity, 0);

        Assert.AreNotEqual(underSessionA, underSessionB);
    }

    [TestMethod]
    public void LearningSessionIdentity_LocalArchiveCardId_DoesNotAffectIdentity()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");
        var map = new Dictionary<string, LearningCardMatchIdentity> { ["card-alpha"] = cardIdentity, ["card-beta"] = cardIdentity };

        var workflow1 = new BackupLearningWorkflow(
            "workflow-1", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, StartedAt, CompletedAt, CompletedAt,
            [new BackupLearningQueueItem("queue-1", "card-alpha", 0, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt)]);

        var workflow2 = new BackupLearningWorkflow(
            "workflow-2", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, StartedAt, CompletedAt, CompletedAt,
            [new BackupLearningQueueItem("queue-1", "card-beta", 0, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt)]);

        Assert.AreEqual(
            LearningWorkflowIdentityPolicy.ComputeSessionIdentity(workflow1, map),
            LearningWorkflowIdentityPolicy.ComputeSessionIdentity(workflow2, map));
    }

    // --- Schema-8 LearningSession / LearningQueueItem (FutureCardIdentity-based, KF-MEANING-001 Slice 9) ---

    [TestMethod]
    public void Schema8SessionIdentity_SameTimestampsQueueAndRatings_Deduplicates()
    {
        var items = new List<(FutureCardIdentity, BackupReviewRating?)>
        {
            (new FutureCardIdentity("CARD-A"), BackupReviewRating.Good),
            (new FutureCardIdentity("CARD-B"), null),
        };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);

        Assert.AreEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_DifferentStartedAt_ProducesDifferentIdentity()
    {
        var items = new List<(FutureCardIdentity, BackupReviewRating?)> { (new FutureCardIdentity("CARD-A"), BackupReviewRating.Good) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt.AddMinutes(1), CompletedAt, items);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_DifferentCompletedAt_ProducesDifferentIdentity()
    {
        var items = new List<(FutureCardIdentity, BackupReviewRating?)> { (new FutureCardIdentity("CARD-A"), BackupReviewRating.Good) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, null, items);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_DifferentRatingForSameCard_ProducesDifferentIdentity()
    {
        var cardIdentity = new FutureCardIdentity("CARD-A");
        var goodItems = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Good) };
        var hardItems = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Hard) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, goodItems);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, hardItems);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_SameCardsDifferentRelativeOrder_ProducesDifferentIdentity()
    {
        var cardA = new FutureCardIdentity("CARD-A");
        var cardB = new FutureCardIdentity("CARD-B");
        var forward = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardA, null), (cardB, null) };
        var reversed = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardB, null), (cardA, null) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, forward);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, reversed);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8QueueItemIdentity_DifferentQueueOrder_ProducesDifferentIdentity()
    {
        var cardIdentity = new FutureCardIdentity("CARD");

        var atOrderZero = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-A", cardIdentity, 0);
        var atOrderOne = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-A", cardIdentity, 1);

        Assert.AreNotEqual(atOrderZero, atOrderOne);
    }

    [TestMethod]
    public void Schema8QueueItemIdentity_DifferentParentSession_ProducesDifferentIdentity()
    {
        var cardIdentity = new FutureCardIdentity("CARD");

        var underSessionA = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-A", cardIdentity, 0);
        var underSessionB = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-B", cardIdentity, 0);

        Assert.AreNotEqual(underSessionA, underSessionB);
    }

    [TestMethod]
    public void Schema8SessionIdentity_ResolvesQueueItemsInOrderThroughMap_NotDeclarationOrder()
    {
        var identityA = new FutureCardIdentity("CARD-A");
        var identityB = new FutureCardIdentity("CARD-B");
        var map = new Dictionary<string, FutureCardIdentity> { ["card-a"] = identityA, ["card-b"] = identityB };

        var workflow = new BackupLearningWorkflowV2(
            "session-1", BackupLearningSessionStatus.Completed, 2, 2, 0, 0, 2, 0, StartedAt, StartedAt, CompletedAt,
            [
                new BackupLearningQueueItemV2("qi-2", "card-b", 1, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt, null),
                new BackupLearningQueueItemV2("qi-1", "card-a", 0, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt, null),
            ]);

        var expected = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(
            StartedAt, CompletedAt, [(identityA, BackupReviewRating.Good), (identityB, BackupReviewRating.Good)]);
        var actual = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(workflow, map);

        Assert.AreEqual(expected, actual, "Queue items must be ordered by QueueOrder, not archive declaration order.");
    }
}
