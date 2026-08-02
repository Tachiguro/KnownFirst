using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 8 — transactional Schema-8 populated-target merge writer. Every database is a
/// synthetic, isolated temporary SQLite fixture (<see cref="Schema7Fixture"/> / <see cref="TemporarySchema8Database"/>
/// + <see cref="Data.Migrations.Schema8.Schema8DormantMigration"/>); no real user database is ever opened.
/// </summary>
[TestClass]
public sealed class MergeWriterServiceTests
{
    private sealed class ThrowAtMutationInjector(int throwAtCount) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
            if (mutationCount == throwAtCount)
            {
                throw new InvalidOperationException($"Injected failure at mutation {mutationCount}.");
            }
        }
    }

    private sealed class CancelAfterMutationInjector(CancellationTokenSource source, int cancelAtCount) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
            if (mutationCount == cancelAtCount)
            {
                source.Cancel();
            }
        }
    }

    private static MergeManifestInfo DummyManifest() =>
        new(BackupFormatLimits.CurrentArchiveFormatVersion, "1.0.0-test", 8, DateTime.UtcNow, BackupSourcePlatform.Windows);

    private static async Task<Schema8BackupSnapshot> CaptureSnapshotAsync(Schema7Fixture fixture)
    {
        Schema8BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        return snapshot!;
    }

    private static async Task<BackupPayloadV2> CapturePayloadAsync(Schema7Fixture fixture) =>
        BackupModelMapperV2.MapToExternal(await CaptureSnapshotAsync(fixture));

    private static async Task<MergePreflightPlan> ComputePlanAsync(Schema7Fixture target, BackupPayloadV2 archive)
    {
        Schema8PortableSnapshotCaptureResult? captureResult = null;
        await target.Connection.RunInTransactionAsync(conn => captureResult = Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(conn));
        var targetPayload = BackupModelMapperV2.MapToExternal(captureResult!.Snapshot!);
        return MergePreflightPlannerV2.CreatePlan(targetPayload, archive, DummyManifest());
    }

    private static async Task<MergePreflightPlan> ComputePlanAsync(IKnownFirstDatabase target, BackupPayloadV2 archive)
    {
        var captureResult = await target.ExecuteSnapshotAsync(Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy);
        var targetPayload = BackupModelMapperV2.MapToExternal(captureResult.Snapshot!);
        return MergePreflightPlannerV2.CreatePlan(targetPayload, archive, DummyManifest());
    }

    private static async Task<int> CountAsync(Schema7Fixture fixture, string sql) =>
        await fixture.Connection.ExecuteScalarAsync<int>(sql);

    /// <summary>Builds the source fixture, merges it into a fresh empty Schema-8 target, and returns the
    /// target for assertion. The source fixture is disposed before returning — only its captured archive
    /// payload is needed once the write completes.</summary>
    private static async Task<Schema7Fixture> MergeRepresentativeIntoEmptyTargetAsync()
    {
        BackupPayloadV2 archive;
        await using (var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync())
        {
            archive = await CapturePayloadAsync(sourceFixture);
        }

        var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var plan = await ComputePlanAsync(targetFixture, archive);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        if (result.Status != MergeWriteStatus.Success)
        {
            throw new InvalidOperationException($"Test setup merge failed: {result.Status}/{result.ErrorCode}");
        }

        return targetFixture;
    }

    /// <summary>Referential-integrity re-check across every FK-shaped relationship the writer populates.
    /// The live schema declares no SQLite-native FOREIGN KEY constraints (verified: no table DDL in
    /// <c>Schema8Ddl</c> uses REFERENCES), so <c>PRAGMA foreign_key_check</c> alone is not a meaningful
    /// check here — these explicit LEFT JOIN checks are the actual assertion.</summary>
    private static async Task AssertReferentialIntegrityAsync(Schema7Fixture target)
    {
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM Senses s LEFT JOIN Words w ON w.Id = s.WordId WHERE w.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM Meanings m LEFT JOIN Words w ON w.Id = m.WordId LEFT JOIN Senses s ON s.Id = m.SenseId WHERE w.Id IS NULL OR s.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM AnswerVariants v LEFT JOIN Senses s ON s.Id = v.SenseId WHERE s.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments a LEFT JOIN Senses s ON s.Id = a.SenseId LEFT JOIN AnswerVariants v ON v.Id = a.AnswerVariantId WHERE s.Id IS NULL OR v.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM LearningCards c LEFT JOIN Words w ON w.Id = c.WordId LEFT JOIN Senses s ON s.Id = c.SenseId LEFT JOIN Meanings m ON m.Id = c.PreferredMeaningId WHERE w.Id IS NULL OR s.Id IS NULL OR m.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningCards c ON c.Id = r.CardId LEFT JOIN LearningSessions ls ON ls.Id = r.SessionId WHERE c.Id IS NULL OR ls.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM AnswerVariantProgress p LEFT JOIN LearningCards c ON c.Id = p.CardId LEFT JOIN AnswerVariants v ON v.Id = p.AnswerVariantId WHERE c.Id IS NULL OR v.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN LearningSessions ls ON ls.Id = q.SessionId LEFT JOIN LearningCards c ON c.Id = q.CardId WHERE ls.Id IS NULL OR c.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(target, "SELECT COUNT(*) FROM ContextSnapshots cs LEFT JOIN Meanings m ON m.Id = cs.MeaningId LEFT JOIN Senses s ON s.Id = cs.SenseId LEFT JOIN Documents d ON d.Id = cs.SourceDocumentId WHERE m.Id IS NULL OR s.Id IS NULL OR d.Id IS NULL"));
    }

    // ---- 1. Non-executable plans are rejected before mutation ----
    [TestMethod]
    public async Task NonExecutablePlan_RejectedBeforeMutation()
    {
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        var nonExecutablePlan = plan with { Status = MergePreflightStatus.RequiresUserDecision, IsExecutable = false };

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, nonExecutablePlan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.NotExecutable, result.Status);
        Assert.AreEqual(0, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words"));
    }

    // ---- 2. Empty target receives a complete valid source graph ----
    [TestMethod]
    public async Task EmptyTarget_ReceivesCompleteValidSourceGraph()
    {
        await using var target = await MergeRepresentativeIntoEmptyTargetAsync();

        Assert.AreEqual(2, await CountAsync(target, "SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(3, await CountAsync(target, "SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(4, await CountAsync(target, "SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(2, await CountAsync(target, "SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(2, await CountAsync(target, "SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(2, await CountAsync(target, "SELECT COUNT(*) FROM LearningSessionCards"));
        await AssertReferentialIntegrityAsync(target);
    }

    // ---- 3. Populated target reuses existing Words and Senses and inserts only missing entities ----
    [TestMethod]
    public async Task PopulatedTarget_ReusesExistingEntities_InsertsOnlyMissing()
    {
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        var bankWordId = await sourceFixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");
        // Additional exact-meaning-variant for the *existing* "bank"/"finance-1" sense — a genuinely new
        // piece of content attached to entities that otherwise already exist in the target.
        await sourceFixture.InsertMeaningAsync(
            bankWordId, displayTerm: "bank", translation: "Sparkasse", selectedMeaningId: "finance-1", definition: "savings institution");
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var wordsBefore = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words");
        var sensesBefore = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Senses");
        var meaningsBefore = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Meanings");
        var bankWordIdBefore = await CountAsync(targetFixture, "SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, result.Status);
        Assert.AreEqual(wordsBefore, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(sensesBefore, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(meaningsBefore + 1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words WHERE CanonicalTerm = 'bank'"));
        Assert.AreEqual(bankWordIdBefore, await CountAsync(targetFixture, "SELECT Id FROM Words WHERE CanonicalTerm = 'bank'"));
    }

    // ---- 4. Two Senses for one Word remain independent after writing ----
    [TestMethod]
    public async Task TwoSensesForOneWord_RemainIndependentAfterWriting()
    {
        await using var target = await MergeRepresentativeIntoEmptyTargetAsync();

        var lightWordId = await target.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'light'");
        var senseIds = await target.Connection.QueryScalarsAsync<int>("SELECT Id FROM Senses WHERE WordId = ?", lightWordId);
        Assert.HasCount(2, senseIds);
        Assert.AreEqual(2, senseIds.Distinct().Count());

        var meaningSenseIds = await target.Connection.QueryScalarsAsync<int>(
            "SELECT DISTINCT SenseId FROM Meanings WHERE WordId = ?", lightWordId);
        Assert.HasCount(2, meaningSenseIds);
        CollectionAssert.AreEquivalent(senseIds, meaningSenseIds);
    }

    // ---- 5. Meaning/AnswerVariant/assignment/progress/card/direction/PreferredMeaning ownership ----
    [TestMethod]
    public async Task EntityOwnership_RemainsCorrectAcrossCardsVariantsAssignmentsAndProgress()
    {
        await using var target = await MergeRepresentativeIntoEmptyTargetAsync();

        var cards = await target.Connection.QueryAsync<CardOwnershipRow>(
            "SELECT Id, SenseId, PreferredMeaningId, Direction FROM LearningCards");
        Assert.HasCount(2, cards);
        var directions = cards.Select(c => c.Direction).OrderBy(d => d).ToList();
        CollectionAssert.AreEqual(new[] { (int)CardDirection.TermToMeaning, (int)CardDirection.MeaningToTerm }.OrderBy(d => d).ToList(), directions);

        foreach (var card in cards)
        {
            var preferredMeaningSenseId = await target.Connection.ExecuteScalarAsync<int>(
                "SELECT SenseId FROM Meanings WHERE Id = ?", card.PreferredMeaningId);
            Assert.AreEqual(card.SenseId, preferredMeaningSenseId, "A card's preferred meaning must belong to the card's own Sense.");
        }

        var variants = await target.Connection.QueryAsync<VariantOwnershipRow>("SELECT Id, SenseId FROM AnswerVariants");
        Assert.IsGreaterThan(0, variants.Count);

        var assignments = await target.Connection.QueryAsync<AssignmentOwnershipRow>(
            "SELECT SenseId, AnswerVariantId FROM SenseAnswerVariantAssignments");
        Assert.IsGreaterThan(0, assignments.Count);
        var variantSenseById = variants.ToDictionary(v => v.Id, v => v.SenseId);
        foreach (var assignment in assignments)
        {
            Assert.AreEqual(assignment.SenseId, variantSenseById[assignment.AnswerVariantId], "An assignment's variant must belong to the assignment's own Sense.");
        }

        var progressRows = await target.Connection.QueryAsync<ProgressOwnershipRow>(
            "SELECT CardId, AnswerVariantId FROM AnswerVariantProgress");
        var cardSenseById = cards.ToDictionary(c => c.Id, c => c.SenseId);
        foreach (var progress in progressRows)
        {
            Assert.AreEqual(cardSenseById[progress.CardId], variantSenseById[progress.AnswerVariantId], "Progress must reference a variant belonging to its card's Sense.");
        }
    }

    private sealed class CardOwnershipRow
    {
        public int Id { get; set; }
        public int SenseId { get; set; }
        public int PreferredMeaningId { get; set; }
        public int Direction { get; set; }
    }

    private sealed class VariantOwnershipRow
    {
        public int Id { get; set; }
        public int SenseId { get; set; }
    }

    private sealed class AssignmentOwnershipRow
    {
        public int SenseId { get; set; }
        public int AnswerVariantId { get; set; }
    }

    private sealed class ProgressOwnershipRow
    {
        public int CardId { get; set; }
        public int AnswerVariantId { get; set; }
    }

    // ---- 6. Nullable queue/review targets remain valid ----
    [TestMethod]
    public async Task NullableQueueAndReviewTargets_RemainValid()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var sourceQueueTargets = await sourceFixture.Connection.QueryScalarsAsync<int?>("SELECT TargetAnswerVariantId FROM LearningSessionCards ORDER BY Id");

        var archive = await CapturePayloadAsync(sourceFixture);
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var plan = await ComputePlanAsync(targetFixture, archive);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status);

        // Nullability of every queue item's TargetAnswerVariantId must be preserved exactly (not
        // silently defaulted to null nor invented as non-null) — compared as a multiset since row order
        // across a merge is not guaranteed to match archive insertion order.
        var targetQueueTargets = await targetFixture.Connection.QueryScalarsAsync<int?>("SELECT TargetAnswerVariantId FROM LearningSessionCards ORDER BY Id");
        Assert.HasCount(sourceQueueTargets.Count, targetQueueTargets);
        CollectionAssert.AreEquivalent(
            sourceQueueTargets.Select(id => id is null).ToList(),
            targetQueueTargets.Select(id => id is null).ToList());

        // Every non-null reference must resolve to a real, existing AnswerVariant row — never orphaned.
        Assert.AreEqual(0, await CountAsync(targetFixture,
            "SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN AnswerVariants v ON v.Id = q.TargetAnswerVariantId WHERE q.TargetAnswerVariantId IS NOT NULL AND v.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(targetFixture,
            "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN AnswerVariants v ON v.Id = r.TargetAnswerVariantId WHERE r.TargetAnswerVariantId IS NOT NULL AND v.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(targetFixture,
            "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN AnswerVariants v ON v.Id = r.MatchedAnswerVariantId WHERE r.MatchedAnswerVariantId IS NOT NULL AND v.Id IS NULL"));
    }

    // ---- 7. Archive-v1 upgraded payload can be written into a Schema-8 target ----
    [TestMethod]
    public async Task ArchiveV1UpgradedPayload_CanBeWrittenIntoSchema8Target()
    {
        await using var sourceFixtureV1 = await Schema7Fixture.CreateAsync();
        var wordId = await sourceFixtureV1.InsertWordAsync("network");
        var meaningId = await sourceFixtureV1.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        await sourceFixtureV1.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        BackupSnapshot? v1Snapshot = null;
        await sourceFixtureV1.Connection.RunInTransactionAsync(conn => v1Snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        var v1Payload = BackupModelMapper.MapToExternal(v1Snapshot!);
        var archiveV2 = BackupArchiveV1UpgradePolicy.Upgrade(v1Payload);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var plan = await ComputePlanAsync(targetFixture, archiveV2);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archiveV2, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, result.Status);
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningCards"));
        await AssertReferentialIntegrityAsync(targetFixture);
    }

    // ---- 8. A forced mid-write failure rolls back every change ----
    [TestMethod]
    public async Task ForcedMidWriteFailure_RollsBackEveryChange()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archive = await CapturePayloadAsync(sourceFixture);
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var plan = await ComputePlanAsync(targetFixture, archive);

        var beforeState = await targetFixture.CapturePersistentStateAsync();

        var injector = new ThrowAtMutationInjector(3);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), injector);
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Failed, result.Status);
        var afterState = await targetFixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(beforeState, afterState);
    }

    // ---- 9. Cancellation rolls back every change ----
    [TestMethod]
    public async Task Cancellation_RollsBackEveryChange()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archive = await CapturePayloadAsync(sourceFixture);
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var plan = await ComputePlanAsync(targetFixture, archive);

        var beforeState = await targetFixture.CapturePersistentStateAsync();

        using var cts = new CancellationTokenSource();
        var injector = new CancelAfterMutationInjector(cts, 3);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), injector);
        var result = await writer.ApplyAsync(archive, plan, cts.Token);

        Assert.AreEqual(MergeWriteStatus.Cancelled, result.Status);
        var afterState = await targetFixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(beforeState, afterState);
    }

    // ---- 10. A stale or mismatched plan is rejected without mutation ----
    [TestMethod]
    public async Task StaleOrMismatchedPlan_RejectedWithoutMutation()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archive = await CapturePayloadAsync(sourceFixture);
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        // Target changes after the plan was computed but before the writer runs — the plan is now stale.
        await targetFixture.InsertWordAsync("bank", status: WordStatus.Learning);

        var beforeState = await targetFixture.CapturePersistentStateAsync();
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.StalePlan, result.Status);
        var afterState = await targetFixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(beforeState, afterState);
    }

    // ---- 11. Reapplying the same source converges without duplicate domain entities or events ----
    [TestMethod]
    public async Task ReapplyingSameSource_ConvergesWithoutDuplicates()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archive = await CapturePayloadAsync(sourceFixture);
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();

        var plan1 = await ComputePlanAsync(targetFixture, archive);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result1 = await writer.ApplyAsync(archive, plan1, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result1.Status);

        var wordsAfterFirst = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words");
        var sensesAfterFirst = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Senses");
        var meaningsAfterFirst = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Meanings");
        var cardsAfterFirst = await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningCards");
        var reviewsAfterFirst = await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningReviews");

        var plan2 = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.NoChanges, plan2.Status);

        var result2 = await writer.ApplyAsync(archive, plan2, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result2.Status);

        Assert.AreEqual(wordsAfterFirst, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(sensesAfterFirst, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(meaningsAfterFirst, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(cardsAfterFirst, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(reviewsAfterFirst, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningReviews"));
    }

    // ---- 12. Foreign-key validation succeeds after commit ----
    [TestMethod]
    public async Task ForeignKeyValidation_SucceedsAfterCommit()
    {
        await using var target = await MergeRepresentativeIntoEmptyTargetAsync();
        await AssertReferentialIntegrityAsync(target);
    }

    // ---- 13. The writer creates no safety-copy or external filesystem artifact ----
    [TestMethod]
    public async Task Writer_CreatesNoSafetyCopyOrExternalFilesystemArtifact()
    {
        var targetDb = new TemporarySchema8Database();
        await targetDb.InitializeAsync();
        try
        {
            await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
            var archive = await CapturePayloadAsync(sourceFixture);
            var plan = await ComputePlanAsync(targetDb, archive);

            var directory = Path.GetDirectoryName(targetDb.DatabasePath)!;
            bool HasSafetyCopyArtifact() => Directory.GetFiles(directory, "merge-safety-*").Length > 0;
            Assert.IsFalse(HasSafetyCopyArtifact(), "Precondition: no safety-copy artifact before the write.");

            var writer = new MergeWriterService(targetDb);
            var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

            Assert.AreEqual(MergeWriteStatus.Success, result.Status);
            Assert.IsFalse(HasSafetyCopyArtifact(), "The writer must never create a safety-copy artifact.");
        }
        finally
        {
            await targetDb.DisposeAsync();
        }
    }
}
