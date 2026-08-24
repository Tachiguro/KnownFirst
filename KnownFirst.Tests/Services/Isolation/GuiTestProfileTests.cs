using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Isolation;

// GuiTestProfile reads a process-wide environment variable, so these tests must not run
// concurrently with each other (or with anything else that touches the same variable).
[TestClass]
[DoNotParallelize]
public class GuiTestProfileTests
{
    private string? _previousValue;
    private string? _createdDirectory;

    [TestInitialize]
    public void Setup()
    {
        _previousValue = Environment.GetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, null);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, _previousValue);
        GuiTestProfile.SupportedOverrideForTests = null;
        if (_createdDirectory is not null && Directory.Exists(_createdDirectory))
        {
            Directory.Delete(_createdDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableNotSet()
    {
        Assert.IsNull(GuiTestProfile.Resolve());
        Assert.IsFalse(GuiTestProfile.IsActive);
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenEnvironmentVariableIsWhitespace()
    {
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, "   ");

        Assert.IsNull(GuiTestProfile.Resolve());
        Assert.IsFalse(GuiTestProfile.IsActive);
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenPathDoesNotResolveUnderRequiredProfilesRoot()
    {
        var untrustedPath = Path.Combine(Path.GetTempPath(), "kf-gui-test-untrusted-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, untrustedPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
        Assert.IsFalse(Directory.Exists(untrustedPath), "A rejected path must never be created on disk.");
    }

    [TestMethod]
    public void Resolve_FailsClosed_ForRealApplicationDataDirectory()
    {
        // Guards specifically against the dangerous case: someone accidentally pointing the
        // isolation variable at the real per-user AppData root instead of a profiles directory.
        var realLookingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "com.tachiguro.knownfirst",
            "Data");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, realLookingPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
    }

    [TestMethod]
    public void Resolve_ReturnsPathAndCreatesDirectory_WhenValid()
    {
        var validPath = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles",
            "run-001");
        _createdDirectory = validPath;
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validPath);

        var resolved = GuiTestProfile.Resolve();

        Assert.IsNotNull(resolved);
        Assert.IsTrue(Directory.Exists(resolved));
        Assert.IsTrue(GuiTestProfile.IsActive);
        Assert.AreEqual(resolved, GuiTestProfile.RootPath);
    }

    [TestMethod]
    public void Resolve_ActivatesInSupportedBuild_WhenPathIsValid()
    {
        // Explicit supported-build activation, independent of whatever the test binary's
        // compiled default happens to be.
        GuiTestProfile.SupportedOverrideForTests = true;
        var validPath = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles",
            "run-supported");
        _createdDirectory = validPath;
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validPath);

        var resolved = GuiTestProfile.Resolve();

        Assert.IsNotNull(resolved);
        Assert.IsTrue(Directory.Exists(resolved));
    }

    [TestMethod]
    public void Resolve_Throws_WhenPathIsRelative()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var relativePath = Path.Combine("artifacts", "gui-tests", "windows", "profiles", "run-relative");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, relativePath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
        Assert.IsFalse(
            Directory.Exists(Path.GetFullPath(relativePath)),
            "A relative path must never be created on disk.");
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenBuildIsUnsupported_EvenWithAnOtherwiseValidPath()
    {
        GuiTestProfile.SupportedOverrideForTests = false;
        var validPath = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles",
            "run-unsupported");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
        Assert.IsFalse(
            Directory.Exists(validPath),
            "An unsupported build must never create the requested profile directory.");
    }

    [TestMethod]
    public void Resolve_ReturnsNull_WhenBuildIsUnsupportedAndEnvironmentVariableIsAbsent()
    {
        GuiTestProfile.SupportedOverrideForTests = false;

        Assert.IsNull(GuiTestProfile.Resolve());
        Assert.IsFalse(GuiTestProfile.IsActive);
    }

    [TestMethod]
    public void RootPath_Throws_WhenNotActive()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.RootPath);
    }

    [TestMethod]
    public void AndroidGuiTestProfile_UsesBoundedUniquePrivateProfilesAndNeverUsesTheWindowsEnvironmentVariable()
    {
        var configure = typeof(GuiTestProfile).GetMethod(
            "ConfigureAndroidGuiTestProfileForCurrentProcess",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var reset = typeof(GuiTestProfile).GetMethod(
            "ResetAndroidGuiTestProfileForTests",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var supportedOverride = typeof(GuiTestProfile).GetProperty(
            "AndroidGuiTestSupportedOverrideForTests",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(configure,
            "P16-A requires an internal Android bootstrap seam rather than arbitrary environment-selected paths.");
        Assert.IsNotNull(reset);
        Assert.IsNotNull(supportedOverride);
        if (configure is null || reset is null || supportedOverride is null)
        {
            return;
        }

        var appDataDirectory = Path.Combine(Path.GetTempPath(), "kf-android-gui-test-" + Guid.NewGuid().ToString("N"));
        _createdDirectory = appDataDirectory;
        try
        {
            supportedOverride.SetValue(null, true);
            Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, "C:\\untrusted\\profile");

            configure.Invoke(null, [appDataDirectory]);
            var first = GuiTestProfile.RootPath;
            Assert.StartsWith(Path.Combine(appDataDirectory, "gui-tests", "android", "profiles"), first, StringComparison.OrdinalIgnoreCase);
            Assert.IsTrue(Path.IsPathFullyQualified(first));
            Assert.IsTrue(Directory.Exists(first));
            Assert.IsFalse(string.IsNullOrWhiteSpace(GuiTestProfile.ProfileId));
            Assert.AreEqual(first, GuiTestProfile.RootPath, "The profile root must be stable within one process.");

            reset.Invoke(null, null);
            configure.Invoke(null, [appDataDirectory]);
            Assert.AreNotEqual(first, GuiTestProfile.RootPath, "A simulated new process must receive a new profile.");
        }
        finally
        {
            reset.Invoke(null, null);
            supportedOverride.SetValue(null, null);
        }
    }

    [TestMethod]
    public void AndroidGuiTestProfile_BootstrapIncludesDeterministicEnglishLightAndSeenReleaseNotesDefaults()
    {
        var profileSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Services", "Isolation", "GuiTestProfile.cs"));

        Assert.Contains("knownfirst.uiLanguage", profileSource);
        Assert.Contains("theme_preference", profileSource);
        Assert.Contains("whats_new_seen_version", profileSource);
        Assert.Contains("onboarding_state", profileSource);
        Assert.Contains("gui-tests", profileSource);
        Assert.Contains("android", profileSource);
    }

    [TestMethod]
    public void GuiTestProfile_InitializesDeterministicCompletedOnboardingState()
    {
        var preferences = new IsolatedFilePreferences(
            Path.Combine(Path.GetTempPath(), "kf-gui-prefs-" + Guid.NewGuid().ToString("N")));

        GuiTestProfile.SupportedOverrideForTests = true;
        var validPath = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles",
            "run-seeded");
        _createdDirectory = validPath;
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, validPath);

        Assert.IsTrue(GuiTestProfile.IsActive);
        GuiTestProfile.InitializeGuiTestPreferences(preferences, "1.0.0");

        Assert.AreEqual("en", preferences.Get("knownfirst.uiLanguage", (string?)null));
        Assert.AreEqual(1, preferences.Get("theme_preference", -1));
        Assert.AreEqual("1.0.0", preferences.Get("whats_new_seen_version", (string?)null));
        Assert.AreEqual((int)KnownFirst.Core.Settings.OnboardingState.Completed, preferences.Get("onboarding_state", -1));
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenPathIsProfilesRootItself()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var rootOnlyPath = Path.Combine(
            Path.GetTempPath(),
            "kf-gui-test-" + Guid.NewGuid().ToString("N"),
            "artifacts",
            "gui-tests",
            "windows",
            "profiles");
        _createdDirectory = rootOnlyPath;
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, rootOnlyPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
    }

    [TestMethod]
    public void Resolve_FailsClosed_WhenPathResolvesUnderRealLocalApplicationDataRoot()
    {
        GuiTestProfile.SupportedOverrideForTests = true;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var subPath = Path.Combine(localAppData, "KnownFirst", "artifacts", "gui-tests", "windows", "profiles", "run-001");
        Environment.SetEnvironmentVariable(GuiTestProfile.EnvironmentVariableName, subPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => GuiTestProfile.Resolve());
    }

    [TestMethod]
    public void ShowsProfileIndicator_ReturnsFalse_WhenOverriddenOrRelease()
    {
        try
        {
            GuiTestProfile.ShowsProfileIndicatorOverrideForTests = false;
            Assert.IsFalse(GuiTestProfile.ShowsProfileIndicator);
        }
        finally
        {
            GuiTestProfile.ShowsProfileIndicatorOverrideForTests = null;
        }
    }

    [TestMethod]
    public void ShowsProfileIndicator_ReturnsTrue_WhenOverriddenOrDebug()
    {
        try
        {
            GuiTestProfile.ShowsProfileIndicatorOverrideForTests = true;
            Assert.IsTrue(GuiTestProfile.ShowsProfileIndicator);
        }
        finally
        {
            GuiTestProfile.ShowsProfileIndicatorOverrideForTests = null;
        }
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
