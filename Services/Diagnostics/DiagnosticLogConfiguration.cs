using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Diagnostics;

internal static class DiagnosticLogConfiguration
{
    public static DiagnosticLogOptions Create(IBuildIdentityService identity)
    {
        var logDirectory = ResolveLogDirectory();
        var isRelease = identity.Identity.Configuration == "Release";

        var minimumLevel = isRelease ? LogLevel.Warning : LogLevel.Trace;
        var retainedFileCount = isRelease ? 3 : 20;
        var maximumFileBytes = isRelease ? 1 * 1024 * 1024L : 10 * 1024 * 1024L;
        var maximumAge = isRelease ? TimeSpan.FromDays(7) : TimeSpan.FromDays(21);

        return new DiagnosticLogOptions
        {
            DirectoryPath = logDirectory,
            ApplicationVersion = identity.Identity.Version,
            BuildConfiguration = identity.Identity.Configuration,
            TargetFramework = TargetFramework,
            Platform = identity.Identity.OS,
            OperatingSystemVersion = identity.Identity.OSVersion,
            MinimumLevel = minimumLevel,
            MaximumFileBytes = maximumFileBytes,
            RetainedFileCount = retainedFileCount,
            MaximumAge = maximumAge,
            SessionId = identity.Identity.SessionId
        };
    }

    private static string TargetFramework
    {
        get
        {
#if ANDROID
            return "net10.0-android";
#elif IOS
            return "net10.0-ios";
#elif MACCATALYST
            return "net10.0-maccatalyst";
#elif WINDOWS
            return "net10.0-windows10.0.19041.0";
#else
            return "net10.0";
#endif
        }
    }

    private static string ResolveLogDirectory()
    {
#if IOS || MACCATALYST || ANDROID
        return Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Logs");
#else
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KnownFirst",
            "Logs");
#endif
    }
}
