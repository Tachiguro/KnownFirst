namespace KnownFirst.Services.Isolation;

public static class GuiTestProfile
{
    public const string EnvironmentVariableName = "KNOWNFIRST_GUI_TEST_ROOT";

    private static readonly string RequiredPathSegment =
        Path.Combine("artifacts", "gui-tests", "windows", "profiles") + Path.DirectorySeparatorChar;

#if KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED
    private const bool CompiledForSupportedBuild = true;
#else
    private const bool CompiledForSupportedBuild = false;
#endif

    // Test-only seam: KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED is a compile-time symbol (defined only
    // for Windows Debug/BetaDiagnostic in KnownFirst.csproj), so a single test binary cannot flip it
    // between builds. This lets tests simulate the unsupported-build path without a second build.
    // Production code never sets this.
    internal static bool? SupportedOverrideForTests { get; set; }

    private static bool IsSupportedBuild => SupportedOverrideForTests ?? CompiledForSupportedBuild;

    // Deliberately not cached: the environment variable is read fresh on every access so that
    // unit tests can exercise different values within a single process, and so the app always
    // reflects the environment it is actually running in rather than a value captured too early.
    public static bool IsActive => Resolve() is not null;

    public static string RootPath => Resolve()
        ?? throw new InvalidOperationException("The GUI test profile is not active.");

    /// <summary>
    /// Fails closed: an env var that is set but names an unsupported build/platform, a relative
    /// path, or a path outside the required profiles root throws rather than silently falling
    /// back to (or activating over) real application data.
    /// </summary>
    public static string? Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!IsSupportedBuild)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set, but GUI test profiles are only supported in " +
                "Windows Debug and Windows BetaDiagnostic builds. Failing closed instead of risking " +
                "real application data on an unsupported build or platform.");
        }

        if (!Path.IsPathFullyQualified(raw))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set to '{raw}', which is not an absolute path. " +
                "Failing closed instead of risking real application data.");
        }

        var fullPath = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.Contains(RequiredPathSegment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set to '{raw}', which does not resolve under a " +
                $"'{RequiredPathSegment}' directory. Failing closed instead of risking real application data.");
        }

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
