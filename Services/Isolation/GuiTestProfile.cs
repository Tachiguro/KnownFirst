namespace KnownFirst.Services.Isolation;

public static class GuiTestProfile
{
    public const string EnvironmentVariableName = "KNOWNFIRST_GUI_TEST_ROOT";

    private static readonly string RequiredPathSegment =
        Path.Combine("artifacts", "gui-tests", "windows", "profiles") + Path.DirectorySeparatorChar;

#if WINDOWS
    private const bool CompiledForSupportedBuild = true;
#elif KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED
    private const bool CompiledForSupportedBuild = true;
#else
    private const bool CompiledForSupportedBuild = false;
#endif

#if KNOWNFIRST_ANDROID_GUI_TEST
    private const bool CompiledForAndroidGuiTest = true;
#else
    private const bool CompiledForAndroidGuiTest = false;
#endif

    private static string? _androidGuiTestRoot;

    // Test-only seam: KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED is a compile-time symbol (defined only
    // for Windows Debug/BetaDiagnostic in KnownFirst.csproj), so a single test binary cannot flip it
    // between builds. This lets tests simulate the unsupported-build path without a second build.
    // Production code never sets this.
    internal static bool? SupportedOverrideForTests { get; set; }

    // The test assembly exercises this path without compiling a second Android target. Production
    // code never sets the override, and normal Android builds compile the support flag as false.
    internal static bool? AndroidGuiTestSupportedOverrideForTests { get; set; }

    internal static bool? ShowsProfileIndicatorOverrideForTests { get; set; }

    public static bool ShowsProfileIndicator => ShowsProfileIndicatorOverrideForTests ??
#if DEBUG || KNOWNFIRST_DIAGNOSTICS || KNOWNFIRST_ANDROID_GUI_TEST
        true;
#else
        false;
#endif

    private static bool IsSupportedBuild => SupportedOverrideForTests ?? CompiledForSupportedBuild;
    private static bool IsAndroidGuiTestBuild => AndroidGuiTestSupportedOverrideForTests ?? CompiledForAndroidGuiTest;

    // Deliberately not cached: the environment variable is read fresh on every access so that
    // unit tests can exercise different values within a single process, and so the app always
    // reflects the environment it is actually running in rather than a value captured too early.
    public static bool IsActive => Resolve() is not null;

    public static string RootPath => Resolve()
        ?? throw new InvalidOperationException("The GUI test profile is not active.");

    public static bool IsAndroidGuiTestProfile => _androidGuiTestRoot is not null;

    public static string? ProfileId => _androidGuiTestRoot is null
        ? null
        : Path.GetFileName(_androidGuiTestRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// Activates the Android GUI-test profile from the app's own private root only. There is no
    /// Android environment-variable path input: callers cannot redirect this profile to arbitrary
    /// storage or to a real KnownFirst installation.
    /// </summary>
    internal static void ConfigureAndroidGuiTestProfileForCurrentProcess(string appDataDirectory)
    {
        if (!IsAndroidGuiTestBuild)
        {
            throw new InvalidOperationException(
                "Android GUI-test profile support is not compiled for this build. Failing closed instead of risking real application data.");
        }

        if (string.IsNullOrWhiteSpace(appDataDirectory) || !Path.IsPathFullyQualified(appDataDirectory))
        {
            throw new InvalidOperationException(
                "The Android GUI-test bootstrap did not provide an absolute application-private root. Failing closed instead of risking real application data.");
        }

        if (_androidGuiTestRoot is not null)
        {
            return;
        }

        var privateRoot = Path.GetFullPath(appDataDirectory);
        var runId = Guid.NewGuid().ToString("N");
        var profileRoot = Path.Combine(privateRoot, "gui-tests", "android", "profiles", runId);
        Directory.CreateDirectory(profileRoot);
        _androidGuiTestRoot = profileRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Initializes only test-owned preferences before normal application services consume them.
    /// The current build version is marked seen so the one-time What's New modal cannot make the
    /// rendered navigation scenario depend on first-launch state.
    /// </summary>
    internal static void InitializeAndroidGuiTestPreferences(Microsoft.Maui.Storage.IPreferences preferences, string buildVersion)
    {
        InitializeGuiTestPreferences(preferences, buildVersion);
    }

    internal static void InitializeGuiTestPreferences(Microsoft.Maui.Storage.IPreferences preferences, string buildVersion)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!IsActive)
        {
            return;
        }

        preferences.Set("knownfirst.uiLanguage", "en");
        preferences.Set("theme_preference", 1);
        preferences.Set("whats_new_seen_version", buildVersion);
        preferences.Set("onboarding_state", (int)KnownFirst.Core.Settings.OnboardingState.Completed);
    }

    internal static void ResetAndroidGuiTestProfileForTests()
    {
        _androidGuiTestRoot = null;
    }

    /// <summary>
    /// Fails closed: an env var that is set but names an unsupported build/platform, a relative
    /// path, or a path outside the required profiles root throws rather than silently falling
    /// back to (or activating over) real application data.
    /// </summary>
    public static string? Resolve()
    {
        if (_androidGuiTestRoot is not null)
        {
            if (!IsAndroidGuiTestBuild)
            {
                throw new InvalidOperationException(
                    "An Android GUI-test profile was configured for an unsupported build. Failing closed instead of risking real application data.");
            }

            return _androidGuiTestRoot;
        }

        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!IsSupportedBuild)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set, but GUI test profiles are only supported in " +
                "Windows builds. Failing closed instead of risking real application data on an unsupported build or platform.");
        }

        if (!Path.IsPathFullyQualified(raw))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set to '{raw}', which is not an absolute path. " +
                "Failing closed instead of risking real application data.");
        }

        var fullPath = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            var realKnownFirstData = Path.Combine(localAppData, "KnownFirst") + Path.DirectorySeparatorChar;
            var realPackageData = Path.Combine(localAppData, "com.tachiguro.knownfirst") + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(realKnownFirstData, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(realPackageData, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{EnvironmentVariableName} was set to '{raw}', which resolves under the real KnownFirst application data root. " +
                    "Failing closed instead of risking real application data.");
            }
        }

        var segmentIndex = fullPath.IndexOf(RequiredPathSegment, StringComparison.OrdinalIgnoreCase);
        if (segmentIndex < 0)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set to '{raw}', which does not resolve under a " +
                $"'{RequiredPathSegment}' directory. Failing closed instead of risking real application data.");
        }

        var childSegment = fullPath.Substring(segmentIndex + RequiredPathSegment.Length)
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(childSegment))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set to the profiles root directory itself. " +
                "Failing closed instead of risking real application data.");
        }

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
