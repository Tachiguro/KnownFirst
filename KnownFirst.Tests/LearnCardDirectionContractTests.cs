using KnownFirst.Core.Learning;
using KnownFirst.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearnCardDirectionContractTests
{
    [TestMethod]
    public void Learn_DirectionPresentation_BranchesOnDirectionAndNotOnlyInteractionMode()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // The top-level presentation branch must inspect CardDirection or an effective reading mode
        // that takes CardDirection into account, not just bare InteractionMode == Reading.
        Assert.IsTrue(
            markup.Contains("Direction == CardDirection.TermToMeaning", StringComparison.Ordinal)
            || markup.Contains("IsEffectiveReadingMode", StringComparison.Ordinal),
            "Learn.razor markup must branch on CardDirection or IsEffectiveReadingMode rather than raw InteractionMode alone.");
    }

    [TestMethod]
    public void Learn_TermToMeaning_NeverRendersSpellingInputEvenIfInteractionModeIsTyping()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // When Direction is TermToMeaning, typing must never be required.
        // The spelling form must only be rendered for MeaningToTerm with typing mode.
        Assert.IsTrue(
            markup.Contains("IsEffectiveReadingMode", StringComparison.Ordinal)
            || (markup.Contains("Direction == CardDirection.MeaningToTerm", StringComparison.Ordinal)
                && markup.Contains("InteractionMode == LearningInteractionMode.Typing", StringComparison.Ordinal)),
            "Learn.razor must gate spelling-form so it is never displayed for TermToMeaning cards.");
    }

    [TestMethod]
    public void Learn_TermToMeaning_ContextViewDoesNotHideTarget()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // In TermToMeaning, ContextView should show target (HideTarget="false")
        // In MeaningToTerm, ContextView should mask target (HideTarget="true")
        Assert.IsTrue(
            markup.Contains("HideTarget=\"false\"", StringComparison.Ordinal)
            && markup.Contains("HideTarget=\"true\"", StringComparison.Ordinal),
            "Learn.razor must render ContextView with HideTarget=\"false\" for TermToMeaning and HideTarget=\"true\" for MeaningToTerm.");
    }

    [TestMethod]
    public void Learn_MeaningToTerm_ReadingMode_RevealsTermAndDetailsOnReveal()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // In MeaningToTerm reading mode, front is meaning-prompt, and when revealed, term is exposed.
        Assert.IsTrue(
            markup.Contains("meaning-prompt", StringComparison.Ordinal),
            "Learn.razor must render meaning-prompt for MeaningToTerm.");
        Assert.IsTrue(
            markup.Contains("_card.Direction == CardDirection.MeaningToTerm", StringComparison.Ordinal)
            || markup.Contains("_card.Direction == CardDirection.TermToMeaning", StringComparison.Ordinal)
            || markup.Contains("IsEffectiveReadingMode", StringComparison.Ordinal),
            "Learn.razor must distinguish prompt and answer revelation based on card direction.");
        Assert.IsTrue(
            markup.Contains("learning-answer", StringComparison.Ordinal)
            || markup.Contains("<AnswerView", StringComparison.Ordinal),
            "Learn.razor must render answer when revealed.");
    }

    [TestMethod]
    public void Learn_KeyboardShortcutHandler_ConsidersEffectiveReadingMode()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // HandleLearningKeyDown must check IsEffectiveReadingMode or Direction for RevealAsync
        Assert.IsTrue(
            markup.Contains("IsEffectiveReadingMode", StringComparison.Ordinal)
            || markup.Contains("_card.Direction == CardDirection.TermToMeaning", StringComparison.Ordinal),
            "HandleLearningKeyDown must use direction-aware reading check for Enter/Space reveal.");
    }

    [TestMethod]
    public void Learn_ActionBar_UsesEffectiveReadingModeForRevealAndRating()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // The action bar must branch on IsEffectiveReadingMode rather than raw InteractionMode == Reading
        Assert.IsTrue(
            markup.Contains("IsEffectiveReadingMode", StringComparison.Ordinal)
            || (markup.Contains("_card.Direction == CardDirection.TermToMeaning", StringComparison.Ordinal)
                && markup.Contains("workflow-action-bar", StringComparison.Ordinal)),
            "Action bar in Learn.razor must branch on IsEffectiveReadingMode.");
    }

    [TestMethod]
    public void Learn_CheckSpellingAsync_GuardsAgainstTermToMeaning()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // CheckSpellingAsync must not execute for TermToMeaning
        Assert.IsTrue(
            markup.Contains("IsEffectiveReadingMode", StringComparison.Ordinal)
            || markup.Contains("_card.Direction == CardDirection.TermToMeaning", StringComparison.Ordinal),
            "CheckSpellingAsync must be protected against invocation on TermToMeaning.");
    }

    [TestMethod]
    public void ContextView_HiddenTarget_UsesDynamicTargetMaskPolicyInsteadOfHardCodedUnderscores()
    {
        var markup = UiWorkflowContractTests.LoadUi("ContextView.razor");

        // ContextView must not hardcode the literal five underscores "_____"
        Assert.IsFalse(
            markup.Contains("_____", StringComparison.Ordinal),
            "ContextView.razor must not hard-code a five-underscore placeholder ('_____').");

        // ContextView must use ContextTargetMaskPolicy to dynamically mask Context.Target
        Assert.IsTrue(
            markup.Contains("ContextTargetMaskPolicy.CreateMask(Context.Target)", StringComparison.Ordinal)
            || markup.Contains("ContextTargetMaskPolicy.CreateMask", StringComparison.Ordinal),
            "ContextView.razor must mask the target dynamically using ContextTargetMaskPolicy.CreateMask.");
    }
}
