using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeLearningIdentityAndEventTests
{
    private static readonly DateTime ReviewedAt = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DueAt = new(2026, 1, 8, 8, 0, 0, DateTimeKind.Utc);

    // --- LearningCard matching identity ---

    [TestMethod]
    public void CardMatchIdentity_UsesVocabularyIdentityPlusDirection()
    {
        var vocabularyIdentity = new VocabularyIdentity("VOCAB-HASH");

        var termToMeaning1 = LearningCardIdentityPolicy.ComputeMatchIdentity(vocabularyIdentity, BackupCardDirection.TermToMeaning);
        var termToMeaning2 = LearningCardIdentityPolicy.ComputeMatchIdentity(vocabularyIdentity, BackupCardDirection.TermToMeaning);
        var meaningToTerm = LearningCardIdentityPolicy.ComputeMatchIdentity(vocabularyIdentity, BackupCardDirection.MeaningToTerm);

        Assert.AreEqual(termToMeaning1, termToMeaning2);
        Assert.AreNotEqual(termToMeaning1, meaningToTerm);
    }

    [TestMethod]
    public void CardMatchIdentity_DifferentVocabulary_SameDirection_IsDistinct()
    {
        var identity1 = LearningCardIdentityPolicy.ComputeMatchIdentity(new VocabularyIdentity("A"), BackupCardDirection.TermToMeaning);
        var identity2 = LearningCardIdentityPolicy.ComputeMatchIdentity(new VocabularyIdentity("B"), BackupCardDirection.TermToMeaning);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void CardMatchIdentity_LocalArchiveCardId_DoesNotAffectIdentity()
    {
        var vocabularyIdentity = new VocabularyIdentity("VOCAB-HASH");
        var card1 = CreateCard(id: "card-alpha", vocabularyId: "vocabulary-1");
        var card2 = CreateCard(id: "card-beta", vocabularyId: "vocabulary-1");
        var map = new Dictionary<string, VocabularyIdentity> { ["vocabulary-1"] = vocabularyIdentity };

        Assert.AreEqual(
            LearningCardIdentityPolicy.ComputeMatchIdentity(card1, map),
            LearningCardIdentityPolicy.ComputeMatchIdentity(card2, map));
    }

    [TestMethod]
    public void CardMatchIdentity_UnmappedArchiveVocabularyId_Throws()
    {
        var card = CreateCard(vocabularyId: "unmapped");
        var map = new Dictionary<string, VocabularyIdentity>();

        Assert.ThrowsExactly<KeyNotFoundException>(() => LearningCardIdentityPolicy.ComputeMatchIdentity(card, map));
    }

    // --- LearningReview fingerprint ---

    [TestMethod]
    public void ReviewFingerprint_IsDeterministic()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var fingerprint1 = ComputeFingerprint(cardIdentity);
        var fingerprint2 = ComputeFingerprint(cardIdentity);

        Assert.AreEqual(fingerprint1, fingerprint2);
    }

    [TestMethod]
    public void ReviewFingerprint_LocalArchiveCardId_DoesNotAffectFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");
        var review1 = CreateReview(cardId: "card-alpha");
        var review2 = CreateReview(cardId: "card-beta");
        var map = new Dictionary<string, LearningCardMatchIdentity> { ["card-alpha"] = cardIdentity, ["card-beta"] = cardIdentity };

        Assert.AreEqual(
            LearningReviewFingerprintPolicy.Compute(review1, map),
            LearningReviewFingerprintPolicy.Compute(review2, map));
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingRating_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var good = ComputeFingerprint(cardIdentity, rating: BackupReviewRating.Good);
        var hard = ComputeFingerprint(cardIdentity, rating: BackupReviewRating.Hard);

        Assert.AreNotEqual(good, hard);
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingWasCorrect_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var correct = ComputeFingerprint(cardIdentity, wasCorrect: true);
        var incorrect = ComputeFingerprint(cardIdentity, wasCorrect: false);

        Assert.AreNotEqual(correct, incorrect);
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingWasTypedAnswer_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var typed = ComputeFingerprint(cardIdentity, wasTypedAnswer: true);
        var notTyped = ComputeFingerprint(cardIdentity, wasTypedAnswer: false);

        Assert.AreNotEqual(typed, notTyped);
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingReviewedAt_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var early = ComputeFingerprint(cardIdentity, reviewedAtUtc: ReviewedAt);
        var late = ComputeFingerprint(cardIdentity, reviewedAtUtc: ReviewedAt.AddMinutes(5));

        Assert.AreNotEqual(early, late);
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingDirection_ViaDifferentCardIdentity_ChangesFingerprint()
    {
        var termToMeaning = LearningCardIdentityPolicy.ComputeMatchIdentity(new VocabularyIdentity("V"), BackupCardDirection.TermToMeaning);
        var meaningToTerm = LearningCardIdentityPolicy.ComputeMatchIdentity(new VocabularyIdentity("V"), BackupCardDirection.MeaningToTerm);

        Assert.AreNotEqual(ComputeFingerprint(termToMeaning), ComputeFingerprint(meaningToTerm));
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingIntervalDays_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var interval1 = ComputeFingerprint(cardIdentity, intervalDays: 3);
        var interval2 = ComputeFingerprint(cardIdentity, intervalDays: 7);

        Assert.AreNotEqual(interval1, interval2);
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingDueDate_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var due1 = ComputeFingerprint(cardIdentity, dueAtUtc: DueAt);
        var due2 = ComputeFingerprint(cardIdentity, dueAtUtc: DueAt.AddDays(1));

        Assert.AreNotEqual(due1, due2);
    }

    [TestMethod]
    public void ReviewFingerprint_ChangingEaseFactor_ChangesFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var ease1 = ComputeFingerprint(cardIdentity, easeFactor: 2.5);
        var ease2 = ComputeFingerprint(cardIdentity, easeFactor: 2.6);

        Assert.AreNotEqual(ease1, ease2);
    }

    [TestMethod]
    public void TwoDistinctSameTimestampEvents_AreBothPreserved()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var goodAt900 = ComputeFingerprint(cardIdentity, reviewedAtUtc: ReviewedAt, rating: BackupReviewRating.Good);
        var hardAt900 = ComputeFingerprint(cardIdentity, reviewedAtUtc: ReviewedAt, rating: BackupReviewRating.Hard);

        Assert.AreNotEqual(goodAt900, hardAt900, "Two textually different events sharing a timestamp must remain distinguishable, not collapse to one.");
    }

    [TestMethod]
    public void ExactDuplicateEvents_CollapseToOneFingerprint()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");

        var event1 = ComputeFingerprint(cardIdentity);
        var event2 = ComputeFingerprint(cardIdentity);

        Assert.AreEqual(event1, event2);
    }

    private static BackupLearningCard CreateCard(string id = "card-1", string vocabularyId = "vocabulary-1") =>
        new(
            id,
            vocabularyId,
            "prepared-1",
            BackupCardDirection.TermToMeaning,
            BackupCardState.Review,
            DueAt,
            3,
            2.5,
            1,
            0,
            ReviewedAt,
            BackupReviewRating.Good,
            ReviewedAt.AddDays(-10),
            ReviewedAt);

    private static BackupLearningReview CreateReview(string cardId = "card-1") =>
        new(cardId, "learning-workflow-1", BackupReviewRating.Good, true, true, ReviewedAt, DueAt, 3, 2.5);

    private static LearningReviewFingerprint ComputeFingerprint(
        LearningCardMatchIdentity cardIdentity,
        DateTime? reviewedAtUtc = null,
        BackupReviewRating rating = BackupReviewRating.Good,
        bool wasTypedAnswer = true,
        bool wasCorrect = true,
        DateTime? dueAtUtc = null,
        int intervalDays = 3,
        double easeFactor = 2.5) =>
        LearningReviewFingerprintPolicy.Compute(
            cardIdentity,
            reviewedAtUtc ?? ReviewedAt,
            rating,
            wasTypedAnswer,
            wasCorrect,
            dueAtUtc ?? DueAt,
            intervalDays,
            easeFactor);
}
