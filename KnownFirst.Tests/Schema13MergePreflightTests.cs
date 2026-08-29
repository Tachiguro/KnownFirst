using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13MergePreflightTests
{
    private static readonly DateTime BaseTime = new(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task PopulatedSchema13Preview_IdenticalV3_ReturnsExecutableNoChange()
    {
        var payload = CreatePayload();
        var archive = await WriteV3Async(payload);
        await using var target = await CreateSchema13TargetAsync(archive);

        var preview = await CreateService(target)
            .PreviewPortableImportAsync(new MemoryStream(archive), CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, preview.Disposition, preview.ErrorCode);
        Assert.IsFalse(preview.CanConfirm);
        Assert.IsFalse(preview.WillMutate);
    }

    [TestMethod]
    public async Task PopulatedSchema13Preview_TargetHistoryExactPrefix_PlansSourceTailExtension()
    {
        var targetPayload = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(await WriteV3Async(targetPayload));

        var preview = await CreateService(target)
            .PreviewPortableImportAsync(
                new MemoryStream(await WriteV3Async(sourcePayload)),
                CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition, preview.ErrorCode);
        Assert.IsTrue(preview.CanConfirm);
        Assert.IsTrue(preview.WillMutate);
    }

    [TestMethod]
    public async Task PopulatedSchema13Preview_DivergentCausalHistories_ReturnsDeterministicConflict()
    {
        var targetPayload = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("target-branch", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var sourcePayload = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("source-branch", 2, BackupReviewRating.Easy, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(await WriteV3Async(targetPayload));

        var preview = await CreateService(target)
            .PreviewPortableImportAsync(
                new MemoryStream(await WriteV3Async(sourcePayload)),
                CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.Blocked, preview.Disposition);
        Assert.AreEqual("schema13-causal-history-conflict", preview.ErrorCode);
        Assert.IsFalse(preview.CanConfirm);
    }

    [TestMethod]
    public void WordLearningControls_AddReconcileAndPreserve_UseEarliestDecision()
    {
        var baseline = CreatePayload();
        var absent = baseline with { WordLearningControls = [] };
        var later = baseline with
        {
            WordLearningControls = [new BackupWordLearningControl("vocab_1", BaseTime.AddDays(2))]
        };

        AssertAction(
            Schema13MergePreflightPlanner.CreatePlan(absent, baseline),
            Schema13MergeActionClassification.AddWordLearningControl,
            "word-learning-control",
            sourceTimestamp: BaseTime);
        AssertAction(
            Schema13MergePreflightPlanner.CreatePlan(later, baseline),
            Schema13MergeActionClassification.ReconcileWordLearningControlTimestamp,
            "word-learning-control",
            targetTimestamp: BaseTime.AddDays(2),
            sourceTimestamp: BaseTime);
        AssertAction(
            Schema13MergePreflightPlanner.CreatePlan(baseline, later),
            Schema13MergeActionClassification.NoChange,
            "word-learning-control",
            targetTimestamp: BaseTime,
            sourceTimestamp: BaseTime.AddDays(2));
        AssertAction(
            Schema13MergePreflightPlanner.CreatePlan(baseline, absent),
            Schema13MergeActionClassification.PreserveTargetOnly,
            "word-learning-control",
            targetTimestamp: BaseTime);
    }

    [TestMethod]
    public void SenseLearningControls_AddReconcileAndPreserve_KeepSiblingSensesDistinct()
    {
        var baseline = CreatePayload();
        var sibling = baseline.Senses[0] with
        {
            Id = "sense_2",
            StableId = "st_sense_2",
            ProviderSenseId = "provider-sibling",
            DefaultMeaningId = null
        };
        var target = baseline with
        {
            Senses = [baseline.Senses[0], sibling],
            SenseLearningControls = [new BackupSenseLearningControl("sense_1", BaseTime.AddDays(2))]
        };
        var source = target with
        {
            SenseLearningControls =
            [
                new BackupSenseLearningControl("sense_1", BaseTime),
                new BackupSenseLearningControl("sense_2", BaseTime.AddDays(1))
            ]
        };

        var plan = Schema13MergePreflightPlanner.CreatePlan(target, source);
        var senseActions = plan.Actions.Where(action =>
            action.ReasonCode.StartsWith("sense-learning-control", StringComparison.Ordinal)).ToList();
        Assert.HasCount(2, senseActions);
        Assert.HasCount(2, senseActions.Select(action => action.SemanticIdentity).Distinct().ToList());
        Assert.HasCount(1, senseActions.Where(action => action.Classification ==
            Schema13MergeActionClassification.ReconcileSenseLearningControlTimestamp).ToList());
        Assert.HasCount(1, senseActions.Where(action => action.Classification ==
            Schema13MergeActionClassification.AddSenseLearningControl).ToList());

        var preserved = Schema13MergePreflightPlanner.CreatePlan(target, source with { SenseLearningControls = [] });
        Assert.HasCount(1, preserved.Actions.Where(action => action.Classification ==
            Schema13MergeActionClassification.PreserveTargetOnly).ToList());
    }

    [TestMethod]
    public void FsrsPrefixExtension_PreservesExactTailOrderAndResultingStateExpectation()
    {
        var target = CreatePayload(new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var source = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)),
            new HistoryFact("event-3", 3, BackupReviewRating.Easy, BaseTime.AddDays(2)));

        var plan = Schema13MergePreflightPlanner.CreatePlan(target, source);
        var appends = plan.Actions
            .Where(action => action.Classification == Schema13MergeActionClassification.AppendFsrsReviewHistory)
            .ToList();

        Assert.IsTrue(plan.IsExecutable);
        CollectionAssert.AreEqual(new[] { 2, 3 }, appends.Select(action => action.ReviewFact!.SequenceNumber).ToArray());
        CollectionAssert.AreEqual(new[] { "event-2", "event-3" }, appends.Select(action => action.ReviewFact!.StableId).ToArray());
        Assert.HasCount(1, plan.Actions.Where(action => action.Classification ==
            Schema13MergeActionClassification.UpdateFsrsCardState).ToList());
        Assert.AreEqual(1, plan.TargetExpectations.Single(expectation =>
            expectation.Kind == Schema13TargetExpectationKind.FsrsLearningCard).FsrsHistory.Count);
    }

    [TestMethod]
    public void FsrsTargetAheadPrefix_PreservesTargetWithoutRollback()
    {
        var source = CreatePayload(new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var target = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));

        var plan = Schema13MergePreflightPlanner.CreatePlan(target, source);

        Assert.IsTrue(plan.IsExecutable);
        Assert.IsFalse(plan.RequiresMutation);
        Assert.HasCount(1, plan.Actions.Where(action => action.ReasonCode ==
            "fsrs-target-history-ahead-preserved").ToList());
    }

    [TestMethod]
    public void FsrsEqualTimestamps_UseSequenceAndStableIdWithoutFalseConflict()
    {
        var target = CreatePayload(new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var source = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime));

        var plan = Schema13MergePreflightPlanner.CreatePlan(target, source);

        Assert.IsTrue(plan.IsExecutable);
        var append = plan.Actions.Single(action => action.Classification ==
            Schema13MergeActionClassification.AppendFsrsReviewHistory);
        Assert.AreEqual(2, append.ReviewFact!.SequenceNumber);
        Assert.AreEqual(BaseTime, append.ReviewFact.ReviewedAtUtc);
    }

    [TestMethod]
    public void FsrsStableIdCollision_WithDifferentFacts_IsNonExecutable()
    {
        var target = CreatePayload(new HistoryFact("shared-id", 1, BackupReviewRating.Good, BaseTime));
        var source = CreatePayload(new HistoryFact("shared-id", 1, BackupReviewRating.Hard, BaseTime));

        var plan = Schema13MergePreflightPlanner.CreatePlan(target, source);

        Assert.IsFalse(plan.IsExecutable);
        Assert.IsTrue(plan.Conflicts.Any(conflict =>
            conflict.ReasonCode == Schema13MergePreflightErrorCodes.StableIdCollision));
        Assert.AreEqual(Schema13MergePreflightErrorCodes.StableIdCollision, plan.Conflicts[0].ReasonCode);
    }

    [TestMethod]
    public void NewSourceCard_PlansHistoryAndStateBySemanticIdentity_WithoutTargetLocalId()
    {
        var source = CreatePayload(new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var target = EmptyPayload();

        var plan = Schema13MergePreflightPlanner.CreatePlan(target, source);

        Assert.IsTrue(plan.IsExecutable);
        Assert.HasCount(1, plan.Actions.Where(action => action.Classification ==
            Schema13MergeActionClassification.AppendFsrsReviewHistory).ToList());
        var state = plan.Actions.Single(action => action.Classification ==
            Schema13MergeActionClassification.InsertFsrsCardState);
        Assert.AreEqual(64, state.SemanticIdentity.Length);
        Assert.IsFalse(state.ExpectedTargetEntityPresent);
        Assert.IsFalse(plan.TargetExpectations.Single(expectation =>
            expectation.Kind == Schema13TargetExpectationKind.FsrsLearningCard).SemanticEntityPresent);
    }

    [TestMethod]
    public void TargetOnlyCard_IsPreservedWithoutDeletionAction()
    {
        var plan = Schema13MergePreflightPlanner.CreatePlan(CreatePayload(), EmptyPayload());

        Assert.IsTrue(plan.IsExecutable);
        Assert.IsFalse(plan.RequiresMutation);
        Assert.IsTrue(plan.Actions.Any(action => action.ReasonCode == "fsrs-card-target-only-preserved"));
        Assert.IsFalse(plan.Actions.Any(action => action.ReasonCode.Contains("delete", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void PlanOrderingAndActionKeys_AreDeterministicAcrossCollectionOrder()
    {
        var baseline = CreatePayload();
        var sibling = baseline.Senses[0] with
        {
            Id = "sense_2",
            StableId = "st_sense_2",
            ProviderSenseId = "provider-sibling",
            DefaultMeaningId = null
        };
        var target = baseline with
        {
            Senses = [baseline.Senses[0], sibling],
            SenseLearningControls =
            [
                new BackupSenseLearningControl("sense_1", BaseTime),
                new BackupSenseLearningControl("sense_2", BaseTime.AddMinutes(1))
            ]
        };
        var source = target;
        var reorderedTarget = target with
        {
            Vocabulary = target.Vocabulary.Reverse().ToList(),
            Senses = target.Senses.Reverse().ToList(),
            SenseLearningControls = target.SenseLearningControls.Reverse().ToList(),
            FsrsCardStates = target.FsrsCardStates.Reverse().ToList()
        };
        var first = Schema13MergePreflightPlanner.CreatePlan(target, source);
        var second = Schema13MergePreflightPlanner.CreatePlan(reorderedTarget, source);

        CollectionAssert.AreEqual(
            first.Actions.Select(action => action.ActionKey).ToArray(),
            second.Actions.Select(action => action.ActionKey).ToArray());
        Assert.AreEqual(first.ExpectedTargetFingerprint, second.ExpectedTargetFingerprint);
    }

    [TestMethod]
    public async Task Preview_IsReadOnly_AndIdenticalImportCompletesWithoutSafetyCopyOrWriter()
    {
        var targetArchive = await WriteV3Async(CreatePayload());
        await using var target = await CreateSchema13TargetAsync(targetArchive);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target);
        var safetyCopy = new RecordingSafetyCopyService();
        var writer = new RecordingWriterService();
        var service = new BackupService(
            database,
            new TestPlatformInfo(),
            mergeSafetyCopyService: safetyCopy,
            mergeWriterService: writer);
        var before = await CapturePayloadBytesAsync(database);

        var preview = await service.PreviewPortableImportAsync(
            new MemoryStream(targetArchive),
            CancellationToken.None);
        var after = await CapturePayloadBytesAsync(database);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, preview.Disposition, preview.ErrorCode);
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(0, safetyCopy.CallCount);
        Assert.AreEqual(0, writer.CallCount);

        var import = await service.ImportPortableArchiveAsync(
            new MemoryStream(targetArchive),
            CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, import.Status, import.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, import.Summary?.Disposition);
        Assert.AreEqual(0, safetyCopy.CallCount);
        Assert.AreEqual(0, writer.CallCount);
    }

    [TestMethod]
    public async Task LegacyV1AndV2_ControlProjection_UsesBootstrapOracleWithoutInventingSenseControls()
    {
        var legacy = MergePreflightFixtures.Payload(
            vocabulary:
            [
                MergePreflightFixtures.Vocabulary(
                    "legacy-word",
                    knowledgeState: BackupKnowledgeState.Known)
            ]);
        var archives = new[]
        {
            await WriteV1Async(legacy),
            await WriteV2Async(BackupArchiveV1UpgradePolicy.Upgrade(legacy))
        };

        foreach (var archive in archives)
        {
            await using var target = await CreateSchema13TargetAsync(archive);
            var plan = await new MergePreflightService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target))
                .CreatePreflightPlanAsync(new MemoryStream(archive), CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.NoChanges, plan.Status, plan.ErrorCode);
            Assert.IsNotNull(plan.Schema13Plan);
            Assert.IsTrue(plan.Schema13Plan.IsExecutable);
            Assert.HasCount(1, plan.Schema13Plan.Actions.Where(action =>
                action.Classification == Schema13MergeActionClassification.NoChange
                && action.ReasonCode == "word-learning-control-identical").ToList());
            Assert.IsFalse(plan.Schema13Plan.Actions.Any(action =>
                action.ReasonCode.StartsWith("sense-learning-control", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public async Task LegacyV1AndV2_ExactPrefixExtension_UsesBootstrapCausalTail()
    {
        var targetLegacy = CreateLegacyPayload(
            new HistoryFact("legacy-source-id-unused", 1, BackupReviewRating.Good, BaseTime));
        var sourceLegacy = CreateLegacyPayload(
            new HistoryFact("legacy-source-id-unused", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("legacy-source-id-unused-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var archivePairs = new[]
        {
            (Target: await WriteV1Async(targetLegacy), Source: await WriteV1Async(sourceLegacy)),
            (
                Target: await WriteV2Async(BackupArchiveV1UpgradePolicy.Upgrade(targetLegacy)),
                Source: await WriteV2Async(BackupArchiveV1UpgradePolicy.Upgrade(sourceLegacy)))
        };

        foreach (var pair in archivePairs)
        {
            await using var target = await CreateSchema13TargetAsync(pair.Target);
            var plan = await new MergePreflightService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target))
                .CreatePreflightPlanAsync(new MemoryStream(pair.Source), CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.Ready, plan.Status, plan.ErrorCode);
            Assert.IsNotNull(plan.Schema13Plan);
            var append = plan.Schema13Plan.Actions.Single(action =>
                action.Classification == Schema13MergeActionClassification.AppendFsrsReviewHistory);
            Assert.AreEqual(2, append.ReviewFact!.SequenceNumber);
            Assert.AreEqual(1, plan.Schema13Plan.TargetExpectations.Single(expectation =>
                expectation.Kind == Schema13TargetExpectationKind.FsrsLearningCard).FsrsHistory.Count);
        }
    }

    [TestMethod]
    public async Task LegacyV2_ProgressedCardWithoutFactualHistory_FailsClosed()
    {
        var legacy = Schema8BackupFixtureBuilders.BuildSingleAssignmentPayloadV2(
            BackupAnswerVariantRequirement.Required,
            Schema8BackupFixtureBuilders.Slice4Boundary.RequiredSinceUtc);
        var archive = await WriteV2Async(legacy);
        await using var target = await CreateSchema13TargetAsync(await WriteV3Async(CreatePayload()));

        var plan = await new MergePreflightService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target))
            .CreatePreflightPlanAsync(new MemoryStream(archive), CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.NonExecutableConflict, plan.Status);
        Assert.AreEqual(Schema13MergePreflightErrorCodes.LegacyHistoryInsufficient, plan.ErrorCode);
        Assert.IsFalse(plan.IsExecutable);
        Assert.IsNotNull(plan.Schema13Plan);
        Assert.HasCount(1, plan.Schema13Plan.Conflicts);
    }

    [TestMethod]
    public void TargetExpectations_ChangeFingerprintAndFailStructuralPlanComparisonWhenTargetChanges()
    {
        var target = CreatePayload(new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var source = CreatePayload(
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var manifest = new MergeManifestInfo(3, "slice4-test", 13, BaseTime, BackupSourcePlatform.Windows);
        var supplied = Schema13MergePreflightPlanner.CreateCombinedPlan(target, source, manifest);
        var recomputed = Schema13MergePreflightPlanner.CreateCombinedPlan(target, source, manifest);
        var changedTarget = target with
        {
            WordLearningControls = [new BackupWordLearningControl("vocab_1", BaseTime.AddMinutes(1))]
        };
        var stale = Schema13MergePreflightPlanner.CreateCombinedPlan(changedTarget, source, manifest);

        Assert.IsTrue(MergeWritePlanComparer.Matches(supplied, recomputed));
        Assert.AreNotEqual(
            supplied.Schema13Plan!.ExpectedTargetFingerprint,
            stale.Schema13Plan!.ExpectedTargetFingerprint);
        Assert.IsFalse(MergeWritePlanComparer.Matches(supplied, stale));
    }

    [TestMethod]
    public void CombinedPlan_PreservesInheritedBaseGraphConflict()
    {
        var target = CreatePayload() with
        {
            Vocabulary = [CreatePayload().Vocabulary[0] with { KnowledgeState = BackupKnowledgeState.Known }]
        };
        var source = CreatePayload() with
        {
            Vocabulary = [CreatePayload().Vocabulary[0] with { KnowledgeState = BackupKnowledgeState.Ignored }]
        };
        var manifest = new MergeManifestInfo(3, "slice4-test", 13, BaseTime, BackupSourcePlatform.Windows);

        var plan = Schema13MergePreflightPlanner.CreateCombinedPlan(target, source, manifest);

        Assert.AreEqual(MergePreflightStatus.RequiresUserDecision, plan.Status);
        Assert.IsFalse(plan.IsExecutable);
        Assert.HasCount(1, plan.KnowledgeStateConflictDecisions);
        Assert.IsNotNull(plan.Schema13Plan);
    }

    private static BackupService CreateService(Schema7Fixture target) =>
        new(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target), new TestPlatformInfo());

    private static async Task<Schema7Fixture> CreateSchema13TargetAsync(byte[] archive)
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);

        var result = await CreateService(fixture)
            .ImportPortableArchiveAsync(new MemoryStream(archive), CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        return fixture;
    }

    private static async Task<byte[]> WriteV3Async(BackupPayloadV3 payload)
    {
        using var stream = new MemoryStream();
        await BackupArchiveWriterV3.WriteArchiveAsync(
            payload,
            new TestPlatformInfo(),
            BaseTime,
            stream,
            CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> WriteV1Async(BackupPayload payload)
    {
        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(
            payload,
            new TestPlatformInfo(),
            new ValidatedSchema7Capability(),
            BaseTime,
            stream,
            CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> WriteV2Async(BackupPayloadV2 payload)
    {
        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(
            payload,
            new TestPlatformInfo(),
            new ValidatedSchema8Capability(),
            BaseTime,
            stream,
            CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> CapturePayloadBytesAsync(IKnownFirstDatabase database)
    {
        using var stream = new MemoryStream();
        await new BackupService(database, new TestPlatformInfo())
            .CreatePortableArchiveAsync(stream, CancellationToken.None);
        stream.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        return BackupJsonCodecV3.SerializeData(validated.V3!.Payload);
    }

    private static BackupPayloadV3 EmptyPayload() => new(
        [], [], [], [], [], [], [],
        new BackupLearningDataV2([], []),
        new BackupWorkflowDataV2([], [], []),
        [], [], [], [], [],
        new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));

    private static void AssertAction(
        Schema13MergePreflightPlan plan,
        Schema13MergeActionClassification classification,
        string reasonPrefix,
        DateTime? targetTimestamp = null,
        DateTime? sourceTimestamp = null)
    {
        var action = plan.Actions.Single(item => item.Classification == classification
            && item.ReasonCode.StartsWith(reasonPrefix, StringComparison.Ordinal));
        Assert.AreEqual(targetTimestamp, action.ExpectedTargetControlDecidedAtUtc);
        Assert.AreEqual(sourceTimestamp, action.SourceControlDecidedAtUtc);
    }

    private static BackupPayloadV3 CreatePayload(params HistoryFact[] history)
    {
        const string text = "network";
        var textSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var fsrsEvents = history
            .OrderBy(item => item.SequenceNumber)
            .Select(item => new Fsrs6ReviewEvent(
                new DateTimeOffset(item.ReviewedAtUtc, TimeSpan.Zero),
                ToCoreRating(item.Rating)))
            .ToList();
        var state = fsrsEvents.Count == 0
            ? Fsrs6Card.New()
            : new Fsrs6Replayer().Replay(Fsrs6Card.New(), fsrsEvents);

        return new BackupPayloadV3(
            SourceMaterials:
            [
                new BackupSourceMaterial(
                    "doc_1", "Document 1", "en", "de", BackupLexicalLookupMode.Definition, null,
                    text, textSha, BaseTime, 1,
                    [new BackupSentenceRange("sent_1", 0, 0, text.Length)],
                    [new BackupOccurrence("vocab_1", "sent_1", 0, text.Length, text, 0, BackupTechnicalTokenFamily.None, null, null, null)])
            ],
            Vocabulary:
            [
                new BackupVocabularyItem(
                    "vocab_1", "en", text, "en:network", BackupTokenKind.Word,
                    BackupKnowledgeState.Unreviewed, BackupPreparationState.Prepared, 1, 1,
                    BaseTime, BaseTime, [new BackupEncounteredForm(text, 1)],
                    new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false), [])
            ],
            Senses:
            [
                new BackupSense(
                    "sense_1", "st_sense_1", "vocab_1", "en", "de", "", "", "", "", "",
                    "prep_1", BackupSenseStatus.Learning, BaseTime, BaseTime)
            ],
            PreparedLearning:
            [
                new BackupPreparedItemV2(
                    "prep_1", "sense_1", "st_prep_1", "vocab_1", "en", "de", text, text, null,
                    BackupTokenKind.Word, null, null, "Netzwerk", null, null, null, null, [], true,
                    new BackupSourceReference("manual", "", "", null, ""), BaseTime, BaseTime, BaseTime,
                    [new BackupContextSnapshotV2("doc_1", "Document 1", text, 0, text.Length, "fp_1", BaseTime, "sense_1")])
            ],
            AnswerVariants:
            [
                new BackupAnswerVariant(
                    "ans_1", "st_ans_1", "sense_1", "de", "Netzwerk", "netzwerk", "prep_1", BaseTime, BaseTime)
            ],
            SenseAnswerVariantAssignments:
            [
                new BackupSenseAnswerVariantAssignment(
                    "asgn_1", "st_asgn_1", "sense_1", BackupCardDirection.MeaningToTerm, "ans_1",
                    BackupAnswerVariantRequirement.Required, true, BaseTime, BaseTime, BaseTime)
            ],
            AnswerVariantProgress: [],
            Learning: new BackupLearningDataV2(
                [new BackupLearningCardV2(
                    "card_1", "vocab_1", "sense_1", "prep_1", BackupCardDirection.MeaningToTerm,
                    BackupCardState.New, BaseTime, 0, 2.5, 0, 0, null, null, BaseTime, BaseTime)],
                []),
            Workflows: new BackupWorkflowDataV2([], [], []),
            DerivedTermEvidence: [],
            WordLearningControls: [new BackupWordLearningControl("vocab_1", BaseTime)],
            SenseLearningControls: [new BackupSenseLearningControl("sense_1", BaseTime)],
            FsrsReviewHistoryEntries: history
                .Select(item => new BackupFsrsReviewHistoryEntry(
                    item.StableId, "card_1", item.SequenceNumber, item.Rating, item.ReviewedAtUtc))
                .ToList(),
            FsrsCardStates:
            [
                new BackupFsrsCardState(
                    "card_1", (BackupFsrsCardStateKind)state.State, state.Stability, state.Difficulty,
                    state.LastReviewedAtUtc?.UtcDateTime, state.StepIndex, state.DueAtUtc?.UtcDateTime)
            ],
            Extensions: new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));
    }

    private static BackupPayload CreateLegacyPayload(params HistoryFact[] history)
    {
        var vocabulary = MergePreflightFixtures.Vocabulary(
            "legacy-vocabulary",
            preparationState: BackupPreparationState.Prepared);
        var prepared = MergePreflightFixtures.PreparedItem("legacy-prepared", vocabulary.Id);
        var card = MergePreflightFixtures.Card("legacy-card", vocabulary.Id, prepared.Id);
        var reviews = history
            .OrderBy(item => item.SequenceNumber)
            .Select(item => MergePreflightFixtures.Review(
                card.Id,
                "legacy-session",
                item.Rating,
                reviewedAtUtc: item.ReviewedAtUtc))
            .ToList();
        var workflow = MergePreflightFixtures.LearningWorkflow(
            "legacy-session",
            [MergePreflightFixtures.QueueItem("legacy-queue", card.Id, rating: history.Last().Rating)]);
        return MergePreflightFixtures.Payload(
            vocabulary: [vocabulary],
            preparedLearning: [prepared],
            cards: [card],
            reviews: reviews,
            learningWorkflows: [workflow]);
    }

    private static ReviewRating ToCoreRating(BackupReviewRating rating) => rating switch
    {
        BackupReviewRating.Again => ReviewRating.Again,
        BackupReviewRating.Hard => ReviewRating.Hard,
        BackupReviewRating.Good => ReviewRating.Good,
        BackupReviewRating.Easy => ReviewRating.Easy,
        _ => throw new ArgumentOutOfRangeException(nameof(rating))
    };

    private sealed record HistoryFact(
        string StableId,
        int SequenceNumber,
        BackupReviewRating Rating,
        DateTime ReviewedAtUtc);

    private sealed class TestPlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-slice4-test";
    }

    private sealed class RecordingSafetyCopyService : IMergeSafetyCopyService
    {
        public int CallCount { get; private set; }

        public Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
            string? sourceDescription,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(MergeSafetyCopyResult.Failed);
        }
    }

    private sealed class RecordingWriterService : IMergeWriterService
    {
        public int CallCount { get; private set; }

        public Task<MergeWriteResult> ApplyAsync(
            BackupPayloadV2 archive,
            MergePreflightPlan plan,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(MergeWriteResult.SuccessResult);
        }

        public Task<MergeWriteResult> ApplySchema13Async(
            BackupPayloadV3 archive,
            MergePreflightPlan plan,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(MergeWriteResult.SuccessResult);
        }
    }
}
