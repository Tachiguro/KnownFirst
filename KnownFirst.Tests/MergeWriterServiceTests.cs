using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
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

    /// <summary>Cancels at a named, semantically-stable writer checkpoint rather than a raw mutation
    /// count — used to prove rollback exactly at the scheduler-replay boundary.</summary>
    private sealed class CancelAtCheckpointInjector(CancellationTokenSource source, string checkpointName) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName2)
        {
            if (string.Equals(checkpointName2, checkpointName, StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }

    /// <summary>Throws the first time the named checkpoint fires — mirrors
    /// <c>Schema8BackupRestoreTests.ThrowAtCheckpointInjector</c>.</summary>
    private sealed class ThrowAtCheckpointInjector(string checkpointName) : IBackupImportFailureInjector
    {
        private bool _fired;

        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName2)
        {
            if (!_fired && string.Equals(checkpointName2, checkpointName, StringComparison.Ordinal))
            {
                _fired = true;
                throw new InvalidOperationException($"Injected failure at checkpoint '{checkpointName2}'.");
            }
        }
    }

    // Fixed anchor for every scheduler-replay test below — never DateTime.UtcNow, so expected schedules
    // are computed the same way on every run.
    private static readonly DateTime SchedulerTestCardCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string DuringSchedulerReplayCheckpoint = "MergeWriter.DuringSchedulerReplay";

    /// <summary>Builds a single-word, single-Sense, single-card fixture ("bank"/"finance-1"/MeaningToTerm)
    /// with the card left at the real creation-time defaults (<see cref="CardSchedule.New"/>) and no
    /// reviews. Building this same helper twice yields byte-identical content on both sides, so the two
    /// databases' Word/Sense/Meaning/Card all match by stable merge identity.</summary>
    private static async Task<Schema7Fixture> BuildBankCardFixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("bank", status: WordStatus.Learning);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "finance-1", definition: "financial institution");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm,
            state: CardState.New, dueAtUtc: SchedulerTestCardCreatedAtUtc, intervalDays: 0,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor, successfulReviewCount: 0, lapseCount: 0,
            lastReviewedAtUtc: null, lastRating: null,
            createdAtUtc: SchedulerTestCardCreatedAtUtc, updatedAtUtc: SchedulerTestCardCreatedAtUtc);
        return fixture;
    }

    /// <summary>Adds one review (plus its owning, otherwise-empty completed LearningSession) to the
    /// fixture's single card, with every scheduling snapshot field supplied explicitly (never a wall-clock
    /// default) so fingerprint/content comparisons across two independently-built fixtures stay exact.</summary>
    private static async Task AddReviewAsync(
        Schema7Fixture fixture, ReviewRating rating, DateTime reviewedAtUtc, DateTime dueAtUtc, int intervalDays, double easeFactor)
    {
        var cardId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards LIMIT 1");
        var sessionId = await fixture.InsertLearningSessionAsync(
            startedAtUtc: reviewedAtUtc, updatedAtUtc: reviewedAtUtc, completedAtUtc: reviewedAtUtc);
        await fixture.InsertReviewAsync(
            cardId, sessionId: sessionId, rating: rating, wasTypedAnswer: true, wasCorrect: rating != ReviewRating.Again,
            reviewedAtUtc: reviewedAtUtc, dueAtUtc: dueAtUtc, intervalDays: intervalDays, easeFactor: easeFactor);
    }

    private static async Task<Schema8CardRow> SingleCardAsync(Schema7Fixture fixture) =>
        (await fixture.Connection.QueryAsync<Schema8CardRow>("SELECT * FROM LearningCards")).Single();

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

    // ==== Scheduler-replay correction (KF-MEANING-001 Slice 8) ====

    // ---- An existing target card plus an archive-only review event inserts the event and recomputes
    // every derived scheduling field; the result matches the ordinary scheduler transition; ownership
    // fields (PreferredMeaningId/SenseId/Direction) stay untouched. ----
    [TestMethod]
    public async Task EnrichedCard_ArchiveOnlyReviewEvent_InsertsEventAndRecomputesDerivedFields()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);
        var cardBefore = await SingleCardAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        await AddReviewAsync(sourceFixture, ReviewRating.Good, reviewedAt, dueAtUtc: reviewedAt.AddDays(3), intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.RequiresSchedulerReplay);

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status);

        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningReviews"));

        var cardAfter = await SingleCardAsync(targetFixture);
        var expected = new SimpleSpacedRepetitionScheduler().Schedule(
            CardSchedule.New(SchedulerTestCardCreatedAtUtc), ReviewRating.Good, reviewedAt);

        Assert.AreEqual(expected.State, cardAfter.State);
        Assert.AreEqual(expected.DueAtUtc, cardAfter.DueAtUtc);
        Assert.AreEqual(expected.IntervalDays, cardAfter.IntervalDays);
        Assert.AreEqual(expected.EaseFactor, cardAfter.EaseFactor);
        Assert.AreEqual(expected.SuccessfulReviewCount, cardAfter.SuccessfulReviewCount);
        Assert.AreEqual(expected.LapseCount, cardAfter.LapseCount);
        Assert.AreEqual(expected.LastReviewedAtUtc, cardAfter.LastReviewedAtUtc);
        Assert.AreEqual(expected.LastRating, cardAfter.LastRating);

        // Ownership fields are never touched by replay.
        Assert.AreEqual(cardBefore.PreferredMeaningId, cardAfter.PreferredMeaningId);
        Assert.AreEqual(cardBefore.SenseId, cardAfter.SenseId);
        Assert.AreEqual(cardBefore.Direction, cardAfter.Direction);
    }

    // ---- An exact duplicate review event causes no duplicate row and no card-state change ----
    [TestMethod]
    public async Task ExactDuplicateReviewEvent_NoDuplicateRow_NoCardStateChange()
    {
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        var dueAt = reviewedAt.AddDays(3);

        await using var targetFixture = await BuildBankCardFixtureAsync();
        await AddReviewAsync(targetFixture, ReviewRating.Good, reviewedAt, dueAtUtc: dueAt, intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);
        var cardBefore = await SingleCardAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        await AddReviewAsync(sourceFixture, ReviewRating.Good, reviewedAt, dueAtUtc: dueAt, intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.IsFalse(plan.RequiresSchedulerReplay);

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status);

        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningReviews"));

        var cardAfter = await SingleCardAsync(targetFixture);
        Assert.AreEqual(cardBefore.State, cardAfter.State);
        Assert.AreEqual(cardBefore.DueAtUtc, cardAfter.DueAtUtc);
        Assert.AreEqual(cardBefore.IntervalDays, cardAfter.IntervalDays);
        Assert.AreEqual(cardBefore.EaseFactor, cardAfter.EaseFactor);
        Assert.AreEqual(cardBefore.SuccessfulReviewCount, cardAfter.SuccessfulReviewCount);
        Assert.AreEqual(cardBefore.LapseCount, cardAfter.LapseCount);
        Assert.AreEqual(cardBefore.LastReviewedAtUtc, cardAfter.LastReviewedAtUtc);
        Assert.AreEqual(cardBefore.LastRating, cardAfter.LastRating);
    }

    // ---- Reapplying the same source is a complete no-op for review rows and card scheduling fields ----
    [TestMethod]
    public async Task ReapplyingSameSource_CardSchedulingFieldsRemainUnchanged()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        await AddReviewAsync(sourceFixture, ReviewRating.Good, reviewedAt, dueAtUtc: reviewedAt.AddDays(3), intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));

        var plan1 = await ComputePlanAsync(targetFixture, archive);
        var result1 = await writer.ApplyAsync(archive, plan1, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result1.Status);
        var cardAfterFirst = await SingleCardAsync(targetFixture);

        var plan2 = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.NoChanges, plan2.Status);
        var result2 = await writer.ApplyAsync(archive, plan2, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result2.Status);

        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningReviews"));
        var cardAfterSecond = await SingleCardAsync(targetFixture);

        Assert.AreEqual(cardAfterFirst.State, cardAfterSecond.State);
        Assert.AreEqual(cardAfterFirst.DueAtUtc, cardAfterSecond.DueAtUtc);
        Assert.AreEqual(cardAfterFirst.IntervalDays, cardAfterSecond.IntervalDays);
        Assert.AreEqual(cardAfterFirst.EaseFactor, cardAfterSecond.EaseFactor);
        Assert.AreEqual(cardAfterFirst.SuccessfulReviewCount, cardAfterSecond.SuccessfulReviewCount);
        Assert.AreEqual(cardAfterFirst.LapseCount, cardAfterSecond.LapseCount);
        Assert.AreEqual(cardAfterFirst.LastReviewedAtUtc, cardAfterSecond.LastReviewedAtUtc);
        Assert.AreEqual(cardAfterFirst.LastRating, cardAfterSecond.LastRating);
    }

    // ---- Cancellation during replay rolls back both inserted reviews and card updates ----
    [TestMethod]
    public async Task Cancellation_DuringSchedulerReplay_RollsBackReviewsAndCardUpdate()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        await AddReviewAsync(sourceFixture, ReviewRating.Good, reviewedAt, dueAtUtc: reviewedAt.AddDays(3), intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.IsTrue(plan.RequiresSchedulerReplay);

        var beforeState = await targetFixture.CapturePersistentStateAsync();

        using var cts = new CancellationTokenSource();
        var injector = new CancelAtCheckpointInjector(cts, DuringSchedulerReplayCheckpoint);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), injector);
        var result = await writer.ApplyAsync(archive, plan, cts.Token);

        Assert.AreEqual(MergeWriteStatus.Cancelled, result.Status);
        var afterState = await targetFixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(beforeState, afterState);
    }

    // ---- An injected mid-operation failure rolls back both review history and card state ----
    [TestMethod]
    public async Task InjectedFailure_DuringSchedulerReplay_RollsBackReviewsAndCardUpdate()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        await AddReviewAsync(sourceFixture, ReviewRating.Good, reviewedAt, dueAtUtc: reviewedAt.AddDays(3), intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.IsTrue(plan.RequiresSchedulerReplay);

        var beforeState = await targetFixture.CapturePersistentStateAsync();

        var injector = new ThrowAtCheckpointInjector(DuringSchedulerReplayCheckpoint);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), injector);
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Failed, result.Status);
        var afterState = await targetFixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(beforeState, afterState);
    }

    // ---- A plan with RequiresSchedulerReplay = true cannot report success with stale derived fields ----
    [TestMethod]
    public async Task PlanRequiringSchedulerReplay_NeverReportsSuccessWithStaleDerivedFields()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);
        var cardBefore = await SingleCardAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        await AddReviewAsync(sourceFixture, ReviewRating.Good, reviewedAt, dueAtUtc: reviewedAt.AddDays(3), intervalDays: 3, easeFactor: 2.5);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.IsTrue(plan.RequiresSchedulerReplay);

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status);

        var cardAfter = await SingleCardAsync(targetFixture);
        Assert.AreNotEqual(cardBefore.State, cardAfter.State);
        Assert.AreNotEqual(cardBefore.DueAtUtc, cardAfter.DueAtUtc);
        Assert.IsNotNull(cardAfter.LastReviewedAtUtc);
    }

    // ==== Distinct LearningSession identity (KF-MEANING-001 Slice 9) ====

    // ---- Regression: a target session and an archive session sharing the same card set but a different
    // StartedAtUtc used to collapse onto one merge identity, so the writer either lost the archive
    // session's own queue item or hit the (SessionId, QueueOrder) uniqueness constraint. Both real
    // sessions, their queue items, and their reviews must now be preserved, and reimporting the same
    // archive again must be a stable no-op. ----
    [TestMethod]
    public async Task DistinctLearningSessions_SameCardDifferentStartedAt_BothPreservedAndReimportConverges()
    {
        var firstStart = SchedulerTestCardCreatedAtUtc;
        var secondStart = SchedulerTestCardCreatedAtUtc.AddDays(1);

        await using var targetFixture = await BuildBankCardFixtureAsync();
        var targetCardId = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards LIMIT 1");
        var targetSessionId = await targetFixture.InsertLearningSessionAsync(
            startedAtUtc: firstStart, updatedAtUtc: firstStart, completedAtUtc: firstStart);
        await targetFixture.InsertQueueItemAsync(
            targetSessionId, targetCardId, queueOrder: 1, isCompleted: true, rating: ReviewRating.Good, completedAtUtc: firstStart);
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        var sourceCardId = await sourceFixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards LIMIT 1");
        var sourceSessionId = await sourceFixture.InsertLearningSessionAsync(
            startedAtUtc: secondStart, updatedAtUtc: secondStart, completedAtUtc: secondStart);
        await sourceFixture.InsertQueueItemAsync(
            sourceSessionId, sourceCardId, queueOrder: 1, isCompleted: true, rating: ReviewRating.Good, completedAtUtc: secondStart);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        var sessionAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.LearningWorkflow);
        Assert.AreEqual(
            MergeEntityClassification.New, sessionAction.Classification,
            "A distinct real session sharing the target's card set but a different StartedAtUtc must not collapse onto the existing session's identity.");

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, result.Status);
        Assert.AreEqual(2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningCards"), "The shared card must still be reused, not duplicated.");
        await AssertReferentialIntegrityAsync(targetFixture);

        // Reimport convergence: the target index must resolve the archive session by exactly the identity
        // preflight already computed for it, so recomputing the plan against the now-merged target reports
        // NoChanges — never a fresh insert or a false duplicate — and reapplying is a stable no-op.
        var reimportPlan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.NoChanges, reimportPlan.Status);

        var reimportResult = await writer.ApplyAsync(archive, reimportPlan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, reimportResult.Status);
        Assert.AreEqual(2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningSessions"), "Reimporting the same archive must not create a duplicate session.");
        Assert.AreEqual(2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningSessionCards"), "Reimporting the same archive must not create a duplicate queue item.");
    }

    // ==== Package A: completed-ReviewSession convergence (planner and target index must agree) ====

    private static readonly DateTime ReviewHistoryStartedAtUtc = new(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReviewHistoryCompletedAtUtc = new(2026, 2, 1, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>Adds one document plus one completed review session over the fixture's existing "bank" word.
    /// Every timestamp is supplied explicitly (never a wall clock), so two independently built fixtures
    /// produce byte-identical review history.</summary>
    private static async Task<int> AddCompletedBankReviewHistoryAsync(
        Schema7Fixture fixture,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        WordStatus candidateStatus = WordStatus.Known,
        int decisionSequence = 1,
        string content = "The bank is open.",
        ReviewSessionOutcomeCounters? counters = null)
    {
        var documentId = await fixture.InsertDocumentAsync(
            title: "Review history document", content: content, importedAt: ReviewHistoryStartedAtUtc);
        var wordId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");
        var effectiveCounters = counters ?? new ReviewSessionOutcomeCounters(
            1, 1,
            candidateStatus == WordStatus.Known ? 1 : 0,
            candidateStatus == WordStatus.UnknownBacklog ? 1 : 0,
            0);

        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            documentId, (int)ReviewSessionStatus.Completed,
            effectiveCounters.TotalCandidates, effectiveCounters.ReviewedCount, effectiveCounters.KnownCount,
            effectiveCounters.UnknownCount, effectiveCounters.IgnoredCount,
            decisionSequence, startedAtUtc, completedAtUtc);
        var sessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewCandidates
                (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount,
                 PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
            VALUES (?, ?, 0, ?, ?, 0, 0, ?, ?, 0, ?)
            """,
            sessionId, wordId, (int)candidateStatus, (int)WordStatus.Unreviewed,
            startedAtUtc, decisionSequence, completedAtUtc);

        return sessionId;
    }

    // ---- Stage 1 characterization: an exact-duplicate completed review workflow must resolve to the
    // existing target row while unrelated new content is still applied. This test is the contract that
    // keeps the planner's ReviewSession identity and MergeWriterTargetIndex's key in the same family. ----
    [TestMethod]
    public async Task Schema9_MixedPlan_ExactDuplicateReviewWorkflowAndUnrelatedNewEntity_ResolvesExistingSessionAndCompletes()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(targetFixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(sourceFixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        var sourceBankWordId = await sourceFixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");
        // Unrelated new content attached to entities that already exist in the target.
        await sourceFixture.InsertMeaningAsync(
            sourceBankWordId, displayTerm: "bank", translation: "Sparkasse", selectedMeaningId: "finance-1", definition: "savings institution");
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var workflowAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow);
        Assert.AreEqual(MergeEntityClassification.ExactDuplicateSkipped, workflowAction.Classification);
        var itemAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewItem);
        Assert.AreEqual(MergeEntityClassification.ExactDuplicateSkipped, itemAction.Classification);

        var meaningsBefore = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Meanings");

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, result.Status, $"Merge failed with error code '{result.ErrorCode}'.");
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM ReviewSessions"), "The duplicate review session must resolve to the existing row.");
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM ReviewCandidates"), "The duplicate review candidate must not be re-inserted.");
        Assert.AreEqual(meaningsBefore + 1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Meanings"), "Unrelated new content must still be applied.");
        await AssertReferentialIntegrityAsync(targetFixture);

        var reimportPlan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.NoChanges, reimportPlan.Status);
        var reimportResult = await writer.ApplyAsync(archive, reimportPlan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, reimportResult.Status);
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM ReviewSessions"));
        Assert.AreEqual(1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM ReviewCandidates"));
    }

    /// <summary>Builds a migrated Schema-8 fixture carrying exactly one completed review session over one
    /// document, and returns both the raw snapshot and the archive DTO projection of it.</summary>
    private static async Task<(Schema8BackupSnapshot Snapshot, BackupPayloadV2 Payload)> CaptureReviewHistoryStateAsync(
        Schema7Fixture fixture)
    {
        var snapshot = await CaptureSnapshotAsync(fixture);
        return (snapshot, BackupModelMapperV2.MapToExternal(snapshot));
    }

    private static ReviewSessionIdentity ArchiveDtoReviewSessionIdentity(BackupPayloadV2 payload)
    {
        var documentIdentities = MergePreflightPlanner.BuildIdentityMap(
            payload.SourceMaterials, d => d.Id, SourceMaterialIdentityPolicy.Compute, "source material");
        var vocabularyIdentities = MergePreflightPlanner.BuildIdentityMap(
            payload.Vocabulary, v => v.Id, VocabularyMergeIdentityPolicy.Compute, "vocabulary");
        var result = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            payload.Workflows.VocabularyReviews.Single(), documentIdentities, vocabularyIdentities);
        Assert.IsFalse(result.HasDuplicateCandidateVocabularyIdentity);
        return result.Identity;
    }

    private static ReviewSessionEntity CopySession(ReviewSessionEntity source, int id) => new()
    {
        Id = id,
        DocumentId = source.DocumentId,
        Status = source.Status,
        TotalCandidates = source.TotalCandidates,
        ReviewedCount = source.ReviewedCount,
        KnownCount = source.KnownCount,
        UnknownCount = source.UnknownCount,
        IgnoredCount = source.IgnoredCount,
        DecisionSequence = source.DecisionSequence,
        StartedAt = source.StartedAt,
        CompletedAt = source.CompletedAt
    };

    private static ReviewCandidateEntity CopyCandidate(ReviewCandidateEntity source, int id, int sessionId, int order) => new()
    {
        Id = id,
        SessionId = sessionId,
        WordId = source.WordId,
        Order = order,
        Status = source.Status,
        PreviousWordStatus = source.PreviousWordStatus,
        PreviousTotalOccurrenceCount = source.PreviousTotalOccurrenceCount,
        PreviousDocumentCount = source.PreviousDocumentCount,
        PreviousUpdatedAt = source.PreviousUpdatedAt,
        DecisionSequence = source.DecisionSequence,
        WasWordCreatedForSession = source.WasWordCreatedForSession,
        DecidedAt = source.DecidedAt
    };

    // ---- Package C parity: BackupModelMapperV2's canonical ReviewSessions ordering and
    // MergeWriterTargetIndex must resolve the same Schema-9 completed-review identity for the same snapshot,
    // so no second definition of completed-review identity can drift into existence. Both reach it through
    // the shared, caller-neutral Schema9ReviewSessionRowIdentities plumbing; this test pins the observable
    // consequence — the order in which the mapper emits two completed histories is exactly the ordinal order
    // of the identities the writer's target index holds for them. ----
    [TestMethod]
    public async Task Schema9_MapperReviewSessionOrderingIdentity_EqualsWriterTargetIndexIdentity_ForTheSameSnapshot()
    {
        await using var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(
            fixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc,
            candidateStatus: WordStatus.Known, decisionSequence: 2);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        await PromoteToSchema9Async(fixture);

        // A second completed history over the SAME document that ties on every session-level field the
        // mapper orders by and differs only through its candidate decision — the exact shape whose ordering
        // used to fall through to the local row id.
        var documentId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Documents LIMIT 1");
        var bankWordId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, ?, 1, 1, 1, 0, 0, 2, ?, ?)
            """,
            documentId, (int)ReviewSessionStatus.Completed, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        var secondSessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewCandidates
                (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount,
                 PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
            VALUES (?, ?, 0, ?, ?, 0, 0, ?, 2, 0, ?)
            """,
            secondSessionId, bankWordId, (int)WordStatus.Ignored, (int)WordStatus.Unreviewed,
            ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);

        var snapshot = await CaptureSnapshotAsync(fixture);
        Assert.HasCount(2, snapshot.ReviewSessions);

        var index = MergeWriterTargetIndex.Build(snapshot);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);
        Assert.HasCount(2, payload.Workflows.VocabularyReviews);

        // Identity of every emitted workflow, in emitted order, computed from the archive DTO side.
        var documentIdentities = MergePreflightPlanner.BuildIdentityMap(
            payload.SourceMaterials, d => d.Id, SourceMaterialIdentityPolicy.Compute, "source material");
        var vocabularyIdentities = MergePreflightPlanner.BuildIdentityMap(
            payload.Vocabulary, v => v.Id, VocabularyMergeIdentityPolicy.Compute, "vocabulary");
        var emittedIdentities = payload.Workflows.VocabularyReviews
            .Select(workflow =>
            {
                var result = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
                    workflow, documentIdentities, vocabularyIdentities);
                Assert.IsFalse(result.HasDuplicateCandidateVocabularyIdentity);
                return result.Identity.Value;
            })
            .ToList();

        CollectionAssert.AreEquivalent(
            index.ReviewSessionIdByIdentity.Keys.Select(identity => identity.Value).ToList(),
            emittedIdentities,
            "The mapper and the writer target index must resolve the same set of Schema-9 completed-review "
            + "identities for one snapshot.");

        CollectionAssert.AreEqual(
            emittedIdentities.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            emittedIdentities,
            "The mapper must emit two session-level-tied completed histories in the ordinal order of the same "
            + "Schema-9 identity the writer target index keys them by — not in local row-id order.");
    }

    // ---- Stage 2 intended behaviour: planner and writer target index must share one identity version ----
    [TestMethod]
    public async Task Schema9_PlannerActionIdentity_AndWriterTargetIndexKey_UseTheSameIdentityVersion()
    {
        await using var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(fixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);

        var (snapshot, payload) = await CaptureReviewHistoryStateAsync(fixture);
        var expectedIdentity = ArchiveDtoReviewSessionIdentity(payload);

        var plan = MergePreflightPlannerV2.CreatePlan(payload, payload, DummyManifest());
        var action = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow);
        Assert.AreEqual(
            expectedIdentity.Value, action.StableIdentity,
            "The planner must record the v2 full-history review-session identity.");

        var index = MergeWriterTargetIndex.Build(snapshot);
        Assert.IsTrue(
            index.ReviewSessionIdByIdentity.ContainsKey(new ReviewSessionIdentity(action.StableIdentity)),
            "The writer target index must be keyed by exactly the identity the planner recorded.");
    }

    [TestMethod]
    public async Task ReviewSessionIdentityV2_ArchiveDtoAndRawSqliteTimestamps_WithSameTicks_ProduceSameIdentity()
    {
        // A tick-precise, non-round completion instant: the archive DTO carries DateTimeKind.Utc while the
        // raw SQLite row is read back as Unspecified with the same ticks.
        var startedAtUtc = new DateTime(2026, 5, 7, 11, 13, 17, DateTimeKind.Utc).AddTicks(1234567);
        var completedAtUtc = startedAtUtc.AddMinutes(42).AddTicks(7654321);
        // Five distinct non-default counters: a transposed or missing counter on either path would break
        // the identity equality below, so this also proves counter parity across both call paths.
        var retainedCounters = new ReviewSessionOutcomeCounters(9, 8, 7, 6, 5);

        await using var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(fixture, startedAtUtc, completedAtUtc, counters: retainedCounters);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);

        var (snapshot, payload) = await CaptureReviewHistoryStateAsync(fixture);
        Assert.AreEqual(DateTimeKind.Utc, payload.Workflows.VocabularyReviews.Single().StartedAtUtc.Kind);
        Assert.AreEqual(startedAtUtc.Ticks, payload.Workflows.VocabularyReviews.Single().StartedAtUtc.Ticks);
        Assert.AreNotEqual(DateTimeKind.Utc, snapshot.ReviewSessions.Single().StartedAt.Kind);
        Assert.AreEqual(startedAtUtc.Ticks, snapshot.ReviewSessions.Single().StartedAt.Ticks);

        var archiveWorkflow = payload.Workflows.VocabularyReviews.Single();
        Assert.AreEqual(
            retainedCounters,
            new ReviewSessionOutcomeCounters(
                archiveWorkflow.TotalCandidates, archiveWorkflow.ReviewedCount, archiveWorkflow.KnownCount,
                archiveWorkflow.UnknownCount, archiveWorkflow.IgnoredCount),
            "Precondition: the archive DTO path must carry the retained outcome counters unchanged.");
        var rawSession = snapshot.ReviewSessions.Single();
        Assert.AreEqual(
            retainedCounters,
            new ReviewSessionOutcomeCounters(
                rawSession.TotalCandidates, rawSession.ReviewedCount, rawSession.KnownCount,
                rawSession.UnknownCount, rawSession.IgnoredCount),
            "Precondition: the raw SQLite path must carry the identical retained outcome counters.");

        var index = MergeWriterTargetIndex.Build(snapshot);

        Assert.AreEqual(
            ArchiveDtoReviewSessionIdentity(payload),
            index.ReviewSessionIdByIdentity.Keys.Single(),
            "Archive DTO and raw SQLite identity computation must be byte-identical.");
    }

    [TestMethod]
    public async Task Schema9_TargetIndex_TwoIdenticalCompletedSessionsForOneDocument_ThrowsBackupFormatDuplicateId()
    {
        await using var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(fixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);

        var snapshot = await CaptureSnapshotAsync(fixture);
        var session = snapshot.ReviewSessions.Single();
        var candidate = snapshot.ReviewCandidates.Single();
        var duplicateSession = CopySession(session, session.Id + 1000);
        var duplicated = snapshot with
        {
            ReviewSessions = [session, duplicateSession],
            ReviewCandidates = [candidate, CopyCandidate(candidate, candidate.Id + 1000, duplicateSession.Id, candidate.Order)]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(() => MergeWriterTargetIndex.Build(duplicated));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public async Task Schema9_TargetIndex_DuplicateCandidateVocabularyIdentity_ThrowsBackupFormatDuplicateId()
    {
        await using var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(fixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);

        var snapshot = await CaptureSnapshotAsync(fixture);
        var candidate = snapshot.ReviewCandidates.Single();
        var duplicated = snapshot with
        {
            ReviewCandidates = [candidate, CopyCandidate(candidate, candidate.Id + 1000, candidate.SessionId, candidate.Order + 1)]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(() => MergeWriterTargetIndex.Build(duplicated));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    // ==== Package B: writer evidence for divergent completed Schema-9 review histories ====
    //
    // Package A proved the planner classification, the v2 full-history identity, and target-index parity,
    // but every writer-level Schema-9 test above still runs against a Schema-8-shaped database:
    // Schema8BackupFixtureBuilders.MigrateAsync stops at Schema 8 and therefore retains the legacy unique
    // ReviewSessions(DocumentId) index, under which a second completed session for one document cannot
    // physically exist. The helper below produces a genuinely Schema-9-shaped target — the same raw-DDL
    // route Schema9RawFixture already establishes — so the writer's New-classification insert path can be
    // exercised for the first time.

    private static readonly DateTime DivergentReviewCompletedAtUtc = new(2026, 2, 1, 10, 15, 0, DateTimeKind.Utc);

    /// <summary>Migrates an already-Schema-8 fixture to a real Schema-9 shape (non-unique DocumentId index
    /// plus the partial unique Active-session index) and sets <c>PRAGMA user_version = 9</c>, then proves
    /// the result actually resolves as Schema 9 through the production capability resolver.</summary>
    private static async Task PromoteToSchema9Async(Schema7Fixture fixture)
    {
        await Schema9RawFixture.ReplaceLegacyIndexAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");

        Assert.AreEqual(9, await fixture.ReadUserVersionAsync());
        BackupSchemaCapabilityResult? capability = null;
        await fixture.Connection.RunInTransactionAsync(connection => capability = BackupSchemaCapability.Resolve(connection));
        Assert.IsInstanceOfType<Schema9CapabilityResult>(
            capability,
            "The Package B target must be a genuinely Schema-9-shaped database, not a Schema-8 fixture.");
    }

    /// <summary>Target: one "bank" card fixture plus one completed review history over the shared document,
    /// promoted to a real Schema-9 shape.</summary>
    private static async Task<Schema7Fixture> BuildSchema9ReviewHistoryTargetAsync()
    {
        var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(fixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        await PromoteToSchema9Async(fixture);
        return fixture;
    }

    /// <summary>Source: the identical "bank" card fixture and the identical document, but a completed review
    /// history that genuinely diverges — a different completion instant and different retained outcome
    /// counters, which is exactly what the v2 full-history identity distinguishes. Stays Schema-8 shaped:
    /// only one session exists on this side, and only the target must be Schema 9.</summary>
    private static async Task<Schema7Fixture> BuildDivergentReviewHistorySourceAsync()
    {
        var fixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(
            fixture, ReviewHistoryStartedAtUtc, DivergentReviewCompletedAtUtc,
            candidateStatus: WordStatus.UnknownBacklog, decisionSequence: 2);
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        return fixture;
    }

    private static async Task<List<ReviewSessionEntity>> ReadReviewSessionsAsync(Schema7Fixture fixture) =>
        await fixture.Connection.QueryAsync<ReviewSessionEntity>("SELECT * FROM ReviewSessions ORDER BY Id");

    private static async Task<List<ReviewCandidateEntity>> ReadReviewCandidatesAsync(Schema7Fixture fixture) =>
        await fixture.Connection.QueryAsync<ReviewCandidateEntity>("SELECT * FROM ReviewCandidates ORDER BY Id");

    private static (int TotalCandidates, int ReviewedCount, int KnownCount, int UnknownCount, int IgnoredCount,
        int DecisionSequence, DateTime StartedAt, DateTime? CompletedAt, int DocumentId, ReviewSessionStatus Status)
        SessionFields(ReviewSessionEntity session) =>
        (session.TotalCandidates, session.ReviewedCount, session.KnownCount, session.UnknownCount,
            session.IgnoredCount, session.DecisionSequence, session.StartedAt, session.CompletedAt,
            session.DocumentId, session.Status);

    // ---- 1. A divergent completed history for an already-known document is inserted alongside the
    // existing completed session, which itself is left untouched. ----
    [TestMethod]
    public async Task Schema9_DivergentCompletedReviewHistory_ForKnownDocument_IsInsertedAlongsideExistingSession()
    {
        await using var targetFixture = await BuildSchema9ReviewHistoryTargetAsync();
        await using var sourceFixture = await BuildDivergentReviewHistorySourceAsync();

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status, $"Unexpected plan error code '{plan.ErrorCode}'.");
        var workflowAction = plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow);
        Assert.AreEqual(
            MergeEntityClassification.New, workflowAction.Classification,
            "A completed review history that diverges on completion instant and retained counters must not collapse "
            + "onto the target's existing session identity.");

        var documentsBefore = await CountAsync(targetFixture, "SELECT COUNT(*) FROM Documents");
        var existingSessionBefore = (await ReadReviewSessionsAsync(targetFixture)).Single();

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, result.Status, $"Merge failed with error code '{result.ErrorCode}'.");
        Assert.AreEqual(
            documentsBefore, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Documents"),
            "The divergent history belongs to the already-known document; no second document may be created.");

        var sessionsAfter = await ReadReviewSessionsAsync(targetFixture);
        Assert.HasCount(2, sessionsAfter, "Both completed review histories must coexist for the one document.");
        Assert.AreEqual(
            1, sessionsAfter.Select(session => session.DocumentId).Distinct().Count(),
            "Both sessions must be attached to the same target document row.");
        Assert.AreEqual(existingSessionBefore.DocumentId, sessionsAfter[0].DocumentId);
        Assert.IsTrue(
            sessionsAfter.All(session => session.Status == ReviewSessionStatus.Completed),
            "Neither coexisting session may be Active.");

        var existingSessionAfter = sessionsAfter.Single(session => session.Id == existingSessionBefore.Id);
        Assert.AreEqual(
            SessionFields(existingSessionBefore), SessionFields(existingSessionAfter),
            "The pre-existing completed history must not be overwritten, merged into, or mutated.");

        var insertedSession = sessionsAfter.Single(session => session.Id != existingSessionBefore.Id);
        Assert.AreEqual(DivergentReviewCompletedAtUtc.Ticks, insertedSession.CompletedAt!.Value.Ticks);
        Assert.AreEqual(2, insertedSession.DecisionSequence);

        // The merged target is still a structurally valid Schema-9 database: two Completed sessions for one
        // document are legal, and the at-most-one-Active-session invariant still holds.
        Assert.AreEqual(9, await targetFixture.ReadUserVersionAsync());
        BackupSchemaCapabilityResult? capabilityAfter = null;
        await targetFixture.Connection.RunInTransactionAsync(
            connection => capabilityAfter = BackupSchemaCapability.Resolve(connection));
        Assert.IsInstanceOfType<Schema9CapabilityResult>(capabilityAfter);
        await AssertReferentialIntegrityAsync(targetFixture);
    }

    // ---- 2. Inserted candidates hang off the newly generated parent session; existing candidates keep
    // their existing parent; document and vocabulary references resolve to target rows. ----
    [TestMethod]
    public async Task Schema9_InsertedCompletedReviewHistory_CandidatesAttachToNewParentSessionOnly()
    {
        await using var targetFixture = await BuildSchema9ReviewHistoryTargetAsync();
        await using var sourceFixture = await BuildDivergentReviewHistorySourceAsync();

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var existingSessionId = (await ReadReviewSessionsAsync(targetFixture)).Single().Id;
        var existingCandidateBefore = (await ReadReviewCandidatesAsync(targetFixture)).Single();
        var targetDocumentId = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Documents");
        var targetBankWordId = await targetFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status, $"Merge failed with error code '{result.ErrorCode}'.");

        var sessionsAfter = await ReadReviewSessionsAsync(targetFixture);
        var insertedSessionId = sessionsAfter.Single(session => session.Id != existingSessionId).Id;

        var candidatesAfter = await ReadReviewCandidatesAsync(targetFixture);
        Assert.HasCount(2, candidatesAfter);

        var existingCandidateAfter = candidatesAfter.Single(candidate => candidate.Id == existingCandidateBefore.Id);
        Assert.AreEqual(
            existingSessionId, existingCandidateAfter.SessionId,
            "The pre-existing candidate must remain attached to the pre-existing session.");
        Assert.AreEqual(existingCandidateBefore.WordId, existingCandidateAfter.WordId);
        Assert.AreEqual(existingCandidateBefore.Status, existingCandidateAfter.Status);

        var insertedCandidate = candidatesAfter.Single(candidate => candidate.Id != existingCandidateBefore.Id);
        Assert.AreEqual(
            insertedSessionId, insertedCandidate.SessionId,
            "The inserted candidate must reference the newly generated target ReviewSession primary key.");
        Assert.AreEqual(
            WordStatus.UnknownBacklog, insertedCandidate.Status,
            "The inserted candidate must carry the archive's own decision, not the target's.");

        // Every relationship resolves through writer-side target mappings to real target rows: the archive's
        // document and vocabulary were remapped onto the rows the target already had, never re-created.
        Assert.IsTrue(
            sessionsAfter.All(session => session.DocumentId == targetDocumentId),
            "Both sessions must reference the existing target Document row.");
        Assert.IsTrue(
            candidatesAfter.All(candidate => candidate.WordId == targetBankWordId),
            "Both candidates must reference the existing target 'bank' Word row.");
        Assert.AreEqual(
            1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM Words WHERE CanonicalTerm = 'bank'"),
            "Vocabulary remapping must reuse the existing word, never duplicate it.");
        Assert.AreEqual(
            0,
            await CountAsync(
                targetFixture,
                "SELECT COUNT(*) FROM ReviewCandidates rc LEFT JOIN ReviewSessions rs ON rs.Id = rc.SessionId "
                + "LEFT JOIN Words w ON w.Id = rc.WordId WHERE rs.Id IS NULL OR w.Id IS NULL"),
            "Every candidate must resolve to a real parent session row and a real word row.");
        await AssertReferentialIntegrityAsync(targetFixture);
    }

    // ---- 3. One plan carrying both an exact-duplicate and a divergent completed history inserts only the
    // divergent one. ----
    [TestMethod]
    public async Task Schema9_DivergentAndExactDuplicateCompletedHistories_InOnePlan_InsertOnlyTheDivergentOne()
    {
        await using var targetFixture = await BuildSchema9ReviewHistoryTargetAsync();

        // The source repeats the target's history verbatim over one document and adds a genuinely different
        // completed history over a second, independent document.
        await using var sourceFixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(sourceFixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await AddCompletedBankReviewHistoryAsync(
            sourceFixture, ReviewHistoryStartedAtUtc, DivergentReviewCompletedAtUtc,
            candidateStatus: WordStatus.UnknownBacklog, decisionSequence: 2,
            content: "A second bank document.");
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status, $"Unexpected plan error code '{plan.ErrorCode}'.");

        var workflowActions = plan.Actions.Where(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow).ToList();
        Assert.HasCount(2, workflowActions);
        Assert.AreEqual(
            1, workflowActions.Count(a => a.Classification == MergeEntityClassification.ExactDuplicateSkipped));
        Assert.AreEqual(1, workflowActions.Count(a => a.Classification == MergeEntityClassification.New));

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, result.Status, $"Merge failed with error code '{result.ErrorCode}'.");
        Assert.AreEqual(
            2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM ReviewSessions"),
            "Exactly one session must be inserted: the duplicate resolves to the existing row.");
        Assert.AreEqual(
            2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM ReviewCandidates"),
            "The duplicate's candidate must not be re-inserted.");
        await AssertReferentialIntegrityAsync(targetFixture);
    }

    // ---- 4. Reimporting the merged completed history is a writer-boundary no-op. ----
    [TestMethod]
    public async Task Schema9_ReimportingMergedCompletedHistory_IsNoChangeAndIdempotentAtWriterBoundary()
    {
        await using var targetFixture = await BuildSchema9ReviewHistoryTargetAsync();
        await using var sourceFixture = await BuildDivergentReviewHistorySourceAsync();

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));

        Assert.AreEqual(MergeWriteStatus.Success, (await writer.ApplyAsync(archive, plan, CancellationToken.None)).Status);

        var sessionsAfterFirstMerge = await ReadReviewSessionsAsync(targetFixture);
        var candidatesAfterFirstMerge = await ReadReviewCandidatesAsync(targetFixture);
        Assert.HasCount(2, sessionsAfterFirstMerge);

        // The target index must now recognize the freshly written session by exactly the identity the
        // planner computes for the archive's own session — otherwise this would report New a second time.
        var reimportPlan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(
            MergePreflightStatus.NoChanges, reimportPlan.Status,
            "Reimporting an already-merged completed history must classify as an exact duplicate.");

        var reimportResult = await writer.ApplyAsync(archive, reimportPlan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Success, reimportResult.Status, $"Reimport failed with '{reimportResult.ErrorCode}'.");
        CollectionAssert.AreEqual(
            sessionsAfterFirstMerge.Select(SessionFields).ToList(),
            (await ReadReviewSessionsAsync(targetFixture)).Select(SessionFields).ToList(),
            "Reimport must leave every ReviewSession row unchanged.");
        Assert.AreEqual(
            candidatesAfterFirstMerge.Count, (await ReadReviewCandidatesAsync(targetFixture)).Count,
            "Reimport must not create a duplicate candidate.");
    }

    // ---- 5. An Active review session on the target blocks the writer entirely, with zero mutation. ----
    [TestMethod]
    public async Task Schema9_TargetWithActiveReviewSession_IsBlockedByActiveWorkflowWithoutMutation()
    {
        await using var targetFixture = await BuildSchema9ReviewHistoryTargetAsync();
        await using var sourceFixture = await BuildDivergentReviewHistorySourceAsync();

        var archive = await CapturePayloadAsync(sourceFixture);

        // The plan is computed while the target is still quiescent; the Active session appears only
        // afterwards, exactly like a user starting a review between preview and confirmation.
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var activeDocumentId = await targetFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM Documents");
        await targetFixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, ?, 1, 0, 0, 0, 0, 0, ?, NULL)
            """,
            activeDocumentId, (int)ReviewSessionStatus.Active, ReviewHistoryStartedAtUtc);

        var sessionsBefore = await ReadReviewSessionsAsync(targetFixture);
        var candidatesBefore = await ReadReviewCandidatesAsync(targetFixture);
        var activeSessionBefore = sessionsBefore.Single(session => session.Status == ReviewSessionStatus.Active);

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.BlockedByActiveWorkflow, result.Status);
        Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, result.ErrorCode);

        var sessionsAfter = await ReadReviewSessionsAsync(targetFixture);
        CollectionAssert.AreEqual(
            sessionsBefore.Select(SessionFields).ToList(), sessionsAfter.Select(SessionFields).ToList(),
            "A blocked merge must not mutate any ReviewSession row.");
        Assert.AreEqual(
            candidatesBefore.Count, (await ReadReviewCandidatesAsync(targetFixture)).Count,
            "A blocked merge must not insert any ReviewCandidate row.");
        Assert.AreEqual(
            SessionFields(activeSessionBefore),
            SessionFields(sessionsAfter.Single(session => session.Id == activeSessionBefore.Id)),
            "The existing active session must remain exactly as it was.");
    }

    // ---- 6. A failure raised after the completed-history insertion rolls the whole transaction back. ----
    [TestMethod]
    public async Task Schema9_InjectedFailure_DuringCompletedReviewHistoryInsertion_RollsBackEveryChange()
    {
        await using var targetFixture = await BuildSchema9ReviewHistoryTargetAsync();
        await using var sourceFixture = await BuildDivergentReviewHistorySourceAsync();

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);

        var sessionsBefore = await ReadReviewSessionsAsync(targetFixture);
        var candidatesBefore = await ReadReviewCandidatesAsync(targetFixture);

        // "MergeWriter.BeforeCompletion" fires after WriteVocabularyReviewWorkflows has already inserted the
        // new session and its candidate, so a throw here proves those specific rows are rolled back.
        var writer = new MergeWriterService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new ThrowAtCheckpointInjector("MergeWriter.BeforeCompletion"));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreEqual(MergeWriteStatus.Failed, result.Status);

        CollectionAssert.AreEqual(
            sessionsBefore.Select(SessionFields).ToList(),
            (await ReadReviewSessionsAsync(targetFixture)).Select(SessionFields).ToList(),
            "Rollback must restore the ReviewSessions table exactly.");
        CollectionAssert.AreEqual(
            candidatesBefore.Select(candidate => (candidate.Id, candidate.SessionId, candidate.WordId, candidate.Order)).ToList(),
            (await ReadReviewCandidatesAsync(targetFixture))
                .Select(candidate => (candidate.Id, candidate.SessionId, candidate.WordId, candidate.Order)).ToList(),
            "Rollback must restore the ReviewCandidates table exactly.");
    }

    // ---- 7. Characterization: the identical plan against a still-Schema-8 target fails closed. The legacy
    // unique ReviewSessions(DocumentId) index physically rejects the second completed session, the writer's
    // single transaction rolls back, and the target is left byte-identical. This asserts only the existing
    // public contract (MergeWriteResult.ErrorCode is non-null whenever Status is not Success); it introduces
    // no new error code and no new behavior. ----
    [TestMethod]
    public async Task Schema8Target_DivergentCompletedReviewHistoryPlan_FailsClosedWithoutMutation()
    {
        await using var targetFixture = await BuildBankCardFixtureAsync();
        await AddCompletedBankReviewHistoryAsync(targetFixture, ReviewHistoryStartedAtUtc, ReviewHistoryCompletedAtUtc);
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);
        Assert.AreEqual(8, await targetFixture.ReadUserVersionAsync(), "This target must deliberately remain Schema 8.");

        await using var sourceFixture = await BuildDivergentReviewHistorySourceAsync();
        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);

        // The V2 planner classifies by content identity and is schema-shape agnostic, so it still reports New.
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.AreEqual(
            MergeEntityClassification.New,
            plan.Actions.Single(a => a.EntityKind == MergeEntityKind.VocabularyReviewWorkflow).Classification);

        var sessionsBefore = await ReadReviewSessionsAsync(targetFixture);
        var candidatesBefore = await ReadReviewCandidatesAsync(targetFixture);

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);

        Assert.AreNotEqual(
            MergeWriteStatus.Success, result.Status,
            "A Schema-8 target cannot physically hold two completed sessions for one document.");
        Assert.IsNotNull(result.ErrorCode, "A non-success merge result must always carry an error code.");
        CollectionAssert.AreEqual(
            sessionsBefore.Select(SessionFields).ToList(),
            (await ReadReviewSessionsAsync(targetFixture)).Select(SessionFields).ToList(),
            "The Schema-8 target must be left byte-identical after the refused merge.");
        Assert.AreEqual(candidatesBefore.Count, (await ReadReviewCandidatesAsync(targetFixture)).Count);
    }

    // ==== Schema-9 LearningReview merge integrity ====

    /// <summary>Adds one completed LearningSession holding <paramref name="ratings"/> reviews for the
    /// fixture's single card, all at the same instant with the same scheduling snapshot, so the rows differ
    /// only in Rating. Built identically on both sides, the session itself matches by merge identity.</summary>
    private static async Task<int> AddSameInstantReviewSessionAsync(
        Schema7Fixture fixture, DateTime reviewedAtUtc, DateTime dueAtUtc, params ReviewRating[] ratings)
    {
        var cardId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards LIMIT 1");
        var sessionId = await fixture.InsertLearningSessionAsync(
            startedAtUtc: reviewedAtUtc, updatedAtUtc: reviewedAtUtc, completedAtUtc: reviewedAtUtc);

        foreach (var rating in ratings)
        {
            await fixture.InsertReviewAsync(
                cardId, sessionId: sessionId, rating: rating, wasTypedAnswer: true, wasCorrect: true,
                reviewedAtUtc: reviewedAtUtc, dueAtUtc: dueAtUtc, intervalDays: 3, easeFactor: 2.5);
        }

        return sessionId;
    }

    [TestMethod]
    public async Task SameCardSameInstantReviews_OneNewOneDuplicate_WriterAppliesEachPlannedActionExactlyOnce()
    {
        // The archive holds two review rows for one card at one instant: one the target already has, one it
        // does not. The planner classifies them separately, but the writer resolved both rows through the
        // same CardId@ReviewedAtUtc action key, so whichever action was recorded last decided both rows —
        // either dropping the genuinely new event or re-inserting the duplicate.
        var reviewedAt = SchedulerTestCardCreatedAtUtc.AddDays(1);
        var dueAt = reviewedAt.AddDays(3);

        await using var targetFixture = await BuildBankCardFixtureAsync();
        var targetSessionId = await AddSameInstantReviewSessionAsync(targetFixture, reviewedAt, dueAt, ReviewRating.Good);
        await Schema8BackupFixtureBuilders.MigrateAsync(targetFixture);

        await using var sourceFixture = await BuildBankCardFixtureAsync();
        await AddSameInstantReviewSessionAsync(sourceFixture, reviewedAt, dueAt, ReviewRating.Good, ReviewRating.Hard);
        await Schema8BackupFixtureBuilders.MigrateAsync(sourceFixture);

        var archive = await CapturePayloadAsync(sourceFixture);
        var plan = await ComputePlanAsync(targetFixture, archive);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].NewCount,
            "Exactly the Hard event is new to the target.");
        Assert.AreEqual(
            1, plan.PerEntity[MergeEntityKind.LearningReview].DeduplicatedEventCount,
            "Exactly the Good event is the already-present duplicate.");

        var writer = new MergeWriterService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture));
        var result = await writer.ApplyAsync(archive, plan, CancellationToken.None);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status);

        Assert.AreEqual(
            2, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningReviews"),
            "The writer must apply each planned action exactly once: the new event inserted, the duplicate "
            + "skipped — never both rows resolved through one shared action key.");
        Assert.AreEqual(
            1, await CountAsync(targetFixture, $"SELECT COUNT(*) FROM LearningReviews WHERE Rating = {(int)ReviewRating.Good}"),
            "The already-present Good event must not be inserted a second time.");
        Assert.AreEqual(
            1, await CountAsync(targetFixture, $"SELECT COUNT(*) FROM LearningReviews WHERE Rating = {(int)ReviewRating.Hard}"),
            "The genuinely new Hard event must be inserted.");

        // Session attachment survives the action-key correction: the archive session matched the target's,
        // so both surviving rows must still hang off that one real session row.
        Assert.AreEqual(
            1, await CountAsync(targetFixture, "SELECT COUNT(*) FROM LearningSessions"),
            "The archive's identical completed session must match the target's rather than duplicate it.");
        Assert.AreEqual(
            2, await CountAsync(targetFixture, $"SELECT COUNT(*) FROM LearningReviews WHERE SessionId = {targetSessionId}"),
            "Every retained review must stay attached to the LearningSession its source row referenced.");
        Assert.AreEqual(
            0,
            await CountAsync(
                targetFixture,
                "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningSessions s ON s.Id = r.SessionId WHERE s.Id IS NULL"),
            "No review may be left with a dangling session reference.");

        await AssertReferentialIntegrityAsync(targetFixture);
    }
}
