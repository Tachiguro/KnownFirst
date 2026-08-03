using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

[TestClass]
public sealed class StagedPortableArchiveExporterTests
{
    private static readonly DateTime FixedUtc = new(2032, 4, 5, 6, 7, 8, DateTimeKind.Utc);

    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"staged-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationDoesNotExist_CreatesValidatedDestinationAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();

        await StagedPortableArchiveExporter.ExportAsync(
            destinationPath,
            ValidWriter("first-word"),
            CancellationToken.None);

        Assert.IsTrue(File.Exists(destinationPath));
        await using (var stream = File.OpenRead(destinationPath))
        {
            await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        }

        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationExists_ReplacesItAtomicallyWithNewContentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var oldBytes = await CreateArchiveBytesAsync("old-word");
        await File.WriteAllBytesAsync(destinationPath, oldBytes);
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();

        await StagedPortableArchiveExporter.ExportAsync(
            destinationPath,
            ValidWriter("new-word"),
            CancellationToken.None);

        var finalBytes = await File.ReadAllBytesAsync(destinationPath);
        CollectionAssert.AreNotEqual(oldBytes, finalBytes);
        await using (var stream = File.OpenRead(destinationPath))
        {
            var validated = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
            Assert.AreEqual(1, validated.V1!.Manifest.RecordCounts.VocabularyItems);
        }

        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_UsesATemporaryFileInTheSameDirectoryAsTheDestination()
    {
        var destinationPath = Path.Combine(_directory, "same-directory.kfarchive");
        string? observedTemporaryPath = null;

        await StagedPortableArchiveExporter.ExportAsync(
            destinationPath,
            ValidWriter("word"),
            CancellationToken.None,
            attempt =>
            {
                var name = $"observed-{Guid.NewGuid():N}.tmp";
                observedTemporaryPath = Path.Combine(_directory, name);
                return name;
            });

        Assert.IsNotNull(observedTemporaryPath);
        Assert.AreEqual(_directory, Path.GetDirectoryName(observedTemporaryPath));
        Assert.IsFalse(File.Exists(observedTemporaryPath));
    }

