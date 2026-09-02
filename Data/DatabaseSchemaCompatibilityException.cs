namespace KnownFirst.Data;

public enum DatabaseSchemaCompatibilityReason
{
    UnsupportedOlderVersion,
    UnsupportedFutureVersion,
    UnknownNonEmptyUnversionedDatabase,
    InvalidCurrentSchema
}

public sealed class DatabaseSchemaCompatibilityException : Exception
{
    public const string StableErrorCode = "unsupported-database-schema";

    public DatabaseSchemaCompatibilityException(int foundVersion, int supportedVersion)
        : this(
            foundVersion,
            supportedVersion,
            DatabaseSchemaCompatibilityReason.UnsupportedFutureVersion,
            diagnosticDetail: null)
    {
    }

    public DatabaseSchemaCompatibilityException(
        int foundVersion,
        int supportedVersion,
        DatabaseSchemaCompatibilityReason reason,
        string? diagnosticDetail = null)
        : base(BuildMessage(foundVersion, supportedVersion, reason, diagnosticDetail))
    {
        FoundVersion = foundVersion;
        SupportedVersion = supportedVersion;
        Reason = reason;
        DiagnosticDetail = diagnosticDetail;
    }

    public int FoundVersion { get; }

    public int SupportedVersion { get; }

    public DatabaseSchemaCompatibilityReason Reason { get; }

    public string? DiagnosticDetail { get; }

    public string ErrorCode => StableErrorCode;

    private static string BuildMessage(
        int foundVersion,
        int supportedVersion,
        DatabaseSchemaCompatibilityReason reason,
        string? diagnosticDetail)
    {
        var classification = reason switch
        {
            DatabaseSchemaCompatibilityReason.UnsupportedOlderVersion =>
                $"database schema {foundVersion} is older than supported schema {supportedVersion}",
            DatabaseSchemaCompatibilityReason.UnsupportedFutureVersion =>
                $"database schema {foundVersion} is newer than supported schema {supportedVersion}",
            DatabaseSchemaCompatibilityReason.UnknownNonEmptyUnversionedDatabase =>
                "database is unversioned but contains non-internal SQLite objects",
            DatabaseSchemaCompatibilityReason.InvalidCurrentSchema =>
                $"database claims current schema {supportedVersion} but failed runtime integrity validation",
            _ => "database schema is unsupported"
        };

        return string.IsNullOrWhiteSpace(diagnosticDetail)
            ? $"{StableErrorCode}: {classification}."
            : $"{StableErrorCode}: {classification}: {diagnosticDetail}";
    }
}
