using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

// Runtime evidence for ProviderPortableArchiveExporter (platform-independent) plus source-contract
// evidence for AndroidPortableArchiveFileService's routing through it. None of these tests touch
// Android, MAUI, a device, an emulator, a real content provider, or the network — every destination is
// an in-process fake, and every archive is produced through the existing production
// BackupService.CreatePortableArchiveAsync path over an isolated temporary SQLite database.
[TestClass]
public sealed class ProviderPortableArchiveExporterTests
{
    private static readonly DateTime FixedUtc = new(2032, 4, 5, 6, 7, 8, DateTimeKind.Utc);

    // --- Phase A: behavior-preserving characterization/hardening evidence. These pin the seam's
    // externally observable contract as extracted; they are not a red/green behavior-change claim. ---

    [TestMethod]
    public async Task ExportAsync_WhenDestinationAcquisitionReturnsNull_ReturnsCancelled()
    {
        // Phase A originally asserted "without invoking the writer" here, matching the pre-existing
        // Android adapter's acquire-before-generate ordering. That claim is intentionally reversed by
        // the Phase B fix below (stage and strictly validate before ever showing the picker), so this
        // Phase A test was narrowed to the part of its contract that Phase B preserves — Cancelled is
        // still returned when the user cancels the picker. The now-writer-runs-first case is covered,
        // together with staging cleanup, by ExportAsync_WhenAcquisitionIsCancelledAfterStaging_
        // ReturnsCancelledAndRemovesStaging below.
        var validBytes = await CreateValidArchiveBytesAsync();

        var status = await ProviderPortableArchiveExporter.ExportAsync(
            ValidWriter(validBytes),
            NullAcquisition,
            CancellationToken.None);

        Assert.AreEqual(PortableArchiveSaveStatus.Cancelled, status);
    }

    [TestMethod]
    public async Task ExportAsync_WithValidArchive_ReturnsSavedAndDestinationReceivesExactBytes()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new CapturingDestination();

        var status = await ProviderPortableArchiveExporter.ExportAsync(
            ValidWriter(validBytes),
            AcquisitionOf(destination),
            CancellationToken.None);

