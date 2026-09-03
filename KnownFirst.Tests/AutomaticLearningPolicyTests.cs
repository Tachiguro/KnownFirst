using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;

namespace KnownFirst.Tests;

[TestClass]
public sealed class AutomaticLearningPolicyTests
{
    [TestMethod]
    public void RecordRecallAssessment_GoodAndEasy_AdvanceCounterAndTransitionToTyping()
    {
        var initial = AutomaticLearningState.Initial;
        Assert.AreEqual(LearningInteractionMode.Reading, initial.InteractionMode);
        Assert.AreEqual(0, initial.ConsecutiveRecallSuccesses);

        var state1 = AutomaticLearningPolicy.RecordRecallAssessment(initial, ReviewRating.Good);
        Assert.AreEqual(LearningInteractionMode.Reading, state1.InteractionMode);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var state2 = AutomaticLearningPolicy.RecordRecallAssessment(state1, ReviewRating.Easy);
        Assert.AreEqual(LearningInteractionMode.Typing, state2.InteractionMode);
        Assert.AreEqual(2, state2.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, state2.ConsecutiveTypingFailures);
    }

    [TestMethod]
    public void RecordRecallAssessment_Hard_HoldsCounterAtZeroAndAtOne()
    {
        var initial = AutomaticLearningState.Initial;
        var heldAtZero = AutomaticLearningPolicy.RecordRecallAssessment(initial, ReviewRating.Hard);
        Assert.AreEqual(LearningInteractionMode.Reading, heldAtZero.InteractionMode);
        Assert.AreEqual(0, heldAtZero.ConsecutiveRecallSuccesses);

        var state1 = AutomaticLearningPolicy.RecordRecallAssessment(initial, ReviewRating.Good);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var heldAtOne = AutomaticLearningPolicy.RecordRecallAssessment(state1, ReviewRating.Hard);
        Assert.AreEqual(LearningInteractionMode.Reading, heldAtOne.InteractionMode);
        Assert.AreEqual(1, heldAtOne.ConsecutiveRecallSuccesses);
    }

    [TestMethod]
    public void RecordRecallAssessment_Again_ResetsCounterToZero()
    {
        var initial = AutomaticLearningState.Initial;
        var state1 = AutomaticLearningPolicy.RecordRecallAssessment(initial, ReviewRating.Good);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var reset = AutomaticLearningPolicy.RecordRecallAssessment(state1, ReviewRating.Again);
        Assert.AreEqual(LearningInteractionMode.Reading, reset.InteractionMode);
        Assert.AreEqual(0, reset.ConsecutiveRecallSuccesses);
    }

    [TestMethod]
    public void RecordRecallAssessment_LegacyBoolOverload_PreservesBinarySemantics()
    {
        var initial = AutomaticLearningState.Initial;
        var state1 = AutomaticLearningPolicy.RecordRecallAssessment(initial, successful: true);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var reset = AutomaticLearningPolicy.RecordRecallAssessment(state1, successful: false);
        Assert.AreEqual(0, reset.ConsecutiveRecallSuccesses);

        var state2 = AutomaticLearningPolicy.RecordRecallAssessment(state1, successful: true);
        Assert.AreEqual(LearningInteractionMode.Typing, state2.InteractionMode);
        Assert.AreEqual(2, state2.ConsecutiveRecallSuccesses);
    }

    [TestMethod]
    public void RecordRecallAssessment_TypedAssessmentOutcome_DirectMapping()
    {
        var initial = AutomaticLearningState.Initial;
        var adv = AutomaticLearningPolicy.RecordRecallAssessment(initial, RecallProgressionAssessment.Advance);
        Assert.AreEqual(1, adv.ConsecutiveRecallSuccesses);

        var hold = AutomaticLearningPolicy.RecordRecallAssessment(adv, RecallProgressionAssessment.Hold);
        Assert.AreEqual(1, hold.ConsecutiveRecallSuccesses);

        var reset = AutomaticLearningPolicy.RecordRecallAssessment(hold, RecallProgressionAssessment.Reset);
        Assert.AreEqual(0, reset.ConsecutiveRecallSuccesses);
    }

    [TestMethod]
    public void RecordTypingAssessment_Correct_IncrementsAndCapsAtTwo()
    {
        var typingState = new AutomaticLearningState(LearningInteractionMode.Typing, 2, 0, 1, false);
        var success1 = AutomaticLearningPolicy.RecordTypingAssessment(typingState, correct: true);
        Assert.AreEqual(1, success1.ConsecutiveTypingSuccesses);
        Assert.AreEqual(0, success1.ConsecutiveTypingFailures);
        Assert.AreEqual(LearningInteractionMode.Typing, success1.InteractionMode);

        var success2 = AutomaticLearningPolicy.RecordTypingAssessment(success1, correct: true);
        Assert.AreEqual(2, success2.ConsecutiveTypingSuccesses);

        var success3 = AutomaticLearningPolicy.RecordTypingAssessment(success2, correct: true);
        Assert.AreEqual(2, success3.ConsecutiveTypingSuccesses, "Typing successes must cap at 2.");
    }

    [TestMethod]
    public void RecordTypingAssessment_TwoFailures_LapsesToReadingAndResetsAllCounters()
    {
        var typingState = new AutomaticLearningState(LearningInteractionMode.Typing, 2, 1, 0, false);
        var failure1 = AutomaticLearningPolicy.RecordTypingAssessment(typingState, correct: false);
        Assert.AreEqual(LearningInteractionMode.Typing, failure1.InteractionMode);
        Assert.AreEqual(0, failure1.ConsecutiveTypingSuccesses);
        Assert.AreEqual(1, failure1.ConsecutiveTypingFailures);

        var failure2 = AutomaticLearningPolicy.RecordTypingAssessment(failure1, correct: false);
        Assert.AreEqual(LearningInteractionMode.Reading, failure2.InteractionMode, "Second failure must lapse to Reading.");
        Assert.AreEqual(0, failure2.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, failure2.ConsecutiveTypingSuccesses);
        Assert.AreEqual(0, failure2.ConsecutiveTypingFailures);
    }

    [TestMethod]
    public void ResolveInteraction_TermToMeaning_AlwaysReturnsReading()
    {
        var typingState = new AutomaticLearningState(LearningInteractionMode.Typing, 2, 2, 0, false);

        Assert.AreEqual(
            LearningInteractionMode.Reading,
            AutomaticLearningPolicy.ResolveInteraction(LearningMode.Automatic, typingState, CardDirection.TermToMeaning));
        Assert.AreEqual(
            LearningInteractionMode.Reading,
            AutomaticLearningPolicy.ResolveInteraction(LearningMode.Typing, typingState, CardDirection.TermToMeaning));
        Assert.AreEqual(
            LearningInteractionMode.Reading,
            AutomaticLearningPolicy.ResolveInteraction(LearningMode.Reading, typingState, CardDirection.TermToMeaning));
    }
}
