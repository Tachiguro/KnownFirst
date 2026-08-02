using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Isolation;

// GuiTestFaultInjection reads process-wide environment variables (its own, and GuiTestProfile's),
// so these tests must not run concurrently with each other or with GuiTestProfileTests.
[TestClass]
[DoNotParallelize]
public class GuiTestFaultInjectionTests
{
    private string? _previousFaultValue;
    private string? _previousProfileValue;

    [TestInitialize]
    public void Setup()
    {
        _previousFaultValue = Environment.GetEnvironmentVariable(GuiTestFaultInjection.EnvironmentVariableName);
        _previousProfileValue = Environment.GetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(GuiTestFaultInjection.EnvironmentVariableName, null);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, null);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(GuiTestFaultInjection.EnvironmentVariableName, _previousFaultValue);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, _previousProfileValue);
        GuiTestProfile.SupportedOverrideForTests = null;
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableNotSet()
    {
        Assert.IsNull(GuiTestFaultInjection.Resolve());
        Assert.IsFalse(GuiTestFaultInjection.IsActive);
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenGuiTestProfileIsNotActive()
    {
        // The isolated profile must already be active before fault injection can ever activate -
        // this must never be reachable against anything but an isolated GUI-test profile.
        Environment.SetEnvironmentVariable(GuiTestFaultInjection.EnvironmentVariableName, "AfterCardInsert");

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestFaultInjection.Resolve());
    }

    [TestMethod]
    public void Resolve_ReturnsCheckpointName_WhenGuiTestProfileIsActive()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var validProfilePath = Path.Combine(
            Path.GetTempPath(), "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts", "gui-tests", "windows", "profiles", "run-fault");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestFaultInjection.EnvironmentVariableName, "  AfterCardInsert  ");

        try
        {
            var resolved = GuiTestFaultInjection.Resolve();

            Assert.AreEqual("AfterCardInsert", resolved);
            Assert.IsTrue(GuiTestFaultInjection.IsActive);
        }
        finally
        {
            if (Directory.Exists(validProfilePath))
            {
                Directory.Delete(validProfilePath, recursive: true);
            }
        }
    }

    [TestMethod]
    public void SingleShotInjector_ThrowsOnceForTheMatchingCheckpoint_ThenNeverAgain()
    {
        var injector = new GuiTestFaultInjection.SingleShotInjector("AfterCardInsert");

        Assert.ThrowsExactly<InvalidOperationException>(() => injector.AtCheckpoint("AfterCardInsert"));
        // A retry after the first (rolled-back) failure must succeed, so a second call at the
        // same checkpoint must not throw again.
        injector.AtCheckpoint("AfterCardInsert");
    }

    [TestMethod]
    public void SingleShotInjector_NeverThrowsForAnyOtherCheckpoint()
    {
        var injector = new GuiTestFaultInjection.SingleShotInjector("AfterCardInsert");

        injector.AtCheckpoint("AfterSenseInsert");
        injector.AtCheckpoint("AfterMeaningInsert");
        injector.AtCheckpoint("BeforeCandidateCompletion");
    }
}
