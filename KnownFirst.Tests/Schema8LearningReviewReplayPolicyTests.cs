using System;
using System.Collections.Generic;
using System.Linq;
using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Services.Study;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema8LearningReviewReplayPolicyTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T3 = new(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private const int WordId = 10;
    private const int SenseId = 20;
    private const int Card40 = 40;
    private const int ReqVar = 70;
    private const int AccVar = 72;

    private static Schema8ReplayReviewEvent CreateEvent(
        int reviewId = 1, int cardId = Card40, ReviewRating rating = ReviewRating.Good,
        bool wasTypedAnswer = true, bool wasCorrect = true, DateTime? reviewedAtUtc = null,
        DateTime? dueAtUtc = null, int intervalDays = 1, double easeFactor = 2.5,
        int? targetVariantId = ReqVar, int? matchedVariantId = ReqVar)
    {
        return new Schema8ReplayReviewEvent(reviewId, cardId, rating, wasTypedAnswer, wasCorrect,
            reviewedAtUtc ?? T1, dueAtUtc ?? T2, intervalDays, easeFactor, targetVariantId, matchedVariantId);
    }

    private static Schema8CardRow CreateCard(int cardId = Card40, DateTime? createdAt = null) =>
        new() { Id = cardId, CreatedAtUtc = createdAt ?? T0 };

    private static Schema8AttributionCandidateRow CreateAssignment(
        int variantId = ReqVar, AnswerVariantRequirement req = AnswerVariantRequirement.Required, DateTime? bound = null) =>
        new() { AnswerVariantId = variantId, Requirement = req, RequiredSinceUtc = bound ?? T0 };

    private static void AssertProgressRowsAreEqual(AnswerVariantProgressRow expected, AnswerVariantProgressRow actual)
    {
        Assert.AreEqual(expected.CardId, actual.CardId);
        Assert.AreEqual(expected.AnswerVariantId, actual.AnswerVariantId);
        Assert.AreEqual(expected.InteractionMode, actual.InteractionMode);
        Assert.AreEqual(expected.ConsecutiveReadingSuccessCount, actual.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(expected.ConsecutiveTypingSuccessCount, actual.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(expected.ConsecutiveTypingFailureCount, actual.ConsecutiveTypingFailureCount);
        Assert.AreEqual(expected.LastAssessedAtUtc, actual.LastAssessedAtUtc);
        Assert.AreEqual(expected.MasteryReviewExtensionScheduled, actual.MasteryReviewExtensionScheduled);
        Assert.AreEqual(expected.IsMastered, actual.IsMastered);
        Assert.AreEqual(expected.ReplayVersion, actual.ReplayVersion);
        Assert.AreEqual(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.AreEqual(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
    }

    // ---- Fingerprint and logical deduplication ----

    [TestMethod]
    public void Fingerprint_IsStableAcrossTwoComputations()
    {
        var ev = CreateEvent();
        var f1 = Schema8LearningReviewReplayPolicy.ComputeFingerprint(ev);
        var f2 = Schema8LearningReviewReplayPolicy.ComputeFingerprint(ev);
        Assert.AreEqual(f1, f2);
        Assert.IsTrue(Schema8LearningReviewReplayPolicy.FingerprintDomain.StartsWith("KnownFirst.LocalLearningReviewReplay.v1"));
    }

    [TestMethod]
    public void Fingerprint_NullMarker_DistinguishesNullFromValue()
    {
        var e1 = CreateEvent(targetVariantId: null);
        var e2 = CreateEvent(targetVariantId: 0);
        Assert.AreNotEqual(Schema8LearningReviewReplayPolicy.ComputeFingerprint(e1), Schema8LearningReviewReplayPolicy.ComputeFingerprint(e2));

        var e3 = CreateEvent(matchedVariantId: null);
        var e4 = CreateEvent(matchedVariantId: 0);
        Assert.AreNotEqual(Schema8LearningReviewReplayPolicy.ComputeFingerprint(e3), Schema8LearningReviewReplayPolicy.ComputeFingerprint(e4));
    }

    [TestMethod]
    public void Dedup_LowestPhysicalIdSurvives()
    {
        var e1 = CreateEvent(reviewId: 5);
        var e2 = CreateEvent(reviewId: 2); // Duplicate logic
        var e3 = CreateEvent(reviewId: 8);

        var diagnostics = new List<Schema8ReplayDiagnostic>();
        var survivors = Schema8LearningReviewReplayPolicy.SelectSurvivors(new[] { e1, e2, e3 }, diagnostics);

        Assert.AreEqual(1, survivors.Count);
        Assert.AreEqual(2, survivors[0].Event.ReviewId);
    }

    [TestMethod]
    public void Order_ReviewedAtUtcAscendingThenIdAscending()
    {
        var e1 = CreateEvent(reviewId: 10, reviewedAtUtc: T1, intervalDays: 10);
        var e2 = CreateEvent(reviewId: 5, reviewedAtUtc: T0, intervalDays: 5);
        var e3 = CreateEvent(reviewId: 2, reviewedAtUtc: T1, intervalDays: 2);

        var survivors = Schema8LearningReviewReplayPolicy.SelectSurvivors(new[] { e1, e2, e3 });

        Assert.AreEqual(3, survivors.Count);
        Assert.AreEqual(5, survivors[0].Event.ReviewId);
        Assert.AreEqual(2, survivors[1].Event.ReviewId);
        Assert.AreEqual(10, survivors[2].Event.ReviewId);
    }

    [TestMethod]
    public void Diagnostics_MalformedReview_IsReportedNotSilentlyDropped()
    {
        var e1 = CreateEvent(reviewId: 1);
        var e2 = CreateEvent(reviewId: 2);
        var diagnostics = new List<Schema8ReplayDiagnostic>();

        Schema8LearningReviewReplayPolicy.SelectSurvivors(new[] { e1, e2 }, diagnostics);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Schema8LearningReviewReplayPolicy.DiagnosticDuplicateFingerprint, diagnostics[0].Code);
    }

    [TestMethod]
    public void Fingerprint_IsSensitiveToFields()
    {
        var cases = new[]
        {
            CreateEvent() with { CardId = 999 },
            CreateEvent() with { ReviewedAtUtc = T3 },
            CreateEvent() with { Rating = ReviewRating.Again },
            CreateEvent() with { WasTypedAnswer = false },
            CreateEvent() with { WasCorrect = false },
            CreateEvent() with { DueAtUtc = T3 },
            CreateEvent() with { IntervalDays = 99 },
            CreateEvent() with { EaseFactor = 1.3 },
            CreateEvent() with { TargetAnswerVariantId = 99 },
            CreateEvent() with { MatchedAnswerVariantId = 99 }
        };

        var baseEvent = CreateEvent();
        foreach (var modified in cases)
        {
            Assert.AreNotEqual(
                Schema8LearningReviewReplayPolicy.ComputeFingerprint(baseEvent),
                Schema8LearningReviewReplayPolicy.ComputeFingerprint(modified));
        }
    }

    // ---- Historical CardSchedule reconstruction ----

    [TestMethod]
    public void HistoricalSchedule_FirstSurvivor_IsCardScheduleNew()
    {
        var sched = Schema8LearningReviewReplayPolicy.FirstPreEventSchedule(T0);
        Assert.AreEqual(CardState.New, sched.State);
        Assert.AreEqual(T0, sched.DueAtUtc);
    }

    [TestMethod]
    public void HistoricalSchedule_FirstSurvivor_IsNeverMasteryReview()
    {
        var sched = Schema8LearningReviewReplayPolicy.FirstPreEventSchedule(T0);
        Assert.IsFalse(AutomaticLearningPolicy.IsMasteryReview(sched));
    }

    [TestMethod]
    public void HistoricalSchedule_PreviousIntervalGreaterZero_YieldsReviewState()
    {
        var prev = CreateEvent(intervalDays: 1, dueAtUtc: T2);
        var sched = Schema8LearningReviewReplayPolicy.NextPreEventSchedule(prev);

        Assert.AreEqual(CardState.Review, sched.State);
        Assert.AreEqual(T2, sched.DueAtUtc);
    }

    [TestMethod]
    public void HistoricalSchedule_PreviousIntervalZero_YieldsLearningState()
    {
        var prev = CreateEvent(intervalDays: 0, dueAtUtc: T2);
        var sched = Schema8LearningReviewReplayPolicy.NextPreEventSchedule(prev);

        Assert.AreEqual(CardState.Learning, sched.State);
    }

    [TestMethod]
    public void HistoricalSchedule_CurrentCardState_IsNeverUsedAsHistoricalState()
    {
        var cardNew = CreateCard(createdAt: T0);
        cardNew.State = CardState.New;
        cardNew.IntervalDays = 0;
        cardNew.EaseFactor = 2.5;

        var cardRetired = CreateCard(createdAt: T0);
        cardRetired.State = CardState.Retired;
        cardRetired.IntervalDays = 365;
        cardRetired.EaseFactor = 1.3;

        var assignments = new[] { CreateAssignment(bound: T0) };
        var events = new[] { CreateEvent(wasTypedAnswer: true, wasCorrect: true, reviewedAtUtc: T1) };
        var persistedProgress = Array.Empty<AnswerVariantProgressRow>();

        var res1 = Schema8LearningReviewReplayPolicy.Replay(cardNew, assignments, events, persistedProgress);
        var res2 = Schema8LearningReviewReplayPolicy.Replay(cardRetired, assignments, events, persistedProgress);

        Assert.AreEqual(res1.Survivors.Count, res2.Survivors.Count);
        for (int i = 0; i < res1.Survivors.Count; i++)
        {
            Assert.AreEqual(res1.Survivors[i].Fingerprint, res2.Survivors[i].Fingerprint);
        }

        Assert.AreEqual(res1.Outcomes[0].ConsumedEventCount, res2.Outcomes[0].ConsumedEventCount);
        Assert.AreEqual(res1.Outcomes[0].State, res2.Outcomes[0].State);
        Assert.AreEqual(res1.Outcomes[0].IsMastered, res2.Outcomes[0].IsMastered);
        AssertProgressRowsAreEqual(res1.Outcomes[0].ToRow(), res2.Outcomes[0].ToRow());
    }

    [TestMethod]
    public void Mastery_Previous365DayInterval_MakesNextEventEligible()
    {
        var prev = CreateEvent(intervalDays: 365, dueAtUtc: T2);
        var sched = Schema8LearningReviewReplayPolicy.NextPreEventSchedule(prev);
        Assert.IsTrue(AutomaticLearningPolicy.IsMasteryReview(sched));
    }

    [TestMethod]
    public void Mastery_RequiresTwoConsecutiveTypingSuccesses()
    {
        var sched = CardSchedule.New(T0) with { State = CardState.Review, IntervalDays = 365 };
        var state = AutomaticLearningState.Initial;

        (state, bool m1) = Schema8LearningReviewReplayPolicy.ApplyEvent(
            state, sched, CreateEvent(wasTypedAnswer: true, wasCorrect: true), true);
        Assert.IsFalse(m1); // 1

        (state, bool m2) = Schema8LearningReviewReplayPolicy.ApplyEvent(
            state, sched, CreateEvent(wasTypedAnswer: true, wasCorrect: true), true);
        Assert.IsTrue(m2); // 2
    }

    [TestMethod]
    public void Extension_MasteryReviewWithoutMastery_SchedulesExtensionOnce()
    {
        var sched = CardSchedule.New(T0) with { State = CardState.Review, IntervalDays = 365 };
        var state = AutomaticLearningState.Initial;

        (state, _) = Schema8LearningReviewReplayPolicy.ApplyEvent(
            state, sched, CreateEvent(rating: ReviewRating.Good, wasTypedAnswer: false), false);

        Assert.IsTrue(state.MasteryReviewExtensionScheduled);
    }

    [TestMethod]
    public void Extension_AgainRatingOnMasteryReview_DoesNotSchedule()
    {
        var sched = CardSchedule.New(T0) with { State = CardState.Review, IntervalDays = 365 };
        var state = AutomaticLearningState.Initial;

        (state, _) = Schema8LearningReviewReplayPolicy.ApplyEvent(
            state, sched, CreateEvent(rating: ReviewRating.Again, wasTypedAnswer: false), false);

        Assert.IsFalse(state.MasteryReviewExtensionScheduled);
    }

    [TestMethod]
    public void LivePreWriteResult_EqualsFullReplayResult()
    {
        var sched = CardSchedule.New(T0) with { State = CardState.Review, IntervalDays = 365 };
        var ev = CreateEvent(wasTypedAnswer: true, wasCorrect: true, reviewedAtUtc: T1);

        var (statePre, mPre) = Schema8LearningReviewReplayPolicy.ApplyEvent(
            AutomaticLearningPolicy.RecordTypingAssessment(AutomaticLearningState.Initial, true),
            sched, ev, true);

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(),
            new[] { CreateAssignment(bound: T0) },
            new[] { CreateEvent(intervalDays: 365, reviewedAtUtc: T0), ev },
            Array.Empty<AnswerVariantProgressRow>());

        var outc = res.Outcomes[0];
        Assert.AreEqual(mPre, outc.IsMastered);
        Assert.AreEqual(statePre.ConsecutiveTypingSuccesses, outc.State.ConsecutiveTypingSuccesses);
    }

    // ---- Required epoch behavior ----

    [TestMethod]
    public void Mastery_CurrentEpochMastery_IsMonotonic()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.IsMastered = true;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T0) }, Array.Empty<Schema8ReplayReviewEvent>(), new[] { row });

        Assert.IsTrue(res.Outcomes[0].IsMastered);
    }

    [TestMethod]
    public void Mastery_AcceptedOnlyMasteredRow_PromotionStartsUnmasteredEpoch()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.IsMastered = true;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) }, Array.Empty<Schema8ReplayReviewEvent>(), new[] { row });

        Assert.IsFalse(res.Outcomes[0].IsMastered);
    }

    [TestMethod]
    public void Mastery_DemotionAndRepromotion_DoesNotCarryPreviousEpochMastery()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.IsMastered = true;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) }, Array.Empty<Schema8ReplayReviewEvent>(), new[] { row });

        Assert.IsFalse(res.Outcomes[0].IsMastered);
    }

    [TestMethod]
    public void Mastery_MigrationCompatibilitySeed_MatchingBoundaryRemainsMastered()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.IsMastered = true;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(createdAt: T0), new[] { CreateAssignment(bound: T0) }, Array.Empty<Schema8ReplayReviewEvent>(), new[] { row });

        Assert.IsTrue(res.Outcomes[0].IsMastered);
    }

    [TestMethod]
    public void Progress_CreatedAtUtcMismatch_ResetsCurrentEpochState()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.ConsecutiveTypingSuccessCount = 1;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) }, Array.Empty<Schema8ReplayReviewEvent>(), new[] { row });

        Assert.AreEqual(0, res.Outcomes[0].State.ConsecutiveTypingSuccesses);
    }

    [TestMethod]
    public void Boundary_ReviewsBeforeRequiredSinceUtc_AreExcluded()
    {
        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) },
            new[] { CreateEvent(reviewedAtUtc: T0) },
            Array.Empty<AnswerVariantProgressRow>());

        Assert.AreEqual(0, res.Outcomes[0].ConsumedEventCount);
    }

    [TestMethod]
    public void Boundary_OldAcceptedOnlyEraReviews_NeverGrantRequiredMastery()
    {
        var ev1 = CreateEvent(intervalDays: 365, reviewedAtUtc: T0, wasTypedAnswer: true, wasCorrect: true);
        var ev2 = CreateEvent(reviewedAtUtc: T0.AddHours(1), wasTypedAnswer: true, wasCorrect: true);

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) },
            new[] { ev1, ev2 },
            Array.Empty<AnswerVariantProgressRow>());

        Assert.IsFalse(res.Outcomes[0].IsMastered);
        Assert.AreEqual(0, res.Outcomes[0].ConsumedEventCount);
    }

    [TestMethod]
    public void Boundary_RePromotion_CreatesNewReplayEpoch()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.IsMastered = true;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T2) },
            new[] { CreateEvent(reviewedAtUtc: T1) },
            new[] { row });

        Assert.AreEqual(0, res.Outcomes[0].ConsumedEventCount);
        Assert.AreEqual(T2, res.Outcomes[0].RequiredSinceUtc);
        Assert.IsFalse(res.Outcomes[0].IsMastered);
    }

    [TestMethod]
    public void AcceptedOnlyProgressRow_IsPreservedByteForByte()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, AccVar, T0);
        var plan = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            new[] { CreateAssignment(variantId: AccVar, req: AnswerVariantRequirement.AcceptedOnly, bound: null) },
            new[] { row },
            new Schema8ReplayResult(new List<Schema8ReplaySurvivor>(), new List<Schema8ReplayVariantOutcome>(), new List<Schema8ReplayDiagnostic>()));

        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void AcceptedOnlyProgressRow_ReplayVersion_IsNotUpgraded()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, AccVar, T0);
        row.ReplayVersion = 0;

        var plan = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            new[] { CreateAssignment(variantId: AccVar, req: AnswerVariantRequirement.AcceptedOnly, bound: null) },
            new[] { row },
            new Schema8ReplayResult(new List<Schema8ReplaySurvivor>(), new List<Schema8ReplayVariantOutcome>(), new List<Schema8ReplayDiagnostic>()));

        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void AcceptedOnlyAssignment_IsExcludedFromRetirement()
    {
        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(variantId: AccVar, req: AnswerVariantRequirement.AcceptedOnly, bound: null) },
            Array.Empty<Schema8ReplayReviewEvent>(),
            Array.Empty<AnswerVariantProgressRow>());

        Assert.IsFalse(Schema8CardRetirementPolicy.AllRequiredMastered(res));
    }

    // ---- Deterministic timestamps and idempotency ----

    [TestMethod]
    public void Timestamps_NoEligibleReviews_UseRequiredSinceUtc()
    {
        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) },
            Array.Empty<Schema8ReplayReviewEvent>(),
            Array.Empty<AnswerVariantProgressRow>());

        var row = res.Outcomes[0].ToRow();
        Assert.AreEqual(T1, row.CreatedAtUtc);
        Assert.AreEqual(T1, row.UpdatedAtUtc);
    }

    [TestMethod]
    public void Timestamps_EligibleReviews_UseLatestReviewedAtUtc()
    {
        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T0) },
            new[] { CreateEvent(reviewedAtUtc: T1), CreateEvent(reviewId: 2, reviewedAtUtc: T2) },
            Array.Empty<AnswerVariantProgressRow>());

        var row = res.Outcomes[0].ToRow();
        Assert.AreEqual(T0, row.CreatedAtUtc);
        Assert.AreEqual(T2, row.UpdatedAtUtc);
    }

    [TestMethod]
    public void Timestamps_MigrationCompatibilitySeed_UsesCardHistory()
    {
        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(createdAt: T0), new[] { CreateAssignment(bound: T0) },
            Array.Empty<Schema8ReplayReviewEvent>(),
            Array.Empty<AnswerVariantProgressRow>());

        var row = res.Outcomes[0].ToRow();
        Assert.AreEqual(T0, row.CreatedAtUtc);
        Assert.AreEqual(T0, row.UpdatedAtUtc);
    }

    [TestMethod]
    public void Replay_AfterRestartAtDifferentClock_ProducesIdenticalProgress()
    {
        var card1 = CreateCard(createdAt: T0);
        var card2 = CreateCard(createdAt: T0);
        var assignments1 = new[] { CreateAssignment(bound: T0) };
        var assignments2 = new[] { CreateAssignment(bound: T0) };
        var events1 = new[] { CreateEvent(reviewedAtUtc: T1) };
        var events2 = new[] { CreateEvent(reviewedAtUtc: T1) };
        var progress1 = Array.Empty<AnswerVariantProgressRow>();
        var progress2 = Array.Empty<AnswerVariantProgressRow>();

        var res1 = Schema8LearningReviewReplayPolicy.Replay(card1, assignments1, events1, progress1);
        var res2 = Schema8LearningReviewReplayPolicy.Replay(card2, assignments2, events2, progress2);

        var row1 = res1.Outcomes[0].ToRow();
        var row2 = res2.Outcomes[0].ToRow();

        AssertProgressRowsAreEqual(row1, row2);
    }

    [TestMethod]
    public void Replay_UnchangedSecondRun_ProducesZeroWrites()
    {
        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T1) },
            Array.Empty<Schema8ReplayReviewEvent>(),
            Array.Empty<AnswerVariantProgressRow>());

        var plan1 = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            new[] { CreateAssignment(bound: T1) }, Array.Empty<AnswerVariantProgressRow>(), res);

        Assert.AreEqual(1, plan1.Inserts.Count);

        var plan2 = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            new[] { CreateAssignment(bound: T1) }, plan1.Inserts, res);

        Assert.IsTrue(plan2.IsEmpty);
        Assert.AreEqual(0, plan2.MutationCount);
    }

    [TestMethod]
    public void OldReplayVersion_IsRebuilt()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.ReplayVersion = 0;

        var res = Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T0) },
            Array.Empty<Schema8ReplayReviewEvent>(),
            new[] { row });

        var plan = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            new[] { CreateAssignment(bound: T0) }, new[] { row }, res);

        Assert.AreEqual(1, plan.Updates.Count);
        Assert.AreEqual(1, plan.Updates[0].ReplayVersion);
    }

    [TestMethod]
    public void FutureReplayVersion_FailsClosed()
    {
        var row = Schema8LearningReviewReplayPolicy.CreateEpochBaseline(Card40, ReqVar, T0);
        row.ReplayVersion = 999;

        var ex = Assert.ThrowsExactly<Schema8LearningDataException>(() => Schema8LearningReviewReplayPolicy.Replay(
            CreateCard(), new[] { CreateAssignment(bound: T0) },
            Array.Empty<Schema8ReplayReviewEvent>(),
            new[] { row }));

        Assert.AreEqual(Schema8LearningDataErrorCode.ReplayVersionUnsupported, ex.Code);
    }
}
