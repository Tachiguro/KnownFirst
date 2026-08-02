using System.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

/// <summary>
/// Service-level tests for <see cref="MergePreflightService"/> (KF-BACKUP-002 Slice 3 & Slice 7 Schema-8):
/// archive validation failure, active/undefined target workflow status, cancellation, Schema-8 native preflight,
/// determinism, and the hard guarantees — no database mutation and no filesystem creation.
/// </summary>
[TestClass]
public sealed class MergePreflightServiceTests
{
    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

    private static async Task<byte[]> BuildValidPortableArchiveAsync(TemporaryKnownFirstDatabase sourceDatabase)
    {
        var service = new BackupService(sourceDatabase, new FakePlatformInfo());
        using var stream = new MemoryStream();
        await service.CreatePortableArchiveAsync(stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> BuildV2PortableArchiveAsync(TemporarySchema8Database sourceDatabase)
    {
        var service = new BackupService(sourceDatabase, new FakePlatformInfo());
        using var stream = new MemoryStream();
        await service.CreatePortableArchiveAsync(stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static BackupAutomaticLearningState DefaultAutoLearning() =>
        new(BackupLearningInteractionMode.Reading, 0, 0, 0, false);

    private static BackupExtensions EmptyExtensions() =>
        new(new Dictionary<string, BackupExtensionPayload>());

    private static MergeManifestInfo CreateDummyManifestInfo() =>
        new(8, "1.0.0-test", 8, DateTime.UtcNow, BackupSourcePlatform.Windows);

    private static BackupPayloadV2 CreateEmptyV2Payload() =>
        new(
            Array.Empty<BackupSourceMaterial>(),
            Array.Empty<BackupVocabularyItem>(),
            Array.Empty<BackupSense>(),
            Array.Empty<BackupPreparedItemV2>(),
            Array.Empty<BackupAnswerVariant>(),
            Array.Empty<BackupSenseAnswerVariantAssignment>(),
            Array.Empty<BackupAnswerVariantProgress>(),
            new BackupLearningDataV2(Array.Empty<BackupLearningCardV2>(), Array.Empty<BackupLearningReviewV2>()),
            new BackupWorkflowDataV2(Array.Empty<BackupVocabularyReviewWorkflow>(), Array.Empty<BackupPreparationWorkflow>(), Array.Empty<BackupLearningWorkflowV2>()),
            EmptyExtensions());

    [TestMethod]
    public async Task ValidArchiveAgainstEmptyTarget_ProducesReady()
    {
        var sourceDatabase = new TemporaryKnownFirstDatabase();
        await sourceDatabase.InitializeAsync();
        var targetDatabase = new TemporaryKnownFirstDatabase();
        await targetDatabase.InitializeAsync();
        try
        {
            await sourceDatabase.RunInTransactionAsync(conn =>
            {
                conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "hello", NormalizedTerm = "hello" });
                return true;
            });

            var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);
            var service = new MergePreflightService(targetDatabase);
            using var archiveStream = new MemoryStream(archiveBytes);

            var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
            Assert.IsTrue(plan.ChecksumVerified);
            Assert.IsNotNull(plan.Manifest);
            Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task InvalidArchiveBytes_ReturnsValidationFailedWithStableCode()
    {
        var targetDatabase = new TemporaryKnownFirstDatabase();
        await targetDatabase.InitializeAsync();
        try
        {
            var service = new MergePreflightService(targetDatabase);
            using var corruptStream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));

            var plan = await service.CreatePreflightPlanAsync(corruptStream, CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.ValidationFailed, plan.Status);
            Assert.IsNotNull(plan.ErrorCode);
            Assert.IsNull(plan.Manifest);
        }
        finally
        {
            await targetDatabase.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ActiveSchema8Target_ProducesReadyWithFullPlan()
    {
        await using var sourceDatabase = new TemporaryKnownFirstDatabase("preflight-schema7-source");
        await using var targetDatabase = new TemporarySchema8Database("preflight-schema8-target");
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();
        await sourceDatabase.RunInTransactionAsync(connection =>
        {
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = "guard",
                NormalizedTerm = "guard"
            });
            return true;
        });
        var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);
        var before = await targetDatabase.ReadAsync(connection =>
            connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        var service = new MergePreflightService(targetDatabase);
        using var archiveStream = new MemoryStream(archiveBytes);

        var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsTrue(plan.ChecksumVerified);
        Assert.IsNotNull(plan.Manifest);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        Assert.AreEqual(0, plan.PerEntity[MergeEntityKind.Sense].NewCount);
        Assert.AreEqual(before, await targetDatabase.ReadAsync(connection =>
            connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words")));
    }

    [TestMethod]
    public async Task V2SourceAgainstEmptySchema8Target_SucceedsWithNewClassifications()
    {
        await using var sourceDatabase = new TemporarySchema8Database("preflight-v2-source");
        await using var targetDatabase = new TemporarySchema8Database("preflight-v2-target");
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();

        await sourceDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "book", NormalizedTerm = "book" });
            var word = conn.Table<WordEntity>().First();
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'en', '', 'general', '', '', '', 0, '2026-01-01', '2026-01-01')", Guid.NewGuid().ToString("N"), word.Id);
            return true;
        });

        var archiveBytes = await BuildV2PortableArchiveAsync(sourceDatabase);
        var service = new MergePreflightService(targetDatabase);
        using var archiveStream = new MemoryStream(archiveBytes);

        var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsTrue(plan.ChecksumVerified);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Sense].NewCount);
    }

    [TestMethod]
    public async Task PopulatedSchema8Target_ProducesExactDuplicateAndEnrichedClassifications()
    {
        await using var sourceDatabase = new TemporarySchema8Database("populated-v2-source");
        await using var targetDatabase = new TemporarySchema8Database("populated-v2-target");
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();

        await targetDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "apple", NormalizedTerm = "apple" });
            return true;
        });

        await sourceDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "apple", NormalizedTerm = "apple" });
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "banana", NormalizedTerm = "banana" });
            return true;
        });

        var archiveBytes = await BuildV2PortableArchiveAsync(sourceDatabase);
        var service = new MergePreflightService(targetDatabase);
        using var archiveStream = new MemoryStream(archiveBytes);

        var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].ExactDuplicateSkippedCount);
        Assert.AreEqual(1, plan.PerEntity[MergeEntityKind.Vocabulary].NewCount);
    }

    [TestMethod]
    public async Task TwoSensesForOneWord_RemainIndependentInPreflightPlan()
    {
        await using var sourceDatabase = new TemporarySchema8Database("twosenses-source");
        await using var targetDatabase = new TemporarySchema8Database("twosenses-target");
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();

        await sourceDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "lead", NormalizedTerm = "lead" });
            var word = conn.Table<WordEntity>().First();
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'en', '', 'metal', '', '', '', 0, '2026-01-01', '2026-01-01')", Guid.NewGuid().ToString("N"), word.Id);
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'en', '', 'guidance', '', '', '', 0, '2026-01-01', '2026-01-01')", Guid.NewGuid().ToString("N"), word.Id);
            return true;
        });

        var archiveBytes = await BuildV2PortableArchiveAsync(sourceDatabase);
        var service = new MergePreflightService(targetDatabase);
        using var archiveStream = new MemoryStream(archiveBytes);

        var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.AreEqual(2, plan.PerEntity[MergeEntityKind.Sense].NewCount);
        var senseActions = plan.Actions.Where(a => a.EntityKind == MergeEntityKind.Sense).ToList();
        Assert.HasCount(2, senseActions);
        Assert.AreNotEqual(senseActions[0].StableIdentity, senseActions[1].StableIdentity);
    }

    [TestMethod]
    public void AnswerVariantAssignmentAndProgressAndCard_PreserveSenseOwnership()
    {
        var targetPayload = CreateEmptyV2Payload();
        var archivePayload = new BackupPayloadV2(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: new[] { new BackupVocabularyItem("v1", "en", "test", "test", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed, BackupPreparationState.Unprepared, 0, 0, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupEncounteredForm>(), DefaultAutoLearning(), Array.Empty<BackupLegacyReviewSummary>()) },
            Senses: new[]
            {
                new BackupSense("s1", "st1", "v1", "en", "en", "", "domain1", "", "", "", null, BackupSenseStatus.Prepared, DateTime.UtcNow, DateTime.UtcNow),
                new BackupSense("s2", "st2", "v1", "en", "en", "", "domain2", "", "", "", null, BackupSenseStatus.Prepared, DateTime.UtcNow, DateTime.UtcNow)
            },
            PreparedLearning: new[]
            {
                new BackupPreparedItemV2("m1", "s1", "st_m1", "v1", "en", "en", "test", null, null, BackupTokenKind.Word, null, null, "test", "def", "ex", "note", null, Array.Empty<string>(), false, new BackupSourceReference("prov", "proj", "title", null, "attr"), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupContextSnapshotV2>()),
                new BackupPreparedItemV2("m2", "s2", "st_m2", "v1", "en", "en", "test", null, null, BackupTokenKind.Word, null, null, "test", "def", "ex", "note", null, Array.Empty<string>(), false, new BackupSourceReference("prov", "proj", "title", null, "attr"), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupContextSnapshotV2>())
            },
            AnswerVariants: new[]
            {
                new BackupAnswerVariant("av1", "st_av1", "s1", "en", "answer", "answer", null, DateTime.UtcNow, DateTime.UtcNow),
                new BackupAnswerVariant("av2", "st_av2", "s2", "en", "answer", "answer", null, DateTime.UtcNow, DateTime.UtcNow)
            },
            SenseAnswerVariantAssignments: new[]
            {
                new BackupSenseAnswerVariantAssignment("asn1", "st_asn1", "s1", BackupCardDirection.TermToMeaning, "av1", BackupAnswerVariantRequirement.AcceptedOnly, true, DateTime.UtcNow, DateTime.UtcNow),
                new BackupSenseAnswerVariantAssignment("asn2", "st_asn2", "s2", BackupCardDirection.TermToMeaning, "av2", BackupAnswerVariantRequirement.AcceptedOnly, true, DateTime.UtcNow, DateTime.UtcNow)
            },
            AnswerVariantProgress: new[]
            {
                new BackupAnswerVariantProgress("c1", "av1", BackupLearningInteractionMode.Reading, 0, 0, 0, null, false, false, 0, DateTime.UtcNow, DateTime.UtcNow),
                new BackupAnswerVariantProgress("c2", "av2", BackupLearningInteractionMode.Reading, 0, 0, 0, null, false, false, 0, DateTime.UtcNow, DateTime.UtcNow)
            },
            Learning: new BackupLearningDataV2(
                Cards: new[]
                {
                    new BackupLearningCardV2("c1", "v1", "s1", "m1", BackupCardDirection.TermToMeaning, BackupCardState.New, DateTime.UtcNow, 0, 2.5, 0, 0, null, null, DateTime.UtcNow, DateTime.UtcNow),
                    new BackupLearningCardV2("c2", "v1", "s2", "m2", BackupCardDirection.TermToMeaning, BackupCardState.New, DateTime.UtcNow, 0, 2.5, 0, 0, null, null, DateTime.UtcNow, DateTime.UtcNow)
                },
                ReviewEvents: Array.Empty<BackupLearningReviewV2>()
            ),
            Workflows: new BackupWorkflowDataV2(
                VocabularyReviews: Array.Empty<BackupVocabularyReviewWorkflow>(),
                PreparationBatches: Array.Empty<BackupPreparationWorkflow>(),
                LearningSessions: Array.Empty<BackupLearningWorkflowV2>()
            ),
            Extensions: EmptyExtensions());

        var plan = MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var variantActions = plan.Actions.Where(a => a.EntityKind == MergeEntityKind.AnswerVariant).ToList();
        Assert.HasCount(2, variantActions);
        Assert.AreNotEqual(variantActions[0].StableIdentity, variantActions[1].StableIdentity);

        var assignmentActions = plan.Actions.Where(a => a.EntityKind == MergeEntityKind.SenseAnswerVariantAssignment).ToList();
        Assert.HasCount(2, assignmentActions);
        Assert.AreNotEqual(assignmentActions[0].StableIdentity, assignmentActions[1].StableIdentity);

        var progressActions = plan.Actions.Where(a => a.EntityKind == MergeEntityKind.AnswerVariantProgress).ToList();
        Assert.HasCount(2, progressActions);
        Assert.AreNotEqual(progressActions[0].StableIdentity, progressActions[1].StableIdentity);

        var cardActions = plan.Actions.Where(a => a.EntityKind == MergeEntityKind.LearningCard).ToList();
        Assert.HasCount(2, cardActions);
        Assert.AreNotEqual(cardActions[0].StableIdentity, cardActions[1].StableIdentity);
    }

    [TestMethod]
    public void LegitimateNullableQueueAndReviewTargets_AreAccepted()
    {
        var targetPayload = CreateEmptyV2Payload();
        var archivePayload = new BackupPayloadV2(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: new[] { new BackupVocabularyItem("v1", "en", "test", "test", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed, BackupPreparationState.Unprepared, 0, 0, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupEncounteredForm>(), DefaultAutoLearning(), Array.Empty<BackupLegacyReviewSummary>()) },
            Senses: new[] { new BackupSense("s1", "st1", "v1", "en", "en", "p1", "topic", "pos", "gram", "acro", null, BackupSenseStatus.Prepared, DateTime.UtcNow, DateTime.UtcNow) },
            PreparedLearning: new[] { new BackupPreparedItemV2("m1", "s1", "st_m1", "v1", "en", "en", "test", null, null, BackupTokenKind.Word, null, null, "test", "def", "ex", "note", null, Array.Empty<string>(), false, new BackupSourceReference("prov", "proj", "title", null, "attr"), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupContextSnapshotV2>()) },
            AnswerVariants: new[] { new BackupAnswerVariant("av1", "st_av1", "s1", "en", "test", "test", null, DateTime.UtcNow, DateTime.UtcNow) },
            SenseAnswerVariantAssignments: Array.Empty<BackupSenseAnswerVariantAssignment>(),
            AnswerVariantProgress: Array.Empty<BackupAnswerVariantProgress>(),
            Learning: new BackupLearningDataV2(
                Cards: new[] { new BackupLearningCardV2("c1", "v1", "s1", "m1", BackupCardDirection.TermToMeaning, BackupCardState.New, DateTime.UtcNow, 0, 2.5, 0, 0, null, null, DateTime.UtcNow, DateTime.UtcNow) },
                ReviewEvents: new[] { new BackupLearningReviewV2("c1", "ls1", BackupReviewRating.Good, false, true, DateTime.UtcNow, DateTime.UtcNow, 1, 2.5, TargetAnswerVariantId: null, MatchedAnswerVariantId: null) }
            ),
            Workflows: new BackupWorkflowDataV2(
                VocabularyReviews: Array.Empty<BackupVocabularyReviewWorkflow>(),
                PreparationBatches: Array.Empty<BackupPreparationWorkflow>(),
                LearningSessions: new[] { new BackupLearningWorkflowV2("ls1", BackupLearningSessionStatus.Active, 1, 0, 0, 0, 0, 0, DateTime.UtcNow, DateTime.UtcNow, null, new[] { new BackupLearningQueueItemV2("qi1", "c1", 0, true, false, false, false, false, false, null, null, TargetAnswerVariantId: null) }) }
            ),
            Extensions: EmptyExtensions());

        var plan = MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
    }

    [TestMethod]
    public void CrossSenseRelationship_FailsClosedDeterministically()
    {
        var targetPayload = CreateEmptyV2Payload();
        var archivePayload = new BackupPayloadV2(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: new[] { new BackupVocabularyItem("v1", "en", "test", "test", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed, BackupPreparationState.Unprepared, 0, 0, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupEncounteredForm>(), DefaultAutoLearning(), Array.Empty<BackupLegacyReviewSummary>()) },
            Senses: new[]
            {
                new BackupSense("s1", "st1", "v1", "en", "en", "p1", "topic1", "pos", "gram", "acro", null, BackupSenseStatus.Prepared, DateTime.UtcNow, DateTime.UtcNow),
                new BackupSense("s2", "st2", "v1", "en", "en", "p2", "topic2", "pos", "gram", "acro", null, BackupSenseStatus.Prepared, DateTime.UtcNow, DateTime.UtcNow)
            },
            PreparedLearning: new[] { new BackupPreparedItemV2("m1", "s1", "st_m1", "v1", "en", "en", "test", null, null, BackupTokenKind.Word, null, null, "test", "def", "ex", "note", null, Array.Empty<string>(), false, new BackupSourceReference("prov", "proj", "title", null, "attr"), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupContextSnapshotV2>()) },
            AnswerVariants: new[] { new BackupAnswerVariant("av1", "st_av1", "s1", "en", "test", "test", null, DateTime.UtcNow, DateTime.UtcNow) },
            SenseAnswerVariantAssignments: Array.Empty<BackupSenseAnswerVariantAssignment>(),
            AnswerVariantProgress: Array.Empty<BackupAnswerVariantProgress>(),
            Learning: new BackupLearningDataV2(
                // Card specifies Sense s2, but PreferredMeaning m1 belongs to Sense s1 (Cross-Sense!)
                Cards: new[] { new BackupLearningCardV2("c1", "v1", "s2", "m1", BackupCardDirection.TermToMeaning, BackupCardState.New, DateTime.UtcNow, 0, 2.5, 0, 0, null, null, DateTime.UtcNow, DateTime.UtcNow) },
                ReviewEvents: Array.Empty<BackupLearningReviewV2>()
            ),
            Workflows: new BackupWorkflowDataV2(
                VocabularyReviews: Array.Empty<BackupVocabularyReviewWorkflow>(),
                PreparationBatches: Array.Empty<BackupPreparationWorkflow>(),
                LearningSessions: Array.Empty<BackupLearningWorkflowV2>()
            ),
            Extensions: EmptyExtensions());

        var ex = Assert.ThrowsExactly<MergePlanningException>(() =>
        {
            MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());
        });

        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public void DuplicateSourceIdentities_ProducesDeterministicFailureCode()
    {
        var targetPayload = CreateEmptyV2Payload();
        var archivePayload = new BackupPayloadV2(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: new[]
            {
                new BackupVocabularyItem("v1", "en", "test", "test", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed, BackupPreparationState.Unprepared, 0, 0, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupEncounteredForm>(), DefaultAutoLearning(), Array.Empty<BackupLegacyReviewSummary>()),
                // Duplicate local ID v1
                new BackupVocabularyItem("v1", "en", "test", "test", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed, BackupPreparationState.Unprepared, 0, 0, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupEncounteredForm>(), DefaultAutoLearning(), Array.Empty<BackupLegacyReviewSummary>())
            },
            Senses: Array.Empty<BackupSense>(),
            PreparedLearning: Array.Empty<BackupPreparedItemV2>(),
            AnswerVariants: Array.Empty<BackupAnswerVariant>(),
            SenseAnswerVariantAssignments: Array.Empty<BackupSenseAnswerVariantAssignment>(),
            AnswerVariantProgress: Array.Empty<BackupAnswerVariantProgress>(),
            Learning: new BackupLearningDataV2(Array.Empty<BackupLearningCardV2>(), Array.Empty<BackupLearningReviewV2>()),
            Workflows: new BackupWorkflowDataV2(Array.Empty<BackupVocabularyReviewWorkflow>(), Array.Empty<BackupPreparationWorkflow>(), Array.Empty<BackupLearningWorkflowV2>()),
            Extensions: EmptyExtensions());

        var ex = Assert.ThrowsExactly<MergePlanningException>(() =>
        {
            MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());
        });

        Assert.AreEqual(BackupErrorCodes.DuplicateId, ex.Code);
    }

    [TestMethod]
    public async Task TwoRepeatedRuns_ReturnIdenticalPlanData()
    {
        await using var sourceDatabase = new TemporarySchema8Database("repeated-run-source");
        await using var targetDatabase = new TemporarySchema8Database("repeated-run-target");
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();

        await sourceDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "repeat", NormalizedTerm = "repeat" });
            return true;
        });

        var archiveBytes = await BuildV2PortableArchiveAsync(sourceDatabase);
        var service = new MergePreflightService(targetDatabase);

        using var stream1 = new MemoryStream(archiveBytes);
        var plan1 = await service.CreatePreflightPlanAsync(stream1, CancellationToken.None);

        using var stream2 = new MemoryStream(archiveBytes);
        var plan2 = await service.CreatePreflightPlanAsync(stream2, CancellationToken.None);

        Assert.AreEqual(plan1.Status, plan2.Status);
        Assert.AreEqual(plan1.IsExecutable, plan2.IsExecutable);
        Assert.AreEqual(plan1.Actions.Count, plan2.Actions.Count);
        for (var i = 0; i < plan1.Actions.Count; i++)
        {
            Assert.AreEqual(plan1.Actions[i].StableIdentity, plan2.Actions[i].StableIdentity);
            Assert.AreEqual(plan1.Actions[i].Classification, plan2.Actions[i].Classification);
        }
    }

    // ==== KF-MEANING-001 Slice 9: distinct LearningSession identity (StartedAtUtc/CompletedAtUtc/queue order/rating) ====

    [TestMethod]
    public void LearningSession_SameCardSetDifferentStartedAt_ClassifiedAsNewNotDuplicate()
    {
        var sharedCard = new BackupLearningCardV2("c1", "v1", "s1", "m1", BackupCardDirection.TermToMeaning, BackupCardState.New, DateTime.UtcNow, 0, 2.5, 0, 0, null, null, DateTime.UtcNow, DateTime.UtcNow);
        var vocabulary = new[] { new BackupVocabularyItem("v1", "en", "test", "test", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed, BackupPreparationState.Unprepared, 0, 0, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupEncounteredForm>(), DefaultAutoLearning(), Array.Empty<BackupLegacyReviewSummary>()) };
        var senses = new[] { new BackupSense("s1", "st1", "v1", "en", "en", "p1", "topic", "pos", "gram", "acro", null, BackupSenseStatus.Prepared, DateTime.UtcNow, DateTime.UtcNow) };
        var prepared = new[] { new BackupPreparedItemV2("m1", "s1", "st_m1", "v1", "en", "en", "test", null, null, BackupTokenKind.Word, null, null, "test", "def", "ex", "note", null, Array.Empty<string>(), false, new BackupSourceReference("prov", "proj", "title", null, "attr"), DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<BackupContextSnapshotV2>()) };

        var firstStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondStart = firstStart.AddDays(1);

        BackupPayloadV2 BuildPayload(string sessionId, DateTime startedAtUtc) => new(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: vocabulary,
            Senses: senses,
            PreparedLearning: prepared,
            AnswerVariants: Array.Empty<BackupAnswerVariant>(),
            SenseAnswerVariantAssignments: Array.Empty<BackupSenseAnswerVariantAssignment>(),
            AnswerVariantProgress: Array.Empty<BackupAnswerVariantProgress>(),
            Learning: new BackupLearningDataV2(new[] { sharedCard }, Array.Empty<BackupLearningReviewV2>()),
            Workflows: new BackupWorkflowDataV2(
                Array.Empty<BackupVocabularyReviewWorkflow>(), Array.Empty<BackupPreparationWorkflow>(),
                new[]
                {
                    new BackupLearningWorkflowV2(
                        sessionId, BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0,
                        startedAtUtc, startedAtUtc, startedAtUtc,
                        new[] { new BackupLearningQueueItemV2("qi1", "c1", 0, true, false, true, false, false, true, BackupReviewRating.Good, startedAtUtc, TargetAnswerVariantId: null) })
                }),
            Extensions: EmptyExtensions());

        var targetPayload = BuildPayload("target-session", firstStart);
        var archivePayload = BuildPayload("archive-session", secondStart);

        var plan = MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        var sessionAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningWorkflow);
        Assert.AreEqual(
            MergeEntityClassification.New, sessionAction.Classification,
            "Two real sessions sharing the same card set but a different StartedAtUtc must not collapse into one merge identity.");

        // Determinism: repeated preflight runs over the same inputs must reproduce the exact same identity.
        var plan2 = MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());
        Assert.AreEqual(
            sessionAction.StableIdentity,
            plan2.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningWorkflow).StableIdentity);
    }

    [TestMethod]
    public void LearningQueueItem_ReferencesMissingCard_FailsClosed()
    {
        var targetPayload = CreateEmptyV2Payload();
        var archivePayload = new BackupPayloadV2(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: Array.Empty<BackupVocabularyItem>(),
            Senses: Array.Empty<BackupSense>(),
            PreparedLearning: Array.Empty<BackupPreparedItemV2>(),
            AnswerVariants: Array.Empty<BackupAnswerVariant>(),
            SenseAnswerVariantAssignments: Array.Empty<BackupSenseAnswerVariantAssignment>(),
            AnswerVariantProgress: Array.Empty<BackupAnswerVariantProgress>(),
            Learning: new BackupLearningDataV2(Array.Empty<BackupLearningCardV2>(), Array.Empty<BackupLearningReviewV2>()),
            Workflows: new BackupWorkflowDataV2(
                Array.Empty<BackupVocabularyReviewWorkflow>(), Array.Empty<BackupPreparationWorkflow>(),
                new[]
                {
                    new BackupLearningWorkflowV2(
                        "ls1", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0,
                        DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow,
                        new[] { new BackupLearningQueueItemV2("qi1", "missing-card", 0, true, false, true, false, false, true, BackupReviewRating.Good, DateTime.UtcNow, TargetAnswerVariantId: null) })
                }),
            Extensions: EmptyExtensions());

        Assert.ThrowsExactly<KeyNotFoundException>(() =>
        {
            MergePreflightPlannerV2.CreatePlan(targetPayload, archivePayload, CreateDummyManifestInfo());
        });
    }

    [TestMethod]
    public async Task ActiveWorkflowOnTarget_ReturnsBlockedByActiveWorkflow()
    {
        var sourceDatabase = new TemporaryKnownFirstDatabase();
        await sourceDatabase.InitializeAsync();
        var targetDatabase = new TemporaryKnownFirstDatabase();
        await targetDatabase.InitializeAsync();
        try
        {
            var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);

            await targetDatabase.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity
                {
                    Title = "Active Doc",
                    TextLanguage = "en",
                    ExplanationLanguage = "de",
                    Content = "content",
                    ContentFingerprint = new string('a', 64),
                    LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                });
                var doc = conn.Table<DocumentEntity>().First();
                conn.Insert(new ReviewSessionEntity
                {
                    DocumentId = doc.Id,
                    Status = KnownFirst.Models.ReviewSessionStatus.Active,
                    StartedAt = DateTime.UtcNow
                });
                return true;
            });

            var service = new MergePreflightService(targetDatabase);
            using var archiveStream = new MemoryStream(archiveBytes);

            var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.BlockedByActiveWorkflow, plan.Status);
            Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, plan.ErrorCode);
            Assert.IsNotNull(plan.Manifest);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task UndefinedWorkflowStatusOnTarget_FailsClosedWithStableCode()
    {
        var sourceDatabase = new TemporaryKnownFirstDatabase();
        await sourceDatabase.InitializeAsync();
        var targetDatabase = new TemporaryKnownFirstDatabase();
        await targetDatabase.InitializeAsync();
        try
        {
            var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);

            await targetDatabase.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity
                {
                    Title = "Doc",
                    TextLanguage = "en",
                    ExplanationLanguage = "de",
                    Content = "content",
                    ContentFingerprint = new string('b', 64),
                    LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                });
                var doc = conn.Table<DocumentEntity>().First();
                conn.Insert(new ReviewSessionEntity
                {
                    DocumentId = doc.Id,
                    // Out-of-range enum value: must fail closed, never be silently treated as terminal.
                    Status = (KnownFirst.Models.ReviewSessionStatus)99,
                    StartedAt = DateTime.UtcNow
                });
                return true;
            });

            var service = new MergePreflightService(targetDatabase);
            using var archiveStream = new MemoryStream(archiveBytes);

            var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.Failed, plan.Status);
            Assert.IsNotNull(plan.ErrorCode);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CancelledBeforeValidation_ReturnsCancelled()
    {
        var sourceDatabase = new TemporaryKnownFirstDatabase();
        await sourceDatabase.InitializeAsync();
        var targetDatabase = new TemporaryKnownFirstDatabase();
        await targetDatabase.InitializeAsync();
        try
        {
            var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);
            var service = new MergePreflightService(targetDatabase);
            using var archiveStream = new MemoryStream(archiveBytes);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var plan = await service.CreatePreflightPlanAsync(archiveStream, cts.Token);

            Assert.AreEqual(MergePreflightStatus.Cancelled, plan.Status);
            Assert.AreEqual(BackupErrorCodes.OperationCancelled, plan.ErrorCode);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Preflight_NeverMutatesTargetDatabase()
    {
        var sourceDatabase = new TemporaryKnownFirstDatabase();
        await sourceDatabase.InitializeAsync();
        var targetDatabase = new TemporaryKnownFirstDatabase();
        await targetDatabase.InitializeAsync();
        try
        {
            await sourceDatabase.RunInTransactionAsync(conn =>
            {
                conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "newword", NormalizedTerm = "newword" });
                return true;
            });

            await targetDatabase.RunInTransactionAsync(conn =>
            {
                conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "existingword", NormalizedTerm = "existingword" });
                return true;
            });

            var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);

            var beforeSnapshot = await targetDatabase.ExecuteSnapshotAsync(BackupSnapshotRepository.CaptureSnapshot);
            var beforeWords = beforeSnapshot.Words.Select(w => (w.Id, w.NormalizedTerm)).ToList();

            var service = new MergePreflightService(targetDatabase);
            using var archiveStream = new MemoryStream(archiveBytes);
            var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

            var afterSnapshot = await targetDatabase.ExecuteSnapshotAsync(BackupSnapshotRepository.CaptureSnapshot);
            var afterWords = afterSnapshot.Words.Select(w => (w.Id, w.NormalizedTerm)).ToList();

            CollectionAssert.AreEqual(beforeWords, afterWords);
            Assert.HasCount(1, afterSnapshot.Words);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Schema8Target_PreflightNeverMutatesTargetDatabase()
    {
        await using var sourceDatabase = new TemporarySchema8Database("schema8-no-mutate-source");
        await using var targetDatabase = new TemporarySchema8Database("schema8-no-mutate-target");
        await sourceDatabase.InitializeAsync();
        await targetDatabase.InitializeAsync();

        await sourceDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "source", NormalizedTerm = "source" });
            return true;
        });

        await targetDatabase.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "existing", NormalizedTerm = "existing" });
            var word = conn.Table<WordEntity>().First();
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'en', '', 'domain', '', '', '', 0, '2026-01-01', '2026-01-01')", Guid.NewGuid().ToString("N"), word.Id);
            return true;
        });

        var archiveBytes = await BuildV2PortableArchiveAsync(sourceDatabase);

        var beforeSnapshot = await targetDatabase.ExecuteSnapshotAsync(Data.Schema8.Schema8BackupSnapshotRepository.CaptureSnapshot);

        var service = new MergePreflightService(targetDatabase);
        using var archiveStream = new MemoryStream(archiveBytes);
        var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var afterSnapshot = await targetDatabase.ExecuteSnapshotAsync(Data.Schema8.Schema8BackupSnapshotRepository.CaptureSnapshot);

        CollectionAssert.AreEqual(
            beforeSnapshot.Words.Select(w => (w.Id, w.NormalizedTerm)).ToList(),
            afterSnapshot.Words.Select(w => (w.Id, w.NormalizedTerm)).ToList());
        CollectionAssert.AreEqual(
            beforeSnapshot.Senses.Select(s => s.StableId).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            afterSnapshot.Senses.Select(s => s.StableId).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.HasCount(1, afterSnapshot.Words);
        Assert.HasCount(1, afterSnapshot.Senses);
    }

    [TestMethod]
    public async Task Preflight_NeverCreatesSafetyCopyDirectoryOrAnyFilesystemArtifact()
    {
        var isolatedRoot = Path.Combine(Path.GetTempPath(), "kf-merge-preflight-fs-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedRoot);
        var sourceDatabase = new TemporaryKnownFirstDatabase();
        await sourceDatabase.InitializeAsync();
        var targetDatabase = new IsolatedTargetDatabase(isolatedRoot);
        await targetDatabase.InitializeAsync();
        try
        {
            await sourceDatabase.RunInTransactionAsync(conn =>
            {
                conn.Insert(new WordEntity { Language = "en", CanonicalTerm = "word", NormalizedTerm = "word" });
                return true;
            });

            var safetyCopyDirectory = Path.Combine(isolatedRoot, "merge-safety-copies");
            Assert.IsFalse(Directory.Exists(safetyCopyDirectory), "Precondition: no safety-copy directory before preflight runs.");
            var filesBefore = Directory.EnumerateFiles(isolatedRoot).ToList();

            var archiveBytes = await BuildValidPortableArchiveAsync(sourceDatabase);
            var service = new MergePreflightService(targetDatabase);
            using var archiveStream = new MemoryStream(archiveBytes);
            var plan = await service.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

            Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
            Assert.IsFalse(Directory.Exists(safetyCopyDirectory), "Preflight must never create a safety-copy directory.");

            var filesAfter = Directory.EnumerateFiles(isolatedRoot).ToList();
            CollectionAssert.AreEquivalent(filesBefore, filesAfter);
        }
        finally
        {
            await sourceDatabase.DisposeAsync();
            await targetDatabase.DisposeAsync();
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    private sealed class IsolatedTargetDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLite.SQLiteAsyncConnection? _connection;
        private bool _initialized;

        public IsolatedTargetDatabase(string directory)
        {
            DatabasePath = Path.Combine(directory, "knownfirst.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            _connection ??= new SQLite.SQLiteAsyncConnection(DatabasePath);
            await Schema7Fixture.InitializeEmptyAsync(_connection);
            _initialized = true;
        }

        public async Task<T> ReadAsync<T>(Func<SQLite.SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                return await operation(_connection!);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> RunInTransactionAsync<T>(Func<SQLite.SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLite.SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ResetAsync() => throw new NotSupportedException("Not used by this test.");

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
            }

            _gate.Dispose();
        }
    }
}
