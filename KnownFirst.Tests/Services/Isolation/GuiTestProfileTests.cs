using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Isolation;

// GuiTestProfile reads a process-wide environment variable, so these tests must not run
// concurrently with each other (or with anything else that touches the same variable).
[TestClass]
[DoNotParallelize]
public class GuiTestProfileTests
{
    private string? _previousValue;
    private string? _createdDirectory;

    [TestInitialize]
    public void Setup()
    {
        _previousValue = Environment.GetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, null);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, _previousValue);
        if (_createdDirectory is not null && Directory.Exists(_createdDirectory))
        {
            Directory.Delete(_createdDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableNotSet()
    {
        Assert.IsNull(GuiTestProfile.Resolve());
        Assert.IsFalse(GuiTestProfile.IsActive);
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableIsWhitespace()
    {
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, "   ");

        Assert.IsNull(GuiTestProfile.Resolve());
        Assert.IsFalse(GuiTestProfile.IsActive);
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenPathDoesNotResolveUnderRequiredProfilesRoot()
    {
        var untrustedPath = Path.Combine(Path.GetTempPath(), "kf-gui-test-untrusted-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, untrustedPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
        Assert.IsFalse(Directory.Exists(untrustedPath), "A rejected path must never be created on disk.");
    }

    [TestMethod]
    public void Resolve_FailsClosed_ForRealApplicationDataDirectory()
    {
        // Guards specifically against the dangerous case: someone accidentally pointing the
        // isolation variable at the real per-user AppData root instead of a profiles directory.
        var realLookingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "com.tachiguro.knownfirst",
            "Data");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, realLookingPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
    }

    [TestMethod]
    public void Resolve_ReturnsPathAndCreatesDirectory_WhenValid()
    {
        var validPath = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles",
            "run-001");
        _createdDirectory = validPath;
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validPath);

        var resolved = GuiTestProfile.Resolve();

        Assert.IsNotNull(resolved);
        Assert.IsTrue(Directory.Exists(resolved));
        Assert.IsTrue(GuiTestProfile.IsActive);
        Assert.AreEqual(resolved, GuiTestProfile.RootPath);
    }

    [TestMethod]
    public void RootPath_Throws_WhenNotActive()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.RootPath);
    }
}
