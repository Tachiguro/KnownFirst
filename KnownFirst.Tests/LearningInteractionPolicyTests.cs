using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningInteractionPolicyTests
{
    [TestMethod]
    public void ResolveInteraction_ExplicitReading_AlwaysReturnsReading()
    {
        var initial = LearningInteractionProgress.Initial;
        var typing = new LearningInteractionProgress(
            InteractionMode: LearningInteractionMode.Typing,
            ConsecutiveRecallSuccesses: 2,
            ConsecutiveTypingFailures: 0);

        Assert.AreEqual(LearningInteractionMode.Reading, LearningInteractionPolicy.ResolveInteraction(LearningMode.Reading, initial));
        Assert.AreEqual(LearningInteractionMode.Reading, LearningInteractionPolicy.ResolveInteraction(LearningMode.Reading, typing));
    }

    [TestMethod]
    public void ResolveInteraction_ExplicitTyping_AlwaysReturnsTyping()
    {
        var initial = LearningInteractionProgress.Initial;
        var reading = new LearningInteractionProgress(
            InteractionMode: LearningInteractionMode.Reading,
            ConsecutiveRecallSuccesses: 0,
            ConsecutiveTypingFailures: 0);

        Assert.AreEqual(LearningInteractionMode.Typing, LearningInteractionPolicy.ResolveInteraction(LearningMode.Typing, initial));
        Assert.AreEqual(LearningInteractionMode.Typing, LearningInteractionPolicy.ResolveInteraction(LearningMode.Typing, reading));
    }

    [TestMethod]
    public void ResolveInteraction_Automatic_FollowsProgressInteractionMode()
    {
        var initial = LearningInteractionProgress.Initial;
        Assert.AreEqual(LearningInteractionMode.Reading, LearningInteractionPolicy.ResolveInteraction(LearningMode.Automatic, initial));

        var typing = new LearningInteractionProgress(
            InteractionMode: LearningInteractionMode.Typing,
            ConsecutiveRecallSuccesses: 2,
            ConsecutiveTypingFailures: 0);
        Assert.AreEqual(LearningInteractionMode.Typing, LearningInteractionPolicy.ResolveInteraction(LearningMode.Automatic, typing));
    }

    [TestMethod]
    public void ResolveInteraction_TermToMeaning_AlwaysReturnsReading_RegardlessOfModeOrProgress()
    {
        var typing = new LearningInteractionProgress(
            InteractionMode: LearningInteractionMode.Typing,
            ConsecutiveRecallSuccesses: 2,
            ConsecutiveTypingFailures: 0);

        Assert.AreEqual(
            LearningInteractionMode.Reading,
            LearningInteractionPolicy.ResolveInteraction(LearningMode.Typing, typing, CardDirection.TermToMeaning));
        Assert.AreEqual(
            LearningInteractionMode.Reading,
            LearningInteractionPolicy.ResolveInteraction(LearningMode.Automatic, typing, CardDirection.TermToMeaning));
        Assert.AreEqual(
            LearningInteractionMode.Reading,
            LearningInteractionPolicy.ResolveInteraction(LearningMode.Reading, typing, CardDirection.TermToMeaning));

        var automaticTyping = new AutomaticLearningState(LearningInteractionMode.Typing, 2, 2, 0, false);
        Assert.AreEqual(
            LearningInteractionMode.Reading,
            AutomaticLearningPolicy.ResolveInteraction(LearningMode.Typing, automaticTyping, CardDirection.TermToMeaning));
        Assert.AreEqual(
            LearningInteractionMode.Reading,
            AutomaticLearningPolicy.ResolveInteraction(LearningMode.Automatic, automaticTyping, CardDirection.TermToMeaning));
    }

    [TestMethod]
    public void AutomaticRecall_RequiredConsecutiveSuccesses_AdvancesToTyping()
    {
        var state0 = LearningInteractionProgress.Initial;
        Assert.AreEqual(LearningInteractionMode.Reading, state0.InteractionMode);
        Assert.AreEqual(0, state0.ConsecutiveRecallSuccesses);

        var state1 = LearningInteractionPolicy.RecordRecallAssessment(state0, successful: true);
        Assert.AreEqual(LearningInteractionMode.Reading, state1.InteractionMode);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var state2 = LearningInteractionPolicy.RecordRecallAssessment(state1, successful: true);
        Assert.AreEqual(LearningInteractionMode.Typing, state2.InteractionMode);
        Assert.AreEqual(2, state2.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, state2.ConsecutiveTypingFailures);
    }

    [TestMethod]
    public void AutomaticRecall_GoodAndEasy_AdvanceToTyping()
    {
        var state0 = LearningInteractionProgress.Initial;
        var state1 = LearningInteractionPolicy.RecordRecallAssessment(state0, ReviewRating.Good);
        Assert.AreEqual(LearningInteractionMode.Reading, state1.InteractionMode);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var state2 = LearningInteractionPolicy.RecordRecallAssessment(state1, ReviewRating.Easy);
        Assert.AreEqual(LearningInteractionMode.Typing, state2.InteractionMode);
        Assert.AreEqual(2, state2.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, state2.ConsecutiveTypingFailures);
    }

    [TestMethod]
    public void AutomaticRecall_Hard_HoldsRecallSuccessCountAtZeroAndAtOne()
    {
        var state0 = LearningInteractionProgress.Initial;
        var held0 = LearningInteractionPolicy.RecordRecallAssessment(state0, ReviewRating.Hard);
        Assert.AreEqual(0, held0.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, held0.InteractionMode);

        var state1 = LearningInteractionPolicy.RecordRecallAssessment(state0, ReviewRating.Good);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var held1 = LearningInteractionPolicy.RecordRecallAssessment(state1, ReviewRating.Hard);
        Assert.AreEqual(1, held1.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, held1.InteractionMode);
    }

    [TestMethod]
    public void AutomaticRecall_Again_ResetsRecallSuccessProgress()
    {
        var state1 = LearningInteractionPolicy.RecordRecallAssessment(
            LearningInteractionProgress.Initial, ReviewRating.Good);
        Assert.AreEqual(1, state1.ConsecutiveRecallSuccesses);

        var stateFailed = LearningInteractionPolicy.RecordRecallAssessment(state1, ReviewRating.Again);
        Assert.AreEqual(0, stateFailed.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, stateFailed.InteractionMode);
    }

    [TestMethod]
    public void AutomaticTyping_CorrectTyping_ResetsTypingFailureProgress_AndRemainsTyping()
    {
        var typingWithFailure = new LearningInteractionProgress(
            InteractionMode: LearningInteractionMode.Typing,
            ConsecutiveRecallSuccesses: 2,
            ConsecutiveTypingFailures: 1);

        var stateCorrect = LearningInteractionPolicy.RecordTypingAssessment(typingWithFailure, correct: true);
        Assert.AreEqual(LearningInteractionMode.Typing, stateCorrect.InteractionMode);
        Assert.AreEqual(0, stateCorrect.ConsecutiveTypingFailures);
    }

    [TestMethod]
    public void AutomaticTyping_RepeatedIncorrectTyping_ReturnsToReading()
    {
        var typingState = new LearningInteractionProgress(
            InteractionMode: LearningInteractionMode.Typing,
            ConsecutiveRecallSuccesses: 2,
            ConsecutiveTypingFailures: 0);

        var failure1 = LearningInteractionPolicy.RecordTypingAssessment(typingState, correct: false);
        Assert.AreEqual(LearningInteractionMode.Typing, failure1.InteractionMode);
        Assert.AreEqual(1, failure1.ConsecutiveTypingFailures);

        var failure2 = LearningInteractionPolicy.RecordTypingAssessment(failure1, correct: false);
        Assert.AreEqual(LearningInteractionMode.Reading, failure2.InteractionMode);
        Assert.AreEqual(0, failure2.ConsecutiveTypingFailures);
        Assert.AreEqual(0, failure2.ConsecutiveRecallSuccesses);
    }

    [TestMethod]
    public void AutomaticCounters_AreBounded()
    {
        var state = LearningInteractionProgress.Initial;
        for (int i = 0; i < 5; i++)
        {
            state = LearningInteractionPolicy.RecordRecallAssessment(state, successful: true);
        }

        Assert.AreEqual(LearningInteractionPolicy.RequiredConsecutiveAssessments, state.ConsecutiveRecallSuccesses);
        Assert.AreEqual(LearningInteractionMode.Typing, state.InteractionMode);

        // One typing failure increments to 1
        state = LearningInteractionPolicy.RecordTypingAssessment(state, correct: false);
        Assert.AreEqual(1, state.ConsecutiveTypingFailures);

        // Another typing failure resets to Reading with 0 failures
        state = LearningInteractionPolicy.RecordTypingAssessment(state, correct: false);
        Assert.AreEqual(0, state.ConsecutiveTypingFailures);
        Assert.AreEqual(LearningInteractionMode.Reading, state.InteractionMode);
    }

    [TestMethod]
    public void Policy_HasNoDependencyOnSchedulerOrMastery()
    {
        var types = new[]
        {
            typeof(LearningInteractionProgress),
            typeof(LearningInteractionPolicy)
        };

        foreach (var type in types)
        {
            var members = type.GetMembers();
            foreach (var member in members)
            {
                Assert.IsFalse(member.Name.Contains("Schedule", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} mentioning 'Schedule'.");
                Assert.IsFalse(member.Name.Contains("Interval", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} mentioning 'Interval'.");
                Assert.IsFalse(member.Name.Contains("Master", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} mentioning 'Master'.");
                Assert.IsFalse(member.Name.Contains("Retired", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} mentioning 'Retired'.");
                Assert.IsFalse(member.Name.Contains("Extension", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} mentioning 'Extension'.");
            }
        }
    }
}
