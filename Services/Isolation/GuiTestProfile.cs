namespace KnownFirst.Services.Isolation;

public static class GuiTestProfile
{
    public const string EnvironmentVariableName = "KNOWNFIRST_GUI_TEST_ROOT";

    private static readonly string RequiredPathSegment =
        Path.Combine("artifacts", "gui-tests", "windows", "profiles") + Path.DirectorySeparatorChar;

    // Deliberately not cached: the environment variable is read fresh on every access so that
    // unit tests can exercise different values within a single process, and so the app always
    // reflects the environment it is actually running in rather than a value captured too early.
    public static bool IsActive => Resolve() is not null;

    public static string RootPath => Resolve()
        ?? throw new InvalidOperationException("The GUI test profile is not active.");

    /// <summary>
    /// Fails closed: an env var that is set but does not resolve under the required
    /// profiles root throws rather than silently falling back to real application data.
    /// </summary>
    public static string? Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
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
