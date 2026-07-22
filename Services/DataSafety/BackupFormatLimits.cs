namespace KnownFirst.Services.DataSafety;

public static class BackupFormatLimits
{
    public const int FormatVersion = 1;

    public const int RequiredZipEntryCount = 2;
    public const long MaxArchiveBytes = 128L * 1024 * 1024;
    public const int MaxManifestUncompressedBytes = 256 * 1024;
    public const int MaxDataUncompressedBytes = 256 * 1024 * 1024;
    public const long MaxTotalUncompressedBytes =
        (long)MaxManifestUncompressedBytes + MaxDataUncompressedBytes;
    public const double MaxCompressionRatio = 100d;
    public const int CompressionRatioMinimumEntryBytes = 1024 * 1024;

    public const int MaxJsonDepth = 64;
    public const int MaxDocumentOrContextUtf8Bytes = 16 * 1024 * 1024;
    public const int MaxStringUtf8Bytes = 1024 * 1024;
    public const int MaxArchiveIdUtf8Bytes = 256;
    public const int MaxFeatureIdentifierUtf8Bytes = 128;
    public const int MaxFeatureCount = 64;

    public const int MaxSourceMaterials = 10_000;
    public const int MaxVocabularyItems = 250_000;
    public const int MaxOccurrences = 1_000_000;
    public const int MaxOtherCountedRecords = 1_000_000;
    public const int MaxArrayItems = 1_000_000;
}
