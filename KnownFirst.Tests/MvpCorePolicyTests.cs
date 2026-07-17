using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Review;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Core.Workflow;
using KnownFirst.Models;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MvpCorePolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
    private readonly SimpleSpacedRepetitionScheduler _scheduler = new();

    [TestMethod]
    public void ReviewActions_OfferKnownUnknownAndUndoButNotIgnore()
    {
        CollectionAssert.AreEqual(
            new[] { ReviewAction.Known, ReviewAction.Unknown, ReviewAction.UndoPreviousDecision },
            ReviewActionPolicy.VisibleActions.ToArray());
    }

    [TestMethod]
    public void PrimaryNavigation_UsesLearnPrepareImportSettingsOrder()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                PrimaryNavigationAction.Learn,
                PrimaryNavigationAction.PrepareWords,
                PrimaryNavigationAction.ImportText,
                PrimaryNavigationAction.Settings
            },
            PrimaryNavigationPolicy.Actions.ToArray());
    }

    [TestMethod]
    public void MeaningPreview_LongTextIsBoundedWithoutChangingStoredText()
    {
        var stored = new string('x', 400);

        var closed = MeaningPreviewPolicy.CreateClosedPreview(stored);
        var alternative = MeaningPreviewPolicy.CreateAlternativePreview(stored);

        Assert.AreEqual(161, closed.Length);
        Assert.AreEqual(241, alternative.Length);
        Assert.AreEqual(400, stored.Length);
        Assert.IsTrue(MeaningPreviewPolicy.IsAlternativeTruncated(stored));
    }

    [TestMethod]
    public void ProviderFormRelations_ResolveOnlyExplicitSupportedRelations()
    {
        Assert.AreEqual("system", ProviderFormRelationPolicy.Resolve("plural of system")!.BaseLemma);
        Assert.AreEqual("risk", ProviderFormRelationPolicy.Resolve("plural form of risk")!.BaseLemma);
        Assert.AreEqual(
            "identify",
            ProviderFormRelationPolicy.Resolve("third-person singular simple present indicative of identify")!.BaseLemma);
        Assert.AreEqual("protect", ProviderFormRelationPolicy.Resolve("past participle of protect")!.BaseLemma);
        Assert.AreEqual("large", ProviderFormRelationPolicy.Resolve("comparative of large")!.BaseLemma);
        Assert.IsNull(ProviderFormRelationPolicy.Resolve("having the qualities of risk"));
    }

    [TestMethod]
    public void LookupOutcome_RetryIsLimitedToRecoverableFailures()
    {
        Assert.IsTrue(LexicalLookupOutcomePolicy.CanRetry(LexicalLookupStatus.TransientFailure));
        Assert.IsTrue(LexicalLookupOutcomePolicy.CanRetry(LexicalLookupStatus.ParseFailure));
        Assert.IsFalse(LexicalLookupOutcomePolicy.CanRetry(LexicalLookupStatus.Success));
        Assert.IsFalse(LexicalLookupOutcomePolicy.CanRetry(LexicalLookupStatus.NotFound));
        Assert.IsFalse(LexicalLookupOutcomePolicy.CanRetry(LexicalLookupStatus.PermanentFailure));
    }

    [TestMethod]
    public void Workflow_PrepareIsEnabledForBacklogOrActivePreparationAndBlockedByReview()
    {
        Assert.IsTrue(new WorkflowSnapshot(false, false, false, 0, 0, 1, WorkflowPrimaryAction.PrepareWords).CanPrepare);
        Assert.IsTrue(new WorkflowSnapshot(false, true, false, 0, 0, 0, WorkflowPrimaryAction.ContinuePreparation).CanPrepare);
        Assert.IsFalse(new WorkflowSnapshot(false, false, false, 0, 0, 0, WorkflowPrimaryAction.ImportText).CanPrepare);
        Assert.IsFalse(new WorkflowSnapshot(true, true, false, 0, 0, 1, WorkflowPrimaryAction.ContinueReview).CanPrepare);
    }

    [TestMethod]
    public void ActiveReview_BlocksImportPrepareAndLearnButNotSettings()
    {
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("import-text", true));
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("prepare-words", true));
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("learn", true));
        Assert.IsFalse(ReviewRoutePolicy.IsBlocked("settings", true));
        Assert.IsFalse(ReviewRoutePolicy.IsBlocked("review-words", true));
    }

    [TestMethod]
    public void LearnEnablement_DependsOnActiveDueOrPreparedCardsAndIsSuppressedByReview()
    {
        Assert.IsFalse(Workflow(false, false, 0, 0).CanLearn);
        Assert.IsTrue(Workflow(false, true, 0, 0).CanLearn);
        Assert.IsTrue(Workflow(false, false, 1, 0).CanLearn);
        Assert.IsTrue(Workflow(false, false, 0, 1).CanLearn);
        Assert.IsFalse(Workflow(true, true, 1, 1).CanLearn);
    }

    [TestMethod]
    public void PreparationLimitPolicy_AcceptsThirtyAndKeepsFiftyAsMaximum()
    {
        Assert.AreEqual(30, PreparationLimitPolicy.Normalize(30));
        CollectionAssert.AreEqual(new[] { 5, 10, 20, 30, 50 }, PreparationLimitPolicy.SupportedLimits.ToArray());
    }

    [TestMethod]
    public void CardDirectionPreference_DefaultsToBothDirections()
    {
        Assert.AreEqual(CardDirectionPreference.Both, CardDirectionPreferencePolicy.Normalize(-1));
        CollectionAssert.AreEqual(
            new[] { CardDirection.TermToMeaning, CardDirection.MeaningToTerm },
            CardDirectionPreferencePolicy.GetDirections(CardDirectionPreference.Both).ToArray());
    }

    [TestMethod]
    public void Acronym_LongFormThenAcronym_IsDetected()
    {
        var detector = new AcronymExpansionDetector();
        Assert.AreEqual(
            "Information Technology",
            detector.FindExpansion("Information Technology (IT) protects data.", "IT", TokenKind.Acronym));
    }

    [TestMethod]
    public void Acronym_AcronymThenLongForm_IsDetected()
    {
        var detector = new AcronymExpansionDetector();
        Assert.AreEqual(
            "Information Technology",
            detector.FindExpansion("IT (Information Technology) protects data.", "IT", TokenKind.Acronym));
    }

    [TestMethod]
    public void Acronym_MultiFactorAuthentication_IsDetected()
    {
        var detector = new AcronymExpansionDetector();
        Assert.AreEqual(
            "Multi-Factor Authentication",
            detector.FindExpansion(
                "Multi-Factor Authentication (MFA) reduces risk.",
                "MFA",
                TokenKind.Acronym));
    }

    [TestMethod]
    public void Acronym_OrdinaryUppercaseWordIsNotBlindlyConfirmed()
    {
        var detector = new AcronymExpansionDetector();
        Assert.IsNull(detector.FindExpansion("SECURITY protects data.", "SECURITY", TokenKind.Acronym));
        Assert.IsNull(detector.FindExpansion("Information Technology (IT).", "IT", TokenKind.Word));
    }

    [TestMethod]
    public void MeaningRanking_UsesTokenKindThenContextOverlapThenProviderOrder()
    {
        var ranker = new MeaningRanker();
        var ordinary = new LexicalMeaning(
            "ordinary",
            "noun",
            "Authentication risk is reduced.",
            null,
            null,
            []);
        var acronym = new LexicalMeaning(
            "acronym",
            "initialism",
            "A security abbreviation.",
            null,
            null,
            []);

        var acronymRanking = ranker.Rank(
            [ordinary, acronym],
            TokenKind.Acronym,
            "Authentication risk is reduced.");
        var wordRanking = ranker.Rank(
            [ordinary, ordinary with { MeaningId = "provider-second" }],
            TokenKind.Word,
            "Authentication risk is reduced.");

        Assert.AreEqual("acronym", acronymRanking[0].MeaningId);
        Assert.AreEqual("ordinary", wordRanking[0].MeaningId);
    }

    [TestMethod]
    public void PreparationSelection_UsesFrequencyFirstSeenAndAlphabeticalTieBreakers()
    {
        var candidates = new[]
        {
            Candidate(1, "zeta", 2, Now.AddMinutes(1)),
            Candidate(2, "beta", 4, Now.AddMinutes(2)),
            Candidate(3, "alpha", 4, Now.AddMinutes(2)),
            Candidate(4, "first", 4, Now)
        };

        var selected = PreparationSelectionPolicy.Select(candidates, 10);

        CollectionAssert.AreEqual(new[] { 4, 3, 2, 1 }, selected.Select(item => item.WordId).ToArray());
    }

    [TestMethod]
    public void PreparationSelection_ExcludesAnythingExceptResolvedUnknownUnpreparedItems()
    {
        var candidates = new[]
        {
            Candidate(1, "included", 1, Now),
            Candidate(2, "known", 5, Now) with { IsUnknown = false },
            Candidate(3, "prepared", 5, Now) with { State = PreparationState.Prepared },
            Candidate(4, "unresolved", 5, Now) with { ReviewIsResolved = false },
            Candidate(5, "duplicate", 5, Now) with { HasPreparedItem = true }
        };

        var selected = PreparationSelectionPolicy.Select(candidates, 10);

        Assert.HasCount(1, selected);
        Assert.AreEqual(1, selected[0].WordId);
    }

    [TestMethod]
    public void PreparationSelection_UsesConfiguredLimit()
    {
        var selected = PreparationSelectionPolicy.Select(
            Enumerable.Range(1, 20).Select(index => Candidate(index, $"word-{index}", index, Now)),
            5);

        Assert.HasCount(5, selected);
    }

    [TestMethod]
    public void PreparationSelection_HardMaximumIsFifty()
    {
        var selected = PreparationSelectionPolicy.Select(
            Enumerable.Range(1, 75).Select(index => Candidate(index, $"word-{index}", index, Now)),
            75);

        Assert.HasCount(50, selected);
    }

    [TestMethod]
    public void Spelling_ExactAnswerIsAccepted()
    {
        var result = new SpellingAnswerComparer().Compare("network", "network", [], TokenKind.Word, "en");
        Assert.IsTrue(result.IsCorrect);
    }

    [TestMethod]
    public void Spelling_AcceptedAliasIsAccepted()
    {
        var result = new SpellingAnswerComparer().Compare("net", "network", ["net"], TokenKind.Word, "en");
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual("net", result.MatchedAlias);
    }

    [TestMethod]
    public void Spelling_UnicodeNormalizationIsApplied()
    {
        var result = new SpellingAnswerComparer().Compare("Cafe\u0301", "Caf\u00E9", [], TokenKind.Word, "en");
        Assert.IsTrue(result.IsCorrect);
    }

    [TestMethod]
    public void Spelling_WrongAnswerHasReadableCharacterDifference()
    {
        var result = new SpellingAnswerComparer().Compare("netwark", "network", [], TokenKind.Word, "en");
        Assert.IsFalse(result.IsCorrect);
        Assert.Contains("a", result.Difference);
        Assert.Contains("o", result.Difference);
    }

    [TestMethod]
    public void Spelling_AcronymCaseErrorIsRejected()
    {
        var result = new SpellingAnswerComparer().Compare("mfa", "MFA", [], TokenKind.Acronym, "en");
        Assert.IsFalse(result.IsCorrect);
    }

    [TestMethod]
    public void Spelling_GermanNounCapitalizationErrorIsRejected()
    {
        var result = new SpellingAnswerComparer().Compare("netzwerk", "Netzwerk", [], TokenKind.Word, "de");
        Assert.IsFalse(result.IsCorrect);
    }

    [TestMethod]
    public void Scheduler_NewAgain_IsTenMinutes()
    {
        var result = ScheduleNew(ReviewRating.Again);
        Assert.AreEqual(CardState.Learning, result.State);
        Assert.AreEqual(Now.AddMinutes(10), result.DueAtUtc);
    }

    [TestMethod]
    public void Scheduler_NewHard_IsOneDay()
    {
        var result = ScheduleNew(ReviewRating.Hard);
        Assert.AreEqual(1, result.IntervalDays);
        Assert.AreEqual(Now.AddDays(1), result.DueAtUtc);
    }

    [TestMethod]
    public void Scheduler_NewGood_IsThreeDays()
    {
        var result = ScheduleNew(ReviewRating.Good);
        Assert.AreEqual(3, result.IntervalDays);
        Assert.AreEqual(Now.AddDays(3), result.DueAtUtc);
    }

    [TestMethod]
    public void Scheduler_NewEasy_IsSevenDays()
    {
        var result = ScheduleNew(ReviewRating.Easy);
        Assert.AreEqual(7, result.IntervalDays);
        Assert.AreEqual(Now.AddDays(7), result.DueAtUtc);
    }

    [TestMethod]
    public void Scheduler_ReviewAgain_EntersRelearningAndLowersEase()
    {
        var result = _scheduler.Schedule(ReviewSchedule(10, 2.5), ReviewRating.Again, Now);
        Assert.AreEqual(CardState.Relearning, result.State);
        Assert.AreEqual(2.3, result.EaseFactor, 0.0001);
        Assert.AreEqual(1, result.LapseCount);
        Assert.AreEqual(Now.AddMinutes(10), result.DueAtUtc);
    }

    [TestMethod]
    public void Scheduler_ReviewHardFormulaIsDeterministic()
    {
        var result = _scheduler.Schedule(ReviewSchedule(10, 2.5), ReviewRating.Hard, Now);
        Assert.AreEqual(12, result.IntervalDays);
        Assert.AreEqual(2.35, result.EaseFactor, 0.0001);
    }

    [TestMethod]
    public void Scheduler_ReviewGoodFormulaIsDeterministic()
    {
        var result = _scheduler.Schedule(ReviewSchedule(10, 2.5), ReviewRating.Good, Now);
        Assert.AreEqual(25, result.IntervalDays);
    }

    [TestMethod]
    public void Scheduler_ReviewEasyFormulaIsDeterministic()
    {
        var result = _scheduler.Schedule(ReviewSchedule(10, 2.5), ReviewRating.Easy, Now);
        Assert.AreEqual(34, result.IntervalDays);
        Assert.AreEqual(2.65, result.EaseFactor, 0.0001);
    }

    [TestMethod]
    public void Scheduler_MinimumEaseIsOnePointThree()
    {
        var hard = _scheduler.Schedule(ReviewSchedule(10, 1.35), ReviewRating.Hard, Now);
        var again = _scheduler.Schedule(ReviewSchedule(10, 1.35), ReviewRating.Again, Now);
        Assert.AreEqual(1.3, hard.EaseFactor, 0.0001);
        Assert.AreEqual(1.3, again.EaseFactor, 0.0001);
    }

    [TestMethod]
    public void Scheduler_IntervalsContinueBeyondSevenAndFourteenDays()
    {
        var result = _scheduler.Schedule(ReviewSchedule(10, 2.5), ReviewRating.Good, Now);
        Assert.IsGreaterThan(14, result.IntervalDays);
        Assert.AreEqual(CardState.Review, result.State);
    }

    [TestMethod]
    public void Scheduler_NoFixedIntervalMarksPermanentlyKnown()
    {
        var result = _scheduler.Schedule(ReviewSchedule(365, 2.5), ReviewRating.Easy, Now);
        Assert.AreEqual(CardState.Review, result.State);
    }

    private CardSchedule ScheduleNew(ReviewRating rating) =>
        _scheduler.Schedule(CardSchedule.New(Now), rating, Now);

    private static CardSchedule ReviewSchedule(int intervalDays, double easeFactor) => new(
        CardState.Review,
        Now,
        intervalDays,
        easeFactor,
        3,
        0,
        Now.AddDays(-intervalDays),
        ReviewRating.Good);

    private static PreparationSelectionCandidate Candidate(
        int wordId,
        string term,
        int frequency,
        DateTime firstSeen) => new(
        wordId,
        term,
        frequency,
        firstSeen,
        true,
        PreparationState.Unprepared,
        true,
        false);

    private static WorkflowSnapshot Workflow(
        bool activeReview,
        bool activeLearning,
        int dueCards,
        int preparedItems) => new(
        activeReview,
        false,
        activeLearning,
        dueCards,
        preparedItems,
        0,
        WorkflowPrimaryAction.ImportText);
}
