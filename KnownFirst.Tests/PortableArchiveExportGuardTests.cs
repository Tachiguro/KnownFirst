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
}
