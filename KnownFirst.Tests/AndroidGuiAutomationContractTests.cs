using System.Text.Json;
using System.Xml.Linq;

namespace KnownFirst.Tests;

[TestClass]
public sealed class AndroidGuiAutomationContractTests
{
    [TestMethod]
    public void AndroidGuiTestVariant_IsDeclaredExactlyOnce()
    {
        const string expectedCondition = "'$(KnownFirstAndroidGuiTest)' == 'true' and '$(Configuration)' == 'Debug' and $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'";
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "KnownFirst.csproj"));

        var matchingGroups = project.Root!
            .Elements("PropertyGroup")
            .Where(group => string.Equals(
                (string?)group.Attribute("Condition"),
                expectedCondition,
                StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, matchingGroups.Length,
            "The opt-in Android GUI-test Debug variant must have exactly one PropertyGroup.");
    }

    [TestMethod]
    public void AndroidGuiTestVariant_IsExplicitlyGatedAndKeepsExistingAndroidIdentities()
    {
        var project = ReadRepositoryFile("KnownFirst.csproj");

        Assert.Contains("KnownFirstAndroidGuiTest", project);
        Assert.Contains("com.tachiguro.knownfirst.guitest", project);
        Assert.Contains("KnownFirst GUI Test", project);
        Assert.Contains("KNOWNFIRST_ANDROID_GUI_TEST", project);
        Assert.Contains("BaseOutputPath", project);
        Assert.Contains("BaseIntermediateOutputPath", project);

        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst</ApplicationId>", project);
        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst.debug</ApplicationId>", project);
        Assert.Contains("<ApplicationId>com.tachiguro.knownfirst.diagnostic</ApplicationId>", project);
        Assert.Contains("KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED", project);
    }

    [TestMethod]
    public void AndroidWebViewAutomationAndRenderedSelectors_AreNarrowlyContracted()
    {
        var activity = ReadRepositoryFile("Platforms", "Android", "MainActivity.cs");
        var settings = ReadRepositoryFile("Components", "Pages", "Settings.razor");
        var releaseNotes = ReadRepositoryFile("Components", "Pages", "ReleaseNotes.razor");
        var navigation = ReadRepositoryFile("Components", "Layout", "NavMenu.razor");
        var home = ReadRepositoryFile("Components", "Pages", "Home.razor");
        var indicator = ReadRepositoryFile("Components", "Shared", "GuiTestProfileIndicator.razor");

        Assert.Contains("#if KNOWNFIRST_ANDROID_GUI_TEST", activity);
        Assert.Contains("SetWebContentsDebuggingEnabled", activity);
        Assert.Contains("id=\"settings-release-notes-link\"", settings);
        Assert.Contains("id=\"release-notes-page\"", releaseNotes);
        Assert.Contains("id=\"nav-home\"", navigation);
        Assert.Contains("id=\"nav-settings\"", navigation);
        Assert.Contains("id=\"stat-document-count\"", home);
        Assert.Contains("data-gui-test-profile-id", indicator);
        Assert.Contains("data-gui-test-provider", indicator);
    }

    [TestMethod]
    public void AndroidHarness_RegistersExactlyOneFailClosedPreMatrixScenario()
    {
        var root = FindRepositoryRoot();
        var harnessRoot = Path.Combine(root, "scripts", "gui-tests", "android");
        var scenariosPath = Path.Combine(harnessRoot, "scenarios.json");
        var runnerPath = Path.Combine(harnessRoot, "Invoke-AndroidGuiTest.ps1");
        var clientPath = Path.Combine(harnessRoot, "runner.mjs");

        Assert.IsTrue(File.Exists(scenariosPath), "The P16-A Android scenario registry must exist.");
        Assert.IsTrue(File.Exists(runnerPath), "The P16-A Android PowerShell entry point must exist.");
        Assert.IsTrue(File.Exists(clientPath), "The P16-A Node scenario client must exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(scenariosPath));
        var scenarios = document.RootElement.GetProperty("scenarios");
        Assert.AreEqual(1, scenarios.GetArrayLength());
        var scenario = scenarios[0];
        Assert.AreEqual("P16A-SettingsReleaseNotesNavigation", scenario.GetProperty("id").GetString());
        Assert.AreEqual(JsonValueKind.Null, scenario.GetProperty("matrixMapping").ValueKind);
        Assert.AreEqual("S36", scenario.GetProperty("relatedMatrixRow").GetString());

        var runner = File.ReadAllText(runnerPath);
        var client = File.ReadAllText(clientPath);
        Assert.Contains("com.tachiguro.knownfirst.guitest", runner);
        Assert.Contains("127.0.0.1", runner);
        Assert.Contains("ChromedriverExecutable", runner);
        Assert.Contains("appium:noReset", client);
        Assert.Contains("appium:fullReset", client);
        Assert.DoesNotContain("pm clear", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uninstall", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb ", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm install", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm update", runner, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void AndroidHarness_AppiumReadinessRequiresTheOwnedListenerAndFailsClosedOnCollisionOrEarlyExit()
    {
        var launcher = ReadRepositoryFile("scripts", "gui-tests", "android", "Invoke-AndroidGuiTest.ps1");

        Assert.Contains("Get-Command -Name 'Get-NetTCPConnection'", launcher);
        var listenerHelperStart = launcher.IndexOf("function Get-AppiumPortListeners", StringComparison.Ordinal);
        var listenerHelperEnd = launcher.IndexOf("function Test-OwnedLoopbackListener", StringComparison.Ordinal);
        Assert.IsTrue(listenerHelperStart >= 0 && listenerHelperEnd > listenerHelperStart,
            "The launcher must isolate fail-closed Appium listener inspection in its helper.");
        var listenerHelper = launcher[listenerHelperStart..listenerHelperEnd];
        const string listenerEnumeration = "$listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop)";
        const string requestedPortFilter = "$_.LocalPort -eq $Port";
        Assert.Contains(listenerEnumeration, listenerHelper);
        Assert.DoesNotContain("Get-NetTCPConnection -State Listen -LocalPort", listenerHelper);
        Assert.Contains("return @($listeners | Where-Object", listenerHelper);
        Assert.Contains(requestedPortFilter, listenerHelper);
        Assert.IsTrue(
            listenerHelper.IndexOf(listenerEnumeration, StringComparison.Ordinal) <
            listenerHelper.IndexOf(requestedPortFilter, StringComparison.Ordinal),
            "The requested port must be filtered only after successful listener enumeration.");
        Assert.Contains("catch", listenerHelper);
        Assert.Contains("throw \"Failed to inspect listeners on Appium port $Port.", listenerHelper);
        Assert.Contains("$existingListeners = @(Get-AppiumPortListeners", launcher);
        Assert.Contains("pre-existing listener", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("node_modules\\appium\\index.js", launcher);
        Assert.Contains("Get-Command -Name 'node'", launcher);
        Assert.Contains("Start-Process -FilePath $nodeExecutable", launcher);
        Assert.DoesNotContain("npx.cmd", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$serverProcess.Refresh()", launcher);
        Assert.Contains("$serverProcess.HasExited", launcher);
        Assert.Contains("$listeners = @(Get-AppiumPortListeners", launcher);
        Assert.Contains("OwningProcess", launcher);
        Assert.Contains("$serverProcess.Id", launcher);
        Assert.Contains("foreign listener", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke-RestMethod", launcher);
        Assert.Contains("$verifiedListeners = @(Get-AppiumPortListeners", launcher);
        Assert.Contains("Stop-Process -Id $serverProcess.Id", launcher);
        Assert.Contains("--address", launcher);
        Assert.Contains("127.0.0.1", launcher);

        Assert.DoesNotContain("dotnet build", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("msbuild", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pm clear", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uninstall", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb ", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm install", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm update", launcher, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void AndroidGuiTestRenderedIdentity_ExposesFullCommitAndDirtyState()
    {
        var indicator = ReadRepositoryFile("Components", "Shared", "GuiTestProfileIndicator.razor");
        var runner = ReadRepositoryFile("scripts", "gui-tests", "android", "runner.mjs");
        var evidence = ReadRepositoryFile("scripts", "gui-tests", "android", "lib", "evidence.mjs");

        Assert.Contains("IBuildIdentityService", indicator);
        Assert.Contains("BuildIdentityService.Identity", indicator);
        Assert.Contains("CommitHash", indicator);
        Assert.Contains("IsDirty", indicator);
        Assert.Contains("Version", indicator);
        Assert.Contains("BuildNumber", indicator);
        Assert.Contains("Configuration", indicator);
        Assert.Contains("PackageId", indicator);
        foreach (var attribute in new[]
        {
            "data-build-commit", "data-build-dirty", "data-build-version", "data-build-number",
            "data-build-configuration", "data-build-package-id"
        })
        {
            Assert.Contains(attribute, indicator);
            Assert.Contains($"getAttribute('{attribute}')", runner);
        }

        Assert.Contains("#gui-test-profile-indicator", runner);
        Assert.Contains("evaluateRuntimeBuildIdentity", runner);
        Assert.Contains("buildIdentity = evaluateRuntimeBuildIdentity", runner);
        Assert.IsTrue(
            runner.IndexOf("buildIdentity = evaluateRuntimeBuildIdentity", StringComparison.Ordinal) <
            runner.IndexOf("scenarioState = await runSettingsReleaseNotesNavigation", StringComparison.Ordinal),
            "Runtime build identity must be verified before the navigation scenario executes.");
        Assert.Contains("if (!buildIdentity.matched)", runner);

        Assert.Contains("expected commit", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observed commit", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("40 hexadecimal", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not match", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dirty", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("com.tachiguro.knownfirst.guitest", evidence);
        Assert.Contains("Debug", evidence);
        Assert.Contains("input.buildIdentity?.matched !== true", evidence);
    }

    [TestMethod]
    public void AndroidWorkspace_PinsExactDirectDependencyAndLockfileVersionsAndDefinesArtifactSchema()
    {
        var root = FindRepositoryRoot();
        var harnessRoot = Path.Combine(root, "scripts", "gui-tests", "android");
        var packagePath = Path.Combine(harnessRoot, "package.json");
        var lockPath = Path.Combine(harnessRoot, "package-lock.json");
        var evidencePath = Path.Combine(harnessRoot, "lib", "evidence.mjs");

        Assert.IsTrue(File.Exists(packagePath));
        Assert.IsTrue(File.Exists(lockPath));
        Assert.IsTrue(File.Exists(evidencePath));

        using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
        var dependencies = package.RootElement.GetProperty("devDependencies");
        AssertExactVersion(dependencies, "appium", "3.6.0");
        AssertExactVersion(dependencies, "appium-uiautomator2-driver", "5.0.7");
        AssertExactVersion(dependencies, "webdriverio", "9.30.1");

        var packageText = File.ReadAllText(packagePath);
        Assert.DoesNotContain("^", packageText);
        Assert.DoesNotContain("~", packageText);

        using var lockfile = JsonDocument.Parse(File.ReadAllText(lockPath));
        var packages = lockfile.RootElement.GetProperty("packages");
        var rootDependencies = packages.GetProperty("").GetProperty("devDependencies");
        AssertExactVersion(rootDependencies, "appium", "3.6.0");
        AssertExactVersion(rootDependencies, "appium-uiautomator2-driver", "5.0.7");
        AssertExactVersion(rootDependencies, "webdriverio", "9.30.1");
        Assert.AreEqual("3.6.0", packages.GetProperty("node_modules/appium").GetProperty("version").GetString());
        Assert.AreEqual("5.0.7", packages.GetProperty("node_modules/appium-uiautomator2-driver").GetProperty("version").GetString());
        Assert.AreEqual("9.30.1", packages.GetProperty("node_modules/webdriverio").GetProperty("version").GetString());

        var evidence = File.ReadAllText(evidencePath);
        foreach (var requiredField in new[]
        {
            "scenarioId", "matrixMapping", "failedStep", "git", "buildIdentity", "packageId",
            "configuration", "toolVersions", "device", "physicalOrEmulator", "orientation",
            "screenshotPixels", "density", "dpViewport", "language", "theme", "contexts",
            "profileId", "safetyBefore", "safetyAfter", "screenshots", "sha256", "timestamps",
            "assertionCounts", "buildPerformed", "installationPerformed", "dataResetPerformed",
            "liveNetworkUsed", "remainingUnproven"
        })
        {
            Assert.Contains(requiredField, evidence);
        }
    }

    private static void AssertExactVersion(JsonElement dependencies, string packageName, string expected)
    {
        var version = dependencies.GetProperty(packageName).GetString();
        Assert.AreEqual(expected, version, $"{packageName} must be pinned to the approved exact version.");
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

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