    [TestMethod]
    public async Task ExportAsync_WhenWriterThrowsBeforeWritingAnyBytes_PreservesExistingDestinationAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                (_, _) => throw new InvalidOperationException("Writer failed before writing."),
                CancellationToken.None));

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
    }

    [TestMethod]
    public async Task ExportAsync_WhenWriterThrowsAfterPartialWrite_PreservesExistingDestinationAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    await stream.WriteAsync("partial-bytes"u8.ToArray(), ct);
                    await stream.FlushAsync(ct);
                    throw new InvalidOperationException("Writer failed after partial write.");
                },
                CancellationToken.None));

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        Assert.IsGreaterThan(0, new FileInfo(destinationPath).Length);
        AssertNoLeftoverTemporaryFiles(destinationPath);
    }

    [TestMethod]
    public async Task ExportAsync_WhenStagedArchiveFailsProductionValidation_ThrowsAndPreservesExistingDestination()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    var garbage = "not a zip archive"u8.ToArray();
                    await stream.WriteAsync(garbage, ct);
                },
                CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.ArchiveLayoutInvalid, exception.Code);
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
    }

    [TestMethod]
    public async Task ExportAsync_WhenCancelledDuringStagedWriting_PreservesExistingDestinationAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    await stream.WriteAsync("partial-bytes"u8.ToArray(), ct);
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                },
                cancellation.Token));

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
    }

    [TestMethod]
    public async Task ExportAsync_WhenCancelledAfterValidationBeforeCommit_PreservesExistingDestinationAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);
        using var cancellation = new CancellationTokenSource();
        var validArchiveBytes = await CreateArchiveBytesAsync("new-word");

        // Writes a fully valid archive and only requests cancellation once writing is complete, so
        // the observed cancellation must originate from the post-write flush/validation path rather
        // than from the writer itself. FlushAsync on an already-cancelled token surfaces as
        // TaskCanceledException (a OperationCanceledException subtype), matching the same convention
        // used for the equivalent post-write cancellation check in PortableArchiveExportGuardTests.
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    await stream.WriteAsync(validArchiveBytes, CancellationToken.None);
                    cancellation.Cancel();
                },
                cancellation.Token));

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
    }

    [TestMethod]
    public async Task ExportAsync_WhenTemporaryNameCollides_RetriesWithAnotherNameWithoutTouchingTheCollidingFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var collidingName = "colliding.tmp";
        var collidingPath = Path.Combine(_directory, collidingName);
        var collidingBytes = "pre-existing unrelated content"u8.ToArray();
        await File.WriteAllBytesAsync(collidingPath, collidingBytes);
        var attempts = 0;

        await StagedPortableArchiveExporter.ExportAsync(
            destinationPath,
            ValidWriter("word"),
            CancellationToken.None,
            attempt =>
            {
                attempts++;
                return attempt == 0 ? collidingName : $"retry-{Guid.NewGuid():N}.tmp";
            });

        Assert.IsGreaterThan(1, attempts);
        Assert.IsTrue(File.Exists(destinationPath));
        CollectionAssert.AreEqual(collidingBytes, await File.ReadAllBytesAsync(collidingPath));
    }

    [TestMethod]
    public async Task ExportAsync_WhenTemporaryNameCollisionPersists_FailsSafelyWithoutTouchingDestinationOrCollidingFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);
        var collidingName = "always-colliding.tmp";
        var collidingPath = Path.Combine(_directory, collidingName);
        var collidingBytes = "pre-existing unrelated content"u8.ToArray();
        await File.WriteAllBytesAsync(collidingPath, collidingBytes);

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                ValidWriter("word"),
                CancellationToken.None,
                _ => collidingName));

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        CollectionAssert.AreEqual(collidingBytes, await File.ReadAllBytesAsync(collidingPath));
    }

    [TestMethod]
    public async Task ExportAsync_DoesNotPerformAnyFileOperationAfterSuccessfulFinalization()
    {
        var destinationPath = Path.Combine(_directory, "post-commit.kfarchive");
        using var cancellation = new CancellationTokenSource();

        await StagedPortableArchiveExporter.ExportAsync(
            destinationPath,
            ValidWriter("word"),
            cancellation.Token);
        var committedBytes = await File.ReadAllBytesAsync(destinationPath);

        // Requested strictly after the awaited call already returned Saved; a compliant
        // implementation performs no further fallible work in reaction to this.
        cancellation.Cancel();

        CollectionAssert.AreEqual(committedBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationDidNotExistAndWriterThrowsBeforeWritingAnyBytes_LeavesDestinationAbsentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                (_, _) => throw new InvalidOperationException("Writer failed before writing."),
                CancellationToken.None));

        Assert.IsFalse(File.Exists(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationDidNotExistAndWriterThrowsAfterPartialWrite_LeavesDestinationAbsentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    await stream.WriteAsync("partial-bytes"u8.ToArray(), ct);
                    await stream.FlushAsync(ct);
                    throw new InvalidOperationException("Writer failed after partial write.");
                },
                CancellationToken.None));

        Assert.IsFalse(File.Exists(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationDidNotExistAndStagedArchiveFailsProductionValidation_LeavesDestinationAbsentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    var garbage = "not a zip archive"u8.ToArray();
                    await stream.WriteAsync(garbage, ct);
                },
                CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.ArchiveLayoutInvalid, exception.Code);
        Assert.IsFalse(File.Exists(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationDidNotExistAndCancelledDuringStagedWriting_LeavesDestinationAbsentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    await stream.WriteAsync("partial-bytes"u8.ToArray(), ct);
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                },
                cancellation.Token));

        Assert.IsFalse(File.Exists(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationDidNotExistAndCancelledAfterValidationBeforeCommit_LeavesDestinationAbsentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();
        using var cancellation = new CancellationTokenSource();
        var validArchiveBytes = await CreateArchiveBytesAsync("new-word");

        // Writes a fully valid archive and only requests cancellation once writing is complete, so
        // the observed cancellation must originate from the post-write flush/validation path rather
        // than from the writer itself, matching the equivalent existing-destination case above.
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                async (stream, ct) =>
                {
                    await stream.WriteAsync(validArchiveBytes, CancellationToken.None);
                    cancellation.Cancel();
                },
                cancellation.Token));

        Assert.IsFalse(File.Exists(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenFinalizationFailsForExistingDestination_PreservesExistingDestinationByteForByteAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "existing-export.kfarchive");
        var originalBytes = await CreateArchiveBytesAsync("kept-word");
        await File.WriteAllBytesAsync(destinationPath, originalBytes);
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();
        var stagedArchiveWasValidatedBeforeFinalizerRan = false;

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                ValidWriter("new-word"),
                CancellationToken.None,
                temporaryFileNameFactory: null,
                finalizeOverride: (temporaryPath, suppliedDestinationPath) =>
                {
                    Assert.AreEqual(destinationPath, suppliedDestinationPath);
                    stagedArchiveWasValidatedBeforeFinalizerRan =
                        RevalidateStagedArchive(temporaryPath);
                    throw new IOException("Simulated finalization failure.");
                }));

        Assert.IsTrue(stagedArchiveWasValidatedBeforeFinalizerRan);
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public async Task ExportAsync_WhenFinalizationFailsForNonexistentDestination_LeavesDestinationAbsentAndRemovesTemporaryFile()
    {
        var destinationPath = Path.Combine(_directory, "new-export.kfarchive");
        var (sentinelPath, sentinelBytes) = CreateSentinelFile();
        var stagedArchiveWasValidatedBeforeFinalizerRan = false;

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            StagedPortableArchiveExporter.ExportAsync(
                destinationPath,
                ValidWriter("word"),
                CancellationToken.None,
                temporaryFileNameFactory: null,
                finalizeOverride: (temporaryPath, suppliedDestinationPath) =>
                {
                    Assert.AreEqual(destinationPath, suppliedDestinationPath);
                    stagedArchiveWasValidatedBeforeFinalizerRan =
                        RevalidateStagedArchive(temporaryPath);
                    throw new IOException("Simulated finalization failure.");
                }));

        Assert.IsTrue(stagedArchiveWasValidatedBeforeFinalizerRan);
        Assert.IsFalse(File.Exists(destinationPath));
        AssertNoLeftoverTemporaryFiles(destinationPath);
        AssertSentinelUnchanged(sentinelPath, sentinelBytes);
    }

    [TestMethod]
    public void WindowsPortableArchiveFileService_RoutesDestinationThroughStagedExporter()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "Platforms", "Windows", "WindowsPortableArchiveFileService.cs"));

        StringAssert.Contains(source, "StagedPortableArchiveExporter.ExportAsync(");
        Assert.DoesNotContain(source, "OpenStreamForWriteAsync", StringComparison.Ordinal);
        Assert.DoesNotContain(source, "SetLength(0)", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KnownFirst.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("KnownFirst repository root was not found.");
    }

    private void AssertNoLeftoverTemporaryFiles(string destinationPath)
    {
        var leftovers = Directory.GetFiles(_directory)
            .Where(path => !string.Equals(path, destinationPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => path.Contains("kfexport-tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.IsEmpty(leftovers);
    }

    private (string Path, byte[] Bytes) CreateSentinelFile()
    {
        var path = Path.Combine(_directory, "unrelated-sentinel.txt");
        var bytes = "unrelated file content that must never be touched"u8.ToArray();
        File.WriteAllBytes(path, bytes);
        return (path, bytes);
    }

    private static void AssertSentinelUnchanged(string path, byte[] expectedBytes)
    {
        CollectionAssert.AreEqual(expectedBytes, File.ReadAllBytes(path));
    }

    // Independently re-runs the real production validator against the staged temporary file at the
    // moment the finalizer is invoked, proving the injected finalization failure occurs strictly after
    // BackupArchiveReader.ValidateVersionedAsync has already accepted the staged archive once.
    private static bool RevalidateStagedArchive(string temporaryPath)
    {
        using var stream = File.OpenRead(temporaryPath);
        var validated = BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return validated.V1 is not null;
    }

    private static Func<Stream, CancellationToken, Task> ValidWriter(string canonicalTerm) =>
        async (stream, ct) =>
        {
            var bytes = await CreateArchiveBytesAsync(canonicalTerm);
            await stream.WriteAsync(bytes, ct);
        };

    private static async Task<byte[]> CreateArchiveBytesAsync(string canonicalTerm)
    {
        await using var database = new TemporaryKnownFirstDatabase($"staged-export-{Guid.NewGuid():N}");
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = canonicalTerm,
                NormalizedTerm = canonicalTerm.ToLowerInvariant(),
                Status = WordStatus.Unreviewed,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Unprepared,
                CreatedAt = FixedUtc,
                UpdatedAt = FixedUtc
            });
            return true;
        });

        var service = new BackupService(database, new FakePlatformInfo());
        using var archive = new MemoryStream();
        await service.CreatePortableArchiveAsync(archive, CancellationToken.None);
        return archive.ToArray();
    }

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;

        public string SourceAppVersion => "1.0.0-test";
    }
}
