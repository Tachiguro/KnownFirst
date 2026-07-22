using KnownFirst.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Diagnostics;

[TestClass]
public class DiagnosticLogConfigurationTests
{
    private sealed class FakeBuildIdentityService : IBuildIdentityService
    {
        public BuildIdentity Identity { get; set; } = null!;
        public string FormatHeader() => "";
        public string GetFormattedBuildIdentity() => "";
    }

    [TestMethod]
    public void Create_ForReleaseBuild_SetsWarningRetention()
    {
        var identity = new BuildIdentity(
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

        var mockService = new FakeBuildIdentityService { Identity = identity };

        var config = DiagnosticLogConfiguration.Create(mockService);

        Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Warning, config.MinimumLevel);
        Assert.AreEqual(3, config.RetainedFileCount);
        Assert.AreEqual(1024 * 1024, config.MaximumFileBytes);
        Assert.AreEqual(7, config.MaximumAge.TotalDays);
    }

    [TestMethod]
    public void Create_ForDebugBuild_SetsTraceRetention()
    {
        TestNonReleaseConfiguration("Debug");
    }

    [TestMethod]
    public void Create_ForBetaDiagnosticBuild_SetsTraceRetention()
    {
        TestNonReleaseConfiguration("BetaDiagnostic");
    }

    private static void TestNonReleaseConfiguration(string configuration)
    {
        var identity = new BuildIdentity(
            "KnownFirst",
            "1.0.0",
            "100",
            "com.tachiguro.knownfirst.debug",
            configuration,
            "unknown",
            "unknown",
            "unknown",
            "windows",
            "10.0.19041.0",
            "unknown",
            "unknown",
            "unknown",
            false);

        var mockService = new FakeBuildIdentityService { Identity = identity };

        var config = DiagnosticLogConfiguration.Create(mockService);

        Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Trace, config.MinimumLevel);
        Assert.AreEqual(20, config.RetainedFileCount);
        Assert.AreEqual(10 * 1024 * 1024, config.MaximumFileBytes);
        Assert.AreEqual(21, config.MaximumAge.TotalDays);
    }
}
