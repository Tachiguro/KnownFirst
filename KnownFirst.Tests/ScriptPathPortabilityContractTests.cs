using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

/// <summary>
/// Static and structural contract tests verifying script organization, dead-script removal,
/// and runtime path portability (Package 2).
/// </summary>
[TestClass]
public sealed class ScriptPathPortabilityContractTests
{
    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string LoadFile(string relativePath)
    {
        var path = Path.Combine(GetRepositoryRoot(), relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        return File.ReadAllText(path);
    }

    [TestMethod]
    public void CanonicalLauncher_ExistsAtCanonicalRoot()
    {
        var repoRoot = GetRepositoryRoot();
        var launcherPath = Path.Combine(repoRoot, "scripts", "knownfirst.ps1");
        Assert.IsTrue(File.Exists(launcherPath), "scripts/knownfirst.ps1 must exist as the canonical entry point.");
    }

    [TestMethod]
    public void PackagingScripts_ExistInPackagingDirectory()
    {
        var repoRoot = GetRepositoryRoot();
        var expectedFiles = new[]
        {
            Path.Combine(repoRoot, "scripts", "packaging", "publish-android-test-packages.ps1"),
            Path.Combine(repoRoot, "scripts", "packaging", "publish-google-play-bundle.ps1"),
            Path.Combine(repoRoot, "scripts", "packaging", "publish-windows-portable.ps1"),
            Path.Combine(repoRoot, "scripts", "packaging", "publish-windows-msix.ps1"),
            Path.Combine(repoRoot, "scripts", "packaging", "windows-distribution-common.ps1"),
        };

        foreach (var file in expectedFiles)
        {
            Assert.IsTrue(File.Exists(file), $"Expected packaging script must exist: {file}");
        }
    }

    [TestMethod]
    public void ValidationScripts_ExistInValidationDirectory()
    {
        var repoRoot = GetRepositoryRoot();
        var expectedFiles = new[]
        {
            Path.Combine(repoRoot, "scripts", "validation", "verify-build-configurations.ps1"),
            Path.Combine(repoRoot, "scripts", "validation", "smoke-test-windows.ps1"),
        };

        foreach (var file in expectedFiles)
        {
            Assert.IsTrue(File.Exists(file), $"Expected validation script must exist: {file}");
        }
    }

    [TestMethod]
    public void ToolsScripts_ExistInToolsDirectory()
    {
        var repoRoot = GetRepositoryRoot();
        var toolScript = Path.Combine(repoRoot, "scripts", "tools", "generate_app_icons.py");
        Assert.IsTrue(File.Exists(toolScript), $"Expected tool script must exist: {toolScript}");
    }

    [TestMethod]
    public void ObsoleteScripts_AreRemovedFromRepository()
    {
        var repoRoot = GetRepositoryRoot();
        var obsoleteFiles = new[]
        {
            Path.Combine(repoRoot, "scripts", "publish-android-beta.ps1"),
            Path.Combine(repoRoot, "scripts", "publish-android-google-play.ps1"),
            Path.Combine(repoRoot, "scripts", "verify_icon.py"),
        };

        foreach (var file in obsoleteFiles)
        {
            Assert.IsFalse(File.Exists(file), $"Obsolete script must be deleted: {file}");
        }
    }

    [TestMethod]
    public void Launcher_DispatchesToPackagingScriptsViaRelativePaths()
    {
        var launcher = LoadFile("scripts/knownfirst.ps1");

        Assert.IsTrue(
            launcher.Contains("packaging\\publish-android-test-packages.ps1") || launcher.Contains("packaging/publish-android-test-packages.ps1"),
            "Launcher must dispatch AndroidTestPackage to scripts/packaging/publish-android-test-packages.ps1.");
        Assert.IsTrue(
            launcher.Contains("packaging\\publish-google-play-bundle.ps1") || launcher.Contains("packaging/publish-google-play-bundle.ps1"),
            "Launcher must dispatch GooglePlayBundle to scripts/packaging/publish-google-play-bundle.ps1.");
        Assert.IsTrue(
            launcher.Contains("packaging\\publish-windows-portable.ps1") || launcher.Contains("packaging/publish-windows-portable.ps1"),
            "Launcher must dispatch WindowsPortablePackage to scripts/packaging/publish-windows-portable.ps1.");
        Assert.IsTrue(
            launcher.Contains("packaging\\publish-windows-msix.ps1") || launcher.Contains("packaging/publish-windows-msix.ps1"),
            "Launcher must dispatch WindowsMsixPackage to scripts/packaging/publish-windows-msix.ps1.");
        Assert.IsTrue(
            launcher.Contains("packaging\\windows-distribution-common.ps1") || launcher.Contains("packaging/windows-distribution-common.ps1"),
            "Launcher must reference windows-distribution-common.ps1 in scripts/packaging/.");
    }

    [TestMethod]
    public void MovedScripts_ContainNoHardcodedAbsoluteDevPaths()
    {
        var repoRoot = GetRepositoryRoot();
        var scripts = Directory.GetFiles(Path.Combine(repoRoot, "scripts"), "*.ps1", SearchOption.AllDirectories);

        foreach (var scriptPath in scripts)
        {
            var content = File.ReadAllText(scriptPath);
            Assert.IsFalse(
                content.Contains(@"C:\Dev\KnownFirst", StringComparison.OrdinalIgnoreCase),
                $"Script must not contain hardcoded C:\\Dev\\KnownFirst: {scriptPath}");
        }
    }

    [TestMethod]
    public void WindowsPackagingScripts_DotSourceCommonHelperFromPSScriptRoot()
    {
        var portable = LoadFile("scripts/packaging/publish-windows-portable.ps1");
        var msix = LoadFile("scripts/packaging/publish-windows-msix.ps1");

        Assert.IsTrue(
            Regex.IsMatch(portable, @"\.\s*\(\s*Join-Path\s+\$PSScriptRoot\s+['""]windows-distribution-common\.ps1['""]\s*\)"),
            "publish-windows-portable.ps1 must dot-source windows-distribution-common.ps1 from $PSScriptRoot.");
        Assert.IsTrue(
            Regex.IsMatch(msix, @"\.\s*\(\s*Join-Path\s+\$PSScriptRoot\s+['""]windows-distribution-common\.ps1['""]\s*\)"),
            "publish-windows-msix.ps1 must dot-source windows-distribution-common.ps1 from $PSScriptRoot.");
    }

    [TestMethod]
    public void GenerateAppIcons_DerivesPathsFromScriptLocation()
    {
        var pyContent = LoadFile("scripts/tools/generate_app_icons.py");
        Assert.IsTrue(
            pyContent.Contains("__file__"),
            "generate_app_icons.py must derive repository-relative paths using __file__.");
        Assert.IsFalse(
            pyContent.Contains("src_path = \"branding/knownfirst_picture.png\"") || pyContent.Contains("src_path = 'branding/knownfirst_picture.png'"),
            "generate_app_icons.py must not rely on caller working directory for branding source.");
    }

    [TestMethod]
    public void GuiTestStartupSmoke_ResolvesMovedValidationSmokeScript()
    {
        var smokeHarness = LoadFile("scripts/gui-tests/windows/run-scenario-startup-smoke.ps1");
        Assert.IsTrue(
            smokeHarness.Contains("scripts\\validation\\smoke-test-windows.ps1") ||
            smokeHarness.Contains("scripts/validation/smoke-test-windows.ps1"),
            "run-scenario-startup-smoke.ps1 must resolve scripts/validation/smoke-test-windows.ps1.");
    }
}
