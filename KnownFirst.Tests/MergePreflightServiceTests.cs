using System.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

/// <summary>
/// Service-level tests for <see cref="MergePreflightService"/> (KF-BACKUP-002 Slice 3): archive
/// validation failure, active/undefined target workflow status, cancellation, and the two hard
/// guarantees the read-only preview must uphold — no database mutation and no filesystem creation
/// (in particular, no safety-copy directory).
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
        // A genuinely valid archive is required so validation reaches its cancellable async read loop
        // (a structurally invalid stream fails during synchronous ZIP parsing, before any cancellation
        // check is ever reached, and would misreport ValidationFailed instead of Cancelled).
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
    public async Task Preflight_NeverCreatesSafetyCopyDirectoryOrAnyFilesystemArtifact()
    {
        // Uses a dedicated, uniquely-named directory for the target database rather than
        // TemporaryKnownFirstDatabase's bare OS temp root: the bare temp root is shared by every test
        // in the suite (including unrelated ones), so asserting "this directory has no new files" only
        // means something reliable when the directory is exclusively this test's own.
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

    /// <summary>
    /// A database rooted in its own unique temp directory, mirroring the isolation rationale in
    /// <c>MergeSafetyCopyServiceTests.IsolatedDatabase</c>: <see cref="TemporaryKnownFirstDatabase"/>
    /// shares the bare OS temp root across every test, which would make a "no new files appeared"
    /// assertion meaningless.
    /// </summary>
    private sealed class IsolatedTargetDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLite.SQLiteAsyncConnection? _connection;

        public IsolatedTargetDatabase(string directory)
        {
            DatabasePath = Path.Combine(directory, "knownfirst.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            _connection ??= new SQLite.SQLiteAsyncConnection(DatabasePath);
            await DatabaseSchema.InitializeAsync(_connection);
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
