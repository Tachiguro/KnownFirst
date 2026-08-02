using System.Text;
using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Isolation;

// GuiTestPortableArchive reads process-wide environment variables (its own, and GuiTestProfile's), so
// these tests must not run concurrently with each other or with GuiTestProfileTests/GuiTestFaultInjectionTests.
[TestClass]
[DoNotParallelize]
public class GuiTestPortableArchiveTests
{
    private string? _previousArchiveValue;
    private string? _previousProfileValue;
    private string? _createdDirectory;

    [TestInitialize]
    public void Setup()
    {
        _previousArchiveValue = Environment.GetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName);
        _previousProfileValue = Environment.GetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, null);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, null);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, _previousArchiveValue);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, _previousProfileValue);
        GuiTestProfile.SupportedOverrideForTests = null;
        if (_createdDirectory is not null && Directory.Exists(_createdDirectory))
        {
            Directory.Delete(_createdDirectory, recursive: true);
        }
    }

    private string CreateValidProfilePath(string suffix)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts", "gui-tests", "windows", "profiles", suffix);
        _createdDirectory = path;
        return path;
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableNotSet()
    {
        Assert.IsNull(GuiTestPortableArchive.Resolve());
        Assert.IsFalse(GuiTestPortableArchive.IsActive);
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenGuiTestProfileIsNotActive()
    {
        // The isolated profile must already be active before the archive seam can ever activate -
        // this must never be reachable against anything but an isolated GUI-test profile.
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, "1");

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestPortableArchive.Resolve());
    }

    [TestMethod]
    public void Resolve_ReturnsMarker_WhenGuiTestProfileIsActive()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var validProfilePath = CreateValidProfilePath("run-archive-seam");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, "  1  ");

        var resolved = GuiTestPortableArchive.Resolve();

        Assert.AreEqual("1", resolved);
        Assert.IsTrue(GuiTestPortableArchive.IsActive);
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenBuildIsUnsupported_EvenWithValidProfilePathAndMarker()
    {
        // Simulates what a real Windows Release or Android build already guarantees at compile time
        // (KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED is never defined there): the runtime gate this seam
        // delegates to (GuiTestProfile.IsActive) must independently refuse to activate.
        GuiTestProfile.SupportedOverrideForTests = false;
        var validProfilePath = CreateValidProfilePath("run-archive-seam-unsupported");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, "1");

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestPortableArchive.Resolve());
        Assert.IsFalse(
            Directory.Exists(validProfilePath),
            "An unsupported build must never create the requested profile directory.");
    }

    [TestMethod]
    public void ArchivePath_Throws_WhenNotActive()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestPortableArchive.ArchivePath);
    }

    [TestMethod]
    public void ArchivePath_ReturnsPathUnderProfileRoot_WhenActive()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var validProfilePath = CreateValidProfilePath("run-archive-path");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, "1");

        var archivePath = GuiTestPortableArchive.ArchivePath;

        var profileRoot = GuiTestProfile.RootPath;
        Assert.StartsWith(profileRoot, archivePath);
        Assert.AreEqual("gui-test-portable-archive.kfarchive", Path.GetFileName(archivePath));
    }

    [TestMethod]
    public async Task ExportAsync_Throws_AndNeverInvokesWriteArchive_WhenSeamNotActive()
    {
        var service = new GuiTestPortableArchiveFileService();
        var writeArchiveInvoked = false;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExportAsync(
            "test.kfarchive",
            (_, _) =>
            {
                writeArchiveInvoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.IsFalse(writeArchiveInvoked, "A fail-closed export must never reach the archive writer.");
    }

    [TestMethod]
    public async Task PickImportAsync_Throws_WhenSeamNotActive()
    {
        var service = new GuiTestPortableArchiveFileService();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.PickImportAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task PickImportAsync_ReturnsNull_WhenNoArchiveFileExistsYet()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var validProfilePath = CreateValidProfilePath("run-archive-no-file");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, "1");
        var service = new GuiTestPortableArchiveFileService();

        var selection = await service.PickImportAsync(CancellationToken.None);

        Assert.IsNull(selection);
    }

    [TestMethod]
    public async Task ExportAsync_WritesArchiveViaCallback_ThenPickImportAsync_ReadsBackExactBytes()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var validProfilePath = CreateValidProfilePath("run-archive-round-trip");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestPortableArchive.EnvironmentVariableName, "1");
        var service = new GuiTestPortableArchiveFileService();
        var expectedBytes = Encoding.UTF8.GetBytes("deterministic fake archive payload for the seam round trip");

        var status = await service.ExportAsync(
            "KnownFirst-Data.kfarchive",
            async (stream, cancellationToken) => await stream.WriteAsync(expectedBytes, cancellationToken),
            CancellationToken.None);

        Assert.AreEqual(KnownFirst.Services.DataSafety.PortableArchiveSaveStatus.Saved, status);
        Assert.IsTrue(File.Exists(GuiTestPortableArchive.ArchivePath));

        await using var selection = await service.PickImportAsync(CancellationToken.None);
        Assert.IsNotNull(selection);
        Assert.AreEqual("gui-test-portable-archive.kfarchive", selection.DisplayName);

        await using var readStream = await selection.OpenReadAsync(CancellationToken.None);
        using var memory = new MemoryStream();
        await readStream.CopyToAsync(memory);

        CollectionAssert.AreEqual(expectedBytes, memory.ToArray());
    }
}
