using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13MergeWriterTests
{
    private static readonly DateTime BaseTime = new(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task PopulatedSchema13V3Import_ExecutablePlan_AppliesControlsHistoryAndExactState()
    {
        var targetPayloadSeed = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var targetPayload = targetPayloadSeed with
        {
            SenseLearningControls = [new BackupSenseLearningControl("sense_1", BaseTime.AddDays(2))]
        };
        var sourcePayload = Combine(
            CreatePayload(
                BaseTime,
                new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
                new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1))),
            CreatePayloadFor(
                "2", "protocol", BaseTime.AddHours(1),
                new HistoryFact("event-new-card", 1, BackupReviewRating.Easy, BaseTime.AddHours(2))));
        await using var target = await CreateSchema13TargetAsync(targetPayload);

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)),
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary?.Disposition);
        Assert.AreEqual(3, await target.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WordLearningControls"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseLearningControls"));
        Assert.AreEqual(
            "2026-08-30T10:00:00.0000000Z",
            await target.Connection.ExecuteScalarAsync<string>(
                "SELECT DecidedAtUtc FROM WordLearningControls"));

        var actual = (await target.Connection.QueryAsync<StateRow>(
            "SELECT s.Stability, s.Difficulty, s.LastReviewedAtUtc, s.StepIndex, s.DueAtUtc FROM FsrsCardStates s JOIN LearningCards c ON c.Id = s.CardId JOIN Senses se ON se.Id = c.SenseId WHERE se.StableId = 'st_sense_1'")).Single();
        var expected = sourcePayload.FsrsCardStates.Single(item => item.CardId == "card_1");
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(expected.Stability!.Value),
            BitConverter.DoubleToInt64Bits(actual.Stability!.Value));
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(expected.Difficulty!.Value),
            BitConverter.DoubleToInt64Bits(actual.Difficulty!.Value));
        Assert.AreEqual(expected.StepIndex, actual.StepIndex);
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_ControlChangesAfterPreflight_RejectsStalePlanBeforeMergeMutation()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target);
        var realPreflight = new MergePreflightService(database);
        var staleTimestamp = BaseTime.AddDays(3);
        var mutatingPreflight = new MutatingPreflightService(
            realPreflight,
            async () => await target.Connection.ExecuteAsync(
                "UPDATE WordLearningControls SET DecidedAtUtc = ?",
                staleTimestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'")));
        var service = new BackupService(
            database,
            new TestPlatformInfo(),
            mergePreflightService: mutatingPreflight,
            mergeSafetyCopyService: new SuccessfulSafetyCopyService());

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)),
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(MergeWriterErrorCodes.StalePlan, result.ErrorCode);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        Assert.AreEqual(
            staleTimestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
            await target.Connection.ExecuteScalarAsync<string>(
                "SELECT DecidedAtUtc FROM WordLearningControls"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_FailureDuringHistoryTail_RollsBackEveryMergeMutation()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            failureInjector: new CheckpointFailureInjector("Schema13MergeWriter.DuringFsrsHistory"),
            mergeSafetyCopyService: new SuccessfulSafetyCopyService());

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)),
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        Assert.AreEqual(
            BaseTime.AddDays(2).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
            await target.Connection.ExecuteScalarAsync<string>(
                "SELECT DecidedAtUtc FROM WordLearningControls"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_IdenticalArchive_IsNoChangeWithoutSafetyCopyOrMutation()
    {
        var payload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        await using var target = await CreateSchema13TargetAsync(payload);
        var before = await target.CapturePersistentStateAsync();

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(payload)), CancellationToken.None);

        CollectionAssert.AreEqual(before, await target.CapturePersistentStateAsync());
        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, result.Summary?.Disposition);
        Assert.IsFalse(result.Summary?.SafetyCopyCreated ?? true);
        Assert.IsFalse(Directory.Exists(Path.Combine(target.RootDirectory, "merge-safety-copies")));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_MutatingMerge_CreatesValidatedV3SafetyCopyOfPreMergeTarget()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.IsTrue(result.Summary?.SafetyCopyCreated);
        var archivePath = Directory.GetFiles(
            Path.Combine(target.RootDirectory, "merge-safety-copies"), "*.kfarchive").Single();
        await using var stream = File.OpenRead(archivePath);
        var validated = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        Assert.IsNotNull(validated.V3);
        Assert.AreEqual(1, validated.V3.Payload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual(BaseTime.AddDays(2), validated.V3.Payload.WordLearningControls.Single().DecidedAtUtc);
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_SafetyCopyFailure_PreventsWriterAndTargetMutation()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var before = await target.CapturePersistentStateAsync();
        var writer = new RecordingWriterService();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            mergeSafetyCopyService: new FailedSafetyCopyService(),
            mergeWriterService: writer);

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(0, writer.Schema13Calls);
        CollectionAssert.AreEqual(before, await target.CapturePersistentStateAsync());
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_HistoryChangesAfterPreflight_RejectsStalePlan()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target);
        var service = new BackupService(
            database,
            new TestPlatformInfo(),
            mergePreflightService: new MutatingPreflightService(
                new MergePreflightService(database),
                () => target.Connection.ExecuteAsync(
                    "UPDATE FsrsReviewHistoryEntries SET StableId = 'fixture-stale-event' WHERE SequenceNumber = 1")),
            mergeSafetyCopyService: new SuccessfulSafetyCopyService());

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(MergeWriterErrorCodes.StalePlan, result.ErrorCode);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        Assert.AreEqual("fixture-stale-event", await target.Connection.ExecuteScalarAsync<string>("SELECT StableId FROM FsrsReviewHistoryEntries"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_ExactStateChangesAfterPreflight_RejectsStalePlan()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target);
        const string staleDueAt = "2026-09-30T10:00:00.0000000Z";
        var service = new BackupService(
            database,
            new TestPlatformInfo(),
            mergePreflightService: new MutatingPreflightService(
                new MergePreflightService(database),
                () => target.Connection.ExecuteAsync("UPDATE FsrsCardStates SET DueAtUtc = ?", staleDueAt)),
            mergeSafetyCopyService: new SuccessfulSafetyCopyService());

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(MergeWriterErrorCodes.StalePlan, result.ErrorCode);
        Assert.AreEqual(staleDueAt, await target.Connection.ExecuteScalarAsync<string>("SELECT DueAtUtc FROM FsrsCardStates"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_FailureAfterBaseGraph_RollsBackNewSemanticRows()
    {
        var targetPayload = CreatePayload(BaseTime);
        var sourcePayload = CreatePayloadFor("2", "protocol", BaseTime);
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            failureInjector: new CheckpointFailureInjector("Schema13MergeWriter.AfterBaseGraph"),
            mergeSafetyCopyService: new SuccessfulSafetyCopyService());

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_FailureBeforeFinalValidation_RollsBackThenRetryConverges()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("event-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("event-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var sourceBytes = await WriteV3Async(sourcePayload);
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target);
        var failing = new BackupService(
            database,
            new TestPlatformInfo(),
            failureInjector: new CheckpointFailureInjector("Schema13MergeWriter.BeforeFinalValidation"),
            mergeSafetyCopyService: new SuccessfulSafetyCopyService());

        var failed = await failing.ImportPortableArchiveAsync(new MemoryStream(sourceBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, failed.Status);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));

        var retry = await new BackupService(database, new TestPlatformInfo(), mergeSafetyCopyService: new SuccessfulSafetyCopyService())
            .ImportPortableArchiveAsync(new MemoryStream(sourceBytes), CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, retry.Status, retry.ErrorCode);
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        Assert.AreEqual(0, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_EqualTimestampTail_PreservesStableIdsAndSequenceOrder()
    {
        var targetPayload = CreatePayload(
            BaseTime,
            new HistoryFact("equal-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("equal-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("equal-2", 2, BackupReviewRating.Hard, BaseTime),
            new HistoryFact("equal-3", 3, BackupReviewRating.Easy, BaseTime));
        await using var target = await CreateSchema13TargetAsync(targetPayload);

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        var rows = await target.Connection.QueryAsync<HistoryRow>(
            "SELECT StableId, SequenceNumber, ReviewedAtUtc FROM FsrsReviewHistoryEntries ORDER BY SequenceNumber");
        CollectionAssert.AreEqual(new[] { "equal-1", "equal-2", "equal-3" }, rows.Select(row => row.StableId).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, rows.Select(row => row.SequenceNumber).ToArray());
        Assert.IsTrue(rows.All(row => row.ReviewedAtUtc == "2026-08-30T10:00:00.0000000Z"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_DivergentHistory_IsRejectedBeforeSafetyCopy()
    {
        var targetPayload = CreatePayload(
            BaseTime,
            new HistoryFact("branch-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("target-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("branch-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("source-2", 2, BackupReviewRating.Easy, BaseTime.AddDays(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var safety = new RecordingSafetyCopyService();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            mergeSafetyCopyService: safety);

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(Schema13MergePreflightErrorCodes.CausalHistoryConflict, result.ErrorCode);
        Assert.AreEqual(0, safety.Calls);
        Assert.AreEqual("target-2", await target.Connection.ExecuteScalarAsync<string>(
            "SELECT StableId FROM FsrsReviewHistoryEntries WHERE SequenceNumber = 2"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_GlobalStableIdCollision_IsRejectedBeforeSafetyCopy()
    {
        var targetPayload = CreatePayload(
            BaseTime,
            new HistoryFact("global-event", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayloadFor(
            "2", "protocol", BaseTime,
            new HistoryFact("global-event", 1, BackupReviewRating.Easy, BaseTime));
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var safety = new RecordingSafetyCopyService();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            mergeSafetyCopyService: safety);

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(Schema13MergePreflightErrorCodes.StableIdCollision, result.ErrorCode);
        Assert.AreEqual(0, safety.Calls);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_TargetAheadAndTargetOnlyControls_ArePreservedWithoutSafetyCopy()
    {
        var targetPayload = CreatePayload(
            BaseTime,
            new HistoryFact("ahead-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("ahead-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var sourceSeed = CreatePayload(
            BaseTime.AddDays(1),
            new HistoryFact("ahead-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = sourceSeed with
        {
            WordLearningControls = [],
            SenseLearningControls = []
        };
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var safety = new RecordingSafetyCopyService();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            mergeSafetyCopyService: safety);

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, result.Summary?.Disposition);
        Assert.AreEqual(0, safety.Calls);
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WordLearningControls"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseLearningControls"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_NewSemanticCard_ResolvesInsertedLocalIdsForHistoryAndState()
    {
        var targetPayload = CreatePayload(BaseTime);
        var sourcePayload = CreatePayloadFor(
            "2", "protocol", BaseTime,
            new HistoryFact("new-card-event", 1, BackupReviewRating.Easy, BaseTime.AddHours(1)));
        await using var target = await CreateSchema13TargetAsync(targetPayload);

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        var row = (await target.Connection.QueryAsync<ResolvedHistoryRow>(
            """
            SELECT h.StableId, h.CardId, c.SenseId
            FROM FsrsReviewHistoryEntries h
            JOIN LearningCards c ON c.Id = h.CardId
            WHERE h.StableId = 'new-card-event'
            """)).Single();
        Assert.IsTrue(row.CardId > 0);
        Assert.IsTrue(row.SenseId > 0);
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsCardStates"));
        Assert.AreEqual(0, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_NewSiblingSense_KeepsControlsAndCardsSeparate()
    {
        var targetPayload = CreatePayload(BaseTime);
        var sourceSeed = CreatePayloadFor("2", "network", BaseTime.AddHours(1));
        var sourcePayload = sourceSeed with
        {
            Senses = [sourceSeed.Senses.Single() with { TopicOrDomain = "network-protocol" }]
        };
        await using var target = await CreateSchema13TargetAsync(targetPayload);

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseLearningControls"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsCardStates"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V1Import_UsesLegacyBootstrapWithoutInventingSenseControlOrHistory()
    {
        await using var legacySource = await Schema7Fixture.CreateAsync();
        await legacySource.InsertWordAsync(
            "legacy",
            status: WordStatus.Known,
            createdAt: BaseTime,
            updatedAt: BaseTime.AddHours(1));
        var archiveBytes = await WriteV1Async(legacySource);
        await using var target = await CreateSchema13TargetAsync(CreatePayload(BaseTime));

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WordLearningControls"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseLearningControls"));
        Assert.AreEqual(0, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V2Import_UsesBootstrapAndResolvesNewSenseCardWithoutInventedHistory()
    {
        var sourceV3 = CreatePayloadFor("2", "protocol", BaseTime);
        var archiveBytes = await WriteV2Async(ToV2(sourceV3));
        await using var target = await CreateSchema13TargetAsync(CreatePayload(BaseTime));

        var result = await CreateService(target).ImportPortableArchiveAsync(
            new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsCardStates"));
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseLearningControls"));
        Assert.AreEqual(0, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V2Import_InsufficientLegacyHistory_FailsBeforeSafetyCopy()
    {
        var sourceV3 = CreatePayloadFor("2", "protocol", BaseTime);
        var basePayload = ToV2(sourceV3);
        var progressedCard = basePayload.Learning.Cards.Single() with
        {
            State = BackupCardState.Review,
            IntervalDays = 4,
            SuccessfulReviewCount = 1,
            LastReviewedAtUtc = BaseTime.AddDays(-1),
            LastRating = BackupReviewRating.Good
        };
        var invalidForBootstrap = basePayload with
        {
            Learning = basePayload.Learning with { Cards = [progressedCard] }
        };
        var archiveBytes = await WriteV2Async(invalidForBootstrap);
        await using var target = await CreateSchema13TargetAsync(CreatePayload(BaseTime));
        var safety = new RecordingSafetyCopyService();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target),
            new TestPlatformInfo(),
            mergeSafetyCopyService: safety);

        var result = await service.ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(Schema13MergePreflightErrorCodes.LegacyHistoryInsufficient, result.ErrorCode);
        Assert.AreEqual(0, safety.Calls);
        Assert.AreEqual(1, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
    }

    [TestMethod]
    public async Task PopulatedSchema13V3Import_RepeatIsIdempotentAndExportConvergesWithoutChangingSourceBytes()
    {
        var targetPayload = CreatePayload(
            BaseTime.AddDays(2),
            new HistoryFact("repeat-1", 1, BackupReviewRating.Good, BaseTime));
        var sourcePayload = CreatePayload(
            BaseTime,
            new HistoryFact("repeat-1", 1, BackupReviewRating.Good, BaseTime),
            new HistoryFact("repeat-2", 2, BackupReviewRating.Hard, BaseTime.AddDays(1)));
        var sourceBytes = await WriteV3Async(sourcePayload);
        var immutableCopy = sourceBytes.ToArray();
        await using var target = await CreateSchema13TargetAsync(targetPayload);
        var service = CreateService(target);

        var first = await service.ImportPortableArchiveAsync(new MemoryStream(sourceBytes), CancellationToken.None);
        var second = await service.ImportPortableArchiveAsync(new MemoryStream(sourceBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, first.Status, first.ErrorCode);
        Assert.AreEqual(PortableImportStatus.Success, second.Status, second.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, second.Summary?.Disposition);
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        CollectionAssert.AreEqual(immutableCopy, sourceBytes);

        using var exported = new MemoryStream();
        await service.CreatePortableArchiveAsync(exported, CancellationToken.None);
        exported.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(exported, CancellationToken.None);
        Assert.IsNotNull(validated.V3);
        Assert.AreEqual(2, validated.V3.Payload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual(1, validated.V3.Payload.WordLearningControls.Count);
    }

    [TestMethod]
    public async Task PopulatedSchema12V2Import_ContinuesUsingEstablishedMergeWriter()
    {
        var targetPayload = ToV2(CreatePayload(BaseTime));
        var sourcePayload = ToV2(CreatePayloadFor("2", "protocol", BaseTime));
        await using var target = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(target.Connection);
        var service = CreateService(target);
        var seed = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV2Async(targetPayload)), CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, seed.Status, seed.ErrorCode);

        var result = await service.ImportPortableArchiveAsync(
            new MemoryStream(await WriteV2Async(sourcePayload)), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary?.Disposition);
        Assert.AreEqual(12, await target.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(2, await target.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(0, await target.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'FsrsCardStates'"));
    }

    private static BackupService CreateService(Schema7Fixture target) =>
        new(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(target), new TestPlatformInfo());

    private static async Task<Schema7Fixture> CreateSchema13TargetAsync(BackupPayloadV3 payload)
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        var result = await CreateService(fixture).ImportPortableArchiveAsync(
            new MemoryStream(await WriteV3Async(payload)),
            CancellationToken.None);
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

    private static async Task<byte[]> WriteV2Async(BackupPayloadV2 payload)
    {
        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(
            payload,
            new TestPlatformInfo(),
            new ValidatedSchema12Capability(),
            BaseTime,
            stream,
            CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> WriteV1Async(Schema7Fixture fixture)
    {
        BackupSnapshot? snapshot = null;
        BackupSchemaCapabilityResult? capability = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            snapshot = BackupSnapshotRepository.CaptureSnapshot(connection);
            capability = BackupSchemaCapability.Resolve(connection);
        });
        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(
            BackupModelMapper.MapToExternal(snapshot!),
            new TestPlatformInfo(),
            ((Schema7CapabilityResult)capability!).Capability,
            BaseTime,
            stream,
            CancellationToken.None);
        return stream.ToArray();
    }

    private static BackupPayloadV2 ToV2(BackupPayloadV3 payload) => new(
        payload.SourceMaterials,
        payload.Vocabulary,
        payload.Senses,
        payload.PreparedLearning,
        payload.AnswerVariants,
        payload.SenseAnswerVariantAssignments,
        payload.AnswerVariantProgress,
        payload.Learning,
        payload.Workflows,
        payload.DerivedTermEvidence,
        payload.Extensions);

    private static BackupPayloadV3 Combine(BackupPayloadV3 first, BackupPayloadV3 second) => new(
        first.SourceMaterials.Concat(second.SourceMaterials).ToList(),
        first.Vocabulary.Concat(second.Vocabulary).ToList(),
        first.Senses.Concat(second.Senses).ToList(),
        first.PreparedLearning.Concat(second.PreparedLearning).ToList(),
        first.AnswerVariants.Concat(second.AnswerVariants).ToList(),
        first.SenseAnswerVariantAssignments.Concat(second.SenseAnswerVariantAssignments).ToList(),
        first.AnswerVariantProgress.Concat(second.AnswerVariantProgress).ToList(),
        new BackupLearningDataV2(
            first.Learning.Cards.Concat(second.Learning.Cards).ToList(),
            first.Learning.ReviewEvents.Concat(second.Learning.ReviewEvents).ToList()),
        new BackupWorkflowDataV2(
            first.Workflows.VocabularyReviews.Concat(second.Workflows.VocabularyReviews).ToList(),
            first.Workflows.PreparationBatches.Concat(second.Workflows.PreparationBatches).ToList(),
            first.Workflows.LearningSessions.Concat(second.Workflows.LearningSessions).ToList()),
        first.DerivedTermEvidence.Concat(second.DerivedTermEvidence).ToList(),
        first.WordLearningControls.Concat(second.WordLearningControls).ToList(),
        first.SenseLearningControls.Concat(second.SenseLearningControls).ToList(),
        first.FsrsReviewHistoryEntries.Concat(second.FsrsReviewHistoryEntries).ToList(),
        first.FsrsCardStates.Concat(second.FsrsCardStates).ToList(),
        new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));

    private static BackupPayloadV3 CreatePayload(DateTime wordControlTimestamp, params HistoryFact[] history) =>
        CreatePayloadFor("1", "network", wordControlTimestamp, history);

    private static BackupPayloadV3 CreatePayloadFor(
        string suffix,
        string text,
        DateTime wordControlTimestamp,
        params HistoryFact[] history)
    {
        var docId = "doc_" + suffix;
        var sentenceId = "sent_" + suffix;
        var vocabularyId = "vocab_" + suffix;
        var senseId = "sense_" + suffix;
        var preparedId = "prep_" + suffix;
        var answerId = "ans_" + suffix;
        var assignmentId = "asgn_" + suffix;
        var cardId = "card_" + suffix;
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
            [new BackupSourceMaterial(
                docId, "Document " + suffix, "en", "de", BackupLexicalLookupMode.Definition, null,
                text, textSha, BaseTime, 1,
                [new BackupSentenceRange(sentenceId, 0, 0, text.Length)],
                [new BackupOccurrence(vocabularyId, sentenceId, 0, text.Length, text, 0, BackupTechnicalTokenFamily.None, null, null, null)])],
            [new BackupVocabularyItem(
                vocabularyId, "en", text, "en:" + text, BackupTokenKind.Word,
                BackupKnowledgeState.Unreviewed, BackupPreparationState.Prepared, 1, 1,
                BaseTime, BaseTime, [new BackupEncounteredForm(text, 1)],
                new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false), [])],
            [new BackupSense(
                senseId, "st_sense_" + suffix, vocabularyId, "en", "de", "", "", "", "", "",
                preparedId, BackupSenseStatus.Learning, BaseTime, BaseTime)],
            [new BackupPreparedItemV2(
                preparedId, senseId, "st_prep_" + suffix, vocabularyId, "en", "de", text, text, null,
                BackupTokenKind.Word, null, null, "Netzwerk", null, null, null, null, [], true,
                new BackupSourceReference("manual", "", "", null, ""), BaseTime, BaseTime, BaseTime,
                [new BackupContextSnapshotV2(docId, "Document " + suffix, text, 0, text.Length, "fp_" + suffix, BaseTime, senseId)])],
            [new BackupAnswerVariant(
                answerId, "st_ans_" + suffix, senseId, "de", "Netzwerk", "netzwerk", preparedId, BaseTime, BaseTime)],
            [new BackupSenseAnswerVariantAssignment(
                assignmentId, "st_asgn_" + suffix, senseId, BackupCardDirection.MeaningToTerm, answerId,
                BackupAnswerVariantRequirement.Required, true, BaseTime, BaseTime, BaseTime)],
            [],
            new BackupLearningDataV2(
                [new BackupLearningCardV2(
                    cardId, vocabularyId, senseId, preparedId, BackupCardDirection.MeaningToTerm,
                    BackupCardState.New, BaseTime, 0, 2.5, 0, 0, null, null, BaseTime, BaseTime)],
                []),
            new BackupWorkflowDataV2([], [], []),
            [],
            [new BackupWordLearningControl(vocabularyId, wordControlTimestamp)],
            [new BackupSenseLearningControl(senseId, BaseTime)],
            history.Select(item => new BackupFsrsReviewHistoryEntry(
                item.StableId, cardId, item.SequenceNumber, item.Rating, item.ReviewedAtUtc)).ToList(),
            [new BackupFsrsCardState(
                cardId, (BackupFsrsCardStateKind)state.State, state.Stability, state.Difficulty,
                state.LastReviewedAtUtc?.UtcDateTime, state.StepIndex, state.DueAtUtc?.UtcDateTime)],
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));
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

    private sealed class StateRow
    {
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
    }

    private sealed class HistoryRow
    {
        public string StableId { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }

    private sealed class ResolvedHistoryRow
    {
        public string StableId { get; set; } = string.Empty;
        public int CardId { get; set; }
        public int SenseId { get; set; }
    }

    private sealed class TestPlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-slice5-test";
    }

    private sealed class SuccessfulSafetyCopyService : IMergeSafetyCopyService
    {
        public Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
            string? sourceDescription,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MergeSafetyCopyResult(
                MergeSafetyCopyStatus.Success,
                "synthetic-v3-safety-copy.kfarchive",
                "synthetic-v3-safety-copy.kfarchive.metadata.json",
                BaseTime,
                1,
                null,
                null,
                null));
    }

    private sealed class FailedSafetyCopyService : IMergeSafetyCopyService
    {
        public Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
            string? sourceDescription,
            CancellationToken cancellationToken) =>
            Task.FromResult(MergeSafetyCopyResult.Failed);
    }

    private sealed class RecordingSafetyCopyService : IMergeSafetyCopyService
    {
        public int Calls { get; private set; }

        public Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
            string? sourceDescription,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new MergeSafetyCopyResult(
                MergeSafetyCopyStatus.Success,
                "recorded-v3-safety-copy.kfarchive",
                "recorded-v3-safety-copy.kfarchive.metadata.json",
                BaseTime,
                1,
                null,
                null,
                null));
        }
    }

    private sealed class RecordingWriterService : IMergeWriterService
    {
        public int Schema13Calls { get; private set; }

        public Task<MergeWriteResult> ApplyAsync(
            BackupPayloadV2 archive,
            MergePreflightPlan plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(MergeWriteResult.SuccessResult);

        public Task<MergeWriteResult> ApplySchema13Async(
            BackupPayloadV3 archive,
            MergePreflightPlan plan,
            CancellationToken cancellationToken)
        {
            Schema13Calls++;
            return Task.FromResult(MergeWriteResult.SuccessResult);
        }
    }

    private sealed class MutatingPreflightService(
        IMergePreflightService inner,
        Func<Task> mutateAfterPreflight) : IMergePreflightService
    {
        public async Task<MergePreflightPlan> CreatePreflightPlanAsync(
            Stream archiveStream,
            CancellationToken cancellationToken)
        {
            var plan = await inner.CreatePreflightPlanAsync(archiveStream, cancellationToken);
            await mutateAfterPreflight();
            return plan;
        }

        public async Task<MergePreflightPlan> CreatePreflightPlanAsync(
            ValidatedBackupArchiveEnvelope validated,
            CancellationToken cancellationToken)
        {
            var plan = await inner.CreatePreflightPlanAsync(validated, cancellationToken);
            await mutateAfterPreflight();
            return plan;
        }
    }

    private sealed class CheckpointFailureInjector(string checkpoint) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName)
        {
            if (string.Equals(checkpointName, checkpoint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Injected Schema-13 merge failure.");
            }
        }
    }
}
