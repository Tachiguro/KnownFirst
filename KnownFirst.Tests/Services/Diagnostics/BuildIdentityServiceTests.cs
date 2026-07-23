using KnownFirst.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Diagnostics;

[TestClass]
public class BuildIdentityServiceTests
{
    [TestMethod]
    public void Identity_HasFallbackValues_WhenAttributesAreMissing()
    {
        var service = new BuildIdentityService();
        var identity = service.Identity;

        Assert.IsNotNull(identity.Product);
        Assert.AreNotEqual(0, identity.Product.Length);
        Assert.IsNotNull(identity.Version);
        Assert.AreNotEqual(0, identity.Version.Length);
        Assert.AreEqual("Debug", identity.Configuration);
        
        Assert.AreEqual("unknown", identity.CommitHash);
        Assert.AreEqual("unknown", identity.ShortCommitHash);
        Assert.AreEqual("unknown", identity.Branch);
    }

    [TestMethod]
    public void FormatHeader_IncludesExpectedFields()
    {
        var service = new BuildIdentityService();
        var header = service.FormatHeader();

        Assert.Contains(service.Identity.Product, header);
        Assert.Contains(service.Identity.Version, header);
        Assert.Contains(service.Identity.Configuration, header);
        Assert.Contains("Commit:", header);
        Assert.Contains("Branch:", header);
        Assert.Contains("Package:", header);
        Assert.Contains("Device:", header);
        Assert.Contains("OS:", header);
    }

    [TestMethod]
    public void ResolveIdentity_ExplicitSemanticVersion_OverridesNumericWindowsPlatformVersion()
    {
        var assembly = typeof(BuildIdentity).Assembly;
        var appInfo = new AppInfoData("KnownFirst", "1.0.0.9", "9", "com.tachiguro.knownfirst");
        var metadata = new Dictionary<string, string>
        {
            ["KnownFirstProductVersion"] = "1.0.0-beta.9",
            ["KnownFirstBuildNumber"] = "9",
            ["KnownFirstProductName"] = "KnownFirst"
        };

        var identity = BuildIdentityService.ResolveIdentity(assembly, appInfo, metadataOverrides: metadata);

        Assert.AreEqual("1.0.0-beta.9", identity.Version);
        Assert.AreEqual("9", identity.BuildNumber);
        Assert.AreEqual("com.tachiguro.knownfirst", identity.PackageId);
    }

    [TestMethod]
    public void ResolveIdentity_UsesPlatformVersion_WhenExplicitMetadataIsAbsent()
    {
        var assembly = typeof(BuildIdentity).Assembly; // Assembly without custom KnownFirstProductVersion
        var appInfo = new AppInfoData("KnownFirst", "1.0.0.9", "9", "com.tachiguro.knownfirst");

        var identity = BuildIdentityService.ResolveIdentity(assembly, appInfo);

        Assert.AreEqual("1.0.0.9", identity.Version);
        Assert.AreEqual("9", identity.BuildNumber);
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForDebug_HasSingleDebugLabel()
    {
        var identity = CreateTestIdentity("KnownFirst Debug", "1.0.0-beta.9", "9", "Debug", isDirty: true);
        var service = new BuildIdentityService(identity);

        var formatted = service.GetFormattedBuildIdentity();

        Assert.AreEqual("KnownFirst \u00B7 1.0.0-beta.9 \u00B7 Debug \u00B7 Build 9 \u00B7 Commit abcdef1 \u00B7 DIRTY", formatted);
        Assert.AreEqual(1, CountOccurrences(formatted, "Debug"));
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForBetaDiagnostic_HasSingleDiagnosticLabel()
    {
        var identity = CreateTestIdentity("KnownFirst Diagnostic", "1.0.0-beta.9", "9", "BetaDiagnostic", isDirty: false);
        var service = new BuildIdentityService(identity);
        
        var formatted = service.GetFormattedBuildIdentity();
        
        Assert.AreEqual("KnownFirst \u00B7 1.0.0-beta.9 \u00B7 Diagnostic \u00B7 Build 9 \u00B7 Commit abcdef1", formatted);
        Assert.AreEqual(1, CountOccurrences(formatted, "Diagnostic"));
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForPrereleaseRelease_IncludesReleaseAndShortCommit()
    {
        var identity = CreateTestIdentity("KnownFirst", "1.0.0-beta.9", "9", "Release", isDirty: false);
        var service = new BuildIdentityService(identity);
        
        var formatted = service.GetFormattedBuildIdentity();
        
        Assert.AreEqual("KnownFirst \u00B7 1.0.0-beta.9 \u00B7 Release \u00B7 Build 9 \u00B7 Commit abcdef1", formatted);
        Assert.Contains("Release", formatted);
        Assert.Contains("Commit abcdef1", formatted);
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_ForStableRelease_OmitsCommitBranchAndDirtyState()
    {
        var identity = CreateTestIdentity("KnownFirst", "1.0.0", "100", "Release", isDirty: true);
        var service = new BuildIdentityService(identity);
        
        var formatted = service.GetFormattedBuildIdentity();
        
        Assert.AreEqual("KnownFirst \u00B7 1.0.0 \u00B7 Release \u00B7 Build 100", formatted);
        Assert.DoesNotContain("Commit", formatted);
        Assert.DoesNotContain("Branch", formatted);
        Assert.DoesNotContain("DIRTY", formatted);
    }

    [TestMethod]
    public void GetFormattedBuildIdentity_AllBeta9Configurations_DisplayVersionBeta9AndBuild9()
    {
        var debugIdentity = CreateTestIdentity("KnownFirst", "1.0.0-beta.9", "9", "Debug", isDirty: false);
        var diagIdentity = CreateTestIdentity("KnownFirst", "1.0.0-beta.9", "9", "BetaDiagnostic", isDirty: false);
        var releaseIdentity = CreateTestIdentity("KnownFirst", "1.0.0-beta.9", "9", "Release", isDirty: false);

        var debugFormatted = new BuildIdentityService(debugIdentity).GetFormattedBuildIdentity();
        var diagFormatted = new BuildIdentityService(diagIdentity).GetFormattedBuildIdentity();
        var releaseFormatted = new BuildIdentityService(releaseIdentity).GetFormattedBuildIdentity();

        Assert.Contains("1.0.0-beta.9", debugFormatted);
        Assert.Contains("Build 9", debugFormatted);

        Assert.Contains("1.0.0-beta.9", diagFormatted);
        Assert.Contains("Build 9", diagFormatted);

        Assert.Contains("1.0.0-beta.9", releaseFormatted);
        Assert.Contains("Build 9", releaseFormatted);
    }

    [TestMethod]
    public void FormatHeader_ForRelease_HidesCommitAndBranch()
    {
        var identity = CreateTestIdentity("KnownFirst", "1.0.0-beta.9", "9", "Release", isDirty: true);
        var service = new BuildIdentityService(identity);
        
        var header = service.FormatHeader();
        
        Assert.Contains("Commit: not included", header);
        Assert.Contains("Branch: not included", header);
        Assert.Contains("Dirty: not included", header);
    }

    private static BuildIdentity CreateTestIdentity(
        string product,
        string version,
        string buildNumber,
        string configuration,
        bool isDirty)
    {
        return new BuildIdentity(
            product,
            version,
            buildNumber,
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

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
