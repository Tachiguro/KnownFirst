using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Diagnostics;

[TestClass]
[DoNotParallelize]
public class LexicalDiagnosticLogIsolationTests
{
    private sealed class FakeBuildIdentityService : IBuildIdentityService
    {
        public BuildIdentity Identity { get; set; } = new(
            "KnownFirst",
            "1.0.0",
            "100",
            "com.tachiguro.knownfirst",
            "Release",
            "unknown",
            "unknown",
            "unknown",
            "windows",
            "10.0.19041.0",
            "unknown",
            "unknown",
            "unknown",
            false);

        public string FormatHeader() => "";
        public string GetFormattedBuildIdentity() => "";
    }

    [TestMethod]
    public void ExportPath_ResolvesUnderGuiTestProfile_WhenProfileIsActive()
    {
        var validProfile = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles",
            "run-lexical-log-test");
        var previous = Environment.GetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName);
        try
        {
            GuiTestProfile.SupportedOverrideForTests = true;
            Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validProfile);

            var log = new LexicalDiagnosticLog(new FakeBuildIdentityService());
            var exportPath = log.ExportPath;

            var expectedPrefix = Path.Combine(validProfile, "Logs");
            Assert.StartsWith(expectedPrefix, exportPath, StringComparison.OrdinalIgnoreCase);
            Assert.AreEqual(Path.Combine(expectedPrefix, "knownfirst-lexical-diagnostics.log"), exportPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, previous);
            GuiTestProfile.SupportedOverrideForTests = null;
            if (Directory.Exists(validProfile))
            {
                Directory.Delete(validProfile, recursive: true);
            }
        }
    }
}
