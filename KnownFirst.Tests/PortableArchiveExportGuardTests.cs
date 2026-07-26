using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PortableArchiveExportGuardTests
{
    [TestMethod]
    public void ValidateArchiveFileName_WithValidName_ReturnsItUnchanged()
    {
        var result = PortableArchiveExportGuard.ValidateArchiveFileName("KnownFirst-Data-2026-07-24.kfarchive");

        Assert.AreEqual("KnownFirst-Data-2026-07-24.kfarchive", result);
    }

    [TestMethod]
    public void ValidateArchiveFileName_StripsDirectoryComponents()
    {
        var result = PortableArchiveExportGuard.ValidateArchiveFileName(
            @"..\..\evil\KnownFirst-Data-2026-07-24.kfarchive");

        Assert.AreEqual("KnownFirst-Data-2026-07-24.kfarchive", result);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateArchiveFileName_WithNullOrWhitespace_Throws(string? fileName)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => PortableArchiveExportGuard.ValidateArchiveFileName(fileName!));
    }

    [TestMethod]
    [DataRow("archive.zip")]
    [DataRow("archive")]
    [DataRow("archive.KFARCHIVEEXTRA")]
    public void ValidateArchiveFileName_WithWrongExtension_Throws(string fileName)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => PortableArchiveExportGuard.ValidateArchiveFileName(fileName));
    }

    [TestMethod]
    public void ValidateArchiveFileName_IsCaseInsensitiveForExtension()
    {
        var result = PortableArchiveExportGuard.ValidateArchiveFileName("Archive.KFARCHIVE");

        Assert.AreEqual("Archive.KFARCHIVE", result);
    }

    [TestMethod]
    public void VerifySavedArchive_WithExistingNonEmptyFile_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"guard-verify-{Guid.NewGuid():N}.kfarchive");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            PortableArchiveExportGuard.VerifySavedArchive(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void VerifySavedArchive_WithMissingFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"guard-missing-{Guid.NewGuid():N}.kfarchive");

        Assert.ThrowsExactly<IOException>(() => PortableArchiveExportGuard.VerifySavedArchive(path));
    }

    [TestMethod]
    public void VerifySavedArchive_WithEmptyFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"guard-empty-{Guid.NewGuid():N}.kfarchive");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.ThrowsExactly<IOException>(() => PortableArchiveExportGuard.VerifySavedArchive(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task VerifySavedArchiveAsync_WithNonSeekableNonEmptyStream_DoesNotThrow()
    {
        await PortableArchiveExportGuard.VerifySavedArchiveAsync(
            () => new NonSeekableStream([1, 2, 3]),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task VerifySavedArchiveAsync_WithNonSeekableEmptyStream_Throws()
    {
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            PortableArchiveExportGuard.VerifySavedArchiveAsync(
                () => new NonSeekableStream([]),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task VerifySavedArchiveAsync_WithNullStream_Throws()
    {
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            PortableArchiveExportGuard.VerifySavedArchiveAsync(
                () => null,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task VerifySavedArchiveAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            PortableArchiveExportGuard.VerifySavedArchiveAsync(
                () => new NonSeekableStream([1, 2, 3]),
                cancellationSource.Token));
    }

    // Mimics an Android ContentResolver stream: non-seekable, throws on Length/Position.
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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

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
