using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 8 Import integration: <see cref="BackupService.ImportPortableArchiveAsync"/> now
/// routes an empty/Schema-7 target through the unchanged restore-into-empty path and a populated, active
/// Schema-8 target through preflight → safety copy → the transactional merge writer. Every database is a
/// synthetic, isolated temporary SQLite fixture; no real user database is ever opened. Fakes are used only
/// for call-order and failure-routing tests — every other test exercises the real components end to end.
/// </summary>
[TestClass]
public sealed class BackupServiceImportRoutingTests
{
    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

    /// <summary>Isolated per-instance Schema-8 target — never shares a directory with another test's
    /// safety-copy folder, so <see cref="MergeSafetyCopyService"/>'s prune-older-copies behavior can never
    /// race with or delete another concurrently-running test's files.</summary>
    private sealed class IsolatedSchema8Database : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _testRoot;
        private SQLiteAsyncConnection? _connection;
        private bool _migrated;

        public IsolatedSchema8Database()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "kf-import-routing-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            DatabasePath = Path.Combine(_testRoot, "knownfirst.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            if (_migrated)
            {
                _connection ??= new SQLiteAsyncConnection(DatabasePath);
                return;
            }

            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            await Schema7Fixture.InitializeEmptyAsync(_connection);
            await Schema8DormantMigration.ApplyAsync(_connection);
            _migrated = true;
        }

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
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

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
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

        public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
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

            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }

            _gate.Dispose();
        }
    }

    /// <summary>Read-only stream — <c>CanSeek</c> is always false — used to prove Import routing never
    /// requires the caller's source stream to be seekable.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingPreflightService(MergePreflightPlan plan, List<string> callLog) : IMergePreflightService
    {
        public Task<MergePreflightPlan> CreatePreflightPlanAsync(Stream archiveStream, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Import routing only ever uses the validated-envelope overload.");

        public Task<MergePreflightPlan> CreatePreflightPlanAsync(ValidatedBackupArchiveEnvelope validated, CancellationToken cancellationToken)
        {
            callLog.Add("preflight");
            return Task.FromResult(plan);
        }
    }

    private sealed class RecordingSafetyCopyService(MergeSafetyCopyResult result, List<string> callLog) : IMergeSafetyCopyService
    {
        public Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(string? sourceDescription, CancellationToken cancellationToken)
        {
            callLog.Add("safety-copy");
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingWriterService(MergeWriteResult result, List<string> callLog) : IMergeWriterService
    {
        public Task<MergeWriteResult> ApplyAsync(BackupPayloadV2 archive, MergePreflightPlan plan, CancellationToken cancellationToken)
        {
            callLog.Add("writer");
            return Task.FromResult(result);
        }
    }

    private static MergeManifestInfo DummyManifest() =>
        new(BackupFormatLimits.CurrentArchiveFormatVersion, "1.0.0-test", 8, DateTime.UtcNow, BackupSourcePlatform.Windows);

    private static MergePreflightPlan BuildPlan(
        MergePreflightStatus status, bool isExecutable, bool requiresSchedulerReplay = false, bool hasInsertable = false, string? errorCode = null)
    {
        var perEntity = Enum.GetValues<MergeEntityKind>().ToDictionary(kind => kind, _ => MergeEntityPlanCounts.Zero);
        if (hasInsertable)
        {
            perEntity[MergeEntityKind.Vocabulary] = MergeEntityPlanCounts.Zero.Increment(MergeEntityClassification.New);
        }

        return new MergePreflightPlan(
            Status: status,
            IsExecutable: isExecutable,
            Manifest: DummyManifest(),
            ChecksumVerified: true,
            PerEntity: perEntity,
            Actions: [],
            DerivedAnswerVariantPlans: [],
            KnowledgeStateConflictDecisions: [],
            WorkflowStatusConflictDecisions: [],
            SemanticMeaningGroupingDecisions: [],
            PreferredVariantSelectionDecisions: [],
            BlockingPrerequisites: [],
            SampleDetails: new Dictionary<MergeEntityClassification, IReadOnlyList<string>>(),
            WarningCodes: [],
            RequiresSchedulerReplay: requiresSchedulerReplay,
            ErrorCode: errorCode);
    }

    private static async Task SeedOneWordAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = "seed",
                NormalizedTerm = "seed",
                Status = WordStatus.Unreviewed,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Unprepared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        });
    }

    private static async Task<int> WordCountAsync(IKnownFirstDatabase database) =>
        await database.ExecuteSnapshotAsync(connection => connection.Table<WordEntity>().Count());

    private static async Task<byte[]> BuildMinimalValidV2ArchiveBytesAsync()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        Schema8BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await fixture.Connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema8CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapperV2.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> BuildV2ArchiveBytesAsync(Schema7Fixture migratedFixture)
    {
        Schema8BackupSnapshot? snapshot = null;
        await migratedFixture.Connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await migratedFixture.Connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema8CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapperV2.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> BuildV1ArchiveBytesAsync(Schema7Fixture fixture)
    {
        BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await fixture.Connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema7CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapper.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> BuildActiveSchema10ArchiveBytesAsync()
    {
        var archiveBytes = await Schema10ActiveArchiveTestData.BuildArchiveBytesAsync();
        var validated = await BackupArchiveReader.ValidateVersionedAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        Assert.IsNotNull(validated.V2);
        Assert.AreEqual(10, validated.V2.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(
            BackupLearningSessionStatus.Active,
            validated.V2.Payload.Workflows.LearningSessions.Single().Status);
        return archiveBytes;
    }

    // ---- 1. Empty Schema-8 target + archive v2 still uses restore-into-empty, no safety copy ----
    [TestMethod]
    public async Task EmptySchema8Target_ArchiveV2_UsesRestoreIntoEmpty_NoSafetyCopy()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await targetDb.InitializeAsync();
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsNotNull(result.Summary);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, result.Summary.Disposition);
        Assert.IsFalse(result.Summary.SafetyCopyCreated);

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.IsFalse(Directory.Exists(safetyCopyDirectory), "Restore-into-empty must never create a safety-copy directory.");
    }

    // ---- 2. Empty Schema-8 target + archive v1 still upgrades and restores ----
    [TestMethod]
    public async Task EmptySchema8Target_ArchiveV1_UpgradesAndRestores()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var wordId = await sourceFixture.InsertWordAsync("network");
        var meaningId = await sourceFixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        await sourceFixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        var archiveBytes = await BuildV1ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, result.Summary!.Disposition);
        Assert.AreEqual(1, await WordCountAsync(targetDb));
    }

    // ---- 3. Populated Schema-8 target + archive v2: real end-to-end merge ----
    [TestMethod]
    public async Task PopulatedSchema8Target_ArchiveV2_RoutesThroughPreflightSafetyCopyWriter_RealEndToEnd()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.IsNotNull(result.Summary);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary.Disposition);
        Assert.IsTrue(result.Summary.SafetyCopyCreated);
        Assert.IsGreaterThan(0, result.Summary.InsertedCount);
        // A deterministic UI-safe summary: never a raw exception, never a database id — only small
        // aggregate counts and a known disposition/error-code vocabulary.
        Assert.IsLessThan(10_000, result.Summary.InsertedCount);

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.HasCount(1, Directory.GetFiles(safetyCopyDirectory, "*.kfarchive"));

        // The seeded word survives, and the archive's own words were inserted alongside it.
        Assert.IsGreaterThan(1, await WordCountAsync(targetDb));
        Assert.AreEqual(1, await targetDb.ExecuteSnapshotAsync(connection =>
            connection.Table<WordEntity>().Count(w => w.CanonicalTerm == "seed")));
    }

    // ---- 4. Populated Schema-8 target + archive v1: real end-to-end upgrade-then-merge ----
    [TestMethod]
    public async Task PopulatedSchema8Target_ArchiveV1_UpgradesInMemoryAndMerges_RealEndToEnd()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var wordId = await sourceFixture.InsertWordAsync("network");
        var meaningId = await sourceFixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        await sourceFixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        var archiveBytes = await BuildV1ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary!.Disposition);
        Assert.IsTrue(result.Summary.SafetyCopyCreated);
        Assert.AreEqual(2, await WordCountAsync(targetDb));
    }

    // ---- 5 & 19. A fully duplicate archive (including reimporting after a successful merge) is a no-change ----
    [TestMethod]
    public async Task ReimportingSameArchiveAfterSuccessfulMerge_ReturnsNoChange()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var service = new BackupService(targetDb, new FakePlatformInfo());

        using (var firstStream = new MemoryStream(archiveBytes))
        {
            var firstResult = await service.ImportPortableArchiveAsync(firstStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, firstResult.Status);
            Assert.AreEqual(PortableImportDisposition.MergeApplied, firstResult.Summary!.Disposition);
        }

        var wordCountAfterFirst = await WordCountAsync(targetDb);
        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        var safetyCopiesAfterFirst = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length;

        using (var secondStream = new MemoryStream(archiveBytes))
        {
            var secondResult = await service.ImportPortableArchiveAsync(secondStream, CancellationToken.None);

            Assert.AreEqual(PortableImportStatus.Success, secondResult.Status);
            Assert.AreEqual(PortableImportDisposition.MergeNoChange, secondResult.Summary!.Disposition);
            Assert.IsFalse(secondResult.Summary.SafetyCopyCreated);
        }

        Assert.AreEqual(wordCountAfterFirst, await WordCountAsync(targetDb));
        Assert.AreEqual(safetyCopiesAfterFirst, Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length);
    }

    // ---- 7. Invalid archive fails before preflight, safety copy, and writer ----
    [TestMethod]
    public async Task InvalidArchive_FailsBeforePreflightSafetyCopyAndWriter()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var corruptStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not a zip archive"));
        var result = await service.ImportPortableArchiveAsync(corruptStream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.IsNotNull(result.ErrorCode);
        Assert.HasCount(0, callLog);
    }

    // ---- 8. Non-executable/blocked preflight fails before safety copy and writer ----
    [TestMethod]
    public async Task NonExecutablePreflightPlan_FailsBeforeSafetyCopyAndWriter()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.RequiresUserDecision, isExecutable: false), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(MergeWriterErrorCodes.PlanNotExecutable, result.ErrorCode);
        CollectionAssert.AreEqual(new[] { "preflight" }, callLog);
    }

    // ---- 6. A plan requiring no mutation returns no-change without a safety copy or the writer ----
    [TestMethod]
    public async Task NoMutationPlan_ReturnsNoChange_WithoutSafetyCopyOrWriter()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.NoChanges, isExecutable: true, hasInsertable: false), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, result.Summary!.Disposition);
        Assert.IsFalse(result.Summary.SafetyCopyCreated);
        CollectionAssert.AreEqual(new[] { "preflight" }, callLog);
    }

    // ---- 10. Safety-copy active-workflow result prevents writer invocation ----
    [TestMethod]
    public async Task SafetyCopyBlockedByActiveWorkflow_PreventsWriterInvocation()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();
        var wordCountBefore = await WordCountAsync(targetDb);

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.BlockedByActiveWorkflow, null, null, null, null, null, null, BackupErrorCodes.ActiveWorkflowUnsupported),
                callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, result.ErrorCode);
        CollectionAssert.AreEqual(new[] { "preflight", "safety-copy" }, callLog);
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));
    }

    // ---- 9. Safety-copy failure prevents writer invocation and leaves the target unchanged ----
    [TestMethod]
    public async Task SafetyCopyFailed_PreventsWriterInvocation_TargetUnchanged()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();
        var wordCountBefore = await WordCountAsync(targetDb);

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.SafetyCopyFailed, null, null, null, null, null, null, BackupErrorCodes.SafetyBackupFailed),
                callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
        CollectionAssert.AreEqual(new[] { "preflight", "safety-copy" }, callLog);
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));
    }

    // ---- 11. Cancellation during safety-copy creation prevents writer invocation, target unchanged ----
    [TestMethod]
    public async Task SafetyCopyCancelled_PreventsWriterInvocation_TargetUnchanged()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();
        var wordCountBefore = await WordCountAsync(targetDb);

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Cancelled, null, null, null, null, null, null, BackupErrorCodes.OperationCancelled),
                callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Cancelled, result.Status);
        Assert.AreEqual(BackupErrorCodes.OperationCancelled, result.ErrorCode);
        CollectionAssert.AreEqual(new[] { "preflight", "safety-copy" }, callLog);
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));
    }

    // ---- 12. Writer stale-plan refusal is surfaced with its stable code and no partial mutation ----
    [TestMethod]
    public async Task WriterStalePlanRefusal_SurfacedWithStableCode_NoPartialMutation()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();
        var wordCountBefore = await WordCountAsync(targetDb);

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
            mergeWriterService: new RecordingWriterService(
                new MergeWriteResult(MergeWriteStatus.StalePlan, MergeWriterErrorCodes.StalePlan), callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(MergeWriterErrorCodes.StalePlan, result.ErrorCode);
        CollectionAssert.AreEqual(new[] { "preflight", "safety-copy", "writer" }, callLog);
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));
    }

    // ---- 13. Writer cancellation/failure is mapped correctly and remains atomic ----
    [TestMethod]
    public async Task WriterCancellationAndFailure_MappedCorrectly()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        async Task<PortableImportResult> RunWithWriterResultAsync(MergeWriteResult writerResult)
        {
            var callLog = new List<string>();
            var service = new BackupService(
                targetDb,
                new FakePlatformInfo(),
                mergePreflightService: new RecordingPreflightService(
                    BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
                mergeSafetyCopyService: new RecordingSafetyCopyService(
                    new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
                mergeWriterService: new RecordingWriterService(writerResult, callLog));

            using var stream = new MemoryStream(archiveBytes);
            return await service.ImportPortableArchiveAsync(stream, CancellationToken.None);
        }

        var cancelledResult = await RunWithWriterResultAsync(new MergeWriteResult(MergeWriteStatus.Cancelled, BackupErrorCodes.OperationCancelled));
        Assert.AreEqual(PortableImportStatus.Cancelled, cancelledResult.Status);
        Assert.AreEqual(BackupErrorCodes.OperationCancelled, cancelledResult.ErrorCode);

        var failedResult = await RunWithWriterResultAsync(new MergeWriteResult(MergeWriteStatus.Failed, MergeWriterErrorCodes.UnexpectedFailure));
        Assert.AreEqual(PortableImportStatus.Failed, failedResult.Status);
        Assert.AreEqual(MergeWriterErrorCodes.UnexpectedFailure, failedResult.ErrorCode);
    }

    // ---- 14. A successfully created safety copy remains present when the writer later refuses ----
    [TestMethod]
    public async Task SuccessfulSafetyCopy_RetainedWhenWriterLaterFails()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var callLog = new List<string>();
        // The safety-copy service is REAL here — a genuine file is written to disk — only the writer is faked.
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeWriterService: new RecordingWriterService(
                new MergeWriteResult(MergeWriteStatus.StalePlan, MergeWriterErrorCodes.StalePlan), callLog));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(MergeWriterErrorCodes.StalePlan, result.ErrorCode);

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.HasCount(1, Directory.GetFiles(safetyCopyDirectory, "*.kfarchive"));
        Assert.HasCount(1, Directory.GetFiles(safetyCopyDirectory, "*.metadata.json"));
    }

    // ---- 15. Ordering is observably: validation -> preflight -> safety copy -> writer ----
    [TestMethod]
    public async Task Ordering_IsObservablyPreflightThenSafetyCopyThenWriter()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        // Validation itself is not logged, but it necessarily ran first: an invalid stream (see
        // InvalidArchive_FailsBeforePreflightSafetyCopyAndWriter) never reaches the preflight call at all.
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        CollectionAssert.AreEqual(new[] { "preflight", "safety-copy", "writer" }, callLog);
    }

    // ---- 16. The operation works with a non-seekable source stream ----
    [TestMethod]
    public async Task NonSeekableSourceStream_PopulatedTarget_MergeSucceeds()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new NonSeekableStream(archiveBytes);
        Assert.IsFalse(stream.CanSeek);

        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary!.Disposition);
    }

    // ---- 17. Archive v2 into Schema-7 remains rejected exactly as before ----
    [TestMethod]
    public async Task ArchiveV2IntoSchema7Target_RemainsRejected()
    {
        var targetDb = new TemporaryKnownFirstDatabase();
        await targetDb.InitializeAsync();
        try
        {
            var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();
            var service = new BackupService(targetDb, new FakePlatformInfo());
            using var stream = new MemoryStream(archiveBytes);
            var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

            Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.Schema8ArchiveIncompatibleWithSchema7Target, result.ErrorCode);
            Assert.IsNull(result.Summary);
        }
        finally
        {
            await targetDb.DisposeAsync();
        }
    }

    // ==== KF-MEANING-001 Slice 9: PreviewPortableImportAsync read-only preview ====

    // ---- Preview 1. Empty Schema-8 target + archive v2 previews RestoreIntoEmpty ----
    [TestMethod]
    public async Task Preview_EmptySchema8Target_ArchiveV2_PreviewsRestoreIntoEmpty()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await targetDb.InitializeAsync();
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.RestoreIntoEmpty, preview.Disposition);
        Assert.IsTrue(preview.CanConfirm);
        Assert.IsTrue(preview.WillMutate);
        Assert.IsFalse(preview.RequiresSafetyCopy);
        Assert.IsNotNull(preview.ArchiveSummary);
        Assert.IsNull(preview.ErrorCode);
        Assert.AreEqual(0, await WordCountAsync(targetDb));

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.IsFalse(Directory.Exists(safetyCopyDirectory), "Preview must never create a safety-copy directory.");
    }

    // ---- Preview 2. Empty Schema-8 target + archive v1 previews RestoreIntoEmpty ----
    [TestMethod]
    public async Task Preview_EmptySchema8Target_ArchiveV1_PreviewsRestoreIntoEmpty()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var wordId = await sourceFixture.InsertWordAsync("network");
        var meaningId = await sourceFixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        await sourceFixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        var archiveBytes = await BuildV1ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await targetDb.InitializeAsync();

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.RestoreIntoEmpty, preview.Disposition);
        Assert.IsNotNull(preview.ArchiveSummary);
        Assert.AreEqual(0, await WordCountAsync(targetDb));
    }

    // ---- Preview 3 & 6. Populated Schema-8 target + new archive data previews MergeChanges and reports a required safety copy ----
    [TestMethod]
    public async Task Preview_PopulatedSchema8Target_NewArchiveData_PreviewsMergeChanges_RequiresSafetyCopy()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var wordCountBefore = await WordCountAsync(targetDb);

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition);
        Assert.IsTrue(preview.CanConfirm);
        Assert.IsTrue(preview.WillMutate);
        Assert.IsTrue(preview.RequiresSafetyCopy);
        Assert.IsGreaterThan(0, preview.InsertedCount);
        Assert.IsNull(preview.ErrorCode);

        // Zero mutation: the preview alone never changes the target's word count.
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.IsFalse(Directory.Exists(safetyCopyDirectory), "Preview must never create a safety-copy directory.");
    }

    // ---- Preview 4 & 7. A fully duplicate archive against a populated target previews MergeNoChange without mutation or a safety copy ----
    [TestMethod]
    public async Task Preview_PopulatedSchema8Target_DuplicateArchive_PreviewsMergeNoChange_NoMutationNoSafetyCopy()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var service = new BackupService(targetDb, new FakePlatformInfo());

        using (var firstStream = new MemoryStream(archiveBytes))
        {
            var firstResult = await service.ImportPortableArchiveAsync(firstStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, firstResult.Status);
            Assert.AreEqual(PortableImportDisposition.MergeApplied, firstResult.Summary!.Disposition);
        }

        var wordCountAfterMerge = await WordCountAsync(targetDb);
        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        var safetyCopiesAfterMerge = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length;

        using (var secondStream = new MemoryStream(archiveBytes))
        {
            var preview = await service.PreviewPortableImportAsync(secondStream, CancellationToken.None);

            Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, preview.Disposition);
            Assert.IsFalse(preview.CanConfirm);
            Assert.IsFalse(preview.WillMutate);
            Assert.IsFalse(preview.RequiresSafetyCopy);
            Assert.IsGreaterThan(0, preview.SkippedCount);
        }

        Assert.AreEqual(wordCountAfterMerge, await WordCountAsync(targetDb));
        Assert.AreEqual(safetyCopiesAfterMerge, Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length);
    }

    // ---- Preview 5. Merge preview reports inserted/enriched/preserved/skipped aggregates from preflight ----
    [TestMethod]
    public async Task Preview_MergeChanges_ReportsAggregateCountsFromPreflight()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var perEntity = Enum.GetValues<MergeEntityKind>().ToDictionary(kind => kind, _ => MergeEntityPlanCounts.Zero);
        perEntity[MergeEntityKind.Vocabulary] = new MergeEntityPlanCounts(
            NewCount: 3, ExactDuplicateSkippedCount: 2, EnrichedCount: 1, PreservedVariantCount: 1,
            UnresolvedConflictCount: 0, DeduplicatedEventCount: 0);
        perEntity[MergeEntityKind.LearningReview] = new MergeEntityPlanCounts(
            NewCount: 0, ExactDuplicateSkippedCount: 0, EnrichedCount: 0, PreservedVariantCount: 0,
            UnresolvedConflictCount: 0, DeduplicatedEventCount: 4);
        var plan = new MergePreflightPlan(
            Status: MergePreflightStatus.Ready,
            IsExecutable: true,
            Manifest: DummyManifest(),
            ChecksumVerified: true,
            PerEntity: perEntity,
            Actions: [],
            DerivedAnswerVariantPlans: [],
            KnowledgeStateConflictDecisions: [],
            WorkflowStatusConflictDecisions: [],
            SemanticMeaningGroupingDecisions: [],
            PreferredVariantSelectionDecisions: [],
            BlockingPrerequisites: [],
            SampleDetails: new Dictionary<MergeEntityClassification, IReadOnlyList<string>>(),
            WarningCodes: [],
            RequiresSchedulerReplay: false,
            ErrorCode: null);

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(plan, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition);
        Assert.AreEqual(3, preview.InsertedCount);
        Assert.AreEqual(1, preview.EnrichedCount);
        Assert.AreEqual(1, preview.PreservedVariantCount);
        Assert.AreEqual(6, preview.SkippedCount);
        CollectionAssert.AreEqual(new[] { "preflight" }, callLog);
    }

    // ---- Preview 8. Invalid archive returns ValidationFailed with zero mutation ----
    [TestMethod]
    public async Task Preview_InvalidArchive_ReturnsValidationFailed_ZeroMutation()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var wordCountBefore = await WordCountAsync(targetDb);

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var corruptStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not a zip archive"));
        var preview = await service.PreviewPortableImportAsync(corruptStream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.ValidationFailed, preview.Disposition);
        Assert.IsFalse(preview.CanConfirm);
        Assert.IsNotNull(preview.ErrorCode);
        Assert.IsNull(preview.ArchiveSummary);
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));
    }

    // ---- Preview 9. Blocked preflight returns Blocked with the existing stable code ----
    [TestMethod]
    public async Task Preview_BlockedPreflight_ReturnsBlockedWithStableCode()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var requiresDecisionPlan = BuildPlan(MergePreflightStatus.RequiresUserDecision, isExecutable: false);
        var callLog1 = new List<string>();
        var service1 = new BackupService(
            targetDb, new FakePlatformInfo(), mergePreflightService: new RecordingPreflightService(requiresDecisionPlan, callLog1));
        using (var stream1 = new MemoryStream(archiveBytes))
        {
            var preview1 = await service1.PreviewPortableImportAsync(stream1, CancellationToken.None);
            Assert.AreEqual(PortableImportPreviewDisposition.Blocked, preview1.Disposition);
            Assert.AreEqual(PortableImportPreviewErrorCodes.MergeRequiresUserDecision, preview1.ErrorCode);
            Assert.IsFalse(preview1.CanConfirm);
        }

        var blockedByPrerequisitePlan = BuildPlan(MergePreflightStatus.BlockedByPrerequisite, isExecutable: false);
        var callLog2 = new List<string>();
        var service2 = new BackupService(
            targetDb, new FakePlatformInfo(), mergePreflightService: new RecordingPreflightService(blockedByPrerequisitePlan, callLog2));
        using (var stream2 = new MemoryStream(archiveBytes))
        {
            var preview2 = await service2.PreviewPortableImportAsync(stream2, CancellationToken.None);
            Assert.AreEqual(PortableImportPreviewDisposition.Blocked, preview2.Disposition);
            Assert.AreEqual(PortableImportPreviewErrorCodes.MergeBlockedByPrerequisite, preview2.ErrorCode);
        }
    }

    // ---- Preview 10. Archive v2 into Schema-7 remains rejected ----
    [TestMethod]
    public async Task Preview_ArchiveV2IntoSchema7Target_RemainsRejected()
    {
        var targetDb = new TemporaryKnownFirstDatabase();
        await targetDb.InitializeAsync();
        try
        {
            var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();
            var service = new BackupService(targetDb, new FakePlatformInfo());
            using var stream = new MemoryStream(archiveBytes);
            var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

            Assert.AreEqual(PortableImportPreviewDisposition.ValidationFailed, preview.Disposition);
            Assert.AreEqual(BackupErrorCodes.Schema8ArchiveIncompatibleWithSchema7Target, preview.ErrorCode);
        }
        finally
        {
            await targetDb.DisposeAsync();
        }
    }

    // ---- Preview 11. A non-seekable source stream is supported ----
    [TestMethod]
    public async Task Preview_NonSeekableSourceStream_IsSupported()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);

        var service = new BackupService(targetDb, new FakePlatformInfo());
        using var stream = new NonSeekableStream(archiveBytes);
        Assert.IsFalse(stream.CanSeek);

        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition);
    }

    // ---- Preview 12. Preview creates no safety-copy directory and invokes no writer ----
    [TestMethod]
    public async Task Preview_NeverCreatesSafetyCopyOrInvokesWriter()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Ready, isExecutable: true, hasInsertable: true), callLog),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(MergeSafetyCopyStatus.Success, "x", "y", DateTime.UtcNow, 1, null, null, null), callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using var stream = new MemoryStream(archiveBytes);
        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition);
        CollectionAssert.AreEqual(new[] { "preflight" }, callLog);

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.IsFalse(Directory.Exists(safetyCopyDirectory));
    }

    // ---- Preview 13. Cancellation returns a stable cancelled result without mutation ----
    [TestMethod]
    public async Task Preview_CancelledPreflight_ReturnsCancelledWithoutMutation()
    {
        await using var targetDb = new IsolatedSchema8Database();
        await SeedOneWordAsync(targetDb);
        var wordCountBefore = await WordCountAsync(targetDb);
        var archiveBytes = await BuildMinimalValidV2ArchiveBytesAsync();

        var callLog = new List<string>();
        var service = new BackupService(
            targetDb,
            new FakePlatformInfo(),
            mergePreflightService: new RecordingPreflightService(
                BuildPlan(MergePreflightStatus.Cancelled, isExecutable: false), callLog));

        using var stream = new MemoryStream(archiveBytes);
        var preview = await service.PreviewPortableImportAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.Cancelled, preview.Disposition);
        Assert.AreEqual(BackupErrorCodes.OperationCancelled, preview.ErrorCode);
        Assert.AreEqual(wordCountBefore, await WordCountAsync(targetDb));
    }
    [TestMethod]
    public async Task Schema10ActiveArchive_PopulatedTarget_PreviewsAndImportsWithSafetyCopy()
    {
        await using var targetFixture = await Schema7Fixture.CreateAsync();
        var targetWordId = await targetFixture.InsertWordAsync("targetword");
        await targetFixture.InsertMeaningAsync(targetWordId, "targetmeaning", "targetmeaning");
        await Schema10ActiveArchiveTestData.MigrateToSchema10Async(targetFixture);
        var bytes = await BuildActiveSchema10ArchiveBytesAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        var service = new BackupService(targetDatabase, new FakePlatformInfo());
        using var previewStream = new MemoryStream(bytes);
        var preview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition, preview.ErrorCode);
        Assert.IsTrue(preview.CanConfirm);
        Assert.IsTrue(preview.WillMutate);
        Assert.IsTrue(preview.RequiresSafetyCopy);

        using var importStream = new MemoryStream(bytes);
        var result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary!.Disposition);
        Assert.IsTrue(result.Summary.SafetyCopyCreated);
        Assert.AreEqual(
            1,
            await targetFixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LearningSessions WHERE StableId = ? AND Status = ?",
                Schema10ActiveArchiveTestData.WorkflowStableId,
                (int)LearningSessionStatus.Active));
    }

    [TestMethod]
    public async Task Schema10ActiveArchive_ReimportAfterMerge_IsNoChange()
    {
        await using var targetFixture = await Schema7Fixture.CreateAsync();
        var targetWordId = await targetFixture.InsertWordAsync("targetword");
        await targetFixture.InsertMeaningAsync(targetWordId, "targetmeaning", "targetmeaning");
        await Schema10ActiveArchiveTestData.MigrateToSchema10Async(targetFixture);
        var bytes = await BuildActiveSchema10ArchiveBytesAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        var service = new BackupService(targetDatabase, new FakePlatformInfo());

        using (var firstStream = new MemoryStream(bytes))
        {
            var first = await service.ImportPortableArchiveAsync(firstStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, first.Status, first.ErrorCode);
            Assert.AreEqual(PortableImportDisposition.MergeApplied, first.Summary!.Disposition);
        }

        var stateAfterFirst = await targetFixture.CapturePersistentStateAsync();
        var safetyCopyDirectory = Path.Combine(
            Path.GetDirectoryName(targetDatabase.DatabasePath)!,
            MergeSafetyCopyService.DirectoryName);
        var safetyCopiesAfterFirst = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length;

        using (var previewStream = new MemoryStream(bytes))
        {
            var preview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);
            Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, preview.Disposition, preview.ErrorCode);
            Assert.IsFalse(preview.RequiresSafetyCopy);
            Assert.IsFalse(preview.WillMutate);
        }

        using (var secondStream = new MemoryStream(bytes))
        {
            var second = await service.ImportPortableArchiveAsync(secondStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, second.Status, second.ErrorCode);
            Assert.AreEqual(PortableImportDisposition.MergeNoChange, second.Summary!.Disposition);
            Assert.IsFalse(second.Summary.SafetyCopyCreated);
        }

        CollectionAssert.AreEqual(stateAfterFirst, await targetFixture.CapturePersistentStateAsync());
        Assert.AreEqual(safetyCopiesAfterFirst, Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length);
    }

    [TestMethod]
    public async Task Schema10ActiveArchive_SameStableIdSemanticMismatch_IsBlockedWithoutMutation()
    {
        await using var targetFixture = await Schema7Fixture.CreateAsync();
        var targetWordId = await targetFixture.InsertWordAsync("targetword");
        await targetFixture.InsertMeaningAsync(targetWordId, "targetmeaning", "targetmeaning");
        await Schema10ActiveArchiveTestData.MigrateToSchema10Async(targetFixture);
        var bytes = await BuildActiveSchema10ArchiveBytesAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        var service = new BackupService(targetDatabase, new FakePlatformInfo());

        using (var firstStream = new MemoryStream(bytes))
        {
            var first = await service.ImportPortableArchiveAsync(firstStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, first.Status, first.ErrorCode);
        }

        await targetFixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET SpellingCorrect = CASE SpellingCorrect WHEN 0 THEN 1 ELSE 0 END WHERE StableId = ?",
            Schema10ActiveArchiveTestData.QueueStableId);
        var divergentState = await targetFixture.CapturePersistentStateAsync();
        var safetyCopyDirectory = Path.Combine(
            Path.GetDirectoryName(targetDatabase.DatabasePath)!,
            MergeSafetyCopyService.DirectoryName);
        var safetyCopiesBeforeConflict = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length;

        using (var previewStream = new MemoryStream(bytes))
        {
            var preview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);
            Assert.AreEqual(PortableImportPreviewDisposition.Blocked, preview.Disposition);
            Assert.AreEqual(PortableImportPreviewErrorCodes.MergeRequiresUserDecision, preview.ErrorCode);
            Assert.IsFalse(preview.CanConfirm);
            Assert.IsFalse(preview.WillMutate);
            Assert.IsFalse(preview.RequiresSafetyCopy);
        }

        using (var importStream = new MemoryStream(bytes))
        {
            var result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Failed, result.Status);
            Assert.AreEqual(MergeWriterErrorCodes.PlanNotExecutable, result.ErrorCode);
        }

        CollectionAssert.AreEqual(divergentState, await targetFixture.CapturePersistentStateAsync());
        Assert.AreEqual(
            safetyCopiesBeforeConflict,
            Directory.GetFiles(safetyCopyDirectory, "*.kfarchive").Length,
            "A non-executable conflict must not create a new safety copy.");
    }

    [TestMethod]
    public async Task Schema10ActiveArchive_SameStableIdDuplicateReviewMultiplicityMismatch_IsBlockedWithoutMutation()
    {
        await using var targetFixture = await Schema7Fixture.CreateAsync();
        var targetWordId = await targetFixture.InsertWordAsync("targetword");
        await targetFixture.InsertMeaningAsync(targetWordId, "targetmeaning", "targetmeaning");
        await Schema10ActiveArchiveTestData.MigrateToSchema10Async(targetFixture);

        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        var originalArchiveBytes = await BuildActiveSchema10ArchiveBytesAsync();
        var seedingService = new BackupService(targetDatabase, new FakePlatformInfo());
        using (var seedStream = new MemoryStream(originalArchiveBytes))
        {
            var seedResult = await seedingService.ImportPortableArchiveAsync(seedStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, seedResult.Status, seedResult.ErrorCode);
        }

        var targetStateBeforeConflict = await targetFixture.CapturePersistentStateAsync();
        var reviewCountBeforeConflict = await targetFixture.Connection.Table<LearningReviewEntity>().CountAsync();
        Assert.AreEqual(1, reviewCountBeforeConflict);

        var payload = Schema10ActiveArchiveTestData.CreatePayload();
        var review = payload.Learning.ReviewEvents.Single();
        var duplicateReviewPayload = payload with
        {
            Learning = payload.Learning with { ReviewEvents = [review, review] }
        };
        using var duplicateArchiveStream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(
            duplicateReviewPayload,
            new FakePlatformInfo(),
            new ValidatedSchema10Capability(),
            BackupTestData.UtcTime,
            duplicateArchiveStream,
            CancellationToken.None);
        var duplicateArchiveBytes = duplicateArchiveStream.ToArray();

        var callLog = new List<string>();
        var conflictService = new BackupService(
            targetDatabase,
            new FakePlatformInfo(),
            mergeSafetyCopyService: new RecordingSafetyCopyService(
                new MergeSafetyCopyResult(
                    MergeSafetyCopyStatus.Success,
                    "unused",
                    "unused",
                    BackupTestData.UtcTime,
                    1,
                    null,
                    null,
                    null),
                callLog),
            mergeWriterService: new RecordingWriterService(MergeWriteResult.SuccessResult, callLog));

        using (var previewStream = new MemoryStream(duplicateArchiveBytes))
        {
            var preview = await conflictService.PreviewPortableImportAsync(previewStream, CancellationToken.None);
            Assert.AreEqual(PortableImportPreviewDisposition.Blocked, preview.Disposition);
            Assert.AreEqual(PortableImportPreviewErrorCodes.MergeRequiresUserDecision, preview.ErrorCode);
            Assert.IsFalse(preview.CanConfirm);
            Assert.IsFalse(preview.WillMutate);
            Assert.IsFalse(preview.RequiresSafetyCopy);
        }

        using (var importStream = new MemoryStream(duplicateArchiveBytes))
        {
            var result = await conflictService.ImportPortableArchiveAsync(importStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Failed, result.Status);
            Assert.AreEqual(MergeWriterErrorCodes.PlanNotExecutable, result.ErrorCode);
        }

        Assert.IsEmpty(callLog, "Conflict handling must not invoke the safety-copy service or merge writer.");
        CollectionAssert.AreEqual(targetStateBeforeConflict, await targetFixture.CapturePersistentStateAsync());
        Assert.AreEqual(
            reviewCountBeforeConflict,
            await targetFixture.Connection.Table<LearningReviewEntity>().CountAsync());
    }
}
