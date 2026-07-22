using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public sealed record LexicalDiagnosticEvent(
    string Phase,
    string NormalizedTerm,
    string SourceLanguage,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    string Provider,
    string CacheOutcome = "-",
    string HttpOutcome = "-",
    string ParserOutcome = "-");

public interface ILexicalDiagnosticLog
{
    void Write(LexicalDiagnosticEvent diagnosticEvent, Exception? exception = null);

    string ReadReport();

    string ExportPath { get; }

    void Clear();
}

public sealed class NullLexicalDiagnosticLog : ILexicalDiagnosticLog
{
    public static NullLexicalDiagnosticLog Instance { get; } = new();

    private NullLexicalDiagnosticLog()
    {
    }

    public string ExportPath => string.Empty;

    public void Write(LexicalDiagnosticEvent diagnosticEvent, Exception? exception = null)
    {
    }

    public string ReadReport() => string.Empty;

    public void Clear()
    {
    }
}
