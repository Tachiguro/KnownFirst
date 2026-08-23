using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace KnownFirst.Tests;

/// <summary>
/// Real MSBuild property evaluation proving what the actual KnownFirst project configuration
/// resolves for the two opt-in Windows distribution packaging variants
/// (<c>KnownFirstWindowsPortablePackaging</c> and <c>KnownFirstWindowsMsixPackaging</c>).
///
/// Two contracts are pinned here because neither can be proven by reading the csproj text alone:
///
/// 1. <b>Store-valid MSIX package version.</b> MAUI's <c>GeneratePackageAppxManifest</c> task
///    replaces the <c>Identity/@Version="0.0.0.0"</c> placeholder with
///    <c>{DisplayVersion[0]}.{DisplayVersion[1]}.{DisplayVersion[2]}.{ApplicationVersion}</c>.
///    The Microsoft Store requires the fourth (revision) section to be 0 and the first section
///    to be non-zero. The unmodified project would produce <c>1.0.0.13</c>, which Partner Center
///    rejects, so the MSIX variant must remap to <c>1.0.&lt;build&gt;.0</c>.
///
/// 2. <b>Output isolation.</b> Neither packaging variant may write into the ordinary
///    <c>bin\</c>/<c>obj\</c> trees, because <c>WindowsBuild</c> and <c>ValidateAll</c> reuse
///    records point at <c>bin\Release\...\win-x64\KnownFirst.dll</c> and only verify that the
///    file exists and is non-empty - a packaging publish that rewrote it in place would leave
///    stale validation evidence looking valid.
///
/// These tests execute no target: they evaluate properties only, so nothing is built, published,
/// packaged, or signed.
/// </summary>
[TestClass]
public sealed class WindowsPackageVersionMappingTests
{
    private const string WindowsTargetFramework = "net10.0-windows10.0.19041.0";
    private const string AndroidTargetFramework = "net10.0-android";

    private const string PortablePackagingProperty = "KnownFirstWindowsPortablePackaging";
    private const string MsixPackagingProperty = "KnownFirstWindowsMsixPackaging";

    private const string ExpectedProductVersion = "1.0.0-beta.13";
    private const string ExpectedBuildNumber = "14";

    /// <summary>The unmodified Windows numeric display version (KnownFirst.csproj).</summary>
    private const string ExpectedDefaultWindowsDisplayVersion = "1.0.0";

    /// <summary>Store-valid four-part package version for the current build number.</summary>
    private const string ExpectedMsixPackageVersion = "1.0.14.0";

    private static readonly string[] QueriedProperties =
    [
        "ApplicationDisplayVersion",
        "ApplicationVersion",
        "KnownFirstProductVersion",
        "KnownFirstBuildNumber",
        "WindowsPackageType",
        "BaseOutputPath",
        "BaseIntermediateOutputPath",
        "RuntimeIdentifier",
        "DefaultItemExcludes",
    ];

    // --- Package version mapping ---------------------------------------------------------

    [TestMethod]
    public void MsixVariant_MapsPackageVersionToStoreValidFourPartVersion()
    {
        var properties = EvaluateWindowsRelease((MsixPackagingProperty, "true"));

        Assert.AreEqual("1.0.14", properties["ApplicationDisplayVersion"],
            "The MSIX variant must place the build number in the third version section.");
        Assert.AreEqual("0", properties["ApplicationVersion"],
            "The MSIX variant must leave the Store-reserved revision section at 0.");

        Assert.AreEqual(ExpectedMsixPackageVersion, ComposeAppxIdentityVersion(properties),
            "MAUI composes Identity/@Version as <display[0]>.<display[1]>.<display[2]>.<ApplicationVersion>.");
    }

    [TestMethod]
    public void MsixVariant_RevisionSectionIsAlwaysZeroAndSectionsAreStoreLegal()
    {
        var properties = EvaluateWindowsRelease((MsixPackagingProperty, "true"));
        var sections = ComposeAppxIdentityVersion(properties).Split('.');

        Assert.AreEqual(4, sections.Length, "A Store package version must have exactly four sections.");
        Assert.AreEqual("0", sections[3],
            "The fourth section is reserved for Microsoft Store use and must be left as 0.");

        foreach (var section in sections)
        {
            Assert.IsTrue(int.TryParse(section, out var value),
                $"Version section '{section}' must be an integer.");
            Assert.IsTrue(value is >= 0 and <= 65535,
                $"Version section '{section}' must be between 0 and 65535.");
        }

        Assert.AreNotEqual(0, int.Parse(sections[0]),
            "The first section of a Store package version cannot be 0.");
    }

