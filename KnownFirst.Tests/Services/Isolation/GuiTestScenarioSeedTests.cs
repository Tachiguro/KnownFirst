using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Isolation;

// GuiTestScenarioSeed reads process-wide environment variables (its own, and GuiTestProfile's), so
// these tests must not run concurrently with each other or with the other GUI-test isolation tests.
[TestClass]
[DoNotParallelize]
public class GuiTestScenarioSeedTests
{
    private string? _previousSeedValue;
    private string? _previousProfileValue;

    [TestInitialize]
    public void Setup()
    {
        _previousSeedValue = Environment.GetEnvironmentVariable(GuiTestScenarioSeed.EnvironmentVariableName);
        _previousProfileValue = Environment.GetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(GuiTestScenarioSeed.EnvironmentVariableName, null);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, null);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(GuiTestScenarioSeed.EnvironmentVariableName, _previousSeedValue);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, _previousProfileValue);
        GuiTestProfile.SupportedOverrideForTests = null;
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableNotSet()
    {
        Assert.IsNull(GuiTestScenarioSeed.Resolve());
        Assert.IsFalse(GuiTestScenarioSeed.IsActive);
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenGuiTestProfileIsNotActive()
    {
        // Seeding must never be reachable against anything but an isolated GUI-test profile.
        Environment.SetEnvironmentVariable(GuiTestScenarioSeed.EnvironmentVariableName, GuiTestScenarioSeed.PreparationSelectedMeaning);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestScenarioSeed.Resolve());
    }

    [TestMethod]
    public void Resolve_ReturnsScenarioName_WhenGuiTestProfileIsActive()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var validProfilePath = Path.Combine(
            Path.GetTempPath(), "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts", "gui-tests", "windows", "profiles", "run-seed");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfilePath);
        Environment.SetEnvironmentVariable(GuiTestScenarioSeed.EnvironmentVariableName, "  " + GuiTestScenarioSeed.PreparationSelectedMeaning + "  ");

        try
        {
            var resolved = GuiTestScenarioSeed.Resolve();

            Assert.AreEqual(GuiTestScenarioSeed.PreparationSelectedMeaning, resolved);
            Assert.IsTrue(GuiTestScenarioSeed.IsActive);
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
    public async Task SeedAsync_DoesNothing_WhenNotActive()
    {
        // With no seed scenario requested, calling SeedAsync must be a safe no-op regardless of
        // what service provider is supplied - it must return before resolving any service.
        await GuiTestScenarioSeed.SeedAsync(services: null!);
    }
}
