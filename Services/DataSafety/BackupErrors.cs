namespace KnownFirst.Services.DataSafety;

public static class BackupErrorCodes
{
    public const string ArchiveLayoutInvalid = "archive-layout-invalid";
    public const string ArchiveTooLarge = "archive-too-large";
    public const string ArchiveCompressionLimit = "archive-compression-limit";
    public const string UnsupportedFormat = "unsupported-format";
    public const string UnsupportedRequiredFeature = "unsupported-required-feature";
    public const string ManifestInvalid = "manifest-invalid";
    public const string ChecksumMismatch = "checksum-mismatch";
    public const string DataJsonInvalid = "data-json-invalid";
    public const string UnknownEnum = "unknown-enum";
    public const string InvalidArchiveId = "invalid-archive-id";
    public const string LimitExceeded = "limit-exceeded";
    public const string InvalidTimestamp = "invalid-timestamp";
    public const string DuplicateId = "duplicate-id";
    public const string MissingReference = "missing-reference";
    public const string InvariantViolation = "invariant-violation";
    public const string RecordCountMismatch = "record-count-mismatch";
    public const string InsufficientSpace = "insufficient-space";
    public const string SafetyBackupFailed = "safety-backup-failed";
    public const string RestoreFailed = "restore-failed";
    public const string OperationCancelled = "operation-cancelled";
    public const string IoFailure = "io-failure";
}

public sealed class BackupFormatException : Exception
{
    public BackupFormatException(string code)
        : base(ValidateCode(code))
    {
        Code = code;
    }

    public BackupFormatException(string code, Exception innerException)
        : base(ValidateCode(code), innerException)
    {
        Code = code;
    }

    public string Code { get; }

    private static string ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A stable backup error code is required.", nameof(code));
        }

        return code;
    }
}

public sealed record BackupError(string Code);
