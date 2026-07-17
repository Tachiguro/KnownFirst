using KnownFirst.Services.Lexical;
using System.Text;

namespace KnownFirst.Services;

public sealed class LexicalDiagnosticLog : ILexicalDiagnosticLog
{
    public const long MaximumLogBytes = 2 * 1024 * 1024;

    private const int MaximumFieldLength = 256;
    private const int MaximumExceptionLength = 8192;
    private const int MaximumStackFrames = 32;
    private const string LogFileName = "knownfirst-lexical-diagnostics.log";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly object _sync = new();

    public string ExportPath
    {
        get
        {
            lock (_sync)
            {
                EnsureLogFile();
                return LogPath;
            }
        }
    }

    private static string LogPath => Path.Combine(FileSystem.AppDataDirectory, LogFileName);

    private static string BuildType
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

    public void Write(LexicalDiagnosticEvent diagnosticEvent, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        try
        {
            var line = FormatEvent(diagnosticEvent, exception);
            lock (_sync)
            {
                EnsureLogFile();
                File.AppendAllText(LogPath, line + Environment.NewLine, Utf8WithoutBom);
                TrimIfRequired();
#if DEBUG || KNOWNFIRST_DIAGNOSTICS
#if ANDROID
                Android.Util.Log.Info("KnownFirst.Lookup", line);
#endif
#endif
            }
        }
        catch
        {
            // Diagnostics must never affect dictionary lookup behavior.
        }
    }

    public string ReadReport()
    {
        lock (_sync)
        {
            EnsureLogFile();
            var builder = new StringBuilder();
            builder.AppendLine("KnownFirst lexical lookup diagnostic report");
            builder.Append("GeneratedUtc=").AppendLine(DateTime.UtcNow.ToString("O"));
            builder.Append("BuildType=").AppendLine(BuildType);
            builder.Append("AppVersion=").AppendLine(Sanitize(AppInfo.Current.VersionString, MaximumFieldLength));
            builder.AppendLine("Content=lookup metadata only; documents, contexts, definitions, credentials, and HTTP headers are excluded");
            builder.AppendLine();
            builder.Append(File.ReadAllText(LogPath, Utf8WithoutBom));
            return builder.ToString();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
            }
        }
    }

    private static string FormatEvent(LexicalDiagnosticEvent diagnosticEvent, Exception? exception)
    {
        var builder = new StringBuilder(1024);
        AppendField(builder, "timestampUtc", DateTime.UtcNow.ToString("O"));
        AppendField(builder, "buildType", BuildType);
        AppendField(builder, "appVersion", AppInfo.Current.VersionString);
        AppendField(builder, "phase", diagnosticEvent.Phase);
        AppendField(builder, "term", diagnosticEvent.NormalizedTerm);
        AppendField(builder, "source", diagnosticEvent.SourceLanguage);
        AppendField(builder, "mode", diagnosticEvent.LookupMode.ToString());
        AppendField(builder, "target", diagnosticEvent.TargetLanguage ?? "-");
        AppendField(builder, "provider", diagnosticEvent.Provider);
        AppendField(builder, "cache", diagnosticEvent.CacheOutcome);
        AppendField(builder, "http", diagnosticEvent.HttpOutcome);
        AppendField(builder, "parser", diagnosticEvent.ParserOutcome);
        if (exception is not null)
        {
            AppendField(builder, "exception", FormatException(exception), MaximumExceptionLength);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendField(
        StringBuilder builder,
        string name,
        string value,
        int maximumLength = MaximumFieldLength)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(name).Append('=').Append(Sanitize(value, maximumLength));
    }

    private static string FormatException(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;
        var frameCount = 0;
        var depth = 0;
        while (current is not null && depth < 8)
        {
            if (depth > 0)
            {
                builder.Append(" | inner: ");
            }

            builder.Append(current.GetType().FullName)
                .Append(": ")
                .Append(current.Message);
            if (!string.IsNullOrWhiteSpace(current.StackTrace) && frameCount < MaximumStackFrames)
            {
                foreach (var rawFrame in current.StackTrace.Split('\n'))
                {
                    if (frameCount >= MaximumStackFrames)
                    {
                        break;
                    }

                    var frame = rawFrame.Trim();
                    var sourcePathStart = frame.IndexOf(" in ", StringComparison.Ordinal);
                    if (sourcePathStart >= 0)
                    {
                        frame = frame[..sourcePathStart];
                    }

                    if (frame.Length > 0)
                    {
                        builder.Append(" > ").Append(frame);
                        frameCount++;
                    }
                }
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (var character in value.Trim())
        {
            if (builder.Length >= maximumLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) || char.IsWhiteSpace(character) ? ' ' : character);
        }

        var sanitized = builder.ToString();
        foreach (var sensitiveName in new[] { "authorization", "cookie", "password", "secret", "token=" })
        {
            if (sanitized.Contains(sensitiveName, StringComparison.OrdinalIgnoreCase))
            {
                return "[redacted]";
            }
        }

        return sanitized;
    }

    private static void EnsureLogFile()
    {
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        if (!File.Exists(LogPath))
        {
            File.WriteAllText(LogPath, string.Empty, Utf8WithoutBom);
        }
    }

    private static void TrimIfRequired()
    {
        var file = new FileInfo(LogPath);
        if (file.Length <= MaximumLogBytes)
        {
            return;
        }

        var bytes = File.ReadAllBytes(LogPath);
        var start = Math.Max(0, bytes.Length - (int)(MaximumLogBytes / 2));
        while (start < bytes.Length && bytes[start] != (byte)'\n')
        {
            start++;
        }

        if (start < bytes.Length)
        {
            start++;
        }

        var retained = bytes[start..];
        var temporaryPath = LogPath + ".tmp";
        File.WriteAllBytes(temporaryPath, retained);
        File.Move(temporaryPath, LogPath, true);
    }
}
