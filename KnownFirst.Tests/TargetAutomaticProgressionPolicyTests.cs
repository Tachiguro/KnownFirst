using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;

namespace KnownFirst.Tests;

[TestClass]
public sealed class TargetAutomaticProgressionPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T4 = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T5 = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void InitialState_IsLowFrictionReadingMode()
    {
        var target = LearningTarget.CreateDefinition("de", "de");
        var state = TargetAutomaticProgressionState.InitialFor(target);

        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode);
        Assert.AreEqual(0, state.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, state.ConsecutiveTypingSuccesses);
        Assert.IsFalse(state.IsTypingQualified);
        Assert.IsFalse(state.IsRecheckArmed);
        Assert.IsFalse(state.TypingOptOut);

        Assert.AreEqual(
            LearningInteractionMode.Reading,
            TargetAutomaticProgressionPolicy.ResolveInteraction(LearningMode.Automatic, state));
    }

    [TestMethod]
    public void GoodAndEasy_AdvanceRecallQualification()
    {
        var state0 = TargetAutomaticProgressionState.Initial;

        var state1 = TargetAutomaticProgressionPolicy.ApplyEvent(
            state0,
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good));
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, state1.InteractionMode);

        var state2 = TargetAutomaticProgressionPolicy.ApplyEvent(
            state1,
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Easy));
        Assert.AreEqual(2, state2.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Typing, state2.InteractionMode);
    }

    [TestMethod]
    public void Hard_HoldsRecallQualificationCounter()
    {
        var state0 = TargetAutomaticProgressionState.Initial;
        var heldAtZero = TargetAutomaticProgressionPolicy.ApplyEvent(
            state0,
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Hard));
        Assert.AreEqual(0, heldAtZero.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, heldAtZero.InteractionMode);

        var state1 = TargetAutomaticProgressionPolicy.ApplyEvent(
            heldAtZero,
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good));
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var heldAtOne = TargetAutomaticProgressionPolicy.ApplyEvent(
            state1,
            TargetInteractionEvent.ScheduledRecall(T2, ReviewRating.Hard));
        Assert.AreEqual(1, heldAtOne.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, heldAtOne.InteractionMode);
    }

    [TestMethod]
    public void Again_ResetsRecallQualificationCounter()
    {
        var state0 = TargetAutomaticProgressionState.Initial;
        var state1 = TargetAutomaticProgressionPolicy.ApplyEvent(
            state0,
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good));
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var resetState = TargetAutomaticProgressionPolicy.ApplyEvent(
            state1,
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Again));
        Assert.AreEqual(0, resetState.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, resetState.InteractionMode);
    }

    [TestMethod]
    public void TwoQualifyingScheduledRecallSuccesses_TransitionsToTypingQualification()
    {
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good)
        };

        var state = TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, events);

        Assert.AreEqual(2, state.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Typing, state.InteractionMode);
        Assert.IsFalse(state.IsTypingQualified);
        Assert.IsFalse(state.IsRecheckArmed);
    }

    [TestMethod]
    public void SameSessionRepeat_DoesNotCountAsDistinctScheduledReview()
    {
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.SessionRepeat(T0.AddMinutes(1), ReviewRating.Good, wasTypedAnswer: false, wasCorrect: true)
        };

        var state = TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, events);

        Assert.AreEqual(1, state.ConsecutiveRecallSuccesses, "Session repeat must not advance recall qualification.");
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode);
    }

    [TestMethod]
    public void TwoSuccessfulTypingChecks_QualifyTypingAndReturnToReadingMaintenance()
    {
        var target = LearningTarget.CreateTranslation("de", "en");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Easy), // transitions to Typing
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Good, wasCorrect: true), // 1st typing success
            TargetInteractionEvent.ScheduledTyping(T3, ReviewRating.Good, wasCorrect: true)  // 2nd typing success -> qualified
        };

        var state = TargetAutomaticProgressionPolicy.Replay(target, events);

        Assert.IsTrue(state.IsTypingQualified, "Two successful typing checks must qualify typing.");
        Assert.AreEqual(2, state.ConsecutiveTypingSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode, "Qualified target returns to low-friction Reading maintenance.");
        Assert.IsFalse(state.IsRecheckArmed);
    }

    [TestMethod]
    public void QualifiedMaintenance_ContinuesInReadingIndefinitely()
    {
        var target = LearningTarget.CreateTranslation("de", "en");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledTyping(T3, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledRecall(T4, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T5, ReviewRating.Hard)
        };

        var state = TargetAutomaticProgressionPolicy.Replay(target, events);

        Assert.IsTrue(state.IsTypingQualified);
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode, "Routine reviews in maintenance remain Reading.");
        Assert.IsFalse(state.IsRecheckArmed);
    }

    [TestMethod]
    public void LaterGenuineScheduledAgain_RearmsExactlyOneTypingRecheck()
    {
        var target = LearningTarget.CreateDefinition("de", "de");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledTyping(T3, ReviewRating.Good, wasCorrect: true),
            // Later genuine scheduled lapse:
            TargetInteractionEvent.ScheduledRecall(T4, ReviewRating.Again)
        };

        var state = TargetAutomaticProgressionPolicy.Replay(target, events);

        Assert.IsTrue(state.IsTypingQualified, "Card retains qualified status.");
        Assert.IsTrue(state.IsRecheckArmed, "Lapse must re-arm exactly one typing re-check.");
        Assert.AreEqual(LearningInteractionMode.Typing, state.InteractionMode, "Re-armed re-check requires Typing.");
    }

    [TestMethod]
    public void SuccessfulRecheck_RestoresQualifiedReadingMaintenance()
    {
        var target = LearningTarget.CreateDefinition("de", "de");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledTyping(T3, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledRecall(T4, ReviewRating.Again), // re-arms re-check
            TargetInteractionEvent.ScheduledTyping(T5, ReviewRating.Good, wasCorrect: true) // successful re-check
        };

        var state = TargetAutomaticProgressionPolicy.Replay(target, events);

        Assert.IsTrue(state.IsTypingQualified);
        Assert.IsFalse(state.IsRecheckArmed, "Successful re-check disarms re-check.");
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode, "Restores low-friction Reading maintenance.");
    }

    [TestMethod]
    public void FailedRecheck_LapsesOutOfQualifiedMaintenanceBackToRecallQualification()
    {
        var target = LearningTarget.CreateDefinition("de", "de");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledTyping(T3, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledRecall(T4, ReviewRating.Again), // re-arms re-check
            TargetInteractionEvent.ScheduledTyping(T5, ReviewRating.Again, wasCorrect: false) // failed re-check
        };

        var state = TargetAutomaticProgressionPolicy.Replay(target, events);

        Assert.IsFalse(state.IsTypingQualified, "Failed re-check forfeits qualified status.");
        Assert.IsFalse(state.IsRecheckArmed);
        Assert.AreEqual(0, state.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, state.ConsecutiveTypingSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode);
    }

    [TestMethod]
    public void TypingOptOut_PreventsEnteringTypingQualification()
    {
        var optOutTarget = LearningTarget.CreateTranslation("de", "en", typingOptOut: true);
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T2, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T3, ReviewRating.Again)
        };

        var state = TargetAutomaticProgressionPolicy.Replay(optOutTarget, events);

        Assert.IsTrue(state.TypingOptOut);
        Assert.IsFalse(state.IsTypingQualified);
        Assert.IsFalse(state.IsRecheckArmed);
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode, "Opted-out target remains strictly Reading.");

        Assert.AreEqual(
            LearningInteractionMode.Reading,
            TargetAutomaticProgressionPolicy.ResolveInteraction(LearningMode.Automatic, state),
            "Automatic mode keeps opted-out target in Reading.");
        Assert.AreEqual(
            LearningInteractionMode.Typing,
            TargetAutomaticProgressionPolicy.ResolveInteraction(LearningMode.Typing, state),
            "Explicit global Typing mode remains authoritative regardless of per-target opt-out.");
    }

    [TestMethod]
    public void TypingFailureInTypingQualification_ResetsRecallAndReturnsToReading()
    {
        var target = LearningTarget.CreateTranslation("de", "en");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good), // transitions to Typing
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Again, wasCorrect: false) // failure
        };

        var state = TargetAutomaticProgressionPolicy.Replay(target, events);

        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode, "Failure in typing qualification returns to Reading.");
        Assert.AreEqual(0, state.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, state.ConsecutiveTypingSuccesses);
        Assert.IsFalse(state.IsTypingQualified);
    }

    [TestMethod]
    public void Replay_TimestampOrderingViolation_ThrowsArgumentOutOfRangeException()
    {
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good) // decreasing timestamp
        };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, events));
    }

    [TestMethod]
    public void LearningTargetIdentity_ValidatesAndNormalizesLanguages()
    {
        var identity1 = new LearningTargetIdentity(LearningTargetKind.Definition, " DE ", " DE ");
        Assert.AreEqual(LearningTargetKind.Definition, identity1.Kind);
        Assert.AreEqual("de", identity1.SourceLanguage);
        Assert.AreEqual("de", identity1.TargetLanguage);

        var identity2 = LearningTargetIdentity.Translation("de", "en");
        var identity3 = LearningTargetIdentity.Translation("en", "de");
        Assert.AreNotEqual(identity2, identity3, "German->English must not equal English->German.");

        Assert.ThrowsExactly<ArgumentException>(() =>
            new LearningTargetIdentity(LearningTargetKind.Definition, "", "de"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LearningTargetIdentity(LearningTargetKind.Translation, "de", "   "));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LearningTargetIdentity((LearningTargetKind)99, "de", "en"));
    }

    [TestMethod]
    public void TargetInteractionEvent_ValidatesUtcOffsetAndRating()
    {
        var nonUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(2));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TargetInteractionEvent(nonUtc, ReviewRating.Good, false, true));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new TargetInteractionEvent(T0, (ReviewRating)99, false, true));
    }

    [TestMethod]
    public void TypingQualification_NonTypedSuccessfulRecall_DoesNotAdvanceTypingQualificationOrQualify()
    {
        var state0 = TargetAutomaticProgressionState.Initial;
        var inTypingQualification = TargetAutomaticProgressionPolicy.Replay(state0, new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good)
        });

        Assert.AreEqual(2, inTypingQualification.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, inTypingQualification.ConsecutiveTypingSuccesses);
        Assert.AreEqual(LearningInteractionMode.Typing, inTypingQualification.InteractionMode);
        Assert.IsFalse(inTypingQualification.IsTypingQualified);

        var nonTypedEvent = TargetInteractionEvent.ScheduledRecall(T2, ReviewRating.Good);
        Assert.IsFalse(nonTypedEvent.WasTypedAnswer);
        Assert.IsTrue(nonTypedEvent.WasCorrect);
        Assert.AreNotEqual(ReviewRating.Again, nonTypedEvent.Rating);

        var stateAfter = TargetAutomaticProgressionPolicy.ApplyEvent(inTypingQualification, nonTypedEvent);

        Assert.AreEqual(0, stateAfter.ConsecutiveTypingSuccesses,
            "Non-typed recall must not increment typing qualification success counter.");
        Assert.IsFalse(stateAfter.IsTypingQualified,
            "Non-typed recall must not qualify typing.");
        Assert.AreEqual(LearningInteractionMode.Typing, stateAfter.InteractionMode,
            "Target remains in Typing qualification interaction mode.");
    }

    [TestMethod]
    public void ArmedRecheck_NonTypedSuccessfulRecall_DoesNotDisarmRecheck()
    {
        var target = LearningTarget.CreateDefinition("de", "de");
        var events = new[]
        {
            TargetInteractionEvent.ScheduledRecall(T0, ReviewRating.Good),
            TargetInteractionEvent.ScheduledRecall(T1, ReviewRating.Good),
            TargetInteractionEvent.ScheduledTyping(T2, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledTyping(T3, ReviewRating.Good, wasCorrect: true),
            TargetInteractionEvent.ScheduledRecall(T4, ReviewRating.Again)
        };

        var armedState = TargetAutomaticProgressionPolicy.Replay(target, events);
        Assert.IsTrue(armedState.IsTypingQualified);
        Assert.IsTrue(armedState.IsRecheckArmed);
        Assert.AreEqual(LearningInteractionMode.Typing, armedState.InteractionMode);

        var nonTypedEvent = TargetInteractionEvent.ScheduledRecall(T5, ReviewRating.Good);
        Assert.IsFalse(nonTypedEvent.WasTypedAnswer);
        Assert.IsTrue(nonTypedEvent.WasCorrect);
        Assert.AreNotEqual(ReviewRating.Again, nonTypedEvent.Rating);

        var stateAfterNonTyped = TargetAutomaticProgressionPolicy.ApplyEvent(armedState, nonTypedEvent);

        Assert.IsTrue(stateAfterNonTyped.IsTypingQualified);
        Assert.IsTrue(stateAfterNonTyped.IsRecheckArmed,
            "Non-typed successful recall must not disarm or satisfy typing re-check.");
        Assert.AreEqual(LearningInteractionMode.Typing, stateAfterNonTyped.InteractionMode,
            "Re-check remains armed requiring Typing.");

        var typedSuccess = TargetInteractionEvent.ScheduledTyping(T5.AddDays(1), ReviewRating.Good, wasCorrect: true);
        var stateAfterTyped = TargetAutomaticProgressionPolicy.ApplyEvent(stateAfterNonTyped, typedSuccess);

        Assert.IsTrue(stateAfterTyped.IsTypingQualified);
        Assert.IsFalse(stateAfterTyped.IsRecheckArmed,
            "Genuine typed success satisfies and disarms the single re-check.");
        Assert.AreEqual(LearningInteractionMode.Reading, stateAfterTyped.InteractionMode,
            "Target returns to low-friction Reading maintenance.");
    }
}
