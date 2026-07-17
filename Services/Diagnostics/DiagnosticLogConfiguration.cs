using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Diagnostics;

internal static class DiagnosticLogConfiguration
{
    public static DiagnosticLogOptions Create()
    {
        var appVersion = TryGetApplicationVersion();
        var platform = TryGetPlatform();
        var logDirectory = ResolveLogDirectory();

        return new DiagnosticLogOptions
        {
            DirectoryPath = logDirectory,
            ApplicationVersion = appVersion,
            BuildConfiguration = BuildConfiguration,
            TargetFramework = TargetFramework,
            Platform = platform,
            OperatingSystemVersion = Environment.OSVersion.VersionString,
            MinimumLevel = MinimumLevel,
            MaximumFileBytes = DiagnosticLogOptions.DefaultMaximumFileBytes,
            RetainedFileCount = DiagnosticLogOptions.DefaultRetainedFileCount,
            MaximumAge = TimeSpan.FromDays(21)
        };
    }

    private static string BuildConfiguration
    {
        get
        {
#if KNOWNFIRST_DIAGNOSTICS
            return "BetaDiagnostic";
#elif DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }
    }

    private static LogLevel MinimumLevel
    {
        get
        {
#if DEBUG || KNOWNFIRST_DIAGNOSTICS
            return LogLevel.Trace;
#else
            return LogLevel.Information;
#endif
        }
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
#if WINDOWS
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KnownFirst",
            "Logs");
#else
        return Path.Combine(FileSystem.AppDataDirectory, "Logs");
#endif
    }

    private static string TryGetApplicationVersion()
    {
        try
        {
            return AppInfo.Current.VersionString;
        }
        catch
        {
            return typeof(MauiProgram).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }

    private static string TryGetPlatform()
    {
        try
        {
            return DeviceInfo.Current.Platform.ToString();
        }
        catch
        {
            return Environment.OSVersion.Platform.ToString();
        }
    }
}