    [TestMethod]
    public void MsixVariant_BuildNumberFitsTheStorePackageVersionSectionRange()
    {
        var properties = EvaluateWindowsRelease((MsixPackagingProperty, "true"));

        Assert.IsTrue(int.TryParse(properties["KnownFirstBuildNumber"], out var buildNumber));
        Assert.IsTrue(buildNumber is > 0 and <= 65535,
            "KnownFirstBuildNumber is carried in a Store package version section and must stay within 1..65535.");
    }

    // --- Authoritative runtime identity is never altered by packaging ----------------------

    [TestMethod]
    public void BothPackagingVariants_PreserveAuthoritativeProductAndBuildIdentity()
    {
        foreach (var packagingProperty in new[] { PortablePackagingProperty, MsixPackagingProperty })
        {
            var properties = EvaluateWindowsRelease((packagingProperty, "true"));

            Assert.AreEqual(ExpectedProductVersion, properties["KnownFirstProductVersion"],
                $"{packagingProperty} must not change the authoritative product version.");
            Assert.AreEqual(ExpectedBuildNumber, properties["KnownFirstBuildNumber"],
                $"{packagingProperty} must not change the authoritative build number.");
        }
    }

    [TestMethod]
    public void AuthoritativeIdentityIsCarriedByAssemblyMetadataNotByApplicationVersion()
    {
        // BuildIdentityService prefers the KnownFirstProductVersion/KnownFirstBuildNumber
        // AssemblyMetadata attributes over MAUI's AppInfo. Those metadata items must keep
        // referencing the authoritative properties directly, so that remapping
        // ApplicationDisplayVersion/ApplicationVersion for MSIX packaging provably cannot
        // change the identity string the running application displays.
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "KnownFirst.csproj"));

        var metadata = project.Root!
            .Elements("ItemGroup")
            .Elements("AssemblyMetadata")
            .ToDictionary(
                item => (string)item.Attribute("Include")!,
                item => (string)item.Attribute("Value")!,
                StringComparer.Ordinal);

        Assert.AreEqual("$(KnownFirstProductVersion)", metadata["KnownFirstProductVersion"]);
        Assert.AreEqual("$(KnownFirstBuildNumber)", metadata["KnownFirstBuildNumber"]);
    }

    [TestMethod]
    public void PortableVariant_DoesNotAlterWindowsVersionIdentityAtAll()
    {
        var properties = EvaluateWindowsRelease((PortablePackagingProperty, "true"));

        Assert.AreEqual(ExpectedDefaultWindowsDisplayVersion, properties["ApplicationDisplayVersion"],
            "The portable variant must leave the normal Windows display version untouched.");
        Assert.AreEqual(ExpectedBuildNumber, properties["ApplicationVersion"],
            "The portable variant must leave ApplicationVersion at the authoritative build number.");
        Assert.AreEqual("None", properties["WindowsPackageType"],
            "The portable variant stays unpackaged.");
    }

    // --- The default (non-packaging) configuration is unchanged ---------------------------

    [TestMethod]
    public void DefaultWindowsRelease_RemainsUnpackagedWithUnchangedVersionsAndBinOutput()
    {
        var properties = EvaluateWindowsRelease();

        Assert.AreEqual("None", properties["WindowsPackageType"],
            "An ordinary Windows Release build must remain unpackaged.");
        Assert.AreEqual(ExpectedDefaultWindowsDisplayVersion, properties["ApplicationDisplayVersion"]);
        Assert.AreEqual(ExpectedBuildNumber, properties["ApplicationVersion"]);
        Assert.AreEqual(ExpectedProductVersion, properties["KnownFirstProductVersion"]);
        Assert.AreEqual(ExpectedBuildNumber, properties["KnownFirstBuildNumber"]);

        AssertOrdinaryOutputRoots(properties, "the default Windows Release configuration");
    }

    // --- Output isolation ------------------------------------------------------------------

    [TestMethod]
    public void PortableVariant_UsesDedicatedOutputAndIntermediateRoots()
    {
        var properties = EvaluateWindowsRelease((PortablePackagingProperty, "true"));

        AssertIsolatedRoots(properties, @"artifacts\build\windows-portable\", @"artifacts\obj\windows-portable\");
    }

    [TestMethod]
    public void MsixVariant_UsesDedicatedOutputAndIntermediateRoots()
    {
        var properties = EvaluateWindowsRelease((MsixPackagingProperty, "true"));

        AssertIsolatedRoots(properties, @"artifacts\build\windows-msix\", @"artifacts\obj\windows-msix\");
    }

    [TestMethod]
    public void PackagingVariantOutputRoots_AreDistinctFromEachOther()
    {
        var portable = EvaluateWindowsRelease((PortablePackagingProperty, "true"));
        var msix = EvaluateWindowsRelease((MsixPackagingProperty, "true"));

        Assert.AreNotEqual(portable["BaseOutputPath"], msix["BaseOutputPath"],
            "Portable and MSIX packaging must never share an output root.");
        Assert.AreNotEqual(portable["BaseIntermediateOutputPath"], msix["BaseIntermediateOutputPath"],
            "Portable and MSIX packaging must never share an intermediate root.");
    }

    [TestMethod]
    public void PackagingVariants_ExcludeTheOrdinaryBinAndObjTreesFromDefaultItemGlobs()
    {
        var configurations = new (string Scenario, Dictionary<string, string> Properties)[]
        {
            ("Ordinary Windows Release", EvaluateWindowsRelease()),
            ("Windows Portable packaging variant", EvaluateWindowsRelease((PortablePackagingProperty, "true"))),
            ("Windows MSIX packaging variant", EvaluateWindowsRelease((MsixPackagingProperty, "true"))),
            ("Android GUI-test Debug variant", EvaluateProperties(AndroidTargetFramework, "Debug", ("KnownFirstAndroidGuiTest", "true"))),
        };

        foreach (var (scenario, properties) in configurations)
        {
            var excludes = properties["DefaultItemExcludes"];

            Assert.IsTrue(
                excludes.Contains(@"bin\**", StringComparison.OrdinalIgnoreCase) ||
                excludes.Contains("bin/**", StringComparison.OrdinalIgnoreCase),
                $"{scenario} must exclude ordinary bin\\** from DefaultItemExcludes to avoid re-globbing existing build outputs.");

            Assert.IsTrue(
                excludes.Contains(@"obj\**", StringComparison.OrdinalIgnoreCase) ||
                excludes.Contains("obj/**", StringComparison.OrdinalIgnoreCase),
                $"{scenario} must exclude ordinary obj\\** from DefaultItemExcludes to avoid re-globbing existing build intermediates.");

            Assert.IsTrue(
                excludes.Contains(@"artifacts\**", StringComparison.OrdinalIgnoreCase) ||
                excludes.Contains("artifacts/**", StringComparison.OrdinalIgnoreCase) ||
                excludes.Contains(properties["BaseOutputPath"], StringComparison.OrdinalIgnoreCase),
                $"{scenario} must exclude its output root from DefaultItemExcludes.");
        }
    }

    // --- Android is unaffected --------------------------------------------------------------

    [TestMethod]
    public void PackagingVariants_DoNotAffectAndroidEvaluation()
    {
        foreach (var packagingProperty in new[] { PortablePackagingProperty, MsixPackagingProperty })
        {
            var properties = EvaluateProperties(
                AndroidTargetFramework,
                "Release",
                (packagingProperty, "true"));

            Assert.AreEqual(ExpectedProductVersion, properties["KnownFirstProductVersion"]);
            Assert.AreEqual(ExpectedBuildNumber, properties["KnownFirstBuildNumber"]);
            Assert.AreEqual(ExpectedBuildNumber, properties["ApplicationVersion"],
                $"{packagingProperty} is Windows-only and must not change the Android version code.");
            Assert.AreEqual(ExpectedProductVersion, properties["ApplicationDisplayVersion"],
                $"{packagingProperty} must not change the Android display version.");

            Assert.AreEqual(@"bin\", properties["BaseOutputPath"],
                $"{packagingProperty} is Windows-only and must not redirect Android build output.");

            // BaseIntermediateOutputPath is deliberately NOT TargetFramework-conditioned: an
            // SDK-style project must fix the intermediate root before restore, and a
            // TargetFramework condition would evaluate too late. A Windows packaging property
            // supplied together with the Android target framework therefore still redirects the
            // scratch obj\ root. That is harmless and strictly safer than the alternative - it
            // moves scratch state further away from the ordinary tree, never into it - and no
            // packaging entry point produces that combination: both publishing scripts always
            // pin -f net10.0-windows10.0.19041.0.
            Assert.AreNotEqual(@"obj\", properties["BaseIntermediateOutputPath"],
                $"{packagingProperty} must never share the ordinary obj\\ tree.");
        }
    }

    // --- Nested tool-project isolation ------------------------------------------------------

    /// <summary>
    /// The root KnownFirst application project must never compile C# files belonging to the
    /// nested offline maintenance tool at <c>scripts\tools\GermanLexiconGenerator\</c>, including
    /// files under that project's own <c>obj\</c>/<c>bin\</c> trees. That nested project builds
    /// its own assembly attributes and AssemblyInfo; if the root SDK-style default Compile glob
    /// ever re-absorbs any file under that directory, the shared attributes collide with the
    /// root project's own generated ones (CS0579) as soon as both projects have been built once.
    /// This asserts the real evaluated Compile item set, not just the DefaultItemExcludes text,
    /// so it remains correct regardless of which generated file names the tool project produces.
    /// </summary>
    [TestMethod]
    public void DefaultCompileItems_ExcludeTheEntireNestedGermanLexiconGeneratorProjectTree()
    {
        var generatorRoot = Path.Combine(FindRepositoryRoot(), "scripts", "tools", "GermanLexiconGenerator");

        foreach (var (scenario, configuration) in new[] { ("Windows Debug", "Debug"), ("Windows Release", "Release") })
        {
            var compileItemFullPaths = EvaluateCompileItemFullPaths(WindowsTargetFramework, configuration);

            var offendingItems = compileItemFullPaths
                .Where(fullPath => fullPath.StartsWith(generatorRoot, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.IsEmpty(offendingItems,
                $"{scenario} must not compile any file under the nested GermanLexiconGenerator project " +
                $"tree ({generatorRoot}), including its own obj\\/bin\\ trees. Offending items: " +
                string.Join(", ", offendingItems));
        }
    }

    // --- RuntimeIdentifierOverride bridge ----------------------------------------------------

    [TestMethod]
    public void RuntimeIdentifierOverrideBridge_IsDeclaredExactlyOnceAndIsWindowsOnlyAndOptIn()
    {
        const string expectedCondition =
            "$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows' and '$(RuntimeIdentifierOverride)' != ''";

        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "KnownFirst.csproj"));

        var bridgeGroups = project.Root!
            .Elements("PropertyGroup")
            .Where(group => group.Element("RuntimeIdentifier") is not null)
            .ToArray();

        Assert.AreEqual(1, bridgeGroups.Length,
            "There must be exactly one RuntimeIdentifier bridge PropertyGroup.");
        Assert.AreEqual(expectedCondition, (string?)bridgeGroups[0].Attribute("Condition"),
            "The bridge must be the Microsoft-documented Windows App SDK workaround (WindowsAppSDK issue #3337).");
        Assert.AreEqual("$(RuntimeIdentifierOverride)", (string?)bridgeGroups[0].Element("RuntimeIdentifier"));
    }

    [TestMethod]
    public void RuntimeIdentifierOverride_ResolvesToPortableWindowsRidAndLeavesAndroidAlone()
    {
        var windows = EvaluateProperties(
            WindowsTargetFramework,
            "Release",
            ("RuntimeIdentifierOverride", "win-x64"));

        Assert.AreEqual("win-x64", windows["RuntimeIdentifier"],
            ".NET 10 removed version-specific Windows RIDs; the portable win-x64 RID must be used.");

        var android = EvaluateProperties(
            AndroidTargetFramework,
            "Release",
            ("RuntimeIdentifierOverride", "win-x64"));

        Assert.AreNotEqual("win-x64", android["RuntimeIdentifier"],
            "The Windows RID bridge must never leak into the Android target framework.");
    }

    // --- Structural guards --------------------------------------------------------------------

    [TestMethod]
    public void PackagingVariantPropertyGroups_AreEachDeclaredExactlyOnceInTheProject()
    {
        const string portableCondition =
            "'$(KnownFirstWindowsPortablePackaging)' == 'true' and '$(Configuration)' == 'Release' and $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'";
        const string msixCondition =
            "'$(KnownFirstWindowsMsixPackaging)' == 'true' and '$(Configuration)' == 'Release' and $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'";

        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "KnownFirst.csproj"));
        var groups = project.Root!.Elements("PropertyGroup").ToArray();

        Assert.AreEqual(1, CountWithCondition(groups, portableCondition),
            "The opt-in Windows portable packaging variant must have exactly one PropertyGroup.");
        Assert.AreEqual(1, CountWithCondition(groups, msixCondition),
            "The opt-in Windows MSIX packaging variant must have exactly one PropertyGroup.");
    }

    [TestMethod]
    public void PackagingIntermediateRoots_AreConfiguredEarlyInDirectoryBuildPropsOnly()
    {
        const string portableCondition =
            "'$(MSBuildProjectName)' == 'KnownFirst' and '$(KnownFirstWindowsPortablePackaging)' == 'true' and '$(Configuration)' == 'Release'";
        const string msixCondition =
            "'$(MSBuildProjectName)' == 'KnownFirst' and '$(KnownFirstWindowsMsixPackaging)' == 'true' and '$(Configuration)' == 'Release'";

        var root = FindRepositoryRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var groups = props.Root!.Elements("PropertyGroup")
            .Where(group => group.Element("BaseIntermediateOutputPath") is not null)
            .ToArray();

        var portableGroups = groups
            .Where(group => string.Equals((string?)group.Attribute("Condition"), portableCondition, StringComparison.Ordinal))
            .ToArray();
        var msixGroups = groups
            .Where(group => string.Equals((string?)group.Attribute("Condition"), msixCondition, StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, portableGroups.Length,
            "There must be exactly one early source for the portable packaging BaseIntermediateOutputPath.");
        Assert.AreEqual(1, msixGroups.Length,
            "There must be exactly one early source for the MSIX packaging BaseIntermediateOutputPath.");

        Assert.AreEqual(@"$(MSBuildThisFileDirectory)artifacts\obj\windows-portable\",
            (string?)portableGroups[0].Element("BaseIntermediateOutputPath"));
        Assert.AreEqual(@"$(MSBuildThisFileDirectory)artifacts\obj\windows-msix\",
            (string?)msixGroups[0].Element("BaseIntermediateOutputPath"));

        foreach (var group in portableGroups.Concat(msixGroups))
        {
            Assert.DoesNotContain("TargetFramework", (string?)group.Attribute("Condition") ?? string.Empty,
                "BaseIntermediateOutputPath must be assigned before any target-framework-specific evaluation.");
        }

        // Preserved from the Android GUI-test contract: the main project must never assign
        // BaseIntermediateOutputPath itself, because an SDK-style csproj assigns it too late.
        Assert.DoesNotContain("BaseIntermediateOutputPath", File.ReadAllText(Path.Combine(root, "KnownFirst.csproj")));
    }

    [TestMethod]
    public void PackageAppxManifest_KeepsTheVersionPlaceholderSoMauiCanSubstituteIt()
    {
        var manifestPath = Path.Combine(FindRepositoryRoot(), "Platforms", "Windows", "Package.appxmanifest");
        var manifest = XDocument.Load(manifestPath);

        var identity = manifest.Root!
            .Elements()
            .Single(element => element.Name.LocalName == "Identity");

        Assert.AreEqual("0.0.0.0", (string?)identity.Attribute("Version"),
            "MAUI only substitutes the generated package version when the manifest still carries the 0.0.0.0 placeholder.");
    }

    // --- Helpers --------------------------------------------------------------------------------

    private static int CountWithCondition(IEnumerable<XElement> groups, string condition) =>
        groups.Count(group => string.Equals(
            (string?)group.Attribute("Condition"),
            condition,
            StringComparison.Ordinal));

    private static string ComposeAppxIdentityVersion(IReadOnlyDictionary<string, string> properties)
    {
        var displaySections = properties["ApplicationDisplayVersion"].Split('.');
        Assert.AreEqual(3, displaySections.Length,
            "ApplicationDisplayVersion must supply exactly the first three package-version sections.");

        return $"{displaySections[0]}.{displaySections[1]}.{displaySections[2]}.{properties["ApplicationVersion"]}";
    }

    private static void AssertIsolatedRoots(
        IReadOnlyDictionary<string, string> properties,
        string expectedOutputSuffix,
        string expectedIntermediateSuffix)
    {
        var repositoryRoot = FindRepositoryRoot();
        var outputPath = properties["BaseOutputPath"];
        var intermediatePath = properties["BaseIntermediateOutputPath"];

        Assert.EndsWith(expectedOutputSuffix, outputPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(expectedIntermediateSuffix, intermediatePath, StringComparison.OrdinalIgnoreCase);

        // Both roots must be absolute and live under artifacts\, which is git-ignored and holds
        // no ordinary build output. The point of the isolation is that a packaging publish can
        // never rewrite the bin\/obj\ trees whose files back the WindowsBuild and ValidateAll
        // reusable-result records.
        Assert.StartsWith(Path.Combine(repositoryRoot, "artifacts"), outputPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.Combine(repositoryRoot, "artifacts"), intermediatePath, StringComparison.OrdinalIgnoreCase);

        AssertIsNotAnOrdinaryBuildRoot(properties, "a Windows packaging variant");
    }

    /// <summary>
    /// The ordinary roots are the SDK defaults <c>bin\</c> and <c>obj\</c>, expressed relative to
    /// the project directory. Anything else is a redirect.
    /// </summary>
    private static void AssertIsNotAnOrdinaryBuildRoot(IReadOnlyDictionary<string, string> properties, string scenario)
    {
        Assert.AreNotEqual(@"bin\", properties["BaseOutputPath"],
            $"{scenario} must not build into the ordinary bin\\ tree.");
        Assert.AreNotEqual(@"obj\", properties["BaseIntermediateOutputPath"],
            $"{scenario} must not build into the ordinary obj\\ tree.");
    }

    private static void AssertOrdinaryOutputRoots(IReadOnlyDictionary<string, string> properties, string scenario)
    {
        Assert.AreEqual(@"bin\", properties["BaseOutputPath"],
            $"{scenario} must keep the ordinary bin\\ output root.");
        Assert.AreEqual(@"obj\", properties["BaseIntermediateOutputPath"],
            $"{scenario} must keep the ordinary obj\\ intermediate root.");
    }

    private static Dictionary<string, string> EvaluateWindowsRelease(params (string Name, string Value)[] globalProperties) =>
        EvaluateProperties(WindowsTargetFramework, "Release", globalProperties);

    private static Dictionary<string, string> EvaluateProperties(
        string targetFramework,
        string configuration,
        params (string Name, string Value)[] globalProperties)
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
        foreach (var property in QueriedProperties)
        {
            startInfo.ArgumentList.Add($"-getProperty:{property}");
        }
        startInfo.ArgumentList.Add($"-p:Configuration={configuration}");
        startInfo.ArgumentList.Add($"-p:TargetFramework={targetFramework}");
        foreach (var (name, value) in globalProperties)
        {
            startInfo.ArgumentList.Add($"-p:{name}={value}");
        }
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the dotnet msbuild evaluation process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        var describedProperties = string.Join(" ", globalProperties.Select(p => $"{p.Name}={p.Value}"));
        Assert.AreEqual(0, process.ExitCode,
            $"dotnet msbuild -getProperty (TargetFramework={targetFramework}, Configuration={configuration}, " +
            $"{describedProperties}) exited {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        using var document = JsonDocument.Parse(stdout);
        var evaluated = document.RootElement.GetProperty("Properties");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in QueriedProperties)
        {
            result[property] = evaluated.GetProperty(property).GetString() ?? string.Empty;
        }
        return result;
    }

    private static List<string> EvaluateCompileItemFullPaths(string targetFramework, string configuration)
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
        startInfo.ArgumentList.Add("-getItem:Compile");
        startInfo.ArgumentList.Add($"-p:Configuration={configuration}");
        startInfo.ArgumentList.Add($"-p:TargetFramework={targetFramework}");
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
            $"dotnet msbuild -getItem:Compile (TargetFramework={targetFramework}, Configuration={configuration}) " +
            $"exited {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        using var document = JsonDocument.Parse(stdout);
        var items = document.RootElement.GetProperty("Items").GetProperty("Compile");

        var result = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            result.Add(item.GetProperty("FullPath").GetString() ?? string.Empty);
        }
        return result;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the KnownFirst repository root.");
    }
}
