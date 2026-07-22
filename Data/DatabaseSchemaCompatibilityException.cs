namespace KnownFirst.Data;

public sealed class DatabaseSchemaCompatibilityException : Exception
{
    public const string StableErrorCode = "unsupported-database-schema";

    public DatabaseSchemaCompatibilityException(int foundVersion, int supportedVersion)
        : base($"{StableErrorCode}: database schema {foundVersion} is newer than supported schema {supportedVersion}.")
    {
        FoundVersion = foundVersion;
        SupportedVersion = supportedVersion;
    }

    public int FoundVersion { get; }

    public int SupportedVersion { get; }

    public string ErrorCode => StableErrorCode;
}
