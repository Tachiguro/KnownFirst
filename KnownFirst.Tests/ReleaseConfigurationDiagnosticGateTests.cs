using System.Diagnostics;
using System.Xml.Linq;

namespace KnownFirst.Tests;

/// <summary>
/// Complements the static source-contract checks in <see cref="UiWorkflowContractTests"/>
/// (which pin that diagnostic/developer UI is guarded exactly by DEBUG and/or
/// KNOWNFIRST_DIAGNOSTICS, and that Support/Report placeholder controls are unconditionally
/// absent) with real MSBuild property evaluation proving what the actual KnownFirst project
/// configuration resolves those gate symbols to for Release versus Debug/BetaDiagnostic.
/// Mandatory Pre-AAB Gate item 10 (docs/BUILD_AND_RELEASE.md §7).
/// </summary>
[TestClass]
public sealed class ReleaseConfigurationDiagnosticGateTests
{
    private const string WindowsTargetFramework = "net10.0-windows10.0.19041.0";
    private const string DebugSymbol = "DEBUG";
    private const string DiagnosticsSymbol = "KNOWNFIRST_DIAGNOSTICS";

    [TestMethod]
    public void ReleaseConfiguration_DefinesNeitherDebugNorDiagnosticsSymbol()
    {
        var tokens = EvaluateDefineConstants("Release");

        Assert.IsFalse(tokens.Contains(DebugSymbol), "Release must not define DEBUG.");
        Assert.IsFalse(tokens.Contains(DiagnosticsSymbol), "Release must not define KNOWNFIRST_DIAGNOSTICS.");
    }

    [TestMethod]
    public void BetaDiagnosticConfiguration_DefinesDiagnosticsButNotDebugSymbol()
    {
        var tokens = EvaluateDefineConstants("BetaDiagnostic");

        Assert.IsTrue(tokens.Contains(DiagnosticsSymbol), "BetaDiagnostic must define KNOWNFIRST_DIAGNOSTICS.");
        Assert.IsFalse(tokens.Contains(DebugSymbol), "BetaDiagnostic must not define DEBUG.");
    }

    [TestMethod]
    public void DebugConfiguration_DefinesDebugButNotDiagnosticsSymbol()
    {
        var tokens = EvaluateDefineConstants("Debug");

        Assert.IsTrue(tokens.Contains(DebugSymbol), "Debug must define DEBUG.");
        Assert.IsFalse(tokens.Contains(DiagnosticsSymbol), "Debug must not define KNOWNFIRST_DIAGNOSTICS.");
    }

    [TestMethod]
    public void DiagnosticGateSymbols_HaveNoRepoControlledTargetFrameworkSpecificDefinition()
    {
        var root = FindRepositoryRoot();

        AssertNoTargetFrameworkConditionedDefinition(
            XDocument.Load(Path.Combine(root, "KnownFirst.csproj")), "KnownFirst.csproj");
        AssertNoTargetFrameworkConditionedDefinition(
            XDocument.Load(Path.Combine(root, "Directory.Build.props")), "Directory.Build.props");
    }

    private static void AssertNoTargetFrameworkConditionedDefinition(XDocument document, string fileName)
    {
        var offendingGroups = document.Root!
            .Descendants("PropertyGroup")
            .Where(group => group.Element("DefineConstants") is { } defineConstants &&
                (defineConstants.Value.Contains(DebugSymbol, StringComparison.Ordinal) ||
                 defineConstants.Value.Contains(DiagnosticsSymbol, StringComparison.Ordinal)))
            .Where(group =>
            {
                var condition = (string?)group.Attribute("Condition") ?? string.Empty;
                return condition.Contains("TargetFramework", StringComparison.Ordinal) ||
                       condition.Contains("GetTargetPlatformIdentifier", StringComparison.Ordinal) ||
                       condition.Contains("android", StringComparison.OrdinalIgnoreCase) ||
                       condition.Contains("windows", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.AreEqual(0, offendingGroups.Length,
            $"{fileName} must not gate DEBUG or KNOWNFIRST_DIAGNOSTICS behind a TargetFramework- or " +
            "platform-specific condition; the Configuration-level MSBuild evaluation tests would not " +
            "cover such a group.");
    }

    private static HashSet<string> EvaluateDefineConstants(string configuration)
    {
        var root = FindRepositoryRoot();
        var csprojPath = Path.Combine(root, "KnownFirst.csproj");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(csprojPath);
        startInfo.ArgumentList.Add("-getProperty:DefineConstants");
        startInfo.ArgumentList.Add($"-p:Configuration={configuration}");
        startInfo.ArgumentList.Add($"-p:TargetFramework={WindowsTargetFramework}");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the dotnet msbuild evaluation process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        Assert.AreEqual(0, process.ExitCode,
            $"dotnet msbuild -getProperty:DefineConstants (Configuration={configuration}) exited " +
            $"{process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        var propertyLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        Assert.IsFalse(string.IsNullOrEmpty(propertyLine),
            $"dotnet msbuild -getProperty:DefineConstants (Configuration={configuration}) exited 0 but " +
            $"produced no property-result line.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        return propertyLine
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the KnownFirst repository root.");
    }
}
