using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Diagnostics;

public sealed class RuntimeExceptionMonitor(
    ILogger<RuntimeExceptionMonitor> logger,
    IAppDiagnosticsService diagnostics) : IDisposable
{
    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        try
        {
            var exception = eventArgs.ExceptionObject as Exception
                ?? new InvalidOperationException("An unhandled non-Exception object reached the application domain.");
            logger.LogCritical(
                DiagnosticEventIds.UnhandledException,
                exception,
                "An unhandled application-domain exception occurred. Terminating = {IsTerminating}",
                eventArgs.IsTerminating);
            diagnostics.Flush();
        }
        catch
        {
            // Fatal diagnostics are best effort and must not mask the original failure.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        try
        {
            logger.LogError(
                DiagnosticEventIds.UnobservedTaskException,
                eventArgs.Exception,
                "An unobserved task exception occurred.");
            diagnostics.Flush();
        }
        catch
        {
            // Runtime diagnostics must never throw into the finalizer thread.
        }
    }
}
