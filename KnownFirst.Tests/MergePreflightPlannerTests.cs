using System.Globalization;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using static KnownFirst.Tests.MergePreflightFixtures;


namespace KnownFirst.Tests;

/// <summary>
/// Focused tests for the pure, read-only <see cref="MergePreflightPlanner"/> (KF-BACKUP-002 Slice 3,
/// corrected for the approved meaning-centric model — see
/// <c>docs/architecture/backup-merge-v1-design.md</c> §17). Expected keys/counts are computed
/// independently of the planner (either by direct reasoning about the fixture data, or by calling the
/// Slice-1/SemanticMeaning identity policies directly — never by invoking the planner a second time to
/// produce the "expected" value). <see cref="PreparedItem"/>'s default <c>definition</c> ("a definition")
/// is <b>not</b> itself a reliable sense discriminator (Definition wording alone never is — see
/// <c>docs/architecture/backup-merge-v1-design.md</c> §18); fixtures below that need to avoid an
/// accidental <see cref="SemanticMeaningGroupingDecision"/> use an explicit <c>ProviderMeaningId</c> (or an
/// identical Definition plus identical everything else, which is not itself ambiguous) rather than relying
/// on Definition presence.
/// </summary>
[TestClass]
public sealed class MergePreflightPlannerTests
{
    [TestMethod]
    public void ExactAllDuplicateArchive_ProducesNoChanges()
    {
        var targetVocab = Vocabulary("v-t", term: "shared", knowledgeState: BackupKnowledgeState.Known, preparationState: BackupPreparationState.Prepared);
        var targetDoc = SourceMaterial("sm-t", "hash-shared");
        var targetMeaning = PreparedItem("p-t", "v-t", definition: "same meaning");
        var targetCard = Card("c-t", "v-t", "p-t");
        var targetReview = Review("c-t");

        var archiveVocab = Vocabulary("v-a", term: "shared", knowledgeState: BackupKnowledgeState.Known, preparationState: BackupPreparationState.Prepared);
        var archiveDoc = SourceMaterial("sm-a", "hash-shared");
        var archiveMeaning = PreparedItem("p-a", "v-a", definition: "same meaning");
        var archiveCard = Card("c-a", "v-a", "p-a");
        var archiveReview = Review("c-a");

        var target = Payload([targetDoc], [targetVocab], [targetMeaning], [targetCard], [targetReview]);
        var archive = Payload([archiveDoc], [archiveVocab], [archiveMeaning], [archiveCard], [archiveReview]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsEmpty(plan.BlockingPrerequisites);
        foreach (var counts in plan.PerEntity.Values)
        {
            Assert.AreEqual(0, counts.NewCount);
            Assert.AreEqual(0, counts.EnrichedCount);
            Assert.AreEqual(0, counts.PreservedVariantCount);
            Assert.AreEqual(0, counts.UnresolvedConflictCount);
        }
    }

    [TestMethod]
    public void ArchiveOnlyCompleteGraph_ProducesReady()
    {
        var target = EmptyPayload();

        var vocab = Vocabulary("v-1", term: "newword");
        var doc = SourceMaterial("sm-1", "hash-new");
        var meaning = PreparedItem("p-1", "v-1");
        var card = Card("c-1", "v-1", "p-1");
        var review = Review("c-1");
        var reviewWorkflow = ReviewWorkflow("vr-1", "sm-1", [ReviewItem("rc-1", "v-1")]);
        var prepWorkflow = PreparationWorkflow("pb-1", items: [PreparationItem("pi-1", "v-1")]);
        var learningWorkflow = LearningWorkflow("ls-1", [QueueItem("lq-1", "c-1")]);

        var archive = Payload(
            [doc], [vocab], [meaning], [card], [review],
            [reviewWorkflow], [prepWorkflow], [learningWorkflow]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsEmpty(plan.BlockingPrerequisites);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.SourceMaterial].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparationWorkflow].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparationItem].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningWorkflow].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningQueueItem].NewCount);
    }

    [TestMethod]
    public void TargetOnlyVocabulary_RemainsUntouched()
    {
        var targetOnly = Vocabulary("v-only-target", term: "onlyontarget");
        var target = Payload(vocabulary: [targetOnly]);
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "differentword")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        var targetOnlyIdentity = VocabularyMergeIdentityPolicy.Compute(targetOnly).Value;
        Assert.IsFalse(plan.Actions.Any(a => a.StableIdentity == targetOnlyIdentity));
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
    }

    [TestMethod]
    public void HigherMonotonicWordProgress_IsClassifiedEnriched()
    {
        var target = Payload(vocabulary: [Vocabulary("v-t", term: "advance", knowledgeState: BackupKnowledgeState.Unreviewed, preparationState: BackupPreparationState.Unprepared)]);
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "advance", knowledgeState: BackupKnowledgeState.Known, preparationState: BackupPreparationState.Unprepared)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].EnrichedCount);
        Assert.AreEqual("vocabulary-progress-advanced", plan.Actions.Single(a => a.EntityKind == MergeEntityKind.Vocabulary).ReasonCode);
    }

    [TestMethod]
    public void EqualWordProgress_IsNoOp()
    {
        var target = Payload(vocabulary: [Vocabulary("v-t", term: "same", knowledgeState: BackupKnowledgeState.Known, preparationState: BackupPreparationState.Prepared)]);
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "same", knowledgeState: BackupKnowledgeState.Known, preparationState: BackupPreparationState.Prepared)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].ExactDuplicateSkippedCount);
    }

    [DataTestMethod]
    [DataRow(BackupKnowledgeState.Known, BackupKnowledgeState.UnknownBacklog)]
    [DataRow(BackupKnowledgeState.Known, BackupKnowledgeState.Ignored)]
    [DataRow(BackupKnowledgeState.UnknownBacklog, BackupKnowledgeState.Ignored)]
    public void SameTierKnowledgeStateConflicts_RequireDecision(BackupKnowledgeState targetState, BackupKnowledgeState archiveState)
    {
        var target = Payload(vocabulary: [Vocabulary("v-t", term: "conflict", knowledgeState: targetState)]);
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "conflict", knowledgeState: archiveState)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].UnresolvedConflictCount);
        Assert.HasCount(1, plan.KnowledgeStateConflictDecisions);
        Assert.AreEqual(targetState, plan.KnowledgeStateConflictDecisions[0].TargetState);
        Assert.AreEqual(archiveState, plan.KnowledgeStateConflictDecisions[0].ArchiveState);
    }

    [TestMethod]
    public void ExactMeaningDeduplication_SkipsInsert()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "the same definition")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "the same definition")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].ExactDuplicateSkippedCount);
    }

    [TestMethod]
    public void DistinctDefinitions_NoReliableDiscriminator_CreatesGroupingDecision_NeverAutoSplits()
    {
        // Final focused review correction: Definition wording alone is not a reliable sense discriminator
        // (checklist item 3). With no ProviderMeaningId/topic/grammar/acronym on either side, differing
        // Definition wording must raise a blocking SemanticMeaningGroupingDecision, never silently split
        // into two distinct SemanticMeanings.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "a definition")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "a completely different definition")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].UnresolvedConflictCount);
        Assert.HasCount(1, plan.SemanticMeaningGroupingDecisions);
        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
    }

    [TestMethod]
    public void SameProviderSenseId_DifferentDefinitions_OneSemanticMeaning_TwoExactVariants_NoGroupingDecision()
    {
        // Checklist item 1: same stable provider sense id, differently-worded Definition text — one
        // SemanticMeaning, preserved as a second exact-content variant, never a grouping decision.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "a place that holds money") with { ProviderMeaningId = "sense-42" }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "a financial institution") with { ProviderMeaningId = "sense-42" }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    [TestMethod]
    public void DifferentProviderSenseIds_AreDistinctSemanticMeanings()
    {
        // Checklist item 5: different stable provider sense ids are strong distinct-sense evidence.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "a definition") with { ProviderMeaningId = "sense-1" }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "a definition") with { ProviderMeaningId = "sense-2" }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    [TestMethod]
    public void DifferentExplanationLanguages_RemainDistinctSemanticMeanings()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "German meaning", explanationLanguage: "de")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "German meaning", explanationLanguage: "ru")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount);
    }

    // ---- Defect 1: Translation must not, by itself, split (or silently merge) a SemanticMeaning ----

    [TestMethod]
    public void TranslationTextAlone_DoesNotSplitSemanticMeaning_WhenAReliableDiscriminatorExists()
    {
        // A real reliable discriminator (stable provider sense id) on both sides; only Translation
        // differs. Final focused review correction: matching Definition text alone is NOT a reliable
        // discriminator (see DistinctDefinitions_NoReliableDiscriminator_CreatesGroupingDecision_
        // NeverAutoSplits and SameWordSameLanguage_NoReliableDiscriminator_DifferingTranslation_
        // CreatesGroupingDecision below) — a genuine discriminator is required for this scenario.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "financial institution", translation: "Bank") with { ProviderMeaningId = "sense-42" }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "financial institution", translation: "Sparkasse") with { ProviderMeaningId = "sense-42" }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        // Same SemanticMeaning (same provider sense id); distinct Translation is a preserved exact-content
        // variant, never a second semantic meaning, and never an ambiguous grouping decision.
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    [TestMethod]
    public void MatchingDefinitionText_WithoutReliableDiscriminator_DifferingTranslation_StillCreatesGroupingDecision()
    {
        // Guards against reintroducing the corrected defect: identical Definition wording on both sides is
        // NOT itself a reliable sense discriminator. With no ProviderMeaningId/topic/grammar/acronym on
        // either side, this must still be ambiguous — never silently merged just because the wording
        // happens to match.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "financial institution", translation: "Bank")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "financial institution", translation: "Sparkasse")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.HasCount(1, plan.SemanticMeaningGroupingDecisions);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);
    }

    [TestMethod]
    public void SameWordSameLanguage_NoReliableDiscriminator_DifferingTranslation_CreatesGroupingDecision()
    {
        // Neither side has Definition, ProviderMeaningId, GrammaticalRelationship, or AcronymExpansion —
        // only Translation differs. The planner must not guess.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: null, translation: "bank (river)")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: null, translation: "bank (financial)")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].UnresolvedConflictCount);
        Assert.HasCount(1, plan.SemanticMeaningGroupingDecisions);
        var decision = plan.SemanticMeaningGroupingDecisions[0];
        Assert.AreEqual("bank (river)", decision.TargetSummary.Translation);
        Assert.AreEqual("bank (financial)", decision.ArchiveSummary.Translation);
        Assert.IsNull(decision.TargetSummary.Definition);
        CollectionAssert.AreEquivalent(
            new[] { SemanticMeaningGroupingChoice.TreatAsSameSemanticMeaning, SemanticMeaningGroupingChoice.TreatAsDistinctSemanticMeanings },
            decision.AvailableChoices.ToArray());
    }

    [TestMethod]
    public void SameWordSameLanguage_ProviderSenseIdPresent_DifferingTranslation_NoGroupingDecision()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: null, translation: "bank (river)") with { ProviderMeaningId = "sense-42" }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: null, translation: "bank (financial)") with { ProviderMeaningId = "sense-42" }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);
    }

    [TestMethod]
    public void ExactFullContentDuplicate_StillDeduplicates_EvenWithoutReliableDiscriminator()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: null, translation: "same translation")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: null, translation: "same translation")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].ExactDuplicateSkippedCount);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    [TestMethod]
    public void NotesExamplesProvenanceDifferences_PreserveExactVariantWithoutSecondSemanticCard()
    {
        // ProviderMeaningId keeps both sides unambiguously the same SemanticMeaning, isolating the
        // note-preservation behavior under test from the unrelated SemanticMeaningGroupingDecision.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "financial institution") with { ProviderMeaningId = "sense-1" }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "financial institution") with { ProviderMeaningId = "sense-1", AdditionalNote = "colloquial usage" }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    // ---- Defect 2: FutureCardIdentity matching + physical-slot collision ----

    [TestMethod]
    public void MatchedCard_SameFutureCardIdentity_IsMatchedNotDuplicatedAsNew()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "financial institution")],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "financial institution")],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].ExactDuplicateSkippedCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningCard].NewCount);
    }

    [TestMethod]
    public void DistinctSemanticMeaning_SamePhysicalSlot_IsNewCard_NeverPreservedVariant_AndBlocksExecution()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "German meaning", explanationLanguage: "de")],
            cards: [Card("c-t", "v-t", "p-t", direction: BackupCardDirection.TermToMeaning)]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "Russian meaning", explanationLanguage: "ru")],
            cards: [Card("c-a", "v-a", "p-a", direction: BackupCardDirection.TermToMeaning)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        // Never a non-blocking preserved variant: this is a distinct planned future card.
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningCard].PreservedVariantCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningCard].UnresolvedConflictCount);

        var cardAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningCard);
        Assert.AreEqual("learning-card-new-future-card-physical-slot-collision", cardAction.ReasonCode);

        Assert.AreEqual(MergePreflightStatus.BlockedByPrerequisite, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.IsTrue(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.MeaningCardSchemaMigrationRequired));
        Assert.IsTrue(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.ArchiveFormatMigrationRequired));
    }

    [TestMethod]
    public void DistinctSemanticMeaning_NoPhysicalCollision_IsPlainNewCard_NoBlockingPrerequisite()
    {
        // Archive's card is for a Direction the target never used at this word — no physical slot
        // collision, so this is a plain new future card, not blocked.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "German meaning")],
            cards: [Card("c-t", "v-t", "p-t", direction: BackupCardDirection.TermToMeaning)]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "German meaning")],
            cards: [Card("c-a", "v-a", "p-a", direction: BackupCardDirection.MeaningToTerm)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].NewCount);
        Assert.AreEqual("learning-card-new", plan.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningCard).ReasonCode);
        Assert.IsEmpty(plan.BlockingPrerequisites);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
    }

    // ---- Defect 3: meaning-aware review-event fingerprint ----

    [TestMethod]
    public void ExactReviewEventDuplicate_IsDeduplicated()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t")],
            cards: [Card("c-t", "v-t", "p-t")],
            reviews: [Review("c-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a")],
            cards: [Card("c-a", "v-a", "p-a")],
            reviews: [Review("c-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].NewCount);
    }

    [TestMethod]
    public void SameTimestampDistinctReviewEvents_BothRetained()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t")],
            cards: [Card("c-t", "v-t", "p-t")],
            reviews: [Review("c-t", rating: BackupReviewRating.Good, wasCorrect: true)]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a")],
            cards: [Card("c-a", "v-a", "p-a")],
            reviews: [Review("c-a", rating: BackupReviewRating.Hard, wasCorrect: false)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
    }

    [TestMethod]
    public void DifferentSemanticMeanings_SameWordDirectionTimestampRatingOutcome_ReviewEventsRemainDistinct()
    {
        // Same Word, same Direction, and every review field identical (timestamp/rating/outcome) — but
        // the two cards reference different SemanticMeanings. The meaning-aware fingerprint must still
        // keep these two events distinct.
        // Distinct ProviderMeaningId (not merely differing Definition text — see the final focused review
        // correction) is what makes these two genuinely distinct SemanticMeanings.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "German meaning") with { ProviderMeaningId = "sense-de" }],
            cards: [Card("c-t", "v-t", "p-t", direction: BackupCardDirection.TermToMeaning)],
            reviews: [Review("c-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "Russian meaning") with { ProviderMeaningId = "sense-ru" }],
            cards: [Card("c-a", "v-a", "p-a", direction: BackupCardDirection.TermToMeaning)],
            reviews: [Review("c-a")]); // identical CardId label, ReviewedAtUtc, Rating, WasTypedAnswer, WasCorrect, DueAtUtc, IntervalDays, EaseFactor as target's

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount, "Distinct SemanticMeaning must keep the review event distinct despite identical other fields.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
    }

    [TestMethod]
    public void ReviewEventInputOrder_DoesNotAffectFingerprintsOrActionOrder()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t")],
            cards: [Card("c-t", "v-t", "p-t")]);

        var reviewA = Review("c-t", rating: BackupReviewRating.Good, reviewedAtUtc: BaseTime.AddDays(1));
        var reviewB = Review("c-t", rating: BackupReviewRating.Hard, reviewedAtUtc: BaseTime.AddDays(2));

        var archiveInOrder = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a")],
            cards: [Card("c-a", "v-a", "p-a")],
            reviews: [Review("c-a", rating: BackupReviewRating.Good, reviewedAtUtc: BaseTime.AddDays(1)), Review("c-a", rating: BackupReviewRating.Hard, reviewedAtUtc: BaseTime.AddDays(2))]);
        var archiveReversed = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a")],
            cards: [Card("c-a", "v-a", "p-a")],
            reviews: [Review("c-a", rating: BackupReviewRating.Hard, reviewedAtUtc: BaseTime.AddDays(2)), Review("c-a", rating: BackupReviewRating.Good, reviewedAtUtc: BaseTime.AddDays(1))]);

        var planInOrder = MergePreflightPlanner.CreatePlan(target, archiveInOrder, Manifest());
        var planReversed = MergePreflightPlanner.CreatePlan(target, archiveReversed, Manifest());

        var identitiesInOrder = planInOrder.Actions.Where(a => a.EntityKind == MergeEntityKind.LearningReview).Select(a => a.StableIdentity).ToList();
        var identitiesReversed = planReversed.Actions.Where(a => a.EntityKind == MergeEntityKind.LearningReview).Select(a => a.StableIdentity).ToList();

        CollectionAssert.AreEqual(identitiesInOrder, identitiesReversed);
    }

    [TestMethod]
    public void DocumentDuplicateWithDifferentTitle_StillExactDuplicate()
    {
        var target = Payload(sourceMaterials: [SourceMaterial("sm-t", "shared-hash", title: "Target Title")]);
        var archive = Payload(sourceMaterials: [SourceMaterial("sm-a", "shared-hash", title: "Archive Title")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.SourceMaterial].ExactDuplicateSkippedCount);
    }

    [TestMethod]
    public void SameTextDifferentLookupMode_BothPreserved()
    {
        var target = Payload(sourceMaterials: [SourceMaterial("sm-t", "shared-hash", lookupMode: BackupLexicalLookupMode.Definition, targetLanguage: null)]);
        var archive = Payload(sourceMaterials: [SourceMaterial("sm-a", "shared-hash", lookupMode: BackupLexicalLookupMode.Translation, targetLanguage: "de")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.SourceMaterial].NewCount);
    }

    [TestMethod]
    public void MissingParentReference_FailsClosed()
    {
        var target = EmptyPayload();
        var archive = Payload(preparedLearning: [PreparedItem("p-orphan", "v-missing")]);

        Assert.ThrowsExactly<KeyNotFoundException>(() => MergePreflightPlanner.CreatePlan(target, archive, Manifest()));
    }

    [TestMethod]
    public void AmbiguousParentReference_FailsClosed()
    {
        var target = EmptyPayload();
        var archive = Payload(vocabulary: [Vocabulary("v-dup", term: "a"), Vocabulary("v-dup", term: "b")]);

        var exception = Assert.ThrowsExactly<MergePlanningException>(() => MergePreflightPlanner.CreatePlan(target, archive, Manifest()));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public void SampleDetails_AreBoundedAtTwenty()
    {
        var target = EmptyPayload();
        var archiveVocabulary = Enumerable.Range(1, 25)
            .Select(i => Vocabulary($"v-{i}", term: $"word{i}"))
            .ToList();
        var archive = Payload(vocabulary: archiveVocabulary);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(25, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        Assert.AreEqual(MergePreflightPlan.MaxSampleDetailsPerCategory, plan.SampleDetails[MergeEntityClassification.New].Count);
    }

    [TestMethod]
    public void DeterministicRepeatedPlan_ProducesEquivalentOutputRegardlessOfInputOrder()
    {
        var wordA = Vocabulary("v-a", term: "alpha");
        var wordB = Vocabulary("v-b", term: "beta");
        var wordC = Vocabulary("v-c", term: "gamma");
        var target = EmptyPayload();

        var archiveInOrder = Payload(vocabulary: [wordA, wordB, wordC]);
        var archiveReversed = Payload(vocabulary: [wordC, wordB, wordA]);

        var plan1 = MergePreflightPlanner.CreatePlan(target, archiveInOrder, Manifest());
        var plan2 = MergePreflightPlanner.CreatePlan(target, archiveReversed, Manifest());

        AssertPlansEquivalent(plan1, plan2);
    }

    [TestMethod]
    public void NonEnglishCurrentCulture_DoesNotAffectPlan()
    {
        var target = EmptyPayload();
        var archive = Payload(
            vocabulary: [Vocabulary("v-1", language: "en", term: "Info")],
            sourceMaterials: [SourceMaterial("sm-1", "hash-1")]);

        var originalCulture = CultureInfo.CurrentCulture;
        MergePreflightPlan invariantPlan;
        MergePreflightPlan turkishPlan;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantPlan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            turkishPlan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        AssertPlansEquivalent(invariantPlan, turkishPlan);
    }

    [TestMethod]
    public void PcPhoneDivergenceScenario_MatchesDesignSection10ExpectedCounts()
    {
        var targetQuarantine = Vocabulary("v-t-quarantine", term: "quarantine", knowledgeState: BackupKnowledgeState.Prepared, preparationState: BackupPreparationState.Prepared);
        var targetOutbreak = Vocabulary("v-t-outbreak", term: "outbreak", knowledgeState: BackupKnowledgeState.Prepared, preparationState: BackupPreparationState.Prepared);
        var targetQuarantineMeaning = PreparedItem("p-t-quarantine-de", "v-t-quarantine", definition: "Schutz vor Gefahr", explanationLanguage: "de");
        var targetOutbreakMeaning = PreparedItem("p-t-outbreak-de", "v-t-outbreak", definition: "Ausbruch einer Krankheit", explanationLanguage: "de");
        var targetQuarantineCard = Card("c-t-quarantine", "v-t-quarantine", "p-t-quarantine-de", direction: BackupCardDirection.TermToMeaning);
        var targetOutbreakCard = Card("c-t-outbreak", "v-t-outbreak", "p-t-outbreak-de", direction: BackupCardDirection.TermToMeaning);
        var targetBaselineReview1 = Review("c-t-quarantine", reviewedAtUtc: BaseTime.AddDays(1));
        var targetBaselineReview2 = Review("c-t-quarantine", reviewedAtUtc: BaseTime.AddDays(2));
        var targetBaselineReview3 = Review("c-t-quarantine", reviewedAtUtc: BaseTime.AddDays(3));
        var targetPhoneOnlyReview = Review("c-t-quarantine", rating: BackupReviewRating.Hard, wasCorrect: false, reviewedAtUtc: BaseTime.AddDays(4));

        var target = Payload(
            vocabulary: [targetQuarantine, targetOutbreak],
            preparedLearning: [targetQuarantineMeaning, targetOutbreakMeaning],
            cards: [targetQuarantineCard, targetOutbreakCard],
            reviews: [targetBaselineReview1, targetBaselineReview2, targetBaselineReview3, targetPhoneOnlyReview]);

        var archiveQuarantine = Vocabulary("v-a-quarantine", term: "quarantine", knowledgeState: BackupKnowledgeState.Prepared, preparationState: BackupPreparationState.Prepared);
        var archiveOutbreak = Vocabulary("v-a-outbreak", term: "outbreak", knowledgeState: BackupKnowledgeState.Prepared, preparationState: BackupPreparationState.Prepared);
        var archiveQuarantineMeaning = PreparedItem("p-a-quarantine-de", "v-a-quarantine", definition: "Schutz vor Gefahr", explanationLanguage: "de");
        var archiveOutbreakMeaning = PreparedItem("p-a-outbreak-de", "v-a-outbreak", definition: "Ausbruch einer Krankheit", explanationLanguage: "de");
        var archiveOutbreakRussianMeaning = PreparedItem("p-a-outbreak-ru", "v-a-outbreak", definition: "Ausbruch einer Krankheit (Russisch)", translation: "vspyshka", explanationLanguage: "ru");
        var archiveQuarantineCard = Card("c-a-quarantine", "v-a-quarantine", "p-a-quarantine-de", direction: BackupCardDirection.TermToMeaning);
        var archiveOutbreakCard = Card("c-a-outbreak", "v-a-outbreak", "p-a-outbreak-ru", direction: BackupCardDirection.TermToMeaning);
        var archiveBaselineReview1 = Review("c-a-quarantine", reviewedAtUtc: BaseTime.AddDays(1));
        var archiveBaselineReview2 = Review("c-a-quarantine", reviewedAtUtc: BaseTime.AddDays(2));
        var archiveBaselineReview3 = Review("c-a-quarantine", reviewedAtUtc: BaseTime.AddDays(3));
        var archivePcOnlyReview1 = Review("c-a-quarantine", rating: BackupReviewRating.Good, reviewedAtUtc: BaseTime.AddDays(5));
        var archivePcOnlyReview2 = Review("c-a-quarantine", rating: BackupReviewRating.Good, reviewedAtUtc: BaseTime.AddDays(6));

        var pcOnlyDoc = SourceMaterial("sm-a-pc-doc", "hash-pc-only-doc", sentences: [new BackupSentenceRange("ss-1", 0, 0, 10)],
            occurrences: [new BackupOccurrence("v-a-quarantine", "ss-1", 0, 10, "quarantine", 0, BackupTechnicalTokenFamily.None, null, null, null)]);
        var pcOnlyReviewWorkflow = ReviewWorkflow("vr-a-1", "sm-a-pc-doc", [ReviewItem("rc-a-1", "v-a-quarantine")]);

        var archive = Payload(
            sourceMaterials: [pcOnlyDoc],
            vocabulary: [archiveQuarantine, archiveOutbreak],
            preparedLearning: [archiveQuarantineMeaning, archiveOutbreakMeaning, archiveOutbreakRussianMeaning],
            cards: [archiveQuarantineCard, archiveOutbreakCard],
            reviews: [archiveBaselineReview1, archiveBaselineReview2, archiveBaselineReview3, archivePcOnlyReview1, archivePcOnlyReview2],
            reviewWorkflows: [pcOnlyReviewWorkflow]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        // "outbreak"'s Russian card collides with the phone's existing physical (Word, Direction) slot —
        // blocking (no decision needed, nothing to choose between).
        Assert.AreEqual(MergePreflightStatus.BlockedByPrerequisite, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.IsTrue(plan.RequiresSchedulerReplay);
        Assert.IsTrue(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.MeaningCardSchemaMigrationRequired));
        Assert.IsTrue(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.ArchiveFormatMigrationRequired));

        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        Assert.AreEqual(2, plan.PerEntity[MergeEntityKind.Vocabulary].ExactDuplicateSkippedCount);

        Assert.AreEqual(2, plan.PerEntity[MergeEntityKind.PreparedMeaning].ExactDuplicateSkippedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].EnrichedCount, "The Russian outbreak sense is a genuinely new SemanticMeaning for an existing word.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].EnrichedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningCard].PreservedVariantCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningCard].UnresolvedConflictCount);

        Assert.AreEqual(3, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
        Assert.AreEqual(2, plan.PerEntity[MergeEntityKind.LearningReview].NewCount);

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.SourceMaterial].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.SentenceRange].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Occurrence].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].NewCount);

        Assert.IsEmpty(plan.KnowledgeStateConflictDecisions);
        Assert.IsEmpty(plan.WorkflowStatusConflictDecisions);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    [TestMethod]
    public void AThenBVersusBThenA_ConvergeToSameDiscoveredEntitySet()
    {
        var shared = Vocabulary("v-shared", term: "shared");
        var target = Payload(vocabulary: [shared]);

        var archiveA = Payload(vocabulary: [Vocabulary("v-a", term: "alpha"), Vocabulary("v-shared-a", term: "shared")]);
        var archiveB = Payload(vocabulary: [Vocabulary("v-b", term: "beta"), Vocabulary("v-shared-b", term: "shared")]);

        var targetPlusA = Payload(vocabulary: [shared, Vocabulary("v-a2", term: "alpha")]);
        var targetPlusB = Payload(vocabulary: [shared, Vocabulary("v-b2", term: "beta")]);

        var planAFirst = MergePreflightPlanner.CreatePlan(target, archiveA, Manifest());
        var planBAfterA = MergePreflightPlanner.CreatePlan(targetPlusA, archiveB, Manifest());

        var planBFirst = MergePreflightPlanner.CreatePlan(target, archiveB, Manifest());
        var planAAfterB = MergePreflightPlanner.CreatePlan(targetPlusB, archiveA, Manifest());

        static HashSet<string> NewVocabularyIdentities(MergePreflightPlan plan) =>
            [.. plan.Actions.Where(a => a.EntityKind == MergeEntityKind.Vocabulary && a.Classification == MergeEntityClassification.New).Select(a => a.StableIdentity)];

        var orderAThenB = new HashSet<string>(NewVocabularyIdentities(planAFirst));
        orderAThenB.UnionWith(NewVocabularyIdentities(planBAfterA));

        var orderBThenA = new HashSet<string>(NewVocabularyIdentities(planBFirst));
        orderBThenA.UnionWith(NewVocabularyIdentities(planAAfterB));

        CollectionAssert.AreEquivalent(orderAThenB.ToList(), orderBThenA.ToList());
    }

    // ---- Derived answer-variant plans (separate from physical archive actions) ----

    [TestMethod]
    public void AnswerVariant_EquivalentNormalizedAlias_Deduplicates()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", AcceptedAliases = ["financial institution"] }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", AcceptedAliases = ["  financial institution  "] }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(2, plan.DerivedAnswerVariantPlans.Count(p => p.Classification == MergeEntityClassification.ExactDuplicateSkipped));
        Assert.AreEqual(0, plan.DerivedAnswerVariantPlans.Count(p => p.Classification is MergeEntityClassification.New or MergeEntityClassification.Enriched));
    }

    [TestMethod]
    public void AnswerVariant_NewSynonym_EnrichesExistingSemanticMeaning()
    {
        // ProviderMeaningId keeps both sides unambiguously the same SemanticMeaning, isolating the
        // new-synonym enrichment behavior under test from the unrelated SemanticMeaningGroupingDecision.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AcceptedAliases = [] }]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AcceptedAliases = ["financial institution"] }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.DerivedAnswerVariantPlans.Count(p => p.Classification == MergeEntityClassification.ExactDuplicateSkipped));
        Assert.AreEqual(1, plan.DerivedAnswerVariantPlans.Count(p => p.Classification == MergeEntityClassification.Enriched));
        Assert.IsTrue(plan.WarningCodes.Contains(MergePreflightSchemaGapCodes.AnswerVariantProgressMigrationRequired));
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        // Derived plans never appear as primary archive-entity actions, and never affect PerEntity counts.
        Assert.IsFalse(plan.Actions.Any(a => a.EntityKind == MergeEntityKind.PreparedMeaning && a.Classification == MergeEntityClassification.Enriched));
    }

    [TestMethod]
    public void OneMeaningWithThreeAliases_RemainsOnePreparedMeaningAction()
    {
        var target = EmptyPayload();
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", AcceptedAliases = ["alias-one", "alias-two", "alias-three"] }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.Actions.Count(a => a.EntityKind == MergeEntityKind.PreparedMeaning));
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].NewCount);
        Assert.AreEqual(4, plan.DerivedAnswerVariantPlans.Count, "1 primary + 3 aliases = 4 derived answer-variant plans.");
    }

    [TestMethod]
    public void SchemaIncompatibility_IsExplicitViaStableWarningCodes()
    {
        var target = Payload(vocabulary: [Vocabulary("v-t", term: "word")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "any meaning")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.IsTrue(plan.WarningCodes.Contains(MergePreflightSchemaGapCodes.TopicPersistenceRequired));
        Assert.AreEqual("meaning-card-schema-migration-required", MergePreflightSchemaGapCodes.MeaningCardSchemaMigrationRequired);
        Assert.AreEqual("archive-format-migration-required", MergePreflightSchemaGapCodes.ArchiveFormatMigrationRequired);
        Assert.AreEqual("workflow-history-schema-migration-required", MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired);
        Assert.AreEqual("answer-variant-progress-migration-required", MergePreflightSchemaGapCodes.AnswerVariantProgressMigrationRequired);
        Assert.AreEqual("topic-persistence-required", MergePreflightSchemaGapCodes.TopicPersistenceRequired);
    }

    [TestMethod]
    public void MissingTopicPersistence_NeverInventsAValue_ButAlwaysFlagsTheGap()
    {
        var target = EmptyPayload();
        var archiveWithNoPreparedItems = Payload(vocabulary: [Vocabulary("v-a", term: "word")]);
        var archiveWithPreparedItem = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a")]);

        var planWithoutMeanings = MergePreflightPlanner.CreatePlan(target, archiveWithNoPreparedItems, Manifest());
        var planWithMeaning = MergePreflightPlanner.CreatePlan(target, archiveWithPreparedItem, Manifest());

        Assert.IsFalse(planWithoutMeanings.WarningCodes.Contains(MergePreflightSchemaGapCodes.TopicPersistenceRequired));
        Assert.IsTrue(planWithMeaning.WarningCodes.Contains(MergePreflightSchemaGapCodes.TopicPersistenceRequired));
        // Informational only: never blocking on its own.
        Assert.IsFalse(planWithMeaning.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.TopicPersistenceRequired));
    }

    [TestMethod]
    public void ContextSnapshot_IsIndependentlyClassified_EvenWhenParentMeaningIsExactDuplicate()
    {
        var sharedContext = ContextSnapshot("sm-shared", "fingerprint-shared");
        var newContext = ContextSnapshot("sm-new-only-in-archive", "fingerprint-new");

        var target = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared"), SourceMaterial("sm-new-only-in-archive", "hash-new")],
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", contexts: [sharedContext])]);
        var archive = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared"), SourceMaterial("sm-new-only-in-archive", "hash-new")],
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", contexts: [sharedContext, newContext])]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergeEntityClassification.ExactDuplicateSkipped, plan.Actions.Single(a => a.EntityKind == MergeEntityKind.PreparedMeaning).Classification);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.ContextSnapshot].ExactDuplicateSkippedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.ContextSnapshot].NewCount);
    }

    // ---- Workflow-child history-divergence tests (§7) ----

    [TestMethod]
    public void ReviewWorkflowItem_MatchingParent_DifferingContent_IsPreservedNotDuplicated()
    {
        var targetDoc = SourceMaterial("sm-shared", "hash-shared");
        var target = Payload(
            sourceMaterials: [targetDoc],
            vocabulary: [Vocabulary("v-t", term: "word")],
            reviewWorkflows: [ReviewWorkflow("vr-t", "sm-shared", [ReviewItem("rc-t", "v-t")])]);

        var archiveDoc = SourceMaterial("sm-shared", "hash-shared");
        var archiveItem = ReviewItem("rc-a", "v-a") with { Status = BackupKnowledgeState.Ignored };
        var archive = Payload(
            sourceMaterials: [archiveDoc],
            vocabulary: [Vocabulary("v-a", term: "word")],
            reviewWorkflows: [ReviewWorkflow("vr-a", "sm-shared", [archiveItem])]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        // Matching (doc,word) identity, but the archive's decision content (Status) differs — must be
        // preserved as an additional row, never silently collapsed to duplicate and never discarded.
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].ExactDuplicateSkippedCount);
        Assert.AreEqual("review-item-preserved-divergent-history", plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem).ReasonCode);
    }

    [TestMethod]
    public void ReviewWorkflowItem_MatchingParentAndIdenticalContent_IsExactDuplicate()
    {
        var target = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            vocabulary: [Vocabulary("v-t", term: "word")],
            reviewWorkflows: [ReviewWorkflow("vr-t", "sm-shared", [ReviewItem("rc-t", "v-t")])]);
        var archive = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            vocabulary: [Vocabulary("v-a", term: "word")],
            reviewWorkflows: [ReviewWorkflow("vr-a", "sm-shared", [ReviewItem("rc-a", "v-a")])]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].ExactDuplicateSkippedCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].NewCount);
    }

    [TestMethod]
    public void PreparationWorkflowItem_MatchingParent_DifferingContent_IsPreservedNotDuplicated()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparationWorkflows: [PreparationWorkflow("pb-t", items: [PreparationItem("pi-t", "v-t")])]);
        var archiveItem = PreparationItem("pi-a", "v-a") with { Status = BackupPreparationCandidateStatus.Failed, LastErrorCode = "timeout" };
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparationWorkflows: [PreparationWorkflow("pb-a", items: [archiveItem])]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparationItem].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.PreparationItem].ExactDuplicateSkippedCount);
        Assert.AreEqual("preparation-item-preserved-divergent-history", plan.Actions.Single(a => a.EntityKind == MergeEntityKind.PreparationItem).ReasonCode);
    }

    [TestMethod]
    public void LearningQueueItem_MatchingParent_DifferingContent_IsPreservedNotDuplicated()
    {
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t")],
            cards: [Card("c-t", "v-t", "p-t")],
            learningWorkflows: [LearningWorkflow("ls-t", [QueueItem("lq-t", "c-t")])]);
        // IsAgainRepeat (not Rating) is varied: Rating is part of the session's own content-fingerprint
        // identity (LearningWorkflowIdentityPolicy digests (CardIdentity, Rating) pairs), so changing it
        // would also change the session's identity and prevent the two sides' sessions from matching at
        // all — this fixture needs the *session* identity to match so the *item* content-divergence path
        // is actually exercised.
        var archiveQueueItem = QueueItem("lq-a", "c-a") with { IsAgainRepeat = true };
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a")],
            cards: [Card("c-a", "v-a", "p-a")],
            learningWorkflows: [LearningWorkflow("ls-a", [archiveQueueItem])]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningQueueItem].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningQueueItem].ExactDuplicateSkippedCount);
        Assert.AreEqual("learning-queue-item-preserved-divergent-history", plan.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningQueueItem).ReasonCode);
    }

    [TestMethod]
    public void VocabularyReviewWorkflow_MatchingDocument_DifferingContent_IsBlockingWorkflowHistoryConflict()
    {
        // ReviewSessionEntity.DocumentId is uniquely indexed: two independently completed sessions for
        // the same document cannot both be preserved as separate rows.
        var target = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            reviewWorkflows: [ReviewWorkflow("vr-t", "sm-shared") with { KnownCount = 3, ReviewedCount = 3 }]);
        var archive = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            reviewWorkflows: [ReviewWorkflow("vr-a", "sm-shared") with { KnownCount = 5, ReviewedCount = 5 }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].UnresolvedConflictCount);
        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.HasCount(1, plan.WorkflowStatusConflictDecisions);
        Assert.IsTrue(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired));

        var action = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow);
        Assert.IsNotNull(action.DecisionId);
        Assert.AreEqual(action.DecisionId, plan.WorkflowStatusConflictDecisions[0].DecisionId);
    }

    [TestMethod]
    public void VocabularyReviewWorkflow_MatchingDocument_IdenticalContent_IsExactDuplicate()
    {
        var target = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            reviewWorkflows: [ReviewWorkflow("vr-t", "sm-shared")]);
        var archive = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            reviewWorkflows: [ReviewWorkflow("vr-a", "sm-shared")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].ExactDuplicateSkippedCount);
        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
    }

    // ---- Preferred-variant selection is blocking (final focused review correction: compared by the
    // matched card's referenced ExactMeaningVariantIdentity, never by DisplayTerm text) ----

    private static readonly VocabularyIdentity SharedWordIdentity = VocabularyMergeIdentityPolicy.Compute("en", "word");

    [TestMethod]
    public void DifferentAdditionalNote_SameDisplayTerm_MatchedCards_CreatesBlockingPreferredVariantConflict()
    {
        // Checklist item 1: same FutureCardIdentity, same DisplayTerm, different AdditionalNote — the cards
        // reference different ExactMeaningVariantIdentity values even though DisplayTerm text agrees.
        var targetItem = PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1" };
        var archiveItem = PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AdditionalNote = "informal usage" };
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [targetItem],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [archiveItem],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        // Independently computed expected identities (never via a second planner call).
        var expectedSemanticIdentity = SemanticMeaningIdentityPolicy.Compute(targetItem, SharedWordIdentity);
        var expectedTargetExactIdentity = ExactMeaningVariantIdentityPolicy.Compute(targetItem, expectedSemanticIdentity);
        var expectedArchiveExactIdentity = ExactMeaningVariantIdentityPolicy.Compute(archiveItem, expectedSemanticIdentity);
        var expectedFutureCardIdentity = FutureCardIdentityPolicy.Compute(expectedSemanticIdentity, BackupCardDirection.TermToMeaning);
        Assert.AreNotEqual(expectedTargetExactIdentity, expectedArchiveExactIdentity);

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.HasCount(1, plan.PreferredVariantSelectionDecisions);
        var decision = plan.PreferredVariantSelectionDecisions[0];
        Assert.AreEqual(expectedFutureCardIdentity, decision.FutureCardIdentity);
        Assert.AreEqual(expectedSemanticIdentity, decision.SemanticMeaningIdentity);
        Assert.AreEqual(expectedTargetExactIdentity, decision.TargetExactMeaningVariantIdentity);
        Assert.AreEqual(expectedArchiveExactIdentity, decision.ArchiveExactMeaningVariantIdentity);
        Assert.HasCount(2, decision.AvailableChoices);
        CollectionAssert.AreEquivalent(
            new[] { PreferredVariantChoice.SelectTargetVariant, PreferredVariantChoice.SelectArchiveVariant },
            decision.AvailableChoices.ToArray());

        // Both exact variants remain preserved regardless of the pending decision — never deleted.
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparedMeaning].PreservedVariantCount);
    }

    [TestMethod]
    public void SameProviderSense_DifferentDefinition_MatchedCards_SameSemanticMeaning_DifferentExactVariant_CreatesDecision()
    {
        // Checklist item 2: same FutureCardIdentity, same DisplayTerm, same stable provider sense,
        // different Definition wording — same SemanticMeaningIdentity, different ExactMeaningVariantIdentity.
        var targetItem = PreparedItem("p-t", "v-t", definition: "a place that holds money") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1" };
        var archiveItem = PreparedItem("p-a", "v-a", definition: "a financial institution") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1" };
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [targetItem],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [archiveItem],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        var expectedTargetSemanticIdentity = SemanticMeaningIdentityPolicy.Compute(targetItem, SharedWordIdentity);
        var expectedArchiveSemanticIdentity = SemanticMeaningIdentityPolicy.Compute(archiveItem, SharedWordIdentity);
        Assert.AreEqual(expectedTargetSemanticIdentity, expectedArchiveSemanticIdentity, "Definition wording alone must not split SemanticMeaningIdentity when ProviderMeaningId agrees.");

        var expectedTargetExactIdentity = ExactMeaningVariantIdentityPolicy.Compute(targetItem, expectedTargetSemanticIdentity);
        var expectedArchiveExactIdentity = ExactMeaningVariantIdentityPolicy.Compute(archiveItem, expectedArchiveSemanticIdentity);
        Assert.AreNotEqual(expectedTargetExactIdentity, expectedArchiveExactIdentity, "Definition must still distinguish ExactMeaningVariantIdentity.");

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.HasCount(1, plan.PreferredVariantSelectionDecisions);
        var decision = plan.PreferredVariantSelectionDecisions[0];
        Assert.AreEqual(expectedTargetSemanticIdentity, decision.SemanticMeaningIdentity);
        Assert.AreEqual(expectedTargetExactIdentity, decision.TargetExactMeaningVariantIdentity);
        Assert.AreEqual(expectedArchiveExactIdentity, decision.ArchiveExactMeaningVariantIdentity);
    }

    [TestMethod]
    public void DifferentTranslationOrProvenance_SameDisplayTerm_MatchedCards_CreatesDecision()
    {
        // Checklist item 3: same FutureCardIdentity, same DisplayTerm, different Translation/provenance —
        // same semantic sense, but different exact variants, still a blocking decision.
        var targetItem = PreparedItem("p-t", "v-t", translation: "Bank (Fluss)") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1" };
        var archiveItem = PreparedItem("p-a", "v-a", translation: "Bank (Geld)") with
        {
            DisplayTerm = "bank",
            ProviderMeaningId = "sense-1",
            Source = new BackupSourceReference("Manual", "", "", null, "user-entered"),
        };
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [targetItem],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [archiveItem],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        var expectedSemanticIdentity = SemanticMeaningIdentityPolicy.Compute(targetItem, SharedWordIdentity);
        var expectedTargetExactIdentity = ExactMeaningVariantIdentityPolicy.Compute(targetItem, expectedSemanticIdentity);
        var expectedArchiveExactIdentity = ExactMeaningVariantIdentityPolicy.Compute(archiveItem, expectedSemanticIdentity);
        Assert.AreNotEqual(expectedTargetExactIdentity, expectedArchiveExactIdentity);

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.HasCount(1, plan.PreferredVariantSelectionDecisions);
        Assert.AreEqual(expectedTargetExactIdentity, plan.PreferredVariantSelectionDecisions[0].TargetExactMeaningVariantIdentity);
        Assert.AreEqual(expectedArchiveExactIdentity, plan.PreferredVariantSelectionDecisions[0].ArchiveExactMeaningVariantIdentity);
    }

    [TestMethod]
    public void SameReferencedExactVariant_AliasOrderDiffers_NoPreferredVariantDecision()
    {
        // Checklist item 4: identical referenced ExactMeaningVariantIdentity (alias order is not part of
        // that identity — see ExactMeaningVariantIdentity_AliasOrderDoesNotAffectIdentity) — no decision.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AcceptedAliases = ["alpha", "beta", "gamma"] }],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AcceptedAliases = ["gamma", "alpha", "beta"] }],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.IsEmpty(plan.PreferredVariantSelectionDecisions);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
    }

    [TestMethod]
    public void DistinctSemanticMeanings_IdenticalDisplayTerm_NoPreferredVariantDecision()
    {
        // Checklist item 5: distinct SemanticMeaningIdentity (different ProviderMeaningId) — never a
        // matched FutureCardIdentity, so no PreferredVariantSelectionDecision; existing distinct-meaning
        // physical-slot-collision behavior remains.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", ProviderMeaningId = "sense-de" }],
            cards: [Card("c-t", "v-t", "p-t", direction: BackupCardDirection.TermToMeaning)]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", ProviderMeaningId = "sense-ru" }],
            cards: [Card("c-a", "v-a", "p-a", direction: BackupCardDirection.TermToMeaning)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.IsEmpty(plan.PreferredVariantSelectionDecisions);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.LearningCard].NewCount);
        Assert.AreEqual("learning-card-new-future-card-physical-slot-collision", plan.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningCard).ReasonCode);
        Assert.AreEqual(MergePreflightStatus.BlockedByPrerequisite, plan.Status);
    }

    [TestMethod]
    public void PreferredVariantDecisionId_IsStableUnderListOrderAndCulture()
    {
        // Checklist item 6: DecisionId is identical under repeated planning, reversed input-list order
        // (non-semantic), and a non-English CurrentCulture.
        var targetItem = PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1" };
        var archiveItem = PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AdditionalNote = "informal usage" };
        var otherTargetItem = PreparedItem("p-t2", "v-t2") with { DisplayTerm = "other", ProviderMeaningId = "sense-2" };
        var otherArchiveItem = PreparedItem("p-a2", "v-a2") with { DisplayTerm = "other", ProviderMeaningId = "sense-2", AdditionalNote = "note" };

        var targetInOrder = Payload(
            vocabulary: [Vocabulary("v-t", term: "word"), Vocabulary("v-t2", term: "otherword")],
            preparedLearning: [targetItem, otherTargetItem],
            cards: [Card("c-t", "v-t", "p-t"), Card("c-t2", "v-t2", "p-t2")]);
        var archiveInOrder = Payload(
            vocabulary: [Vocabulary("v-a", term: "word"), Vocabulary("v-a2", term: "otherword")],
            preparedLearning: [archiveItem, otherArchiveItem],
            cards: [Card("c-a", "v-a", "p-a"), Card("c-a2", "v-a2", "p-a2")]);

        var targetReversed = Payload(
            vocabulary: [Vocabulary("v-t2", term: "otherword"), Vocabulary("v-t", term: "word")],
            preparedLearning: [otherTargetItem, targetItem],
            cards: [Card("c-t2", "v-t2", "p-t2"), Card("c-t", "v-t", "p-t")]);
        var archiveReversed = Payload(
            vocabulary: [Vocabulary("v-a2", term: "otherword"), Vocabulary("v-a", term: "word")],
            preparedLearning: [otherArchiveItem, archiveItem],
            cards: [Card("c-a2", "v-a2", "p-a2"), Card("c-a", "v-a", "p-a")]);

        var planFirstRun = MergePreflightPlanner.CreatePlan(targetInOrder, archiveInOrder, Manifest());
        var planRepeatRun = MergePreflightPlanner.CreatePlan(targetInOrder, archiveInOrder, Manifest());
        var planReversed = MergePreflightPlanner.CreatePlan(targetReversed, archiveReversed, Manifest());

        var decisionIdFirst = planFirstRun.PreferredVariantSelectionDecisions.Single(d => d.TargetPreferredAnswerText == "bank").DecisionId;
        var decisionIdRepeat = planRepeatRun.PreferredVariantSelectionDecisions.Single(d => d.TargetPreferredAnswerText == "bank").DecisionId;
        var decisionIdReversed = planReversed.PreferredVariantSelectionDecisions.Single(d => d.TargetPreferredAnswerText == "bank").DecisionId;

        Assert.AreEqual(decisionIdFirst, decisionIdRepeat, "Repeated planning must produce an identical DecisionId.");
        Assert.AreEqual(decisionIdFirst, decisionIdReversed, "Reversed, non-semantic input-list order must not affect DecisionId.");

        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            var planUnderTurkishCulture = MergePreflightPlanner.CreatePlan(targetInOrder, archiveInOrder, Manifest());
            var decisionIdUnderTurkishCulture = planUnderTurkishCulture.PreferredVariantSelectionDecisions.Single(d => d.TargetPreferredAnswerText == "bank").DecisionId;
            Assert.AreEqual(decisionIdFirst, decisionIdUnderTurkishCulture, "Non-English CurrentCulture (tr-TR, notorious for locale-sensitive casing) must not affect DecisionId.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    public void RequiresUserDecision_ForPreferredVariantConflict_NeverHasEmptyDecisionCollection()
    {
        // Checklist item 7.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1" }],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a") with { DisplayTerm = "bank", ProviderMeaningId = "sense-1", AdditionalNote = "informal usage" }],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.IsNotEmpty(plan.PreferredVariantSelectionDecisions);
    }

    // ---- Decision-completeness / executable-status invariants ----

    [TestMethod]
    public void EveryArchiveRow_ReceivesExactlyOnePrimaryAction()
    {
        var plan = BuildRichMixedFixturePlan(out var archive);

        var actionCountByKind = plan.Actions
            .GroupBy(a => a.EntityKind)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.AreEqual(archive.SourceMaterials.Count, actionCountByKind.GetValueOrDefault(MergeEntityKind.SourceMaterial));
        Assert.AreEqual(archive.SourceMaterials.Sum(d => d.Sentences.Count), actionCountByKind.GetValueOrDefault(MergeEntityKind.SentenceRange));
        Assert.AreEqual(archive.SourceMaterials.Sum(d => d.Occurrences.Count), actionCountByKind.GetValueOrDefault(MergeEntityKind.Occurrence));
        Assert.AreEqual(archive.Vocabulary.Count, actionCountByKind.GetValueOrDefault(MergeEntityKind.Vocabulary));
        Assert.AreEqual(archive.PreparedLearning.Count, actionCountByKind.GetValueOrDefault(MergeEntityKind.PreparedMeaning));
        Assert.AreEqual(archive.PreparedLearning.Sum(m => m.Contexts.Count), actionCountByKind.GetValueOrDefault(MergeEntityKind.ContextSnapshot));
        Assert.AreEqual(archive.Learning.Cards.Count, actionCountByKind.GetValueOrDefault(MergeEntityKind.LearningCard));
        Assert.AreEqual(archive.Learning.ReviewEvents.Count, actionCountByKind.GetValueOrDefault(MergeEntityKind.LearningReview));
        Assert.AreEqual(archive.Workflows.VocabularyReviews.Count, actionCountByKind.GetValueOrDefault(MergeEntityKind.VocabularyReviewWorkflow));
        Assert.AreEqual(archive.Workflows.VocabularyReviews.Sum(w => w.Items.Count), actionCountByKind.GetValueOrDefault(MergeEntityKind.VocabularyReviewItem));

        // Physical action total reconciles exactly with the archive's own record counts; derived answer
        // plans never contribute to this total.
        var expectedPhysicalTotal = archive.SourceMaterials.Count
            + archive.SourceMaterials.Sum(d => d.Sentences.Count)
            + archive.Vocabulary.Count
            + archive.SourceMaterials.Sum(d => d.Occurrences.Count)
            + archive.PreparedLearning.Count
            + archive.PreparedLearning.Sum(m => m.Contexts.Count)
            + archive.Vocabulary.Sum(v => v.EncounteredForms.Count)
            + archive.Vocabulary.Sum(v => v.LegacyReviewSummaries.Count)
            + archive.Workflows.VocabularyReviews.Count
            + archive.Workflows.VocabularyReviews.Sum(w => w.Items.Count)
            + archive.Workflows.PreparationBatches.Count
            + archive.Workflows.PreparationBatches.Sum(w => w.Items.Count)
            + archive.Learning.Cards.Count
            + archive.Learning.ReviewEvents.Count
            + archive.Workflows.LearningSessions.Count
            + archive.Workflows.LearningSessions.Sum(w => w.QueueItems.Count);

        Assert.AreEqual(expectedPhysicalTotal, plan.Actions.Count);
        Assert.AreEqual(expectedPhysicalTotal, plan.PerEntity.Values.Sum(c => c.Total));
    }

    [TestMethod]
    public void EveryUnresolvedConflictAction_ReferencesADecision_AndViceVersa()
    {
        var plan = BuildRichMixedFixturePlan(out _);

        var unresolvedActions = plan.Actions.Where(a => a.Classification == MergeEntityClassification.UnresolvedConflict).ToList();
        Assert.IsTrue(unresolvedActions.All(a => a.DecisionId is not null), "Every UnresolvedConflict action must carry a DecisionId.");

        var allDecisionIds = plan.KnowledgeStateConflictDecisions.Select(d => d.DecisionId)
            .Concat(plan.WorkflowStatusConflictDecisions.Select(d => d.DecisionId))
            .Concat(plan.SemanticMeaningGroupingDecisions.Select(d => d.DecisionId))
            .ToHashSet();

        CollectionAssert.AreEquivalent(
            unresolvedActions.Select(a => a.DecisionId!.Value).ToList(),
            allDecisionIds.ToList());

        Assert.IsTrue(plan.Actions.Where(a => a.Classification != MergeEntityClassification.UnresolvedConflict).All(a => a.DecisionId is null));
    }

    [TestMethod]
    public void RequiresUserDecision_NeverHasAnEmptyDecisionList()
    {
        var target = Payload(vocabulary: [Vocabulary("v-t", term: "conflict", knowledgeState: BackupKnowledgeState.Known)]);
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "conflict", knowledgeState: BackupKnowledgeState.Ignored)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        var totalDecisions = plan.KnowledgeStateConflictDecisions.Count
            + plan.WorkflowStatusConflictDecisions.Count
            + plan.SemanticMeaningGroupingDecisions.Count
            + plan.PreferredVariantSelectionDecisions.Count;
        Assert.IsTrue(totalDecisions > 0);
    }

    [TestMethod]
    public void Ready_ImpliesNoUnresolvedDecisionsAndNoBlockingPrerequisites()
    {
        var target = EmptyPayload();
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "word")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsEmpty(plan.KnowledgeStateConflictDecisions);
        Assert.IsEmpty(plan.WorkflowStatusConflictDecisions);
        Assert.IsEmpty(plan.SemanticMeaningGroupingDecisions);
        Assert.IsEmpty(plan.PreferredVariantSelectionDecisions);
        Assert.IsEmpty(plan.BlockingPrerequisites);
    }

    [TestMethod]
    public void NoChanges_ImpliesNoUnresolvedDecisionsAndNoBlockingPrerequisites()
    {
        var target = Payload(vocabulary: [Vocabulary("v-t", term: "same", knowledgeState: BackupKnowledgeState.Known)]);
        var archive = Payload(vocabulary: [Vocabulary("v-a", term: "same", knowledgeState: BackupKnowledgeState.Known)]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsEmpty(plan.BlockingPrerequisites);
        var totalDecisions = plan.KnowledgeStateConflictDecisions.Count
            + plan.WorkflowStatusConflictDecisions.Count
            + plan.SemanticMeaningGroupingDecisions.Count
            + plan.PreferredVariantSelectionDecisions.Count;
        Assert.AreEqual(0, totalDecisions);
    }

    [TestMethod]
    public void BlockedByPrerequisite_OccursOnlyWhenPrerequisitesExistWithoutDecisions()
    {
        // Distinct ProviderMeaningId (not merely differing Definition text — see the final focused review
        // correction) is what makes these two genuinely distinct SemanticMeanings colliding on one slot.
        var target = Payload(
            vocabulary: [Vocabulary("v-t", term: "word")],
            preparedLearning: [PreparedItem("p-t", "v-t", definition: "German meaning") with { ProviderMeaningId = "sense-de" }],
            cards: [Card("c-t", "v-t", "p-t")]);
        var archive = Payload(
            vocabulary: [Vocabulary("v-a", term: "word")],
            preparedLearning: [PreparedItem("p-a", "v-a", definition: "Russian meaning") with { ProviderMeaningId = "sense-ru" }],
            cards: [Card("c-a", "v-a", "p-a")]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.BlockedByPrerequisite, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.IsTrue(plan.BlockingPrerequisites.Count > 0);
        var totalDecisions = plan.KnowledgeStateConflictDecisions.Count
            + plan.WorkflowStatusConflictDecisions.Count
            + plan.SemanticMeaningGroupingDecisions.Count
            + plan.PreferredVariantSelectionDecisions.Count;
        Assert.AreEqual(0, totalDecisions);
    }

    /// <summary>Builds a fixture exercising every entity kind at least once, including one KnowledgeState conflict, for the two general invariant tests above.</summary>
    private static MergePreflightPlan BuildRichMixedFixturePlan(out BackupPayload archive)
    {
        var target = Payload(
            sourceMaterials: [SourceMaterial("sm-t", "hash-t")],
            vocabulary: [
                Vocabulary("v-t1", term: "conflictword", knowledgeState: BackupKnowledgeState.Known),
                Vocabulary("v-t2", term: "otherword")],
            preparedLearning: [PreparedItem("p-t2", "v-t2")],
            cards: [Card("c-t2", "v-t2", "p-t2")],
            reviews: [Review("c-t2")]);

        archive = Payload(
            sourceMaterials: [SourceMaterial("sm-a", "hash-a", sentences: [new BackupSentenceRange("ss-1", 0, 0, 5)],
                occurrences: [new BackupOccurrence("v-a2", "ss-1", 0, 5, "otherword", 0, BackupTechnicalTokenFamily.None, null, null, null)])],
            vocabulary: [
                Vocabulary("v-a1", term: "conflictword", knowledgeState: BackupKnowledgeState.UnknownBacklog),
                Vocabulary("v-a2", term: "otherword")],
            preparedLearning: [PreparedItem("p-a2", "v-a2", contexts: [ContextSnapshot("sm-a", "fp-1")])],
            cards: [Card("c-a2", "v-a2", "p-a2")],
            reviews: [Review("c-a2")],
            reviewWorkflows: [ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a2")])]);

        return MergePreflightPlanner.CreatePlan(target, archive, Manifest());
    }

    // ==== Package A: Schema-9 completed-ReviewSession convergence ====
    //
    // The Schema-7 planner (MergePreflightPlanner) keeps its v1 ReviewSession identity — document identity
    // alone — and therefore keeps its blocking history-divergence behaviour. Only the Schema-9 planner
    // (MergePreflightPlannerV2) adopts the full-history v2 identity.

    private static MergeManifestInfo ManifestV2() => new(
        BackupFormatLimits.CurrentArchiveFormatVersion, "1.0.0-test", 8, BaseTime, BackupSourcePlatform.Windows);

    /// <summary>Minimal v2 payload: only the collections the review/preparation workflow paths need. Every
    /// other v2 collection is empty, which the planner handles without special-casing.</summary>
    private static BackupPayloadV2 PayloadV2(
        IReadOnlyList<BackupSourceMaterial>? sourceMaterials = null,
        IReadOnlyList<BackupVocabularyItem>? vocabulary = null,
        IReadOnlyList<BackupVocabularyReviewWorkflow>? reviewWorkflows = null,
        IReadOnlyList<BackupPreparationWorkflow>? preparationWorkflows = null) => new(
        sourceMaterials ?? [],
        vocabulary ?? [],
        [],
        [],
        [],
        [],
        [],
        new BackupLearningDataV2([], []),
        new BackupWorkflowDataV2(reviewWorkflows ?? [], preparationWorkflows ?? [], []),
        new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));

    // ---- Stage 1 characterization: unchanged Schema-7 and unrelated-decision behaviour ----

    [TestMethod]
    public void Schema7Path_VocabularyReviewWorkflow_DivergentHistory_RemainsBlocking()
    {
        var target = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            vocabulary: [Vocabulary("v-t", term: "shared")],
            reviewWorkflows: [ReviewWorkflow("vr-t", "sm-shared", [ReviewItem("rc-t", "v-t")]) with { KnownCount = 3, ReviewedCount = 3 }]);
        var archive = Payload(
            sourceMaterials: [SourceMaterial("sm-shared", "hash-shared")],
            vocabulary: [Vocabulary("v-a", term: "shared")],
            reviewWorkflows: [ReviewWorkflow("vr-a", "sm-shared", [ReviewItem("rc-a", "v-a")]) with { KnownCount = 5, ReviewedCount = 5 }]);

        var plan = MergePreflightPlanner.CreatePlan(target, archive, Manifest());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].UnresolvedConflictCount);
        Assert.HasCount(1, plan.WorkflowStatusConflictDecisions);
        Assert.IsTrue(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired));
    }

    [TestMethod]
    public void Schema9_UnrelatedWorkflowStatusConflictDecisionBehavior_IsUnchanged()
    {
        // PreparationWorkflow's two same-tier terminal statuses remain a WorkflowStatusConflictDecision:
        // the review-session identity change must not touch this path.
        var target = PayloadV2(
            vocabulary: [Vocabulary("v-t", term: "shared")],
            preparationWorkflows: [PreparationWorkflow("pb-t", BackupPreparationSessionStatus.Completed, [PreparationItem("pi-t", "v-t")])]);
        var archive = PayloadV2(
            vocabulary: [Vocabulary("v-a", term: "shared")],
            preparationWorkflows: [PreparationWorkflow("pb-a", BackupPreparationSessionStatus.Cancelled, [PreparationItem("pi-a", "v-a")])]);

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.PreparationWorkflow].UnresolvedConflictCount);
        Assert.HasCount(1, plan.WorkflowStatusConflictDecisions);
        Assert.AreEqual(MergeEntityKind.PreparationWorkflow, plan.WorkflowStatusConflictDecisions[0].EntityKind);
        Assert.AreEqual("preparation-session-status-terminal-outcome-conflict", plan.WorkflowStatusConflictDecisions[0].ReasonCode);
        Assert.IsEmpty(plan.BlockingPrerequisites);
    }

    // ---- Stage 2 intended behaviour: full-history v2 ReviewSession identity in the Schema-9 planner ----

    private static BackupPayloadV2 SharedDocumentTargetV2(params BackupVocabularyReviewWorkflow[] reviewWorkflows) =>
        PayloadV2(
            [SourceMaterial("sm-t", "hash-shared")],
            [Vocabulary("v-t", term: "shared"), Vocabulary("v-t2", term: "second")],
            reviewWorkflows);

    private static BackupPayloadV2 SharedDocumentArchiveV2(params BackupVocabularyReviewWorkflow[] reviewWorkflows) =>
        PayloadV2(
            [SourceMaterial("sm-a", "hash-shared")],
            [Vocabulary("v-a", term: "shared"), Vocabulary("v-a2", term: "second")],
            reviewWorkflows);

    [TestMethod]
    public void Schema9_VocabularyReviewWorkflow_ExactCompletedDuplicate_IsSkippedWithoutBlocker()
    {
        var target = SharedDocumentTargetV2(ReviewWorkflow("vr-t", "sm-t", [ReviewItem("rc-t", "v-t")]));
        var archive = SharedDocumentArchiveV2(ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a")]));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].ExactDuplicateSkippedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].ExactDuplicateSkippedCount);
        Assert.AreEqual(
            "review-workflow-exact-duplicate",
            plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow).ReasonCode);
        Assert.AreEqual(
            "review-item-exact-duplicate",
            plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem).ReasonCode);
        Assert.IsEmpty(plan.BlockingPrerequisites);
        Assert.IsEmpty(plan.WorkflowStatusConflictDecisions);
    }

    [TestMethod]
    public void Schema9_VocabularyReviewWorkflow_DivergentCompletedHistories_ClassifyAsNew()
    {
        var target = SharedDocumentTargetV2(ReviewWorkflow("vr-t", "sm-t", [ReviewItem("rc-t", "v-t")]));
        var archive = SharedDocumentArchiveV2(
            ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a")])
                with { CompletedAtUtc = BaseTime.AddMinutes(9), KnownCount = 5, ReviewedCount = 5 });

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].UnresolvedConflictCount);
        Assert.AreEqual(
            "review-workflow-new",
            plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow).ReasonCode);
        Assert.IsEmpty(plan.WorkflowStatusConflictDecisions);
        Assert.IsEmpty(plan.BlockingPrerequisites);
    }

    [TestMethod]
    public void Schema9_ReviewCandidates_ClassifyUnderTheirOwnParentSession()
    {
        // The archive session is a genuinely different completed history over the same document. Its
        // candidate content is byte-identical to the target's, so only the parent session identity can
        // keep the two candidates apart.
        var target = SharedDocumentTargetV2(ReviewWorkflow("vr-t", "sm-t", [ReviewItem("rc-t", "v-t")]));
        var archive = SharedDocumentArchiveV2(
            ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a")]) with { CompletedAtUtc = BaseTime.AddMinutes(9) });

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].ExactDuplicateSkippedCount);
        Assert.AreEqual(
            "review-item-new",
            plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem).ReasonCode);
    }

    [TestMethod]
    public void Schema9_TargetWithTwoCompletedSessionsForOneDocument_PlansWithoutDuplicateIdFailure()
    {
        var target = SharedDocumentTargetV2(
            ReviewWorkflow("vr-t1", "sm-t", [ReviewItem("rc-t1", "v-t")]),
            ReviewWorkflow("vr-t2", "sm-t", [ReviewItem("rc-t2", "v-t")])
                with { CompletedAtUtc = BaseTime.AddMinutes(9), DecisionSequence = 2 });
        var archive = SharedDocumentArchiveV2(ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a")]));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].ExactDuplicateSkippedCount,
            "Two legal completed sessions for one document must not make planning fail.");
        Assert.IsEmpty(plan.BlockingPrerequisites);
    }

    [TestMethod]
    public void Schema9_ObsoleteWorkflowHistoryBlocker_IsAbsentForRepresentableCompletedHistories()
    {
        var target = SharedDocumentTargetV2(ReviewWorkflow("vr-t", "sm-t", [ReviewItem("rc-t", "v-t")]));
        var archive = SharedDocumentArchiveV2(
            ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a")]) with { CompletedAtUtc = BaseTime.AddMinutes(9) });

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.IsFalse(
            plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired),
            "Two representable completed review histories are no longer a schema gap for the Schema-9 planner.");
        Assert.IsFalse(
            plan.WarningCodes.Contains("review-workflow-history-divergence"));
        Assert.IsEmpty(plan.WorkflowStatusConflictDecisions);
    }

    [TestMethod]
    public void Schema9_ReviewWorkflowPlan_IsDeterministicAcrossRepeatedInvocations()
    {
        var target = SharedDocumentTargetV2(
            ReviewWorkflow("vr-t1", "sm-t", [ReviewItem("rc-t1", "v-t")]),
            ReviewWorkflow("vr-t2", "sm-t", [ReviewItem("rc-t2", "v-t")])
                with { CompletedAtUtc = BaseTime.AddMinutes(9), DecisionSequence = 2 });
        var archive = SharedDocumentArchiveV2(
            ReviewWorkflow("vr-a1", "sm-a", [ReviewItem("rc-a1", "v-a"), ReviewItem("rc-a2", "v-a2", order: 1)]));

        var first = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());
        var second = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        AssertPlansEquivalent(first, second);
        CollectionAssert.AreEqual(
            first.Actions.Where(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem).Select(a => a.StableIdentity).ToList(),
            second.Actions.Where(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem).Select(a => a.StableIdentity).ToList());
    }

    [TestMethod]
    public void Schema9_ArchiveReviewWorkflow_DuplicateCandidateVocabularyIdentity_FailsClosedWithDuplicateId()
    {
        // Two archive-local vocabulary rows resolving to one stable identity, both referenced by one
        // review workflow — the planner path must fail closed with its own exception contract.
        var archive = PayloadV2(
            [SourceMaterial("sm-a", "hash-shared")],
            [Vocabulary("v-a1", term: "shared"), Vocabulary("v-a2", term: "shared")],
            [ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a1", "v-a1"), ReviewItem("rc-a2", "v-a2", order: 1)])]);

        var exception = Assert.ThrowsExactly<MergePlanningException>(
            () => MergePreflightPlannerV2.CreatePlan(PayloadV2(), archive, ManifestV2()));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    // ---- Correction: retained outcome counters are the only surviving evidence once ordinary completion
    // has deleted every ReviewCandidate row, so a counter-only divergence is a genuinely different history. ----

    private static ReviewSessionIdentity SessionIdentityV2(BackupVocabularyReviewWorkflow workflow, string documentContentSha256) =>
        ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            workflow,
            new Dictionary<string, SourceMaterialIdentity>(StringComparer.Ordinal)
            {
                [workflow.SourceMaterialId] = SourceMaterialIdentityPolicy.Compute(
                    SourceMaterial(workflow.SourceMaterialId, documentContentSha256))
            },
            new Dictionary<string, VocabularyIdentity>(StringComparer.Ordinal)).Identity;

    [TestMethod]
    public void Schema9_VocabularyReviewWorkflow_CounterOnlyDivergence_ClassifiesAsNew()
    {
        // The valid ordinary-output collision: both sessions completed, both exported with zero Items
        // (completion deleted the candidate rows), identical timestamps and DecisionSequence. Only the
        // retained outcome counters differ — 2 known / 1 unknown against 1 known / 2 unknown.
        var targetWorkflow = ReviewWorkflow("vr-t", "sm-t")
            with { TotalCandidates = 3, ReviewedCount = 3, KnownCount = 2, UnknownCount = 1, IgnoredCount = 0 };
        var archiveWorkflow = ReviewWorkflow("vr-a", "sm-a")
            with { TotalCandidates = 3, ReviewedCount = 3, KnownCount = 1, UnknownCount = 2, IgnoredCount = 0 };

        Assert.IsEmpty(targetWorkflow.Items, "Precondition: a retained completed session exports with Items empty.");
        Assert.IsEmpty(archiveWorkflow.Items);
        Assert.AreEqual(targetWorkflow.StartedAtUtc, archiveWorkflow.StartedAtUtc);
        Assert.AreEqual(targetWorkflow.CompletedAtUtc, archiveWorkflow.CompletedAtUtc);
        Assert.AreEqual(targetWorkflow.DecisionSequence, archiveWorkflow.DecisionSequence);
        Assert.AreNotEqual(
            SessionIdentityV2(targetWorkflow, "hash-shared"),
            SessionIdentityV2(archiveWorkflow, "hash-shared"),
            "Two retained completed histories that differ only in their outcome counters are distinct identities.");

        var plan = MergePreflightPlannerV2.CreatePlan(
            SharedDocumentTargetV2(targetWorkflow), SharedDocumentArchiveV2(archiveWorkflow), ManifestV2());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].ExactDuplicateSkippedCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].UnresolvedConflictCount);
        Assert.IsFalse(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired));
        Assert.AreEqual(
            SessionIdentityV2(archiveWorkflow, "hash-shared").Value,
            plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow).StableIdentity);
    }

    [TestMethod]
    public void Schema9_ArchiveWithTwoIdenticalCompletedReviewHistories_FailsClosedWithDuplicateId()
    {
        // Two distinct archive-local workflow ids over one logical document, carrying byte-identical v2
        // history including all five retained counters. The archive would otherwise insert two rows with
        // one identity, so planning must fail closed before any action is recorded.
        var archive = SharedDocumentArchiveV2(
            ReviewWorkflow("vr-a1", "sm-a", [ReviewItem("rc-a1", "v-a")])
                with { TotalCandidates = 3, ReviewedCount = 3, KnownCount = 2, UnknownCount = 1, IgnoredCount = 0 },
            ReviewWorkflow("vr-a2", "sm-a", [ReviewItem("rc-a2", "v-a")])
                with { TotalCandidates = 3, ReviewedCount = 3, KnownCount = 2, UnknownCount = 1, IgnoredCount = 0 });

        var exception = Assert.ThrowsExactly<MergePlanningException>(
            () => MergePreflightPlannerV2.CreatePlan(PayloadV2(), archive, ManifestV2()));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public void Schema9_TargetActive_ArchiveCompletedReviewHistory_ClassifiesArchiveAsNewWithoutConflict()
    {
        // Target active review session: Active status, no completion timestamp, zero reviewed counters
        var targetActiveWorkflow = ReviewWorkflow("vr-t", "sm-t", [ReviewItem("rc-t", "v-t")]) with
        {
            Status = BackupReviewSessionStatus.Active,
            CompletedAtUtc = null,
            ReviewedCount = 0,
            KnownCount = 0,
            UnknownCount = 0,
            IgnoredCount = 0,
            DecisionSequence = 0
        };

        // Archive completed review session: Completed status, valid completion timestamp, outcome counters
        var archiveCompletedWorkflow = ReviewWorkflow("vr-a", "sm-a", [ReviewItem("rc-a", "v-a")]) with
        {
            Status = BackupReviewSessionStatus.Completed,
            StartedAtUtc = BaseTime,
            CompletedAtUtc = BaseTime.AddMinutes(5),
            TotalCandidates = 1,
            ReviewedCount = 1,
            KnownCount = 1,
            UnknownCount = 0,
            IgnoredCount = 0,
            DecisionSequence = 1
        };

        var target = SharedDocumentTargetV2(targetActiveWorkflow);
        var archive = SharedDocumentArchiveV2(archiveCompletedWorkflow);

        // Precondition checks on document and candidate vocabulary identities
        var targetDocIdentity = SourceMaterialIdentityPolicy.Compute(SourceMaterial("sm-t", "hash-shared"));
        var archiveDocIdentity = SourceMaterialIdentityPolicy.Compute(SourceMaterial("sm-a", "hash-shared"));
        Assert.AreEqual(targetDocIdentity, archiveDocIdentity, "Precondition: logical document identities match.");

        var targetVocabIdentity = VocabularyMergeIdentityPolicy.Compute(Vocabulary("v-t", term: "shared"));
        var archiveVocabIdentity = VocabularyMergeIdentityPolicy.Compute(Vocabulary("v-a", term: "shared"));
        Assert.AreEqual(targetVocabIdentity, archiveVocabIdentity, "Precondition: candidate vocabulary identities match.");

        // Compute expected independent v2 session identities for target Active vs archive Completed
        var targetActiveSessionIdentityResult = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            targetActiveWorkflow,
            new Dictionary<string, SourceMaterialIdentity>(StringComparer.Ordinal) { ["sm-t"] = targetDocIdentity },
            new Dictionary<string, VocabularyIdentity>(StringComparer.Ordinal) { ["v-t"] = targetVocabIdentity });
        var targetActiveSessionIdentity = targetActiveSessionIdentityResult.Identity;

        var archiveCompletedSessionIdentityResult = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            archiveCompletedWorkflow,
            new Dictionary<string, SourceMaterialIdentity>(StringComparer.Ordinal) { ["sm-a"] = archiveDocIdentity },
            new Dictionary<string, VocabularyIdentity>(StringComparer.Ordinal) { ["v-a"] = archiveVocabIdentity });
        var archiveCompletedSessionIdentity = archiveCompletedSessionIdentityResult.Identity;

        Assert.AreNotEqual(targetActiveSessionIdentity, archiveCompletedSessionIdentity,
            "Target Active session and Archive Completed session must have distinct v2 identities.");

        // Execute preflight planning
        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        // 1. Planning succeeds without MergePlanningException, plan is executable, status is Ready (not NoChanges)
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.AreNotEqual(MergePreflightStatus.NoChanges, plan.Status);
        Assert.IsTrue(plan.IsExecutable);

        // 2. Archive Completed session is classified as New, not ExactDuplicateSkipped
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].ExactDuplicateSkippedCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewWorkflow].UnresolvedConflictCount);

        var workflowAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow);
        Assert.AreEqual(MergeEntityClassification.New, workflowAction.Classification);
        Assert.AreEqual("review-workflow-new", workflowAction.ReasonCode);
        Assert.AreEqual("vr-a", workflowAction.ArchiveLocalId);

        // 3. Completed workflow action uses the expected stable v2 ReviewSession identity
        Assert.AreEqual(archiveCompletedSessionIdentity.Value, workflowAction.StableIdentity);

        // 4. Candidate actions remain bound to the archive Completed session identity rather than the target Active session identity
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].ExactDuplicateSkippedCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.VocabularyReviewItem].UnresolvedConflictCount);

        var itemAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem);
        Assert.AreEqual(MergeEntityClassification.New, itemAction.Classification);
        Assert.AreEqual("review-item-new", itemAction.ReasonCode);
        Assert.AreEqual("rc-a", itemAction.ArchiveLocalId);

        var expectedArchiveCandidateIdentity = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(archiveCompletedSessionIdentity, archiveVocabIdentity);
        var unexpectedTargetCandidateIdentity = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(targetActiveSessionIdentity, targetVocabIdentity);
        Assert.AreEqual(expectedArchiveCandidateIdentity.Value, itemAction.StableIdentity);
        Assert.AreNotEqual(unexpectedTargetCandidateIdentity.Value, itemAction.StableIdentity);

        // 5. Plan contains no WorkflowHistorySchemaMigrationRequired and no WorkflowStatusConflictDecision for review workflow
        Assert.IsFalse(plan.BlockingPrerequisites.Contains(MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired));
        Assert.IsEmpty(plan.BlockingPrerequisites);
        Assert.IsFalse(plan.WorkflowStatusConflictDecisions.Any(d => d.EntityKind == MergeEntityKind.VocabularyReviewWorkflow));
        Assert.IsEmpty(plan.WorkflowStatusConflictDecisions);

        // 6. Planner counts are exact and deterministic
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.SourceMaterial].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.SourceMaterial].ExactDuplicateSkippedCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        Assert.AreEqual(2, plan.PerEntity[MergeEntityKind.Vocabulary].ExactDuplicateSkippedCount);

        // 7. Target structure before and after planning is unchanged (target still contains exactly the original Active session)
        Assert.HasCount(1, target.Workflows.VocabularyReviews);
        Assert.AreEqual("vr-t", target.Workflows.VocabularyReviews[0].Id);
        Assert.AreEqual(BackupReviewSessionStatus.Active, target.Workflows.VocabularyReviews[0].Status);
        Assert.IsNull(target.Workflows.VocabularyReviews[0].CompletedAtUtc);
        Assert.HasCount(1, target.Workflows.VocabularyReviews[0].Items);
        Assert.AreEqual("rc-t", target.Workflows.VocabularyReviews[0].Items[0].Id);
    }


    private static void AssertPlansEquivalent(MergePreflightPlan left, MergePreflightPlan right)
    {
        Assert.AreEqual(left.Status, right.Status);
        Assert.AreEqual(left.IsExecutable, right.IsExecutable);
        Assert.AreEqual(left.ChecksumVerified, right.ChecksumVerified);
        Assert.AreEqual(left.RequiresSchedulerReplay, right.RequiresSchedulerReplay);
        Assert.AreEqual(left.ErrorCode, right.ErrorCode);

        foreach (var kind in Enum.GetValues<MergeEntityKind>())
        {
            Assert.AreEqual(left.PerEntity[kind], right.PerEntity[kind], $"Counts differ for {kind}");
        }

        CollectionAssert.AreEqual(left.Actions.ToList(), right.Actions.ToList());
        CollectionAssert.AreEqual(left.DerivedAnswerVariantPlans.ToList(), right.DerivedAnswerVariantPlans.ToList());
        CollectionAssert.AreEqual(left.KnowledgeStateConflictDecisions.ToList(), right.KnowledgeStateConflictDecisions.ToList());
        CollectionAssert.AreEqual(left.WorkflowStatusConflictDecisions.ToList(), right.WorkflowStatusConflictDecisions.ToList());
        CollectionAssert.AreEqual(left.SemanticMeaningGroupingDecisions.ToList(), right.SemanticMeaningGroupingDecisions.ToList());
        CollectionAssert.AreEqual(left.PreferredVariantSelectionDecisions.ToList(), right.PreferredVariantSelectionDecisions.ToList());
        CollectionAssert.AreEqual(left.BlockingPrerequisites.ToList(), right.BlockingPrerequisites.ToList());
        CollectionAssert.AreEqual(left.WarningCodes.ToList(), right.WarningCodes.ToList());

        foreach (var classification in Enum.GetValues<MergeEntityClassification>())
        {
            var leftSamples = left.SampleDetails.TryGetValue(classification, out var l) ? l : [];
            var rightSamples = right.SampleDetails.TryGetValue(classification, out var r) ? r : [];
            CollectionAssert.AreEqual(leftSamples.ToList(), rightSamples.ToList());
        }
    }

    // ==== Schema-9 LearningReview merge integrity ====
    //
    // Design §6's exact-duplicate rule is "any field difference at the same (stableCardKey, ReviewedAtUtc)
    // means they are not the same event". The Schema-9 meaning-aware fingerprint stopped at EaseFactor and
    // never consulted the emitted TargetAnswerVariantId/MatchedAnswerVariantId, and the LearningReview action
    // key was the non-unique content label CardId@ReviewedAtUtc rather than the synthesized positional label
    // MergePlanAction.ArchiveLocalId documents. LearningSessionId stays out of event identity by design.

    private const string ReviewIntegrityFirstVariantText = "alpha-answer";
    private const string ReviewIntegritySecondVariantText = "beta-answer";

    /// <summary>
    /// One vocabulary item, Sense, prepared item and card, plus two AnswerVariants assigned to the card's
    /// direction. Every archive-local id and StableId carries <paramref name="side"/>, so a target and an
    /// archive payload built here share no raw local id — only content-derived identities can ever match
    /// them. Each requested review is emitted at the same instant with the same scheduling outcome, so two
    /// reviews differ only in the fields the tuple names.
    /// </summary>
    private static BackupPayloadV2 ReviewIntegrityPayload(
        string side,
        params (BackupReviewRating Rating, int TargetVariant, int MatchedVariant)[] reviews)
    {
        string VariantId(int index) => index switch
        {
            1 => $"av1-{side}",
            2 => $"av2-{side}",
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        var sense = new BackupSense(
            $"s-{side}", $"sense-stable-{side}", $"v-{side}", "en", "de", "bank-1", string.Empty, string.Empty,
            string.Empty, string.Empty, null, BackupSenseStatus.Learning, BaseTime, BaseTime);

        var prepared = new BackupPreparedItemV2(
            $"p-{side}", $"s-{side}", $"prepared-stable-{side}", $"v-{side}", "en", "de", "bank", null, null,
            BackupTokenKind.Word, "bank-1", null, "Bank", "a financial institution", null, null, null, [], true,
            SourceReference(), BaseTime, BaseTime, BaseTime, []);

        var card = new BackupLearningCardV2(
            $"c-{side}", $"v-{side}", $"s-{side}", $"p-{side}", BackupCardDirection.TermToMeaning,
            BackupCardState.Review, BaseTime.AddDays(3), 3, 2.5, 1, 0, BaseTime, BackupReviewRating.Good,
            BaseTime, BaseTime);

        BackupAnswerVariant Variant(int index, string normalizedText) => new(
            VariantId(index), $"variant-stable-{index}-{side}", $"s-{side}", "de", normalizedText,
            normalizedText, null, BaseTime, BaseTime);

        BackupSenseAnswerVariantAssignment Assignment(int index) => new(
            $"asg{index}-{side}", $"assignment-stable-{index}-{side}", $"s-{side}",
            BackupCardDirection.TermToMeaning, VariantId(index), BackupAnswerVariantRequirement.AcceptedOnly,
            false, BaseTime, BaseTime);

        var reviewRows = reviews
            .Select(review => new BackupLearningReviewV2(
                $"c-{side}", $"ls-{side}", review.Rating, true, true, BaseTime, BaseTime.AddDays(3), 3, 2.5,
                review.TargetVariant == 0 ? null : VariantId(review.TargetVariant),
                review.MatchedVariant == 0 ? null : VariantId(review.MatchedVariant)))
            .ToList();

        return new BackupPayloadV2(
            [],
            [Vocabulary($"v-{side}", term: "bank")],
            [sense],
            [prepared],
            [Variant(1, ReviewIntegrityFirstVariantText), Variant(2, ReviewIntegritySecondVariantText)],
            [Assignment(1), Assignment(2)],
            [],
            new BackupLearningDataV2([card], reviewRows),
            new BackupWorkflowDataV2([], [], []),
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));
    }

    private static List<MergePlanAction> ReviewActions(MergePreflightPlan plan) =>
        [.. plan.Actions.Where(a => a.EntityKind == MergeEntityKind.LearningReview)];

    [TestMethod]
    public void SameCardSameInstantReviews_ProduceDistinctArchiveActionKeys()
    {
        // Two physical archive review rows for one card at one instant. They are genuinely distinct events
        // (different Rating), so the planner must emit two separately addressable actions — the writer keys
        // its per-row lookup by ArchiveLocalId, so two rows sharing one key silently collapse there.
        var archive = ReviewIntegrityPayload(
            "a",
            (BackupReviewRating.Good, 0, 0),
            (BackupReviewRating.Hard, 0, 0));

        var plan = MergePreflightPlannerV2.CreatePlan(ReviewIntegrityPayload("t"), archive, ManifestV2());

        var actions = ReviewActions(plan);
        Assert.HasCount(2, actions, "Each physical archive review row must keep exactly one primary action.");
        Assert.AreEqual(
            2,
            actions.Select(a => a.ArchiveLocalId).Distinct(StringComparer.Ordinal).Count(),
            "Two archive review rows sharing CardId and ReviewedAtUtc must still receive distinct "
            + "ArchiveLocalId lookup keys, or the writer's action map collapses them.");
        Assert.AreEqual(
            2,
            actions.Select(a => a.StableIdentity).Distinct(StringComparer.Ordinal).Count(),
            "The two events are genuinely distinct and must keep distinct merge identities.");

        foreach (var action in actions)
        {
            Assert.AreNotEqual(
                action.StableIdentity, action.ArchiveLocalId,
                "ArchiveLocalId is a synthesized positional lookup label, never the merge identity itself.");
        }
    }

    [TestMethod]
    public void EquivalentReviewsAcrossInstallations_DedupeThroughStableVariantIdentity()
    {
        // Control for the tests below: the target and archive answer variants carry different archive-local
        // ids but identical normalized text, so the same real event must still dedupe.
        var target = ReviewIntegrityPayload("t", (BackupReviewRating.Good, 1, 1));
        var archive = ReviewIntegrityPayload("a", (BackupReviewRating.Good, 1, 1));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount,
            "Equivalent variants under different archive-local ids must resolve to the same stable identity.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].NewCount);
    }

    [TestMethod]
    public void ReviewsDifferingOnlyInTargetAnswerVariantIdentity_RemainDistinct()
    {
        var target = ReviewIntegrityPayload("t", (BackupReviewRating.Good, 1, 0));
        var archive = ReviewIntegrityPayload("a", (BackupReviewRating.Good, 2, 0));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount,
            "Two reviews tying on every other emitted field but exercising a different target answer "
            + "variant are distinct events and must both be retained.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
    }

    [TestMethod]
    public void ReviewsDifferingOnlyInTargetAnswerVariantPresence_RemainDistinct()
    {
        var target = ReviewIntegrityPayload("t", (BackupReviewRating.Good, 0, 0));
        var archive = ReviewIntegrityPayload("a", (BackupReviewRating.Good, 1, 0));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount,
            "An absent target answer variant is a distinct event from a present one; null presence must be "
            + "encoded explicitly.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
    }

    [TestMethod]
    public void ReviewsDifferingOnlyInMatchedAnswerVariantIdentity_RemainDistinct()
    {
        var target = ReviewIntegrityPayload("t", (BackupReviewRating.Good, 1, 1));
        var archive = ReviewIntegrityPayload("a", (BackupReviewRating.Good, 1, 2));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount,
            "Two reviews whose matched answer variant resolves to a different stable identity are distinct "
            + "events and must both be retained.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
    }

    [TestMethod]
    public void ReviewsDifferingOnlyInMatchedAnswerVariantPresence_RemainDistinct()
    {
        var target = ReviewIntegrityPayload("t", (BackupReviewRating.Good, 1, 0));
        var archive = ReviewIntegrityPayload("a", (BackupReviewRating.Good, 1, 1));

        var plan = MergePreflightPlannerV2.CreatePlan(target, archive, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount,
            "A matched answer variant that is absent on one side and present on the other must not collapse.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount);
    }

    [TestMethod]
    public void ReviewsDifferingOnlyInLearningSessionReference_RemainOneEvent()
    {
        // The resolved architecture decision: LearningSessionId is referential workflow attachment, not
        // LearningReview event identity. The same real event exported under two workflow-session references
        // must still dedupe, or repeated cross-installation exchange would duplicate historical reviews.
        var target = ReviewIntegrityPayload("t", (BackupReviewRating.Good, 1, 1));
        var archive = ReviewIntegrityPayload("a", (BackupReviewRating.Good, 1, 1));
        var archiveWithOtherSession = archive with
        {
            Learning = archive.Learning with
            {
                ReviewEvents = [.. archive.Learning.ReviewEvents.Select(r => r with { LearningSessionId = "ls-other" })]
            }
        };

        var plan = MergePreflightPlannerV2.CreatePlan(target, archiveWithOtherSession, ManifestV2());

        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount,
            "LearningSessionId must never enter LearningReview merge identity.");
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.LearningReview].NewCount);
    }
}
