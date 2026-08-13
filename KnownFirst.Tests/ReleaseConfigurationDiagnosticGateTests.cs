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

    [TestMethod]
    public void AssertNoTargetFrameworkConditionedDefinition_RejectsElementLevelCondition()
    {
        var doc = XDocument.Parse("""
            <Project>
                <PropertyGroup>
                    <DefineConstants Condition="'$(TargetFramework)' == 'net10.0-android'">KNOWNFIRST_DIAGNOSTICS</DefineConstants>
                </PropertyGroup>
            </Project>
            """);

        var ex = Assert.ThrowsExactly<AssertFailedException>(() =>
            AssertNoTargetFrameworkConditionedDefinition(doc, "synthetic.csproj"));
        Assert.IsTrue(ex.Message.Contains("synthetic.csproj"), "Exception message must report the input file name.");
    }

    [TestMethod]
    public void AssertNoTargetFrameworkConditionedDefinition_InspectsAllDefineConstantsElements()
    {
        var doc = XDocument.Parse("""
            <Project>
                <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0-android'">
                    <DefineConstants>TRACE</DefineConstants>
                    <DefineConstants>$(DefineConstants);KNOWNFIRST_DIAGNOSTICS</DefineConstants>
                </PropertyGroup>
            </Project>
            """);

        var ex = Assert.ThrowsExactly<AssertFailedException>(() =>
            AssertNoTargetFrameworkConditionedDefinition(doc, "synthetic.csproj"));
        Assert.IsTrue(ex.Message.Contains("synthetic.csproj"), "Exception message must report the input file name.");
    }

    [TestMethod]
    public void AssertNoTargetFrameworkConditionedDefinition_RejectsWhenAncestorCondition()
    {
        var doc = XDocument.Parse("""
            <Project>
                <Choose>
                    <When Condition="'$(TargetFramework)' == 'net10.0-android'">
                        <PropertyGroup>
                            <DefineConstants>KNOWNFIRST_DIAGNOSTICS</DefineConstants>
                        </PropertyGroup>
                    </When>
                </Choose>
            </Project>
            """);

        var ex = Assert.ThrowsExactly<AssertFailedException>(() =>
            AssertNoTargetFrameworkConditionedDefinition(doc, "synthetic.csproj"));
        Assert.IsTrue(ex.Message.Contains("synthetic.csproj"), "Exception message must report the input file name.");
    }

    [TestMethod]
    public void AssertNoTargetFrameworkConditionedDefinition_RejectsUnconditionalTargetDefinition()
    {
        var doc = XDocument.Parse("""
            <Project>
                <Target Name="AddDiagnostics" BeforeTargets="CoreCompile">
                    <PropertyGroup>
                        <DefineConstants>$(DefineConstants);KNOWNFIRST_DIAGNOSTICS</DefineConstants>
                    </PropertyGroup>
                </Target>
            </Project>
            """);

        var ex = Assert.ThrowsExactly<AssertFailedException>(() =>
            AssertNoTargetFrameworkConditionedDefinition(doc, "synthetic.csproj"));
        Assert.IsTrue(ex.Message.Contains("synthetic.csproj"), "Exception message must report the input file name.");
    }

    [TestMethod]
    public void AssertNoTargetFrameworkConditionedDefinition_RejectsReleaseConditionedTargetDefinition()
    {
        var doc = XDocument.Parse("""
            <Project>
                <Target Name="AddDiagnostics" BeforeTargets="CoreCompile" Condition="'$(Configuration)' == 'Release'">
                    <PropertyGroup>
                        <DefineConstants>$(DefineConstants);KNOWNFIRST_DIAGNOSTICS</DefineConstants>
                    </PropertyGroup>
                </Target>
            </Project>
            """);

        var ex = Assert.ThrowsExactly<AssertFailedException>(() =>
            AssertNoTargetFrameworkConditionedDefinition(doc, "synthetic.csproj"));
        Assert.IsTrue(ex.Message.Contains("synthetic.csproj"), "Exception message must report the input file name.");
    }

    /// <summary>
    /// Characterization: the repository's real diagnostic-symbol definition is an evaluation-time
    /// Configuration-conditioned PropertyGroup, which the Configuration-level evaluation tests above
    /// do cover. The structural backstop must keep allowing that shape.
    /// </summary>
    [TestMethod]
    public void AssertNoTargetFrameworkConditionedDefinition_AllowsEvaluationTimeBetaDiagnosticDefinition()
    {
        var doc = XDocument.Parse("""
            <Project>
                <PropertyGroup Condition="'$(Configuration)' == 'BetaDiagnostic'">
                    <DefineConstants>$(DefineConstants);KNOWNFIRST_DIAGNOSTICS</DefineConstants>
                </PropertyGroup>
            </Project>
            """);

        AssertNoTargetFrameworkConditionedDefinition(doc, "synthetic.csproj");
    }

    private static void AssertNoTargetFrameworkConditionedDefinition(XDocument document, string fileName)
    {
        var offendingElements = document.Root!
            .Descendants("DefineConstants")
            .Where(element =>
            {
                var tokens = element.Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return tokens.Contains(DebugSymbol, StringComparer.Ordinal) ||
                       tokens.Contains(DiagnosticsSymbol, StringComparer.Ordinal);
            })
            .Where(element =>
            {
                // A PropertyGroup nested in a Target is an execution-time property mutation: it lands
                // in the property bag while targets run, so it can still reach CoreCompile and be
                // handed to Csc as DefineConstants. The evaluation tests above execute no target at
                // all, which makes any such assignment invisible to them no matter what its Condition
                // or scheduling metadata says. Target-nested gate-symbol definitions are therefore
                // forbidden outright rather than analyzed: deciding which targets really run before
                // compilation would require reconstructing BeforeTargets/AfterTargets/DependsOnTargets
                // across imported SDK targets, and every partial reconstruction fails open.
                if (element.Ancestors("Target").Any())
                {
                    return true;
                }

                return element.AncestorsAndSelf().Any(ancestor =>
                {
                    var condition = (string?)ancestor.Attribute("Condition") ?? string.Empty;
                    return condition.Contains("TargetFramework", StringComparison.Ordinal) ||
                           condition.Contains("GetTargetPlatformIdentifier", StringComparison.Ordinal) ||
                           condition.Contains("android", StringComparison.OrdinalIgnoreCase) ||
                           condition.Contains("windows", StringComparison.OrdinalIgnoreCase);
                });
            })
            .ToArray();

        Assert.AreEqual(0, offendingElements.Length,
            $"{fileName} must define DEBUG and KNOWNFIRST_DIAGNOSTICS at evaluation time only, and must " +
            "not gate them behind a TargetFramework- or platform-specific condition: the " +
            "Configuration-level MSBuild evaluation tests execute no target and pin a single Windows " +
            "TargetFramework, so neither a Target-nested nor a platform-conditioned definition would " +
            "be covered.");
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
