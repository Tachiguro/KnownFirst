namespace KnownFirst.Services.Diagnostics;

public interface IAppDiagnosticsService
{
    string LogDirectory { get; }

    string SessionId { get; }

    AppDiagnosticsSummary GetSummary();

    void Flush();
}

public sealed record AppDiagnosticsSummary(
    string LogDirectory,
    string SessionId,
    string? CurrentLogFile,
    int RetainedFileCount,
    long RetainedBytes);

public sealed class AppDiagnosticsService(RollingFileLoggerProvider provider) : IAppDiagnosticsService
{
    public string LogDirectory => provider.DirectoryPath;

    public string SessionId => provider.SessionId;

    public AppDiagnosticsSummary GetSummary()
    {
        try
        {
            var files = Directory.Exists(LogDirectory)
                ? new DirectoryInfo(LogDirectory)
                    .EnumerateFiles("knownfirst-*.jsonl", SearchOption.TopDirectoryOnly)
                    .ToArray()
                : [];
            return new AppDiagnosticsSummary(
                LogDirectory,
                SessionId,
                provider.CurrentLogFilePath,
                files.Length,
                files.Sum(file => file.Length));
        }
        catch
        {
            return new AppDiagnosticsSummary(
                LogDirectory,
                SessionId,
                provider.CurrentLogFilePath,
                0,
                0);
        }
    }

    public void Flush() => provider.Flush();
}

public static class DiagnosticEventIds
{
    public static readonly Microsoft.Extensions.Logging.EventId StartupBeginning = new(1000, nameof(StartupBeginning));
    public static readonly Microsoft.Extensions.Logging.EventId StartupCompleted = new(1001, nameof(StartupCompleted));
    public static readonly Microsoft.Extensions.Logging.EventId StartupFailed = new(1002, nameof(StartupFailed));
    public static readonly Microsoft.Extensions.Logging.EventId Shutdown = new(1010, nameof(Shutdown));
    public static readonly Microsoft.Extensions.Logging.EventId UnhandledException = new(9000, nameof(UnhandledException));
    public static readonly Microsoft.Extensions.Logging.EventId UnobservedTaskException = new(9001, nameof(UnobservedTaskException));
    public static readonly Microsoft.Extensions.Logging.EventId BlazorUnhandledException = new(9002, nameof(BlazorUnhandledException));
    public static readonly Microsoft.Extensions.Logging.EventId WinUiUnhandledException = new(9003, nameof(WinUiUnhandledException));
}
