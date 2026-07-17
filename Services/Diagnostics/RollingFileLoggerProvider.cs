using Microsoft.Extensions.Logging;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KnownFirst.Services.Diagnostics;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const int MaximumCategoryLength = 512;
    private const int MaximumMessageLength = 16 * 1024;
    private const int MaximumPropertyLength = 4 * 1024;
    private const int MaximumExceptionMessageLength = 8 * 1024;
    private const int MaximumStackTraceLength = 64 * 1024;
    private const int MaximumExceptionDepth = 8;

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SensitiveAssignmentPattern = new(
        "(?i)\\b(password|passwd|secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key)\\b\\s*[:=]\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s,;]+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex SensitiveHeaderPattern = new(
        @"(?i)\b(authorization|cookie|set-cookie)\b\s*[:=]\s*[^\r\n]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly object _sync = new();
    private readonly DiagnosticLogOptions _options;
    private StreamWriter? _writer;
    private string? _currentLogFilePath;
    private DateOnly _currentDate;
    private int _currentPart;
    private bool _disposed;

    public RollingFileLoggerProvider(DiagnosticLogOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        SessionId = NormalizeFileSegment(options.SessionId, Guid.NewGuid().ToString("N"));
        DirectoryPath = options.DirectoryPath;

        try
        {
            lock (_sync)
            {
                EnsureWriter(DateTimeOffset.Now);
            }
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }

    public string DirectoryPath { get; }

    public string SessionId { get; }

    public string? CurrentLogFilePath
    {
        get
        {
            lock (_sync)
            {
                return _currentLogFilePath;
            }
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new RollingFileLogger(this, categoryName ?? string.Empty);

    public void Flush()
    {
        try
        {
            lock (_sync)
            {
                _writer?.Flush();
                if (_writer?.BaseStream is FileStream stream)
                {
                    stream.Flush(flushToDisk: true);
                }
            }
        }
        catch
        {
            // A failed flush must not affect application behavior.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // Provider disposal is best effort.
            }
            finally
            {
                _writer = null;
            }
        }
    }

    internal bool IsEnabled(LogLevel logLevel) =>
        !_disposed
        && logLevel != LogLevel.None
        && logLevel >= _options.MinimumLevel;

    internal void Write<TState>(
        string category,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            var timestamp = DateTimeOffset.Now;
            var properties = ExtractProperties(state, out var valuesToRedact);
            var message = formatter(state, exception) ?? string.Empty;
            foreach (var value in valuesToRedact)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    message = message.Replace(value, "[REDACTED]", StringComparison.Ordinal);
                }
            }

            var entry = new DiagnosticLogEntry(
                timestamp,
                logLevel.ToString(),
                SanitizeText(category, MaximumCategoryLength),
                new DiagnosticEvent(eventId.Id, SanitizeText(eventId.Name, MaximumPropertyLength)),
                _options.ApplicationVersion,
                _options.BuildConfiguration,
                _options.TargetFramework,
                _options.Platform,
                _options.OperatingSystemVersion,
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                SessionId,
                SanitizeText(message, MaximumMessageLength),
                properties,
                CreateException(exception, 0));
            var serialized = JsonSerializer.Serialize(entry, SerializerOptions);
            var serializedBytes = Utf8WithoutBom.GetByteCount(serialized) + Environment.NewLine.Length;

            lock (_sync)
            {
                EnsureWriter(timestamp);
                if (_writer is null)
                {
                    return;
                }

                if (_writer.BaseStream.Length > 0
                    && _writer.BaseStream.Length + serializedBytes > EffectiveMaximumFileBytes)
                {
                    Roll(timestamp);
                }

                _writer?.WriteLine(serialized);
                _writer?.Flush();
            }
        }
        catch
        {
            // Logging failures are intentionally isolated from application behavior.
        }
    }

    private long EffectiveMaximumFileBytes => Math.Max(256, _options.MaximumFileBytes);

    private void EnsureWriter(DateTimeOffset timestamp)
    {
        if (_disposed)
        {
            return;
        }

        var date = DateOnly.FromDateTime(timestamp.LocalDateTime);
        if (_writer is not null && date == _currentDate)
        {
            return;
        }

        _writer?.Dispose();
        _writer = null;
        _currentDate = date;
        _currentPart = 0;

        Directory.CreateDirectory(DirectoryPath);
        DeleteExpiredFiles();
        OpenWriter();
    }

    private void Roll(DateTimeOffset timestamp)
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _currentDate = DateOnly.FromDateTime(timestamp.LocalDateTime);
        _currentPart++;
        OpenWriter();
        DeleteExpiredFiles();
    }

    private void OpenWriter()
    {
        var prefix = NormalizeFileSegment(_options.FilePrefix, "knownfirst");
        string path;
        do
        {
            path = Path.Combine(
                DirectoryPath,
                $"{prefix}-{_currentDate:yyyyMMdd}-{SessionId}-{_currentPart:D3}.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length >= EffectiveMaximumFileBytes)
            {
                _currentPart++;
                continue;
            }

            break;
        }
        while (true);

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        _writer = new StreamWriter(stream, Utf8WithoutBom) { AutoFlush = true };
        _currentLogFilePath = path;
    }

    private void DeleteExpiredFiles()
    {
        try
        {
            var prefix = NormalizeFileSegment(_options.FilePrefix, "knownfirst");
            var files = new DirectoryInfo(DirectoryPath)
                .EnumerateFiles($"{prefix}-*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            var cutoff = DateTime.UtcNow - _options.MaximumAge;
            var retainedCount = Math.Max(1, _options.RetainedFileCount);
            var historicalFiles = files
                .Where(file => !string.Equals(
                    file.FullName,
                    _currentLogFilePath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var historicalLimit = _currentLogFilePath is null
                ? retainedCount
                : Math.Max(0, retainedCount - 1);
            for (var index = 0; index < historicalFiles.Length; index++)
            {
                if (index >= historicalLimit || historicalFiles[index].LastWriteTimeUtc < cutoff)
                {
                    TryDelete(historicalFiles[index].FullName);
                }
            }
        }
        catch
        {
            // Retention cleanup is best effort.
        }
    }

    private void TryDelete(string path)
    {
        if (string.Equals(path, _currentLogFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // A locked historical file can be retried during a later cleanup.
        }
    }

    private static IReadOnlyDictionary<string, string?> ExtractProperties<TState>(
        TState state,
        out IReadOnlyList<string> valuesToRedact)
    {
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal);
        var redactions = new List<string>();
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = SanitizeText(pair.Key, MaximumPropertyLength) ?? string.Empty;
                var rawValue = ConvertValue(pair.Value);
                if (IsSensitiveKey(key))
                {
                    if (!string.IsNullOrEmpty(rawValue))
                    {
                        redactions.Add(rawValue);
                    }

                    properties[key] = "[REDACTED]";
                }
                else
                {
                    properties[key] = SanitizeText(rawValue, MaximumPropertyLength);
                }
            }
        }

        valuesToRedact = redactions;
        return properties;
    }

    private static string? ConvertValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable and not byte[])
        {
            return value.GetType().Name;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized is "authorization"
            or "cookie"
            or "setcookie"
            or "password"
            or "secret"
            or "token"
            or "accesstoken"
            or "refreshtoken"
            or "apikey"
            or "content"
            or "text"
            or "rawtext"
            or "importedtext"
            or "documentcontent"
            or "context"
            or "definition"
            or "sentence"
            || normalized.EndsWith("password", StringComparison.Ordinal)
            || normalized.EndsWith("token", StringComparison.Ordinal)
            || normalized.EndsWith("secret", StringComparison.Ordinal);
    }

    private static DiagnosticException? CreateException(Exception? exception, int depth)
    {
        if (exception is null || depth >= MaximumExceptionDepth)
        {
            return null;
        }

        return new DiagnosticException(
            exception.GetType().FullName ?? exception.GetType().Name,
            SanitizeText(exception.Message, MaximumExceptionMessageLength),
            SanitizeText(exception.StackTrace, MaximumStackTraceLength),
            CreateException(exception.InnerException, depth + 1));
    }

    private static string? SanitizeText(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        var sanitized = SensitiveHeaderPattern.Replace(value, "$1=[REDACTED]");
        sanitized = SensitiveAssignmentPattern.Replace(sanitized, "$1=[REDACTED]");
        sanitized = sanitized.Replace('\0', ' ');
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength] + "...[truncated]";
    }

    private static string NormalizeFileSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(64)
            .ToArray());
        return string.IsNullOrEmpty(normalized) ? fallback : normalized;
    }

    private sealed class RollingFileLogger(
        RollingFileLoggerProvider provider,
        string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record DiagnosticLogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string? Category,
        DiagnosticEvent EventId,
        string ApplicationVersion,
        string BuildConfiguration,
        string TargetFramework,
        string Platform,
        string OperatingSystemVersion,
        int ProcessId,
        int ThreadId,
        string SessionId,
        string? Message,
        IReadOnlyDictionary<string, string?> Properties,
        DiagnosticException? Exception);

    private sealed record DiagnosticEvent(int Id, string? Name);

    private sealed record DiagnosticException(
        string Type,
        string? Message,
        string? StackTrace,
        DiagnosticException? InnerException);
}
