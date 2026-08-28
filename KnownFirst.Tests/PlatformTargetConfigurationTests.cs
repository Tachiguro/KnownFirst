using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PlatformTargetConfigurationTests
{
    private static string ProjectRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(PlatformTargetConfigurationTests).Assembly.Location)!,
        "../../../../"));

    [TestMethod]
    public void MainProject_ExposesOnlyAndroidAndWindowsTargets()
    {
        var project = XDocument.Load(Path.Combine(ProjectRoot, "KnownFirst.csproj"));
        var targetFrameworks = project.Descendants("TargetFrameworks")
            .Single(element => element.Attribute("Condition") is null)
            .Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        CollectionAssert.AreEquivalent(
            new[] { "net10.0-android", "net10.0-windows10.0.19041.0" },
            targetFrameworks);
        Assert.IsFalse(targetFrameworks.Any(framework =>
            framework.Contains("ios", StringComparison.OrdinalIgnoreCase) ||
            framework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ApplePlatformDirectoriesAreRemovedAndSolutionContainsExpectedProjects()
    {
        Assert.IsFalse(Directory.Exists(Path.Combine(ProjectRoot, "Platforms", "iOS")));
        Assert.IsFalse(Directory.Exists(Path.Combine(ProjectRoot, "Platforms", "MacCatalyst")));

        var solution = XDocument.Load(Path.Combine(ProjectRoot, "KnownFirst.slnx"));
        var projectPaths = solution.Descendants("Project")
            .Select(element =>
            {
                var path = (string?)element.Attribute("Path");
                Assert.IsNotNull(path, "Project element in KnownFirst.slnx must have a Path attribute.");
                return path.Replace('\\', '/');
            })
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "KnownFirst.csproj",
                "KnownFirst.Core/KnownFirst.Core.csproj",
                "KnownFirst.Application/KnownFirst.Application.csproj",
                "KnownFirst.Tests/KnownFirst.Tests.csproj"
            },
            projectPaths);

        Assert.IsTrue(File.Exists(Path.Combine(ProjectRoot, "Platforms", "Android", "AndroidManifest.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(ProjectRoot, "Platforms", "Windows", "Package.appxmanifest")));
    }

    [TestMethod]
    public void ProjectIdentityAndAndroidAotTrimmingSettingsRemainStable()
    {
        var project = File.ReadAllText(Path.Combine(ProjectRoot, "KnownFirst.csproj"));

        StringAssert.Contains(project, "<ApplicationId>com.tachiguro.knownfirst</ApplicationId>");
        StringAssert.Contains(project, "<KnownFirstBuildNumber>15</KnownFirstBuildNumber>");
        StringAssert.Contains(project, "<ApplicationVersion>$(KnownFirstBuildNumber)</ApplicationVersion>");
        StringAssert.Contains(project, "<PublishTrimmed>true</PublishTrimmed>");
        StringAssert.Contains(project, "<RunAOTCompilation>true</RunAOTCompilation>");
        StringAssert.Contains(project, "<SupportedOSPlatformVersion Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'\">24.0</SupportedOSPlatformVersion>");
        Assert.IsFalse(project.Contains("net10.0-ios", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(project.Contains("net10.0-maccatalyst", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void MainProject_ExcludesNestedSiblingProjectDirectoriesFromDefaultItems()
    {
        var project = XDocument.Load(Path.Combine(ProjectRoot, "KnownFirst.csproj"));
        var defaultItemExcludesElement = project.Descendants("DefaultItemExcludes")
            .FirstOrDefault(element => element.Attribute("Condition") is null);

        Assert.IsNotNull(defaultItemExcludesElement, "KnownFirst.csproj must declare an unconditional DefaultItemExcludes element.");

        var excludes = defaultItemExcludesElement.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pattern => pattern.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedExcludedDirectories = new[]
        {
            "KnownFirst.Core/**",
            "KnownFirst.Application/**",
            "KnownFirst.Tests/**"
        };

        foreach (var expected in expectedExcludedDirectories)
        {
            Assert.IsTrue(
                excludes.Contains(expected),
                $"DefaultItemExcludes in KnownFirst.csproj must contain '{expected}' to prevent nested sibling project source and generated items from being absorbed into the root project.");
        }
    }
}
