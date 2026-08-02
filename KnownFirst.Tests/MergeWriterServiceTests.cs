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
}
