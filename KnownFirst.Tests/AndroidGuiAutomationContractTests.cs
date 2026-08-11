using System.Text.Json;

namespace KnownFirst.Tests;

[TestClass]
public sealed class AndroidGuiAutomationContractTests
{
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
    public void AndroidWorkspace_PinsDependenciesAndDefinesTheFullArtifactSchema()
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
        AssertVersion(dependencies, "appium", "3.");
        AssertVersion(dependencies, "appium-uiautomator2-driver", "5.");
        AssertVersion(dependencies, "webdriverio", "9.");

        var packageText = File.ReadAllText(packagePath);
        Assert.DoesNotContain("^", packageText);
        Assert.DoesNotContain("~", packageText);

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

    private static void AssertVersion(JsonElement dependencies, string packageName, string prefix)
    {
        var version = dependencies.GetProperty(packageName).GetString();
        Assert.IsNotNull(version);
        Assert.IsTrue(version.StartsWith(prefix, StringComparison.Ordinal),
            $"{packageName} must be pinned to major {prefix.TrimEnd('.')}.");
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
