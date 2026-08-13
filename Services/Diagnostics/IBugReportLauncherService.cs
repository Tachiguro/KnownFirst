namespace KnownFirst.Services.Diagnostics;

public interface IBugReportLauncherService
{
    string RecipientEmail { get; }

    Task<bool> LaunchBugReportAsync(CancellationToken cancellationToken = default);
}