        Assert.AreEqual(PortableArchiveSaveStatus.Saved, status);
        CollectionAssert.AreEqual(validBytes, destination.WrittenBytes);
        Assert.IsTrue(destination.DisposeCalled);
    }

    [TestMethod]
    public async Task ExportAsync_WhenWriterThrows_PropagatesExceptionInsteadOfReturningSaved()
    {
        Func<Stream, CancellationToken, Task> writer =
            (_, _) => throw new InvalidOperationException("Simulated writer failure.");
        var destination = new CapturingDestination();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                writer,
                AcquisitionOf(destination),
                CancellationToken.None));
        Assert.IsFalse(destination.OpenWriteInvoked);
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationOpenWriteFails_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => throw new IOException("Simulated destination-open failure."));

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationCopyFails_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new FailingWriteStream()));

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_WhenCancelledDuringDestinationCopy_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new CancellingDuringWriteStream()));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationFlushFails_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new FlushFailingStream()));

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationReadBackOpenFails_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new MemoryStream()),
            openReadBack: _ => throw new IOException("Simulated destination readback-open failure."));

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationReadBackIsEmpty_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new MemoryStream()),
            openReadBack: _ => Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>())));

        // Phase-invariant on purpose: Phase A's one-byte check and Phase B's strict archive
        // validation reject an empty readback for different reasons and with different exception
        // types (IOException vs BackupFormatException); this test only pins "never Saved on an
        // empty destination readback", not the exact exception identity.
        await Assert.ThrowsAsync<Exception>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_OnSuccess_RemovesExactStagingArtifact()
    {
        var stagingName = $"exporter-test-{Guid.NewGuid():N}.kfexport-stage";
        var stagingPath = Path.Combine(Path.GetTempPath(), stagingName);
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new CapturingDestination();

        var status = await ProviderPortableArchiveExporter.ExportAsync(
            ValidWriter(validBytes),
            AcquisitionOf(destination),
            CancellationToken.None,
            stagingFileNameFactory: _ => stagingName);

        Assert.AreEqual(PortableArchiveSaveStatus.Saved, status);
        Assert.IsFalse(File.Exists(stagingPath));
    }

    [TestMethod]
    public async Task ExportAsync_OnFailure_RemovesExactStagingArtifact()
    {
        var stagingName = $"exporter-test-{Guid.NewGuid():N}.kfexport-stage";
        var stagingPath = Path.Combine(Path.GetTempPath(), stagingName);
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new FailingWriteStream()));

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None,
                stagingFileNameFactory: _ => stagingName));

        Assert.IsFalse(File.Exists(stagingPath));
    }

    [TestMethod]
    public void ExportAsync_NeverAttemptsDestinationDeletion()
    {
        // Structural evidence, not a runtime assertion: IPortableArchiveExportDestination exposes no
        // delete/rename/move/replace member at all, so ProviderPortableArchiveExporter is incapable of
        // requesting one regardless of outcome.
        var members = typeof(IPortableArchiveExportDestination).GetMethods().Select(m => m.Name).ToArray();
        Assert.IsFalse(members.Any(name =>
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rename", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Move", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Replace", StringComparison.OrdinalIgnoreCase)));
    }

    // --- Phase A source-contract evidence: proves the Android adapter's *shape*, not runtime provider
    // behavior. AndroidPortableArchiveFileService.cs is Android-only (net10.0-android) and is not
    // compiled by KnownFirst.Tests (net10.0), so these are the only automated checks this focused
    // filter can perform against it; see docs/TESTING.md "Layered Confidence Model". ---

    [TestMethod]
    public void ProviderPortableArchiveExporter_ContainsNoPlatformDependency()
    {
        // Checks actual code dependencies (using-directives and qualified type references), not prose:
        // the file's own doc comments legitimately name AndroidPortableArchiveFileService as the caller
        // this seam was extracted from, which would falsely trip a plain substring-of-"Android" check.
        var source = ReadRepositoryFile("Services", "DataSafety", "ProviderPortableArchiveExporter.cs");
        var codeLines = source
            .Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal))
            .Where(line => !line.StartsWith("///", StringComparison.Ordinal))
            .Where(line => !line.StartsWith("*", StringComparison.Ordinal));
        var codeOnly = string.Join('\n', codeLines);

        Assert.DoesNotContain("using Android", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("using Microsoft.Maui", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("Android.", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Maui.", codeOnly, StringComparison.Ordinal);
    }

    [TestMethod]
    public void AndroidPortableArchiveFileService_RoutesExportThroughProviderExporterWithDeferredPickerAcquisition()
    {
        var source = ReadRepositoryFile("Platforms", "Android", "AndroidPortableArchiveFileService.cs");

        Assert.DoesNotContain("VerifySavedArchiveAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("writeArchive(output", source, StringComparison.Ordinal);

        var exportStart = source.IndexOf(
            "public async Task<PortableArchiveSaveStatus> ExportAsync(", StringComparison.Ordinal);
        var exportEnd = source.IndexOf(
            "private static async Task<IPortableArchiveExportDestination?> AcquireDestinationAsync",
            exportStart,
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, exportStart);
        Assert.IsGreaterThan(exportStart, exportEnd);
        var exportBody = source[exportStart..exportEnd];

        StringAssert.Contains(exportBody, "ProviderPortableArchiveExporter.ExportAsync(");
        // The picker (StartActivityForResult / ACTION_CREATE_DOCUMENT) is launched inside the deferred
        // acquisition delegate handed to the exporter, never directly inside ExportAsync itself.
        Assert.DoesNotContain("StartActivityForResult", exportBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionCreateDocument", exportBody, StringComparison.Ordinal);
    }

    // --- Phase B: genuine behavior-changing red/green evidence. Each test below compiles against the
    // Phase A seam and is expected to fail on an assertion (not a compilation error) until the Phase B
    // production change lands. ---

    [TestMethod]
    public async Task ExportAsync_WhenWriterFails_NeverAcquiresDestination()
    {
        Func<Stream, CancellationToken, Task> writer =
            (_, _) => throw new InvalidOperationException("Simulated writer failure.");
        var probe = new AcquisitionProbe(new CapturingDestination());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(writer, probe.AcquireAsync, CancellationToken.None));

        Assert.IsFalse(probe.Invoked);
    }

    [TestMethod]
    public async Task ExportAsync_WhenWriterWritesPartialOutputThenFails_NeverAcquiresDestination()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var probe = new AcquisitionProbe(new CapturingDestination());

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                PartialThenFailingWriter(validBytes), probe.AcquireAsync, CancellationToken.None));

        Assert.IsFalse(probe.Invoked);
    }

    [TestMethod]
    public async Task ExportAsync_WhenStagedBytesAreCorruptButNonEmpty_NeverAcquiresDestination()
    {
        var probe = new AcquisitionProbe(new CapturingDestination());

        await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                CorruptNonEmptyWriter(), probe.AcquireAsync, CancellationToken.None));

        Assert.IsFalse(probe.Invoked);
    }

    [TestMethod]
    public async Task ExportAsync_WhenStagedArchiveVersionIsUnsupported_NeverAcquiresDestination()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var unsupportedVersionBytes = await MutateFormatVersionAsync(validBytes, "99");
        var probe = new AcquisitionProbe(new CapturingDestination());

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(unsupportedVersionBytes), probe.AcquireAsync, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.UnsupportedFormat, exception.Code);
        Assert.IsFalse(probe.Invoked);
    }

    [TestMethod]
    public async Task ExportAsync_AcquiresDestinationOnlyAfterStrictStagingValidation()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var callOrder = new List<string>();
        Func<Stream, CancellationToken, Task> writer = async (stream, ct) =>
        {
            callOrder.Add("writer");
            await stream.WriteAsync(validBytes, ct);
        };
        Task<IPortableArchiveExportDestination?> Acquire(CancellationToken _)
        {
            callOrder.Add("acquire");
            return Task.FromResult<IPortableArchiveExportDestination?>(new CapturingDestination());
        }

        var status = await ProviderPortableArchiveExporter.ExportAsync(writer, Acquire, CancellationToken.None);

        Assert.AreEqual(PortableArchiveSaveStatus.Saved, status);
        CollectionAssert.AreEqual(new[] { "writer", "acquire" }, callOrder);
    }

    [TestMethod]
    public async Task ExportAsync_WhenAcquisitionIsCancelledAfterStaging_ReturnsCancelledAndRemovesStaging()
    {
        var stagingName = $"exporter-test-{Guid.NewGuid():N}.kfexport-stage";
        var stagingPath = Path.Combine(Path.GetTempPath(), stagingName);
        var validBytes = await CreateValidArchiveBytesAsync();
        var writerInvoked = false;
        Func<Stream, CancellationToken, Task> writer = async (stream, ct) =>
        {
            writerInvoked = true;
            await stream.WriteAsync(validBytes, ct);
        };

        var status = await ProviderPortableArchiveExporter.ExportAsync(
            writer,
            NullAcquisition,
            CancellationToken.None,
            stagingFileNameFactory: _ => stagingName);

        Assert.AreEqual(PortableArchiveSaveStatus.Cancelled, status);
        Assert.IsTrue(writerInvoked, "the archive must be staged and validated before the destination is acquired");
        Assert.IsFalse(File.Exists(stagingPath));
    }

    [TestMethod]
    public async Task ExportAsync_WhenDestinationReadBackIsCorruptButNonEmpty_ThrowsInsteadOfReturningSaved()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        var destination = new CapturingDestination();
        destination.OverrideReadBack("not a valid kfarchive"u8.ToArray());

        await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            ProviderPortableArchiveExporter.ExportAsync(
                ValidWriter(validBytes),
                AcquisitionOf(destination),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ExportAsync_ReturnsSavedOnlyAfterStrictDestinationReadbackValidation()
    {
        var validBytes = await CreateValidArchiveBytesAsync();
        CountingNonSeekableStream? readBackStream = null;
        var destination = new FakeExportDestination(
            openWrite: _ => Task.FromResult<Stream>(new MemoryStream()),
            openReadBack: _ =>
            {
                readBackStream = new CountingNonSeekableStream(validBytes);
                return Task.FromResult<Stream>(readBackStream);
            });

        var status = await ProviderPortableArchiveExporter.ExportAsync(
            ValidWriter(validBytes),
            AcquisitionOf(destination),
            CancellationToken.None);

        Assert.AreEqual(PortableArchiveSaveStatus.Saved, status);
        Assert.IsNotNull(readBackStream);
        // A one-byte non-empty check would leave TotalBytesRead at 1; strict archive validation must
        // consume (and therefore prove) the complete destination content.
        Assert.AreEqual(validBytes.Length, readBackStream!.TotalBytesRead);
    }

    [TestMethod]
    public void AndroidPortableArchiveFileService_UsesExclusiveTruncatingWriteModeNotReadWriteTruncate()
    {
        var source = ReadRepositoryFile("Platforms", "Android", "AndroidPortableArchiveFileService.cs");

        Assert.DoesNotContain("\"rwt\"", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "\"wt\"");
    }

    // ---- Fixtures and fakes ----

    private static Func<CancellationToken, Task<IPortableArchiveExportDestination?>> NullAcquisition =>
        _ => Task.FromResult<IPortableArchiveExportDestination?>(null);

    private static Func<CancellationToken, Task<IPortableArchiveExportDestination?>> AcquisitionOf(
        IPortableArchiveExportDestination destination) =>
        _ => Task.FromResult<IPortableArchiveExportDestination?>(destination);

    private static Func<Stream, CancellationToken, Task> ValidWriter(byte[] bytes) =>
        async (stream, ct) => await stream.WriteAsync(bytes, ct);

    private static Func<Stream, CancellationToken, Task> CorruptNonEmptyWriter() =>
        async (stream, ct) =>
        {
            var bytes = "not a valid kfarchive"u8.ToArray();
            await stream.WriteAsync(bytes, ct);
        };

    private static Func<Stream, CancellationToken, Task> PartialThenFailingWriter(byte[] validBytes) =>
        async (stream, ct) =>
        {
            var partial = validBytes.AsSpan(0, validBytes.Length / 2).ToArray();
            await stream.WriteAsync(partial, ct);
            await stream.FlushAsync(ct);
            throw new IOException("Simulated partial writer failure.");
        };

    private static async Task<byte[]> CreateValidArchiveBytesAsync(string canonicalTerm = "seed-word")
    {
        await using var database = new TemporaryKnownFirstDatabase($"provider-export-{Guid.NewGuid():N}");
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

    // Mirrors the technique already used by BackupArchiveV2Tests.BuildArchiveWithMutatedFormatVersionAsync:
    // rewrites the manifest's formatVersion field in place, without needing to keep the data checksum
    // consistent, because format-version range rejection happens before checksum verification.
    private static async Task<byte[]> MutateFormatVersionAsync(byte[] validArchiveBytes, string newFormatVersion)
    {
        byte[] manifestBytes;
        byte[] dataBytes;
        using (var buffer = new MemoryStream(validArchiveBytes))
        using (var readArchive = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            using var manifestStream = readArchive.GetEntry("manifest.json")!.Open();
            using var manifestMemory = new MemoryStream();
            await manifestStream.CopyToAsync(manifestMemory);
            manifestBytes = manifestMemory.ToArray();

            using var dataStream = readArchive.GetEntry("data.json")!.Open();
            using var dataMemory = new MemoryStream();
            await dataStream.CopyToAsync(dataMemory);
            dataBytes = dataMemory.ToArray();
        }

        var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes)
            .Replace("\"formatVersion\":1", $"\"formatVersion\":{newFormatVersion}", StringComparison.Ordinal);
        var mutatedManifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        using var output = new MemoryStream();
        using (var writeArchive = new System.IO.Compression.ZipArchive(
            output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = writeArchive.CreateEntry(
                "manifest.json", System.IO.Compression.CompressionLevel.NoCompression);
            await using (var entryStream = manifestEntry.Open())
            {
                await entryStream.WriteAsync(mutatedManifestBytes);
            }

            var dataEntry = writeArchive.CreateEntry(
                "data.json", System.IO.Compression.CompressionLevel.NoCompression);
            await using (var entryStream = dataEntry.Open())
            {
                await entryStream.WriteAsync(dataBytes);
            }
        }

        return output.ToArray();
    }

    private static string ReadRepositoryFile(params string[] relativeSegments) =>
        File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(relativeSegments).ToArray()));

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

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Android;

        public string SourceAppVersion => "1.0.0-test";
    }

    // Records whether the acquisition delegate was invoked; used to prove "never acquires a
    // destination" outcomes.
    private sealed class AcquisitionProbe(IPortableArchiveExportDestination destination)
    {
        public bool Invoked { get; private set; }

        public Task<IPortableArchiveExportDestination?> AcquireAsync(CancellationToken cancellationToken)
        {
            Invoked = true;
            return Task.FromResult<IPortableArchiveExportDestination?>(destination);
        }
    }

    // A configurable destination fake for failure-injection scenarios that do not need to capture or
    // replay bytes.
    private sealed class FakeExportDestination(
        Func<CancellationToken, Task<Stream>> openWrite,
        Func<CancellationToken, Task<Stream>>? openReadBack = null) : IPortableArchiveExportDestination
    {
        public bool DisposeCalled { get; private set; }

        public bool OpenWriteInvoked { get; private set; }

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            OpenWriteInvoked = true;
            return openWrite(cancellationToken);
        }

        public Task<Stream> OpenReadBackAsync(CancellationToken cancellationToken) =>
            (openReadBack ?? throw new InvalidOperationException(
                "OpenReadBackAsync was not configured for this fake."))(cancellationToken);

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    // A destination that captures exactly what was written and, by default, replays those same bytes
    // back on readback (matching a real provider round-trip); OverrideReadBack lets a test substitute
    // different bytes to simulate destination-side corruption independent of what was actually sent.
    private sealed class CapturingDestination : IPortableArchiveExportDestination
    {
        private readonly MemoryStream _written = new();
        private byte[]? _readBackOverride;

        public bool DisposeCalled { get; private set; }

        public bool OpenWriteInvoked { get; private set; }

        public bool OpenReadBackInvoked { get; private set; }

        public byte[] WrittenBytes => _written.ToArray();

        public void OverrideReadBack(byte[] bytes) => _readBackOverride = bytes;

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            OpenWriteInvoked = true;
            return Task.FromResult<Stream>(new NonClosingWriteStream(_written));
        }

        public Task<Stream> OpenReadBackAsync(CancellationToken cancellationToken)
        {
            OpenReadBackInvoked = true;
            var bytes = _readBackOverride ?? _written.ToArray();
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    // Writes into a shared MemoryStream without ever disposing it, so the test can inspect the
    // accumulated bytes after the orchestrator closes this stream.
    private sealed class NonClosingWriteStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            // Intentionally does not dispose the shared underlying buffer.
        }
    }

    // Mimics a provider destination stream that fails mid-write, mirroring
    // PortableRecoveryTests.FailingWriteStream's role for the Windows/Android destination boundary.
    private sealed class FailingWriteStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated destination write failure.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("Simulated destination write failure.");

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated destination write failure.");
    }

    // Simulates cancellation surfacing mid-copy as an OperationCanceledException from the destination
    // write call itself, without needing real CancellationTokenSource timing.
    private sealed class CancellingDuringWriteStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new OperationCanceledException("Simulated cancellation during destination copy.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new OperationCanceledException("Simulated cancellation during destination copy.");

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException("Simulated cancellation during destination copy.");
    }

    // Accepts writes silently but fails on flush, simulating a destination close/flush failure without
    // needing the copy itself to fail.
    private sealed class FlushFailingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new IOException("Simulated destination flush failure.");

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            throw new IOException("Simulated destination flush failure.");

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            // Accepted and discarded; only Flush fails.
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    // Mimics a non-seekable Android ContentResolver readback stream (see
    // PortableArchiveExportGuardTests.NonSeekableStream) while counting exactly how many bytes were
    // actually consumed, to distinguish a one-byte peek from full strict validation.
    private sealed class CountingNonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public int TotalBytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            TotalBytesRead += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            TotalBytesRead += read;
            return read;
        }

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
