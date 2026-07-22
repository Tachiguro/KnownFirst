using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Diagnostics;

[TestClass]
public class BuildIdentityServiceTests
{
    [TestMethod]
    public void Identity_HasFallbackValues_WhenAttributesAreMissing()
    {
        // This test runs with the test assembly's metadata, 
        // which does NOT have Git metadata injected by our MSBuild target
        var service = new BuildIdentityService();
        var identity = service.Identity;

        Assert.IsNotNull(identity.Product);
        Assert.IsTrue(identity.Product.Length > 0);
        Assert.IsNotNull(identity.Version);
        Assert.IsTrue(identity.Version.Length > 0);
        Assert.AreEqual("Debug", identity.Configuration); // Tests are typically built in Debug
        
        // Since test assembly doesn't have the Git attributes, they should fallback
        Assert.AreEqual("unknown", identity.CommitHash);
        Assert.AreEqual("unknown", identity.ShortCommitHash);
        Assert.AreEqual("unknown", identity.Branch);
    }

    [TestMethod]
    public void FormatHeader_IncludesExpectedFields()
    {
        var service = new BuildIdentityService();
        var header = service.FormatHeader();

        Assert.IsTrue(header.Contains(service.Identity.Product));
        Assert.IsTrue(header.Contains(service.Identity.Version));
        Assert.IsTrue(header.Contains(service.Identity.Configuration));
        Assert.IsTrue(header.Contains("Commit:"));
        Assert.IsTrue(header.Contains("Branch:"));
        Assert.IsTrue(header.Contains("Package:"));
        Assert.IsTrue(header.Contains("Device:"));
        Assert.IsTrue(header.Contains("OS:"));
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForDebug_ReturnsCorrectFormat()
    {
        var identity = CreateTestIdentity("Debug", isDirty: true);
        var service = new BuildIdentityService(identity);
        
        var formatted = service.GetFormattedBuildIdentity();
        
        Assert.AreEqual("KnownFirst Debug 1.0.0 \u00B7 Build 100 \u00B7 Commit abcdef1 \u00B7 DIRTY", formatted);
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForBetaDiagnostic_ReturnsCorrectFormat()
    {
        var identity = CreateTestIdentity("BetaDiagnostic", isDirty: false);
        var service = new BuildIdentityService(identity);
        
        var formatted = service.GetFormattedBuildIdentity();
        
        Assert.AreEqual("KnownFirst Diagnostic 1.0.0 \u00B7 Build 100 \u00B7 Commit abcdef1", formatted);
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForRelease_ReturnsCorrectFormat()
    {
        var identity = CreateTestIdentity("Release", isDirty: true); // Even if dirty, release doesn't show it
        var service = new BuildIdentityService(identity);
        
        var formatted = service.GetFormattedBuildIdentity();
        
        Assert.AreEqual("KnownFirst 1.0.0 \u00B7 Build 100", formatted);
        Assert.IsFalse(formatted.Contains("Commit"));
        Assert.IsFalse(formatted.Contains("Branch"));
        Assert.IsFalse(formatted.Contains("DIRTY"));
    }

    [TestMethod]
    public void FormatHeader_ForRelease_HidesCommitAndBranch()
    {
        var identity = CreateTestIdentity("Release", isDirty: true);
        var service = new BuildIdentityService(identity);
        
        var header = service.FormatHeader();
        
        Assert.IsTrue(header.Contains("Commit: not included"));
        Assert.IsTrue(header.Contains("Branch: not included"));
        Assert.IsTrue(header.Contains("Dirty: not included"));
    }

    private static BuildIdentity CreateTestIdentity(string configuration, bool isDirty)
    {
        return new BuildIdentity(
            "KnownFirst",
            "1.0.0",
            "100",
            "com.tachiguro.knownfirst",
            configuration,
            "abcdef1234567890",
            "abcdef1",
            "main",
            "windows",
            "10.0",
            "TestDevice",
            ".NET",
            "test-session",
            isDirty);
    }
}
