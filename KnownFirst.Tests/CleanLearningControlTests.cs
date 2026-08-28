using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class CleanLearningControlTests
{
    private static readonly DateTime ValidUtc = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterUtc = new(2026, 8, 28, 13, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void AlreadyKnownDecision_WithUtcTimestamp_Succeeds()
    {
        var decision = new AlreadyKnownDecision(ValidUtc);
        Assert.AreEqual(ValidUtc, decision.DecidedAtUtc);
    }

    [TestMethod]
    [DataRow(DateTimeKind.Local)]
    [DataRow(DateTimeKind.Unspecified)]
    public void AlreadyKnownDecision_WithNonUtcTimestamp_ThrowsArgumentException(DateTimeKind kind)
    {
        var timestamp = new DateTime(2026, 8, 28, 12, 0, 0, kind);
        Assert.ThrowsExactly<ArgumentException>(() => new AlreadyKnownDecision(timestamp));
    }

    [TestMethod]
    public void StopLearningDecision_WithUtcTimestamp_Succeeds()
    {
        var decision = new StopLearningDecision(ValidUtc);
        Assert.AreEqual(ValidUtc, decision.DecidedAtUtc);
    }

    [TestMethod]
    [DataRow(DateTimeKind.Local)]
    [DataRow(DateTimeKind.Unspecified)]
    public void StopLearningDecision_WithNonUtcTimestamp_ThrowsArgumentException(DateTimeKind kind)
    {
        var timestamp = new DateTime(2026, 8, 28, 12, 0, 0, kind);
        Assert.ThrowsExactly<ArgumentException>(() => new StopLearningDecision(timestamp));
    }

    [TestMethod]
    public void WordLearningControl_MarkAlreadyKnown_IsIdempotent_PreservesOriginalTimestamp()
    {
        var control = WordLearningControl.Default;
        Assert.IsFalse(control.IsAlreadyKnown);
        Assert.IsNull(control.AlreadyKnown);

        var marked = control.MarkAlreadyKnown(ValidUtc);
        Assert.IsTrue(marked.IsAlreadyKnown);
        Assert.IsNotNull(marked.AlreadyKnown);
        Assert.AreEqual(ValidUtc, marked.AlreadyKnown.DecidedAtUtc);

        var markedAgain = marked.MarkAlreadyKnown(LaterUtc);
        Assert.AreSame(marked, markedAgain);
        Assert.AreEqual(ValidUtc, markedAgain.AlreadyKnown!.DecidedAtUtc);

        var explicitDecision = new AlreadyKnownDecision(LaterUtc);
        var markedAgainWithDecision = marked.MarkAlreadyKnown(explicitDecision);
        Assert.AreSame(marked, markedAgainWithDecision);
        Assert.AreEqual(ValidUtc, markedAgainWithDecision.AlreadyKnown!.DecidedAtUtc);
    }

    [TestMethod]
    public void WordLearningControl_ClearAlreadyKnown_RestoresNormalControlState()
    {
        var control = WordLearningControl.Default.MarkAlreadyKnown(ValidUtc);
        Assert.IsTrue(control.IsAlreadyKnown);

        var cleared = control.ClearAlreadyKnown();
        Assert.IsFalse(cleared.IsAlreadyKnown);
        Assert.IsNull(cleared.AlreadyKnown);

        var clearedAgain = cleared.ClearAlreadyKnown();
        Assert.AreSame(cleared, clearedAgain);
    }

    [TestMethod]
    public void SenseLearningControl_StopAndResume_TogglesStopState()
    {
        var control = SenseLearningControl.Default;
        Assert.IsFalse(control.IsStopped);
        Assert.IsNull(control.StopLearning);

        var stopped = control.Stop(ValidUtc);
        Assert.IsTrue(stopped.IsStopped);
        Assert.IsNotNull(stopped.StopLearning);
        Assert.AreEqual(ValidUtc, stopped.StopLearning.DecidedAtUtc);

        var stoppedAgain = stopped.Stop(LaterUtc);
        Assert.AreSame(stopped, stoppedAgain);
        Assert.AreEqual(ValidUtc, stoppedAgain.StopLearning!.DecidedAtUtc);

        var resumed = stopped.Resume();
        Assert.IsFalse(resumed.IsStopped);
        Assert.IsNull(resumed.StopLearning);

        var resumedAgain = resumed.Resume();
        Assert.AreSame(resumed, resumedAgain);
    }

    [TestMethod]
    public void ActiveLearningEligibilityPolicy_AlreadyKnown_GatesAllSenseEligibility()
    {
        var wordControl = WordLearningControl.Default.MarkAlreadyKnown(ValidUtc);
        var senseControlActive = SenseLearningControl.Default;
        var senseControlStopped = SenseLearningControl.Default.Stop(ValidUtc);

        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseControlActive));
        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseControlStopped));
    }

    [TestMethod]
    public void ActiveLearningEligibilityPolicy_StopLearning_GatesOnlySelectedSense()
    {
        var wordControl = WordLearningControl.Default;
        var sense1 = SenseLearningControl.Default;
        var sense2 = SenseLearningControl.Default.Stop(ValidUtc);

        Assert.IsTrue(ActiveLearningEligibilityPolicy.IsEligible(wordControl, sense1));
        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, sense2));
    }

    [TestMethod]
    public void ActiveLearningEligibilityPolicy_Resume_RestoresEligibility()
    {
        var wordControl = WordLearningControl.Default;
        var sense = SenseLearningControl.Default.Stop(ValidUtc);

        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, sense));

        var resumedSense = sense.Resume();
        Assert.IsTrue(ActiveLearningEligibilityPolicy.IsEligible(wordControl, resumedSense));
    }

    [TestMethod]
    public void ActiveLearningEligibilityPolicy_ClearAlreadyKnown_RestoresEligibility()
    {
        var wordControl = WordLearningControl.Default.MarkAlreadyKnown(ValidUtc);
        var sense = SenseLearningControl.Default;

        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, sense));

        var clearedWordControl = wordControl.ClearAlreadyKnown();
        Assert.IsTrue(ActiveLearningEligibilityPolicy.IsEligible(clearedWordControl, sense));
    }

    [TestMethod]
    public void ActiveLearningEligibilityPolicy_MultipleSenses_RemainIndependent()
    {
        var wordControl = WordLearningControl.Default;
        var senseA = SenseLearningControl.Default;
        var senseB = SenseLearningControl.Default.Stop(ValidUtc);

        Assert.IsTrue(ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseA));
        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseB));

        var senseAStopped = senseA.Stop(ValidUtc);
        var senseBResumed = senseB.Resume();

        Assert.IsFalse(ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseAStopped));
        Assert.IsTrue(ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseBResumed));
    }

    [TestMethod]
    public void AlreadyKnownAndStopLearning_AreIndependentlyRepresentable()
    {
        var word = WordLearningControl.Default.MarkAlreadyKnown(ValidUtc);
        var sense = SenseLearningControl.Default.Stop(LaterUtc);

        Assert.IsTrue(word.IsAlreadyKnown);
        Assert.AreEqual(ValidUtc, word.AlreadyKnown!.DecidedAtUtc);
        Assert.IsTrue(sense.IsStopped);
        Assert.AreEqual(LaterUtc, sense.StopLearning!.DecidedAtUtc);
    }

    [TestMethod]
    public void PreparationCandidateDisposition_Excluded_IsWorkflowCandidateDisposition_DoesNotCreateWordIgnoreDomainState()
    {
        var disposition = PreparationCandidateDisposition.Excluded;
        Assert.AreEqual(PreparationCandidateDisposition.Excluded, disposition);

        var names = Enum.GetNames<PreparationCandidateDisposition>();
        CollectionAssert.Contains(names, "Excluded");
        CollectionAssert.DoesNotContain(names, "Ignored");
        CollectionAssert.DoesNotContain(names, "PermanentIgnore");
        CollectionAssert.DoesNotContain(names, "Mastered");
        CollectionAssert.DoesNotContain(names, "Retired");
    }

    [TestMethod]
    public void CleanLearningControlTypes_DoNotExposeMasteredOrRetiredSemantics()
    {
        var types = new[]
        {
            typeof(WordLearningControl),
            typeof(SenseLearningControl),
            typeof(AlreadyKnownDecision),
            typeof(StopLearningDecision),
            typeof(ActiveLearningEligibilityPolicy),
            typeof(PreparationCandidateDisposition),
            typeof(AnswerVariantRole)
        };

        foreach (var type in types)
        {
            var members = type.GetMembers();
            foreach (var member in members)
            {
                Assert.IsFalse(member.Name.Contains("Mastered", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} containing 'Mastered'.");
                Assert.IsFalse(member.Name.Contains("Retired", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} containing 'Retired'.");
                Assert.IsFalse(member.Name.Contains("Ignore", StringComparison.OrdinalIgnoreCase),
                    $"Type {type.Name} exposes member {member.Name} containing 'Ignore'.");
            }
        }
    }

    [TestMethod]
    public void AnswerVariantRole_RequiredAndAcceptedOnly_RetainIntendedDistinction_WithoutCardIdentity()
    {
        var required = AnswerVariantRole.Required;
        var acceptedOnly = AnswerVariantRole.AcceptedOnly;

        Assert.AreNotEqual(required, acceptedOnly);
        Assert.AreEqual(0, (int)required);
        Assert.AreEqual(1, (int)acceptedOnly);

        var names = Enum.GetNames<AnswerVariantRole>();
        Assert.AreEqual(2, names.Length);
        CollectionAssert.AreEqual(new[] { "Required", "AcceptedOnly" }, names);
    }
}
