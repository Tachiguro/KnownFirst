using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeWorkflowIdentityTests
{
    private static readonly DateTime StartedAt = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    // --- ReviewSession / ReviewCandidate ---

    [TestMethod]
    public void ReviewSessionIdentity_EqualsAcrossArchives_WhenDocumentIdentityMatches()
    {
        var documentIdentity = new SourceMaterialIdentity("DOC-HASH");

        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(documentIdentity),
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(documentIdentity));
    }

    [TestMethod]
    public void ReviewSessionIdentity_ResolvesArchiveLocalSourceMaterialIdThroughMap()
    {
        var documentIdentity = new SourceMaterialIdentity("DOC-HASH");
        var map = new Dictionary<string, SourceMaterialIdentity> { ["source-1"] = documentIdentity };
        var workflow = new BackupVocabularyReviewWorkflow(
            "workflow-1", "source-1", BackupReviewSessionStatus.Completed, 1, 1, 1, 0, 0, 1,
            StartedAt, CompletedAt, []);

        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(documentIdentity),
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(workflow, map));
    }

    [TestMethod]
    public void ReviewCandidateIdentity_CombinesDocumentAndVocabularyIdentity()
    {
        var documentIdentity = new SourceMaterialIdentity("DOC-HASH");
        var vocabularyIdentity = new VocabularyIdentity("VOCAB-HASH");
        var otherVocabularyIdentity = new VocabularyIdentity("OTHER-VOCAB-HASH");

        var identity1 = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(documentIdentity, vocabularyIdentity);
        var identity2 = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(documentIdentity, vocabularyIdentity);
        var differentVocabulary = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(documentIdentity, otherVocabularyIdentity);

        Assert.AreEqual(identity1, identity2);
        Assert.AreNotEqual(identity1, differentVocabulary);
    }

    // --- Package A: v2 completed-ReviewSession / ReviewCandidate identity ---

    private static readonly SourceMaterialIdentity DocumentIdentity = new("DOC-HASH");
    private static readonly SourceMaterialIdentity OtherDocumentIdentity = new("OTHER-DOC-HASH");

    private static ReviewSessionCandidateContent Candidate(
        string vocabularyValue,
        int order = 0,
        BackupKnowledgeState status = BackupKnowledgeState.Known,
        BackupKnowledgeState previousKnowledgeState = BackupKnowledgeState.Unreviewed,
        int previousTotalOccurrenceCount = 2,
        int previousDocumentCount = 1,
        DateTime? previousUpdatedAtUtc = null,
        int decisionSequence = 1,
        bool wasVocabularyCreatedForSession = false,
        DateTime? decidedAtUtc = null,
        bool withoutDecidedAt = false) =>
        new(new VocabularyIdentity(vocabularyValue), order, status, previousKnowledgeState,
            previousTotalOccurrenceCount, previousDocumentCount, previousUpdatedAtUtc ?? StartedAt,
            decisionSequence, wasVocabularyCreatedForSession,
            withoutDecidedAt ? null : decidedAtUtc ?? CompletedAt);

    private static readonly ReviewSessionOutcomeCounters BaselineCounters = new(1, 1, 1, 0, 0);

    private static ReviewSessionIdentity SessionV2(
        IReadOnlyList<ReviewSessionCandidateContent> candidates,
        DateTime? startedAtUtc = null,
        DateTime? completedAtUtc = null,
        bool withoutCompletedAt = false,
        BackupReviewSessionStatus status = BackupReviewSessionStatus.Completed,
        int decisionSequence = 1,
        SourceMaterialIdentity? documentIdentity = null,
        ReviewSessionOutcomeCounters? counters = null)
    {
        var result = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            documentIdentity ?? DocumentIdentity, status, startedAtUtc ?? StartedAt,
            withoutCompletedAt ? null : completedAtUtc ?? CompletedAt, decisionSequence,
            counters ?? BaselineCounters, candidates);
        Assert.IsFalse(result.HasDuplicateCandidateVocabularyIdentity);
        return result.Identity;
    }

    private static BackupVocabularyReviewItem WorkflowItem(
        string id, string vocabularyId, int order = 0, BackupKnowledgeState status = BackupKnowledgeState.Known) =>
        new(id, vocabularyId, order, status, BackupKnowledgeState.Unreviewed, 2, 1, StartedAt, 1, false, CompletedAt);

    private static BackupVocabularyReviewWorkflow Workflow(
        string id, string sourceMaterialId, IReadOnlyList<BackupVocabularyReviewItem> items,
        int totalCandidates = 1, int reviewedCount = 1, int knownCount = 1, int unknownCount = 0, int ignoredCount = 0,
        int decisionSequence = 1, DateTime? startedAtUtc = null, DateTime? completedAtUtc = null) =>
        new(id, sourceMaterialId, BackupReviewSessionStatus.Completed, totalCandidates, reviewedCount,
            knownCount, unknownCount, ignoredCount, decisionSequence,
            startedAtUtc ?? StartedAt, completedAtUtc ?? CompletedAt, items);

    [TestMethod]
    public void ReviewSessionIdentityV2_SameCompletedHistory_DifferentArchiveLocalIds_IsEqual()
    {
        var documentMapA = new Dictionary<string, SourceMaterialIdentity> { ["sm-a"] = DocumentIdentity };
        var documentMapB = new Dictionary<string, SourceMaterialIdentity> { ["sm-b"] = DocumentIdentity };
        var vocabularyMapA = new Dictionary<string, VocabularyIdentity> { ["v-a"] = new("VOCAB") };
        var vocabularyMapB = new Dictionary<string, VocabularyIdentity> { ["v-b"] = new("VOCAB") };

        var workflowA = Workflow("vr-1", "sm-a", [WorkflowItem("rc-1", "v-a")]);
        var workflowB = Workflow("vr-99", "sm-b", [WorkflowItem("rc-77", "v-b")]);

        var identityA = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(workflowA, documentMapA, vocabularyMapA);
        var identityB = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(workflowB, documentMapB, vocabularyMapB);

        Assert.IsFalse(identityA.HasDuplicateCandidateVocabularyIdentity);
        Assert.IsFalse(identityB.HasDuplicateCandidateVocabularyIdentity);
        Assert.AreEqual(identityA.Identity, identityB.Identity, "Archive-local workflow/item ids must never affect the identity.");
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_DifferentCandidateDecisions_ProducesDifferentIdentity()
    {
        var known = SessionV2([Candidate("VOCAB", status: BackupKnowledgeState.Known)]);
        var ignored = SessionV2([Candidate("VOCAB", status: BackupKnowledgeState.Ignored)]);
        var laterDecision = SessionV2([Candidate("VOCAB", decisionSequence: 2)]);
        var otherPreviousState = SessionV2([Candidate("VOCAB", previousKnowledgeState: BackupKnowledgeState.UnknownBacklog)]);
        var otherDecidedAt = SessionV2([Candidate("VOCAB", decidedAtUtc: CompletedAt.AddSeconds(1))]);
        var createdForSession = SessionV2([Candidate("VOCAB", wasVocabularyCreatedForSession: true)]);
        var otherPreviousCounts = SessionV2([Candidate("VOCAB", previousTotalOccurrenceCount: 3)]);
        var otherPreviousUpdatedAt = SessionV2([Candidate("VOCAB", previousUpdatedAtUtc: StartedAt.AddMinutes(1))]);

        var all = new[]
        {
            known, ignored, laterDecision, otherPreviousState, otherDecidedAt,
            createdForSession, otherPreviousCounts, otherPreviousUpdatedAt
        };

        CollectionAssert.AllItemsAreUnique(all, "Every candidate decision field must participate in the session identity.");
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_DifferentCompletedAtUtc_ProducesDifferentIdentity()
    {
        var baseline = SessionV2([Candidate("VOCAB")]);
        var laterCompletion = SessionV2([Candidate("VOCAB")], completedAtUtc: CompletedAt.AddMinutes(1));
        var laterStart = SessionV2([Candidate("VOCAB")], startedAtUtc: StartedAt.AddMinutes(1));

        Assert.AreNotEqual(baseline, laterCompletion);
        Assert.AreNotEqual(baseline, laterStart);
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_IncludesRetainedOutcomeCounters()
    {
        // Ordinary completion deletes every ReviewCandidate row, so a retained Completed session normally
        // exports with Items empty and these five counters are its only surviving outcome summary. They are
        // therefore authoritative retained content, not derived data.
        IReadOnlyList<ReviewSessionCandidateContent> candidates = [Candidate("VOCAB")];
        var baselineCounters = new ReviewSessionOutcomeCounters(3, 3, 2, 1, 0);
        var baseline = SessionV2(candidates, counters: baselineCounters);

        (string Field, ReviewSessionOutcomeCounters Counters)[] variations =
        [
            ("TotalCandidates", baselineCounters with { TotalCandidates = 4 }),
            ("ReviewedCount", baselineCounters with { ReviewedCount = 2 }),
            ("KnownCount", baselineCounters with { KnownCount = 1 }),
            ("UnknownCount", baselineCounters with { UnknownCount = 2 }),
            ("IgnoredCount", baselineCounters with { IgnoredCount = 1 })
        ];

        foreach (var (field, counters) in variations)
        {
            Assert.AreNotEqual(
                baseline,
                SessionV2(candidates, counters: counters),
                $"{field} is retained completed-review outcome data and must participate in the v2 identity.");
        }

        // Pairwise distinct as well, so no two counters can share one encoding slot.
        CollectionAssert.AllItemsAreUnique(
            variations.Select(variation => SessionV2(candidates, counters: variation.Counters)).ToArray(),
            "Each retained outcome counter must occupy its own position in the canonical encoding.");
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_ExcludesAbsoluteCandidateOrder()
    {
        // Same candidates, same content, same relative ordering — only the absolute Order values differ.
        var baseline = SessionV2([Candidate("VOCAB-A", order: 0), Candidate("VOCAB-B", order: 1)]);
        var renumbered = SessionV2([Candidate("VOCAB-A", order: 40), Candidate("VOCAB-B", order: 41)]);

        Assert.AreEqual(
            baseline, renumbered,
            "Absolute candidate Order values are positional and must stay excluded from the identity.");
    }

    [TestMethod]
    public void ReviewCandidateIdentityV2_ParentSessionIdentitySeparatesCandidates()
    {
        var vocabularyIdentity = new VocabularyIdentity("VOCAB");
        var sessionA = SessionV2([Candidate("VOCAB")]);
        var sessionB = SessionV2([Candidate("VOCAB")], completedAtUtc: CompletedAt.AddMinutes(5));

        Assert.AreNotEqual(sessionA, sessionB, "Precondition: the two parent sessions must be distinct.");
        Assert.AreNotEqual(
            ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(sessionA, vocabularyIdentity),
            ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(sessionB, vocabularyIdentity),
            "The same vocabulary under a different parent session must be a distinct candidate identity.");
        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(sessionA, vocabularyIdentity),
            ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(sessionA, vocabularyIdentity));
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_CanonicalizationIsDeterministicAcrossEnumerationOrder()
    {
        var first = Candidate("VOCAB-A", order: 0);
        var second = Candidate("VOCAB-B", order: 1);
        Assert.AreEqual(SessionV2([first, second]), SessionV2([second, first]));

        // Equal Order values are tie-broken by ordinal VocabularyIdentity, never by declaration order.
        var tieA = Candidate("VOCAB-A", order: 7);
        var tieB = Candidate("VOCAB-B", order: 7);
        Assert.AreEqual(SessionV2([tieA, tieB]), SessionV2([tieB, tieA]));

        // Relative order still matters: swapping which vocabulary was decided first is a different history.
        Assert.AreNotEqual(
            SessionV2([Candidate("VOCAB-A", order: 0), Candidate("VOCAB-B", order: 1, decisionSequence: 2)]),
            SessionV2([Candidate("VOCAB-B", order: 0), Candidate("VOCAB-A", order: 1, decisionSequence: 2)]));
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_DuplicateCandidateVocabularyIdentity_FailsClosedWithDuplicateId()
    {
        var result = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            DocumentIdentity,
            BackupReviewSessionStatus.Completed,
            StartedAt,
            CompletedAt,
            2,
            BaselineCounters,
            [Candidate("VOCAB", order: 0), Candidate("VOCAB", order: 1, decisionSequence: 2)]);

        Assert.IsTrue(
            result.HasDuplicateCandidateVocabularyIdentity,
            "Two candidates resolving to one stable vocabulary identity must fail closed, never silently collapse.");
        Assert.AreEqual(new VocabularyIdentity("VOCAB"), result.DuplicateCandidateVocabularyIdentity);
    }

    [TestMethod]
    public void ReviewSessionIdentityV1_RemainsUnchanged()
    {
        var map = new Dictionary<string, SourceMaterialIdentity> { ["sm-1"] = DocumentIdentity };
        var lean = Workflow("vr-1", "sm-1", [WorkflowItem("rc-1", "v-1")], knownCount: 1);
        var divergent = Workflow("vr-2", "sm-1", [WorkflowItem("rc-2", "v-1", status: BackupKnowledgeState.Ignored)], knownCount: 5);

        // v1 is document identity alone — unchanged, and therefore still blind to history divergence.
        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(DocumentIdentity),
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(lean, map));
        Assert.AreEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(lean, map),
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(divergent, map));
        Assert.AreNotEqual(
            ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(DocumentIdentity),
            SessionV2([Candidate("VOCAB")]),
            "v1 and v2 must never share a hash space.");

        Assert.AreNotEqual(
            ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(DocumentIdentity, new VocabularyIdentity("VOCAB")),
            ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(OtherDocumentIdentity, new VocabularyIdentity("VOCAB")));
    }

    // --- Package A: timestamp canonicalization ---

    [TestMethod]
    public void ReviewSessionIdentityV2_UtcAndUnspecifiedKindWithSameTicks_CanonicalizeIdentically()
    {
        var utc = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc).AddTicks(1234567);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        Assert.AreEqual(
            SessionV2([Candidate("VOCAB", previousUpdatedAtUtc: utc, decidedAtUtc: utc)], startedAtUtc: utc, completedAtUtc: utc),
            SessionV2([Candidate("VOCAB", previousUpdatedAtUtc: unspecified, decidedAtUtc: unspecified)], startedAtUtc: unspecified, completedAtUtc: unspecified),
            "A raw SQLite (Unspecified) value and an archive DTO (Utc) value with identical ticks must canonicalize identically.");
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_IsIndependentOfLocalMachineTimeZone()
    {
        var utc = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        var baseline = SessionV2([Candidate("VOCAB", previousUpdatedAtUtc: utc)], startedAtUtc: utc, completedAtUtc: utc);

        // A Local value is rejected outright, so no machine-time-zone conversion can ever run.
        Assert.ThrowsExactly<ArgumentException>(() => SessionV2(
            [Candidate("VOCAB", previousUpdatedAtUtc: utc)],
            startedAtUtc: DateTime.SpecifyKind(utc, DateTimeKind.Local),
            completedAtUtc: utc));

        // And the wall-clock value a naive local conversion would have produced is a different identity.
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(utc);
        var naivelyConverted = DateTime.SpecifyKind(utc.Add(localOffset), DateTimeKind.Utc);
        var shifted = SessionV2([Candidate("VOCAB", previousUpdatedAtUtc: utc)], startedAtUtc: naivelyConverted, completedAtUtc: utc);
        Assert.IsTrue(
            localOffset == TimeSpan.Zero || !baseline.Equals(shifted),
            "A time-zone-shifted instant must never canonicalize onto the UTC instant's identity.");
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_NullableCompletedAtUtc_IsEncodedDeterministically()
    {
        var withoutCompletion = SessionV2([Candidate("VOCAB")], withoutCompletedAt: true);
        var withoutCompletionAgain = SessionV2([Candidate("VOCAB")], withoutCompletedAt: true);
        var withCompletion = SessionV2([Candidate("VOCAB")], completedAtUtc: CompletedAt);
        var withoutDecision = SessionV2([Candidate("VOCAB", withoutDecidedAt: true)]);

        Assert.AreEqual(withoutCompletion, withoutCompletionAgain);
        Assert.AreNotEqual(withoutCompletion, withCompletion);
        Assert.AreNotEqual(withoutDecision, SessionV2([Candidate("VOCAB")]));
    }

    [TestMethod]
    public void ReviewSessionIdentityV2_LocalKindTimestamp_IsRejectedDeterministically()
    {
        var local = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Local);

        Assert.ThrowsExactly<ArgumentException>(() => ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            DocumentIdentity, BackupReviewSessionStatus.Completed, local, CompletedAt, 1, BaselineCounters, [Candidate("VOCAB")]));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            DocumentIdentity, BackupReviewSessionStatus.Completed, StartedAt, local, 1, BaselineCounters, [Candidate("VOCAB")]));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            DocumentIdentity, BackupReviewSessionStatus.Completed, StartedAt, CompletedAt, 1, BaselineCounters,
            [Candidate("VOCAB", previousUpdatedAtUtc: local)]));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            DocumentIdentity, BackupReviewSessionStatus.Completed, StartedAt, CompletedAt, 1, BaselineCounters,
            [Candidate("VOCAB", decidedAtUtc: local)]));
    }

    // --- PreparationSession / PreparationCandidate ---

    [TestMethod]
    public void PreparationSessionIdentity_SameStartCompletedAndItems_Deduplicates()
    {
        var items = new List<VocabularyIdentity> { new("A"), new("B") };

        var identity1 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);

        Assert.AreEqual(identity1, identity2);
    }

    [TestMethod]
    public void PreparationSessionIdentity_DifferentItemOrder_ProducesDifferentIdentity()
    {
        var forward = new List<VocabularyIdentity> { new("A"), new("B") };
        var reversed = new List<VocabularyIdentity> { new("B"), new("A") };

        var identity1 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, forward);
        var identity2 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, reversed);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void PreparationSessionIdentity_DifferentCompletedAt_ProducesDifferentIdentity()
    {
        var items = new List<VocabularyIdentity> { new("A") };

        var identity1 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt.AddMinutes(1), items);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void PreparationSessionIdentity_ResolvesItemsInOrderThroughMap()
    {
        var identityA = new VocabularyIdentity("A");
        var identityB = new VocabularyIdentity("B");
        var map = new Dictionary<string, VocabularyIdentity> { ["vocab-a"] = identityA, ["vocab-b"] = identityB };

        var workflow = new BackupPreparationWorkflow(
            "workflow-1", BackupPreparationSessionStatus.Completed, BackupPreparationMethod.Manual,
            2, 2, StartedAt, CompletedAt, CompletedAt,
            [
                new BackupPreparationItem("item-2", "vocab-b", 1, BackupPreparationCandidateStatus.Prepared, 0, null, 1, CompletedAt, null),
                new BackupPreparationItem("item-1", "vocab-a", 0, BackupPreparationCandidateStatus.Prepared, 0, null, 1, CompletedAt, null),
            ]);

        var expected = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, [identityA, identityB]);
        var actual = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(workflow, map);

        Assert.AreEqual(expected, actual, "Items must be ordered by their Order field, not archive declaration order.");
    }

    [TestMethod]
    public void PreparationCandidateIdentity_CascadesFromParentSession()
    {
        var sessionIdentity1 = new PreparationSessionIdentity("SESSION-A");
        var sessionIdentity2 = new PreparationSessionIdentity("SESSION-B");
        var vocabularyIdentity = new VocabularyIdentity("VOCAB");

        var underSessionA = PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(sessionIdentity1, vocabularyIdentity, 0);
        var underSessionB = PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(sessionIdentity2, vocabularyIdentity, 0);

        Assert.AreNotEqual(underSessionA, underSessionB, "Same vocabulary/order under a different parent session must be a distinct candidate identity.");
    }

    // --- LearningSession / LearningSessionCard ---

    [TestMethod]
    public void LearningSessionIdentity_SameQueueContent_Deduplicates()
    {
        var items = new List<(LearningCardMatchIdentity, BackupReviewRating?)>
        {
            (new LearningCardMatchIdentity("CARD-A"), BackupReviewRating.Good),
            (new LearningCardMatchIdentity("CARD-B"), null),
        };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, items);

        Assert.AreEqual(identity1, identity2);
    }

    [TestMethod]
    public void LearningSessionIdentity_DifferentRatingForSameCard_ProducesDifferentIdentity()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-A");

        var goodItems = new List<(LearningCardMatchIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Good) };
        var hardItems = new List<(LearningCardMatchIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Hard) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, goodItems);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSessionIdentity(StartedAt, CompletedAt, hardItems);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void LearningSessionCardIdentity_CascadesFromParentSession()
    {
        var sessionIdentity1 = new LearningSessionIdentity("SESSION-A");
        var sessionIdentity2 = new LearningSessionIdentity("SESSION-B");
        var cardIdentity = new LearningCardMatchIdentity("CARD");

        var underSessionA = LearningWorkflowIdentityPolicy.ComputeSessionCardIdentity(sessionIdentity1, cardIdentity, 0);
        var underSessionB = LearningWorkflowIdentityPolicy.ComputeSessionCardIdentity(sessionIdentity2, cardIdentity, 0);

        Assert.AreNotEqual(underSessionA, underSessionB);
    }

    [TestMethod]
    public void LearningSessionIdentity_LocalArchiveCardId_DoesNotAffectIdentity()
    {
        var cardIdentity = new LearningCardMatchIdentity("CARD-HASH");
        var map = new Dictionary<string, LearningCardMatchIdentity> { ["card-alpha"] = cardIdentity, ["card-beta"] = cardIdentity };

        var workflow1 = new BackupLearningWorkflow(
            "workflow-1", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, StartedAt, CompletedAt, CompletedAt,
            [new BackupLearningQueueItem("queue-1", "card-alpha", 0, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt)]);

        var workflow2 = new BackupLearningWorkflow(
            "workflow-2", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, StartedAt, CompletedAt, CompletedAt,
            [new BackupLearningQueueItem("queue-1", "card-beta", 0, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt)]);

        Assert.AreEqual(
            LearningWorkflowIdentityPolicy.ComputeSessionIdentity(workflow1, map),
            LearningWorkflowIdentityPolicy.ComputeSessionIdentity(workflow2, map));
    }

    // --- Schema-8 LearningSession / LearningQueueItem (FutureCardIdentity-based, KF-MEANING-001 Slice 9) ---

    [TestMethod]
    public void Schema8SessionIdentity_SameTimestampsQueueAndRatings_Deduplicates()
    {
        var items = new List<(FutureCardIdentity, BackupReviewRating?)>
        {
            (new FutureCardIdentity("CARD-A"), BackupReviewRating.Good),
            (new FutureCardIdentity("CARD-B"), null),
        };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);

        Assert.AreEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_DifferentStartedAt_ProducesDifferentIdentity()
    {
        var items = new List<(FutureCardIdentity, BackupReviewRating?)> { (new FutureCardIdentity("CARD-A"), BackupReviewRating.Good) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt.AddMinutes(1), CompletedAt, items);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_DifferentCompletedAt_ProducesDifferentIdentity()
    {
        var items = new List<(FutureCardIdentity, BackupReviewRating?)> { (new FutureCardIdentity("CARD-A"), BackupReviewRating.Good) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, items);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, null, items);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_DifferentRatingForSameCard_ProducesDifferentIdentity()
    {
        var cardIdentity = new FutureCardIdentity("CARD-A");
        var goodItems = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Good) };
        var hardItems = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardIdentity, BackupReviewRating.Hard) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, goodItems);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, hardItems);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8SessionIdentity_SameCardsDifferentRelativeOrder_ProducesDifferentIdentity()
    {
        var cardA = new FutureCardIdentity("CARD-A");
        var cardB = new FutureCardIdentity("CARD-B");
        var forward = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardA, null), (cardB, null) };
        var reversed = new List<(FutureCardIdentity, BackupReviewRating?)> { (cardB, null), (cardA, null) };

        var identity1 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, forward);
        var identity2 = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(StartedAt, CompletedAt, reversed);

        Assert.AreNotEqual(identity1, identity2);
    }

    [TestMethod]
    public void Schema8QueueItemIdentity_DifferentQueueOrder_ProducesDifferentIdentity()
    {
        var cardIdentity = new FutureCardIdentity("CARD");

        var atOrderZero = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-A", cardIdentity, 0);
        var atOrderOne = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-A", cardIdentity, 1);

        Assert.AreNotEqual(atOrderZero, atOrderOne);
    }

    [TestMethod]
    public void Schema8QueueItemIdentity_DifferentParentSession_ProducesDifferentIdentity()
    {
        var cardIdentity = new FutureCardIdentity("CARD");

        var underSessionA = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-A", cardIdentity, 0);
        var underSessionB = LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity("SESSION-B", cardIdentity, 0);

        Assert.AreNotEqual(underSessionA, underSessionB);
    }

    [TestMethod]
    public void Schema8SessionIdentity_ResolvesQueueItemsInOrderThroughMap_NotDeclarationOrder()
    {
        var identityA = new FutureCardIdentity("CARD-A");
        var identityB = new FutureCardIdentity("CARD-B");
        var map = new Dictionary<string, FutureCardIdentity> { ["card-a"] = identityA, ["card-b"] = identityB };

        var workflow = new BackupLearningWorkflowV2(
            "session-1", BackupLearningSessionStatus.Completed, 2, 2, 0, 0, 2, 0, StartedAt, StartedAt, CompletedAt,
            [
                new BackupLearningQueueItemV2("qi-2", "card-b", 1, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt, null),
                new BackupLearningQueueItemV2("qi-1", "card-a", 0, true, false, true, false, false, true, BackupReviewRating.Good, CompletedAt, null),
            ]);

        var expected = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(
            StartedAt, CompletedAt, [(identityA, BackupReviewRating.Good), (identityB, BackupReviewRating.Good)]);
        var actual = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(workflow, map);

        Assert.AreEqual(expected, actual, "Queue items must be ordered by QueueOrder, not archive declaration order.");
    }
}
