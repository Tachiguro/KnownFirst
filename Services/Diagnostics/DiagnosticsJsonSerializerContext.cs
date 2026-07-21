using KnownFirst.Models;
using System.Text.Json.Serialization;

namespace KnownFirst.Services.Diagnostics;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiagnosticLogEntry))]
[JsonSerializable(typeof(ReviewDiagnosticsSnapshot))]
internal sealed partial class DiagnosticsJsonSerializerContext : JsonSerializerContext
{
}

internal sealed record DiagnosticLogEntry(
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

internal sealed record DiagnosticEvent(int Id, string? Name);

internal sealed record DiagnosticException(
    string Type,
    string? Message,
    string? StackTrace,
    DiagnosticException? InnerException);
