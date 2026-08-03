using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeSafetyCopyServiceTests
{
    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public KnownFirst.Models.Backup.BackupSourcePlatform SourcePlatform =>
            KnownFirst.Models.Backup.BackupSourcePlatform.Windows;

        public string SourceAppVersion => "1.0.0-test";
    }

    private sealed class FixedIdentityProvider(DateTime utcNow, string shortId)
        : IMergeSafetyCopyIdentityProvider
    {
        public DateTime UtcNow { get; } = utcNow;

        public string NewShortId() => shortId;
    }

    private sealed class ThrowingInjector : MergeSafetyCopyFailureInjector
    {
        private Action? _beforeArchiveWrite;
        private Action<string>? _afterArchiveWritten;
        private Action<string>? _afterArchiveMoved;
        private Action? _beforeMetadataWrite;
        private Action<string>? _afterMetadataWritten;
        private Action? _afterCommit;

        public ThrowingInjector WithBeforeArchiveWrite(Action action)
        {
            _beforeArchiveWrite = action;
            return this;
        }

        public ThrowingInjector WithAfterArchiveWritten(Action<string> action)
        {
            _afterArchiveWritten = action;
            return this;
        }

        public ThrowingInjector WithAfterArchiveMoved(Action<string> action)
        {
            _afterArchiveMoved = action;
            return this;
        }

        public ThrowingInjector WithBeforeMetadataWrite(Action action)
        {
            _beforeMetadataWrite = action;
            return this;
        }

        public ThrowingInjector WithAfterMetadataWritten(Action<string> action)
        {
            _afterMetadataWritten = action;
            return this;
        }

        public ThrowingInjector WithAfterCommit(Action action)
        {
            _afterCommit = action;
            return this;
        }

        public override void BeforeArchiveWrite() => _beforeArchiveWrite?.Invoke();

        public override void AfterArchiveWritten(string stagingArchivePath) =>
            _afterArchiveWritten?.Invoke(stagingArchivePath);

        public override void AfterArchiveMoved(string finalArchivePath) =>
            _afterArchiveMoved?.Invoke(finalArchivePath);

        public override void BeforeMetadataWrite() => _beforeMetadataWrite?.Invoke();

        public override void AfterMetadataWritten(string stagingMetadataPath) =>
            _afterMetadataWritten?.Invoke(stagingMetadataPath);

        public override void AfterCommit() => _afterCommit?.Invoke();
    }

    private static ThrowingInjector NewInjector() => new();

    private sealed class FakeDatabaseWithBadPath(string databasePath) : IKnownFirstDatabase
    {
        public string DatabasePath => databasePath;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<T> ReadAsync<T>(Func<SQLite.SQLiteAsyncConnection, Task<T>> operation) =>
            throw new NotSupportedException("Not used by this test.");

        public Task<T> RunInTransactionAsync<T>(Func<SQLite.SQLiteConnection, T> operation) =>
            throw new NotSupportedException("Not used by this test.");

        public Task ResetAsync() => Task.CompletedTask;

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLite.SQLiteConnection, T> operation) =>
            throw new NotSupportedException("Not used by this test.");
    }

    /// <summary>
    /// A database rooted in its own unique temp directory tree, so its sibling "merge-safety-copies"
    /// directory (derived from <see cref="DatabasePath"/>'s parent) can never collide with another
    /// test's directory. <see cref="TemporaryKnownFirstDatabase"/> is unsuitable here because every
    /// instance shares the bare OS temp root as its directory, which would make every test's
    /// safety-copy folder the same shared folder. Optionally nests extra path segments to model an
    /// isolated profile (e.g. a GUI test profile) living several directories deep.
    /// </summary>
    private sealed class IsolatedDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _testRoot;
        private SQLite.SQLiteAsyncConnection? _connection;
        private bool _initialized;

        public IsolatedDatabase(params string[] extraSegments)
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "kf-safety-copy-test", Guid.NewGuid().ToString("N"));
            var directory = extraSegments.Length == 0
                ? _testRoot
                : Path.Combine([_testRoot, .. extraSegments]);
            Directory.CreateDirectory(directory);
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

    private static MergeSafetyCopyService CreateService(
        IKnownFirstDatabase database,
        IMergeSafetyCopyIdentityProvider? identityProvider = null,
        MergeSafetyCopyFailureInjector? failureInjector = null) =>
        new(database, new FakePlatformInfo(), identityProvider, failureInjector);

    private static string StorageRoot(IKnownFirstDatabase database) =>
        Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, MergeSafetyCopyService.DirectoryName);

    private static async Task SeedMinimalDurableGraphAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            const string text = "The house.";
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            connection.Insert(new DocumentEntity
            {
                Title = "Doc",
                TextLanguage = "en",
                ExplanationLanguage = "de",
                LookupMode = LexicalLookupMode.Definition,
                Content = text,
                ContentFingerprint = fingerprint,
                ImportedAt = DateTime.UtcNow,
                WordCount = 1
            });
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = "house",
                NormalizedTerm = "house",
                Status = WordStatus.Unreviewed,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Unprepared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        });
    }

    private static async Task<int> CountDurableRowsAsync(IKnownFirstDatabase database) =>
        await database.ExecuteSnapshotAsync(connection =>
            connection.Table<DocumentEntity>().Count()
            + connection.Table<WordEntity>().Count()
            + connection.Table<ReviewSessionEntity>().Count()
            + connection.Table<PreparationSessionEntity>().Count()
            + connection.Table<LearningSessionEntity>().Count());

    private static async Task SeedActiveReviewSessionAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new DocumentEntity
            {
                Title = "Doc",
                TextLanguage = "en",
                ExplanationLanguage = "de",
                LookupMode = LexicalLookupMode.Definition,
                Content = "c",
                ContentFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("c")))
            });
            connection.Insert(new ReviewSessionEntity
            {
                DocumentId = 1,
                Status = ReviewSessionStatus.Active,
                StartedAt = DateTime.UtcNow
            });
            return true;
        });
    }

    private static async Task SeedActivePreparationSessionAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new PreparationSessionEntity
            {
                Status = PreparationSessionStatus.Active,
                Method = PreparationMethod.Manual,
                StartedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            return true;
        });
    }

    private static async Task SeedActiveLearningSessionAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new LearningSessionEntity
            {
                Status = LearningSessionStatus.Active,
                StartedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            return true;
        });
    }

    // --- Success ---

    [TestMethod]
    public async Task CreateSafetyCopyAsync_PopulatedTargetWithNoActiveWorkflow_ProducesValidatedPair()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 4, 5, 6, 7, 8, DateTimeKind.Utc), "abc123"));

            var result = await service.CreateSafetyCopyAsync("test-source", CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.Success, result.Status);
            Assert.IsNull(result.ErrorCode);
            Assert.IsNotNull(result.ArchivePath);
            Assert.IsNotNull(result.MetadataPath);

            var expectedRoot = StorageRoot(database);
            StringAssert.StartsWith(result.ArchivePath!, expectedRoot);
            StringAssert.StartsWith(result.MetadataPath!, expectedRoot);
            Assert.IsTrue(File.Exists(result.ArchivePath));
            Assert.IsTrue(File.Exists(result.MetadataPath));

            await using (var readStream = new FileStream(result.ArchivePath!, FileMode.Open, FileAccess.Read))
            {
                var validated = await BackupArchiveReader.ValidateAsync(readStream, CancellationToken.None);
                Assert.AreEqual(1, validated.Manifest.RecordCounts.SourceMaterials);
                Assert.AreEqual(1, validated.Manifest.RecordCounts.VocabularyItems);
            }

            var metadataBytes = await File.ReadAllBytesAsync(result.MetadataPath!);
            var metadata = JsonSerializer.Deserialize(
                metadataBytes,
                MergeSafetyCopyMetadataJsonSerializerContext.Default.MergeSafetyCopyMetadata);
            Assert.IsNotNull(metadata);
            Assert.AreEqual(Path.GetFileName(result.ArchivePath), metadata!.ArchiveFileName);
            Assert.AreEqual("test-source", metadata.SourceDescription);

            var fileSizeOnDisk = new FileInfo(result.ArchivePath!).Length;
            Assert.AreEqual(fileSizeOnDisk, result.ArchiveSizeBytes);
            Assert.AreEqual(fileSizeOnDisk, metadata.SizeBytes);
            Assert.AreEqual(result.RecordCounts, metadata.RecordCounts);
            Assert.AreEqual(result.ValidatedManifest!.Counts, result.RecordCounts);

            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));

            var remainingFiles = Directory.EnumerateFiles(expectedRoot).ToList();
            Assert.IsFalse(remainingFiles.Any(f => f.Contains(".mergestaging", StringComparison.Ordinal)));

            // Ordinary export remains valid and behavior-compatible after a safety copy was created.
            using var exportStream = new MemoryStream();
            await new BackupService(database, new FakePlatformInfo())
                .CreatePortableArchiveAsync(exportStream, CancellationToken.None);
            Assert.IsGreaterThan(0, exportStream.Length);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    // --- Active workflow blocking ---

    [TestMethod]
    public async Task CreateSafetyCopyAsync_ActiveReviewSession_IsBlockedWithoutAnyFileCreation()
    {
        var database = new IsolatedDatabase();
        await SeedActiveReviewSessionAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.BlockedByActiveWorkflow, result.Status);
            Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, result.ErrorCode);
            Assert.IsNull(result.ArchivePath);
            Assert.IsNull(result.MetadataPath);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_ActivePreparationSession_IsBlockedWithoutAnyFileCreation()
    {
        var database = new IsolatedDatabase();
        await SeedActivePreparationSessionAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.BlockedByActiveWorkflow, result.Status);
            Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_ActiveLearningSession_IsBlockedWithoutAnyFileCreation()
    {
        var database = new IsolatedDatabase();
        await SeedActiveLearningSessionAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.BlockedByActiveWorkflow, result.Status);
            Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    // --- Failure paths ---

    [TestMethod]
    public async Task CreateSafetyCopyAsync_ArchiveWriteFails_ReturnsSafetyCopyFailedAndRetainsPreviousCopy()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            var injector = NewInjector().WithBeforeArchiveWrite(
                () => throw new IOException("Simulated archive write failure."));
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_StagedArchiveValidationFails_ReturnsSafetyCopyFailedAndRetainsPreviousCopy()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            var injector = NewInjector().WithAfterArchiveWritten(CorruptLastByte);
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_FinalArchiveValidationFails_ReturnsSafetyCopyFailedAndRetainsPreviousCopy()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            var injector = NewInjector().WithAfterArchiveMoved(CorruptLastByte);
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_MetadataWriteFails_ReturnsSafetyCopyFailedAndRetainsPreviousCopy()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            var injector = NewInjector().WithBeforeMetadataWrite(
                () => throw new IOException("Simulated metadata write failure."));
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_MetadataRereadValidationFails_ReturnsSafetyCopyFailedAndRetainsPreviousCopy()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            var injector = NewInjector().WithAfterMetadataWritten(
                path => File.WriteAllText(path, "{ not valid metadata json"));
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_CancelledBeforeFileCreation_ReturnsCancelledWithoutAnyFile()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, cts.Token);

            Assert.AreEqual(MergeSafetyCopyStatus.Cancelled, result.Status);
            Assert.AreEqual(BackupErrorCodes.OperationCancelled, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_CancelledAfterStagingBegins_ReturnsCancelledAndLeavesNoStagingFile()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            using var cts = new CancellationTokenSource();
            var injector = NewInjector().WithAfterArchiveWritten(_ => cts.Cancel());
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "ccc111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, cts.Token);

            Assert.AreEqual(MergeSafetyCopyStatus.Cancelled, result.Status);
            Assert.AreEqual(BackupErrorCodes.OperationCancelled, result.ErrorCode);
            var root = StorageRoot(database);
            Assert.IsTrue(Directory.Exists(root));
            Assert.IsEmpty(Directory.EnumerateFiles(root));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_CancelledAfterArchiveFinalizedBeforeMetadataCommit_ReturnsCancelledAndRetainsPreviousCopy()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            using var cts = new CancellationTokenSource();
            var injector = NewInjector().WithBeforeMetadataWrite(() => cts.Cancel());
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, cts.Token);

            Assert.AreEqual(MergeSafetyCopyStatus.Cancelled, result.Status);
            Assert.AreEqual(BackupErrorCodes.OperationCancelled, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_MetadataSemanticFieldMismatch_ReturnsSafetyCopyFailedAndRetainsPreviousCopy()
    {
        // The metadata re-read step must cross-check every field against the validated final
        // archive manifest, not just ArchiveFileName/SizeBytes. Tamper a field (ArchiveFormatVersion)
        // that the filename/size-only check would miss, to prove full semantic cross-validation.
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            var injector = NewInjector().WithAfterMetadataWritten(CorruptArchiveFormatVersion);
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_DeterministicFilenameCollision_ReturnsSafetyCopyFailedAndRetainsExistingPair()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var identity = new FixedIdentityProvider(
                new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "collide1");

            var first = await CreateService(database, identity).CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            // Reusing the identical timestamp+shortId deterministically reproduces the same final
            // archive/metadata filenames; the second attempt must fail closed rather than overwrite.
            var second = await CreateService(database, identity).CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, second.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, second.ErrorCode);
            AssertOnlyFirstPairRemains(database, first.ArchivePath!, first.MetadataPath!);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_CancelledImmediatelyAfterCommitPoint_StillReturnsSuccessAndSupersedesOldPair()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            using var cts = new CancellationTokenSource();
            var injector = NewInjector().WithAfterCommit(() => cts.Cancel());
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"),
                injector);

            var result = await service.CreateSafetyCopyAsync(null, cts.Token);

            // Cancellation requested exactly at the commit point must not turn a completed,
            // validated pair into a Cancelled/Failed result, and best-effort old-copy cleanup must
            // still have run despite the pending cancellation.
            Assert.AreEqual(MergeSafetyCopyStatus.Success, result.Status);
            Assert.IsTrue(File.Exists(result.ArchivePath));
            Assert.IsTrue(File.Exists(result.MetadataPath));
            Assert.IsFalse(File.Exists(first.ArchivePath));
            Assert.IsFalse(File.Exists(first.MetadataPath));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_InvalidRelativeDatabasePath_ReturnsSafetyCopyFailed()
    {
        var database = new FakeDatabaseWithBadPath(Path.Combine("relative", "path", "knownfirst.db3"));
        var service = CreateService(database);

        var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

        Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_ParentlessDatabasePath_ReturnsSafetyCopyFailed()
    {
        // A fully-qualified drive root (e.g. "C:\") has no parent directory, unlike an ordinary
        // fully-qualified file path — Path.GetDirectoryName returns null for it.
        var rootPath = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.IsTrue(Path.IsPathFullyQualified(rootPath));
        Assert.IsNull(Path.GetDirectoryName(rootPath));
        var database = new FakeDatabaseWithBadPath(rootPath);
        var service = CreateService(database);

        var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

        Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_FutureSchemaVersion_ReturnsSafetyCopyFailedWithoutMutation()
    {
        var database = new IsolatedDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                connection.Execute("PRAGMA user_version = 99");
                return true;
            });

            var service = CreateService(database);
            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    // --- Retention ---

    [TestMethod]
    public async Task CreateSafetyCopyAsync_SecondSuccessfulAttempt_SupersedesFirstAfterFinalValidation()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);
            Assert.IsTrue(File.Exists(first.ArchivePath));
            Assert.IsTrue(File.Exists(first.MetadataPath));

            var second = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.Success, second.Status);
            Assert.AreNotEqual(first.ArchivePath, second.ArchivePath);
            Assert.IsTrue(File.Exists(second.ArchivePath));
            Assert.IsTrue(File.Exists(second.MetadataPath));
            Assert.IsFalse(File.Exists(first.ArchivePath));
            Assert.IsFalse(File.Exists(first.MetadataPath));

            var root = StorageRoot(database);
            var remaining = Directory.EnumerateFiles(root).Select(Path.GetFileName).ToList();
            CollectionAssert.AreEquivalent(
                new[] { Path.GetFileName(second.ArchivePath), Path.GetFileName(second.MetadataPath) },
                remaining);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_UnrelatedFileInDirectory_IsNeverDeleted()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var root = StorageRoot(database);
            Directory.CreateDirectory(root);
            var unrelatedPath = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(unrelatedPath, "unrelated user file");

            await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.IsTrue(File.Exists(unrelatedPath));
            Assert.AreEqual("unrelated user file", await File.ReadAllTextAsync(unrelatedPath));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_CleanupFailureForOlderPair_IsNonFatal()
    {
        var database = new IsolatedDatabase();
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var first = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "aaa000"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);
            Assert.AreEqual(MergeSafetyCopyStatus.Success, first.Status);

            await using var lockStream = new FileStream(
                first.ArchivePath!,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var second = await CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 2, 0, 0, 0, DateTimeKind.Utc), "bbb111"))
                .CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.Success, second.Status);
            Assert.IsTrue(File.Exists(second.ArchivePath));
            Assert.IsTrue(File.Exists(second.MetadataPath));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    // --- Isolation ---

    [TestMethod]
    public async Task CreateSafetyCopyAsync_NestedIsolatedProfileDatabase_KeepsSafetyCopiesUnderThatProfile()
    {
        var database = new IsolatedDatabase("artifacts", "gui-tests", "windows", "profiles", "profile-under-test");
        await SeedMinimalDurableGraphAsync(database);
        try
        {
            var service = CreateService(
                database,
                new FixedIdentityProvider(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc), "d00001"));

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.Success, result.Status);
            var expectedRoot = StorageRoot(database);
            StringAssert.StartsWith(result.ArchivePath!, expectedRoot);
            StringAssert.Contains(expectedRoot, Path.Combine("gui-tests", "windows", "profiles"));
            Assert.IsFalse(expectedRoot.Contains(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    // --- Fail-closed workflow-status classification ---

    private static object InvokeClassifier(string methodName, object statusValue)
    {
        var method = typeof(BackupSnapshotRepository).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Classifier method '{methodName}' not found.");
        try
        {
            return method.Invoke(null, [statusValue])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    [TestMethod]
    [DataRow(ReviewSessionStatus.Active, true)]
    [DataRow(ReviewSessionStatus.Completed, false)]
    public void IsBlockingReviewStatus_EveryCurrentEnumMember_HasIntendedExplicitClassification(
        ReviewSessionStatus status, bool expectedBlocking)
    {
        Assert.AreEqual(expectedBlocking, InvokeClassifier("IsBlockingReviewStatus", status));
    }

    [TestMethod]
    [DataRow(PreparationSessionStatus.Active, true)]
    [DataRow(PreparationSessionStatus.Completed, false)]
    [DataRow(PreparationSessionStatus.Cancelled, false)]
    public void IsBlockingPreparationStatus_EveryCurrentEnumMember_HasIntendedExplicitClassification(
        PreparationSessionStatus status, bool expectedBlocking)
    {
        Assert.AreEqual(expectedBlocking, InvokeClassifier("IsBlockingPreparationStatus", status));
    }

    [TestMethod]
    [DataRow(LearningSessionStatus.Active, true)]
    [DataRow(LearningSessionStatus.Completed, false)]
    public void IsBlockingLearningStatus_EveryCurrentEnumMember_HasIntendedExplicitClassification(
        LearningSessionStatus status, bool expectedBlocking)
    {
        Assert.AreEqual(expectedBlocking, InvokeClassifier("IsBlockingLearningStatus", status));
    }

    /// <summary>
    /// Iterates every currently-defined member via Enum.GetValues and asserts the classifier never
    /// throws for a defined member. If a future value is added to the enum without a matching switch
    /// arm being added to the classifier, that new member falls into the classifier's default arm and
    /// throws, which fails this test — this is the intended tripwire, not an incidental side effect.
    /// </summary>
    [TestMethod]
    public void IsBlockingReviewStatus_EnumGetValuesCoverage_NeverThrowsForDefinedMembers()
    {
        foreach (ReviewSessionStatus status in Enum.GetValues<ReviewSessionStatus>())
        {
            InvokeClassifier("IsBlockingReviewStatus", status);
        }
    }

    [TestMethod]
    public void IsBlockingPreparationStatus_EnumGetValuesCoverage_NeverThrowsForDefinedMembers()
    {
        foreach (PreparationSessionStatus status in Enum.GetValues<PreparationSessionStatus>())
        {
            InvokeClassifier("IsBlockingPreparationStatus", status);
        }
    }

    [TestMethod]
    public void IsBlockingLearningStatus_EnumGetValuesCoverage_NeverThrowsForDefinedMembers()
    {
        foreach (LearningSessionStatus status in Enum.GetValues<LearningSessionStatus>())
        {
            InvokeClassifier("IsBlockingLearningStatus", status);
        }
    }

    [TestMethod]
    public void IsBlockingReviewStatus_UndefinedValue_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => InvokeClassifier("IsBlockingReviewStatus", (ReviewSessionStatus)99));
    }

    [TestMethod]
    public void IsBlockingPreparationStatus_UndefinedValue_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => InvokeClassifier("IsBlockingPreparationStatus", (PreparationSessionStatus)99));
    }

    [TestMethod]
    public void IsBlockingLearningStatus_UndefinedValue_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => InvokeClassifier("IsBlockingLearningStatus", (LearningSessionStatus)99));
    }

    private static async Task SeedUndefinedReviewSessionStatusAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new DocumentEntity
            {
                Title = "Doc",
                TextLanguage = "en",
                ExplanationLanguage = "de",
                LookupMode = LexicalLookupMode.Definition,
                Content = "c",
                ContentFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("c")))
            });
            connection.Insert(new ReviewSessionEntity
            {
                DocumentId = 1,
                Status = (ReviewSessionStatus)99,
                StartedAt = DateTime.UtcNow
            });
            return true;
        });
    }

    private static async Task SeedUndefinedPreparationSessionStatusAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new PreparationSessionEntity
            {
                Status = (PreparationSessionStatus)99,
                Method = PreparationMethod.Manual,
                StartedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            return true;
        });
    }

    private static async Task SeedUndefinedLearningSessionStatusAsync(IKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new LearningSessionEntity
            {
                Status = (LearningSessionStatus)99,
                StartedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            return true;
        });
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_UndefinedStoredReviewSessionStatus_ReturnsSafetyCopyFailedWithoutDirectoryOrMutation()
    {
        var database = new IsolatedDatabase();
        await SeedUndefinedReviewSessionStatusAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_UndefinedStoredPreparationSessionStatus_ReturnsSafetyCopyFailedWithoutDirectoryOrMutation()
    {
        var database = new IsolatedDatabase();
        await SeedUndefinedPreparationSessionStatusAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_UndefinedStoredLearningSessionStatus_ReturnsSafetyCopyFailedWithoutDirectoryOrMutation()
    {
        var database = new IsolatedDatabase();
        await SeedUndefinedLearningSessionStatusAsync(database);
        try
        {
            var rowsBefore = await CountDurableRowsAsync(database);
            var service = CreateService(database);

            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.SafetyCopyFailed, result.Status);
            Assert.AreEqual(BackupErrorCodes.SafetyBackupFailed, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(StorageRoot(database)));
            Assert.AreEqual(rowsBefore, await CountDurableRowsAsync(database));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static void CorruptLastByte(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = stream.Length - 1;
        var lastByte = stream.ReadByte();
        stream.Position = stream.Length - 1;
        stream.WriteByte((byte)(lastByte ^ 0xFF));
    }

    private static void CorruptArchiveFormatVersion(string stagingMetadataPath)
    {
        var bytes = File.ReadAllBytes(stagingMetadataPath);
        var metadata = JsonSerializer.Deserialize(
            bytes,
            MergeSafetyCopyMetadataJsonSerializerContext.Default.MergeSafetyCopyMetadata)!;
        var tampered = metadata with { ArchiveFormatVersion = metadata.ArchiveFormatVersion + 1 };
        var tamperedBytes = JsonSerializer.SerializeToUtf8Bytes(
            tampered,
            MergeSafetyCopyMetadataJsonSerializerContext.Default.MergeSafetyCopyMetadata);
        File.WriteAllBytes(stagingMetadataPath, tamperedBytes);
    }

    private static void AssertOnlyFirstPairRemains(
        IKnownFirstDatabase database,
        string firstArchivePath,
        string firstMetadataPath)
    {
        Assert.IsTrue(File.Exists(firstArchivePath));
        Assert.IsTrue(File.Exists(firstMetadataPath));

        var root = StorageRoot(database);
        var remaining = Directory.EnumerateFiles(root).Select(Path.GetFileName).ToList();
        CollectionAssert.AreEquivalent(
            new[] { Path.GetFileName(firstArchivePath), Path.GetFileName(firstMetadataPath) },
            remaining);
    }

    // ---- KF-MEANING-001 Slice 2: Core 10 — Schema-8 merge safety copy ----

    [TestMethod]
    [DoNotParallelize]
    public async Task CreateSafetyCopy_FromNormallyInitializedCurrentSchemaDatabase_ProducesValidV2Archive()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("safety-copy");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "safety-copy", translation: "Sicherungskopie");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        try
        {
            var service = CreateService(database, new FixedIdentityProvider(DateTime.UtcNow, "aabbcc"));
            var result = await service.CreateSafetyCopyAsync("schema8-test", CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.Success, result.Status);
            Assert.IsNotNull(result.ArchivePath);
            Assert.IsTrue(File.Exists(result.ArchivePath));

            await using var readStream = new FileStream(result.ArchivePath!, FileMode.Open, FileAccess.Read);
            var versioned = await BackupArchiveReader.ValidateVersionedAsync(readStream, CancellationToken.None);
            Assert.AreEqual(2, versioned.FormatVersion);
            Assert.IsNotNull(versioned.V2);
            Assert.AreEqual(DatabaseSchema.CurrentVersion, versioned.V2!.Manifest.SourceDatabaseSchemaVersion);
            Assert.IsGreaterThan(0, versioned.V2.Payload.Senses.Count);

            Assert.AreEqual(2, result.ValidatedManifest!.FormatVersion);
            Assert.IsNotNull(result.RecordCounts!.Senses);
        }
        finally
        {
            var root = StorageRoot(database);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task CreateSafetyCopy_FromSchema8Database_ActiveReviewSession_BlocksFailClosedNoFilesWritten()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO ReviewSessions (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount, DecisionSequence, StartedAt) VALUES (0, ?, 0, 0, 0, 0, 0, 0, ?)",
            (int)ReviewSessionStatus.Active, DateTime.UtcNow);

        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var root = StorageRoot(database);
        try
        {
            var service = CreateService(database);
            var result = await service.CreateSafetyCopyAsync(null, CancellationToken.None);

            Assert.AreEqual(MergeSafetyCopyStatus.BlockedByActiveWorkflow, result.Status);
            Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, result.ErrorCode);
            Assert.IsFalse(Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
