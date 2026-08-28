using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6StateValidationTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ValidNewCard_Accepted()
    {
        var card = Fsrs6Card.New();

        Assert.AreEqual(Fsrs6CardState.New, card.State);
        Assert.IsNull(card.Stability);
        Assert.IsNull(card.Difficulty);
        Assert.IsNull(card.LastReviewedAtUtc);
        Assert.IsNull(card.StepIndex);
        Assert.IsNull(card.DueAtUtc);
    }

    [TestMethod]
    public void ValidLearningCard_Accepted()
    {
        var card = Fsrs6Card.Learning(
            stability: 2.5,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0);

        Assert.AreEqual(Fsrs6CardState.Learning, card.State);
        Assert.AreEqual(2.5, card.Stability);
        Assert.AreEqual(5.0, card.Difficulty);
        Assert.AreEqual(UtcNow, card.LastReviewedAtUtc);
        Assert.AreEqual(0, card.StepIndex);
    }

    [TestMethod]
    public void ValidReviewCard_Accepted()
    {
        var card = Fsrs6Card.Review(
            stability: 10.0,
            difficulty: 4.5,
            lastReviewedAtUtc: UtcNow);

        Assert.AreEqual(Fsrs6CardState.Review, card.State);
        Assert.AreEqual(10.0, card.Stability);
        Assert.AreEqual(4.5, card.Difficulty);
        Assert.AreEqual(UtcNow, card.LastReviewedAtUtc);
        Assert.IsNull(card.StepIndex);
    }

    [TestMethod]
    public void ValidRelearningCard_Accepted()
    {
        var card = Fsrs6Card.Relearning(
            stability: 1.5,
            difficulty: 6.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0);

        Assert.AreEqual(Fsrs6CardState.Relearning, card.State);
        Assert.AreEqual(1.5, card.Stability);
        Assert.AreEqual(6.0, card.Difficulty);
        Assert.AreEqual(UtcNow, card.LastReviewedAtUtc);
        Assert.AreEqual(0, card.StepIndex);
    }

    [TestMethod]
    public void NewCard_RejectsNonNullStability()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.New,
            stability: 1.0,
            difficulty: null,
            lastReviewedAtUtc: null,
            stepIndex: null));
    }

    [TestMethod]
    public void NewCard_RejectsNonNullDifficulty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.New,
            stability: null,
            difficulty: 5.0,
            lastReviewedAtUtc: null,
            stepIndex: null));
    }

    [TestMethod]
    public void NewCard_RejectsNonNullLastReviewedAtUtc()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.New,
            stability: null,
            difficulty: null,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));
    }

    [TestMethod]
    public void NewCard_RejectsNonNullStepIndex()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.New,
            stability: null,
            difficulty: null,
            lastReviewedAtUtc: null,
            stepIndex: 0));
    }

    [TestMethod]
    public void LearningCard_RejectsNullStability()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Learning,
            stability: null,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0));
    }

    [TestMethod]
    public void LearningCard_RejectsNullDifficulty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Learning,
            stability: 2.5,
            difficulty: null,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0));
    }

    [TestMethod]
    public void LearningCard_RejectsNullLastReviewedAtUtc()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Learning,
            stability: 2.5,
            difficulty: 5.0,
            lastReviewedAtUtc: null,
            stepIndex: 0));
    }

    [TestMethod]
    public void LearningCard_RejectsNullOrNonZeroStepIndex()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Learning,
            stability: 2.5,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Learning,
            stability: 2.5,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 1));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Learning,
            stability: 2.5,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: -1));
    }

    [TestMethod]
    public void ReviewCard_RejectsNullStability()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: null,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));
    }

    [TestMethod]
    public void ReviewCard_RejectsNullDifficulty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 5.0,
            difficulty: null,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));
    }

    [TestMethod]
    public void ReviewCard_RejectsNullLastReviewedAtUtc()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 5.0,
            difficulty: 5.0,
            lastReviewedAtUtc: null,
            stepIndex: null));
    }

    [TestMethod]
    public void ReviewCard_RejectsNonNullStepIndex()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 5.0,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0));
    }

    [TestMethod]
    public void RelearningCard_RejectsNullStability()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Relearning,
            stability: null,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0));
    }

    [TestMethod]
    public void RelearningCard_RejectsNullDifficulty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Relearning,
            stability: 2.0,
            difficulty: null,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 0));
    }

    [TestMethod]
    public void RelearningCard_RejectsNullLastReviewedAtUtc()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Relearning,
            stability: 2.0,
            difficulty: 5.0,
            lastReviewedAtUtc: null,
            stepIndex: 0));
    }

    [TestMethod]
    public void RelearningCard_RejectsNullOrNonZeroStepIndex()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Relearning,
            stability: 2.0,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Relearning,
            stability: 2.0,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: 1));
    }

    [TestMethod]
    public void Stability_RejectsBelowMinimumAndAcceptsBoundary()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 0.0009,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 0.0,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: -1.0,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        // Exact boundary 0.001 is accepted
        var card = new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 0.001,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null);
        Assert.AreEqual(0.001, card.Stability);
    }

    [TestMethod]
    public void Stability_RejectsNonFinite()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: double.NaN,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: double.PositiveInfinity,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: double.NegativeInfinity,
            difficulty: 5.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));
    }

    [TestMethod]
    public void Difficulty_RejectsBelowMinimumAndAboveMaximum()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: 0.99,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: 10.01,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        // Exact boundaries 1.0 and 10.0 are accepted
        var minCard = new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: 1.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null);
        Assert.AreEqual(1.0, minCard.Difficulty);

        var maxCard = new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: 10.0,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null);
        Assert.AreEqual(10.0, maxCard.Difficulty);
    }

    [TestMethod]
    public void Difficulty_RejectsNonFinite()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: double.NaN,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: double.PositiveInfinity,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));

        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: double.NegativeInfinity,
            lastReviewedAtUtc: UtcNow,
            stepIndex: null));
    }

    [TestMethod]
    public void Timestamp_RejectsNonUtcLastReviewedAt()
    {
        var nonUtcPositive = new DateTimeOffset(2026, 8, 28, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: 5.0,
            lastReviewedAtUtc: nonUtcPositive,
            stepIndex: null));

        var nonUtcNegative = new DateTimeOffset(2026, 8, 28, 7, 0, 0, TimeSpan.FromHours(-5));
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.Review,
            stability: 2.0,
            difficulty: 5.0,
            lastReviewedAtUtc: nonUtcNegative,
            stepIndex: null));
    }

    [TestMethod]
    public void Timestamp_RejectsNonUtcDueAtUtc()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 28, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Card(
            Fsrs6CardState.New,
            stability: null,
            difficulty: null,
            lastReviewedAtUtc: null,
            stepIndex: null,
            dueAtUtc: nonUtc));
    }

    [TestMethod]
    public void State_RejectsUndefinedEnumValue()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Card(
            (Fsrs6CardState)999,
            stability: null,
            difficulty: null,
            lastReviewedAtUtc: null,
            stepIndex: null));
    }

    [TestMethod]
    public void ReviewEvent_ValidUtcAndRating_Accepted()
    {
        var evt = new Fsrs6ReviewEvent(UtcNow, ReviewRating.Good);

        Assert.AreEqual(UtcNow, evt.ReviewedAtUtc);
        Assert.AreEqual(ReviewRating.Good, evt.Rating);
    }

    [TestMethod]
    public void ReviewEvent_RejectsNonUtcTimestamp()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 28, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6ReviewEvent(nonUtc, ReviewRating.Good));
    }

    [TestMethod]
    public void ReviewEvent_RejectsInvalidRating()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6ReviewEvent(UtcNow, (ReviewRating)999));
    }
}
