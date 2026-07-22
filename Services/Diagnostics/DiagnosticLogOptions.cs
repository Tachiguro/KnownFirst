using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Diagnostics;

public sealed class DiagnosticLogOptions
{
    public const long DefaultMaximumFileBytes = 10 * 1024 * 1024;
    public const int DefaultRetainedFileCount = 20;

    public required string DirectoryPath { get; init; }

    public required string ApplicationVersion { get; init; }

    public required string BuildConfiguration { get; init; }

    public required string TargetFramework { get; init; }

    public required string Platform { get; init; }

    public required string OperatingSystemVersion { get; init; }

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    public long MaximumFileBytes { get; init; } = DefaultMaximumFileBytes;

    public int RetainedFileCount { get; init; } = DefaultRetainedFileCount;

    public TimeSpan MaximumAge { get; init; } = TimeSpan.FromDays(21);

    public string FilePrefix { get; init; } = "knownfirst";

    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
}
