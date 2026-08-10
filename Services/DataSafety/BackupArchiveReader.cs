using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public sealed record ValidatedBackupArchive(
    BackupManifest Manifest,
    BackupPayload Payload);

/// <summary>Archive format v2 counterpart of <see cref="ValidatedBackupArchive"/>.</summary>
public sealed record ValidatedBackupArchiveV2(
    BackupManifestV2 Manifest,
    BackupPayloadV2 Payload);

/// <summary>Discriminated result of <see cref="BackupArchiveReader.ValidateVersionedAsync"/> — exactly
/// one of <see cref="V1"/>/<see cref="V2"/> is populated, selected by <see cref="FormatVersion"/>.</summary>
public sealed record ValidatedBackupArchiveEnvelope(
    int FormatVersion,
    ValidatedBackupArchive? V1,
    ValidatedBackupArchiveV2? V2);

public static class BackupArchiveReader
{
    /// <summary>
    /// Version-aware entry point (KF-MEANING-001 Slice 2): reads and validates the manifest envelope,
    /// rejects an unsupported format before interpreting the data payload, verifies checksum and JSON
    /// structural safety, deserializes with the correct version-specific source-generated JSON
    /// contract, validates version-specific model and graph invariants, and returns a clearly versioned
    /// validated result. <see cref="ValidateAsync"/> (v1-only) is kept unchanged alongside this method
    /// for exact v1-only callers/tests — it is not implemented in terms of this method, to avoid any
    /// risk of changing its existing, proven behavior.
    /// </summary>
    public static async Task<ValidatedBackupArchiveEnvelope> ValidateVersionedAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);

        Stream archiveStream = sourceStream;
        MemoryStream? bufferedStream = null;
        try
        {
            if (sourceStream.CanSeek)
            {
                if (sourceStream.Length > BackupFormatLimits.MaxArchiveBytes)
                {
                    throw new BackupFormatException(BackupErrorCodes.ArchiveTooLarge);
                }

                sourceStream.Position = 0;
            }
            else
            {
                bufferedStream = new MemoryStream();
                await CopyBoundedAsync(
                    sourceStream,
                    bufferedStream,
                    BackupFormatLimits.MaxArchiveBytes,
                    BackupErrorCodes.ArchiveTooLarge,
                    cancellationToken);
                bufferedStream.Position = 0;
                archiveStream = bufferedStream;
            }

            using var archive = OpenArchive(archiveStream);
            ValidateEntries(archive);

            var manifestEntry = archive.GetEntry("manifest.json")!;
            var dataEntry = archive.GetEntry("data.json")!;
            var manifestBytes = await ReadEntryAsync(
                manifestEntry,
                BackupFormatLimits.MaxManifestUncompressedBytes,
                cancellationToken);

            // Structural safety (duplicate-property rejection) must run before the manifest's
            // formatVersion field is ever interpreted.
            ValidateNoDuplicateProperties(manifestBytes, BackupErrorCodes.ManifestInvalid);

            // Peek formatVersion via the source-generated (never reflection-based) envelope, and
            // reject an unsupported format before touching data.json at all.
            var formatVersion = BackupJsonCodecV2.PeekFormatVersion(manifestBytes);
            if (formatVersion < BackupFormatLimits.MinimumSupportedFormatVersion
                || formatVersion > BackupFormatLimits.CurrentArchiveFormatVersion)
            {
                throw new BackupFormatException(BackupErrorCodes.UnsupportedFormat);
            }

            if (formatVersion == BackupFormatLimits.FormatVersion)
            {
                var manifest = BackupJsonCodec.DeserializeManifest(manifestBytes);
                if (manifest.RequiredFeatures.Count != 0 || manifest.OptionalFeatures.Count != 0)
                {
                    throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
                }

                var dataBytes = await ReadEntryAsync(dataEntry, BackupFormatLimits.MaxDataUncompressedBytes, cancellationToken);
                VerifyChecksum(manifest.DataChecksum, dataBytes);
                ValidateNoDuplicateProperties(dataBytes, BackupErrorCodes.DataJsonInvalid);
                var payload = BackupJsonCodec.DeserializeData(dataBytes);
                if (payload.Extensions.Features.Count != 0)
                {
                    throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
                }

                BackupModelContract.ValidateRecordCounts(manifest, payload);
                BackupArchiveWriter.ValidatePayloadGraph(payload);
                ValidatePortableRecoveryScope(payload);

                return new ValidatedBackupArchiveEnvelope(
                    formatVersion,
                    new ValidatedBackupArchive(manifest, payload),
                    null);
            }
            else
            {
                var manifest = BackupJsonCodecV2.DeserializeManifest(manifestBytes);
                if (manifest.RequiredFeatures.Count != 0 || manifest.OptionalFeatures.Count != 0)
                {
                    throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
                }

                var dataBytes = await ReadEntryAsync(dataEntry, BackupFormatLimits.MaxDataUncompressedBytes, cancellationToken);
                VerifyChecksum(manifest.DataChecksum, dataBytes);
                ValidateNoDuplicateProperties(dataBytes, BackupErrorCodes.DataJsonInvalid);
                var payload = BackupJsonCodecV2.DeserializeData(dataBytes);
                if (payload.Extensions.Features.Count != 0)
                {
                    throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
                }

                BackupModelContractV2.ValidateRecordCounts(manifest, payload);
                BackupArchiveWriterV2.ValidatePayloadGraphV2(payload);
                ValidatePortableRecoveryScopeV2(manifest, payload);

                return new ValidatedBackupArchiveEnvelope(
                    formatVersion,
                    null,
                    new ValidatedBackupArchiveV2(manifest, payload));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackupFormatException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.ArchiveLayoutInvalid, exception);
        }
        catch (IOException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.IoFailure, exception);
        }
        finally
        {
            bufferedStream?.Dispose();
        }
    }


    public static async Task<ValidatedBackupArchive> ValidateAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);

        Stream archiveStream = sourceStream;
        MemoryStream? bufferedStream = null;
        try
        {
            if (sourceStream.CanSeek)
            {
                if (sourceStream.Length > BackupFormatLimits.MaxArchiveBytes)
                {
                    throw new BackupFormatException(BackupErrorCodes.ArchiveTooLarge);
                }

                sourceStream.Position = 0;
            }
            else
            {
                bufferedStream = new MemoryStream();
                await CopyBoundedAsync(
                    sourceStream,
                    bufferedStream,
                    BackupFormatLimits.MaxArchiveBytes,
                    BackupErrorCodes.ArchiveTooLarge,
                    cancellationToken);
                bufferedStream.Position = 0;
                archiveStream = bufferedStream;
            }

            using var archive = OpenArchive(archiveStream);
            ValidateEntries(archive);

            var manifestEntry = archive.GetEntry("manifest.json")!;
            var dataEntry = archive.GetEntry("data.json")!;
            var manifestBytes = await ReadEntryAsync(
                manifestEntry,
                BackupFormatLimits.MaxManifestUncompressedBytes,
                cancellationToken);
            ValidateNoDuplicateProperties(manifestBytes, BackupErrorCodes.ManifestInvalid);
            var manifest = BackupJsonCodec.DeserializeManifest(manifestBytes);
            if (manifest.RequiredFeatures.Count != 0
                || manifest.OptionalFeatures.Count != 0)
            {
                throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
            }

            var dataBytes = await ReadEntryAsync(
                dataEntry,
                BackupFormatLimits.MaxDataUncompressedBytes,
                cancellationToken);
            VerifyChecksum(manifest.DataChecksum, dataBytes);
            ValidateNoDuplicateProperties(dataBytes, BackupErrorCodes.DataJsonInvalid);
            var payload = BackupJsonCodec.DeserializeData(dataBytes);
            if (payload.Extensions.Features.Count != 0)
            {
                throw new BackupFormatException(BackupErrorCodes.UnsupportedRequiredFeature);
            }
            BackupModelContract.ValidateRecordCounts(manifest, payload);
            BackupArchiveWriter.ValidatePayloadGraph(payload);
            ValidatePortableRecoveryScope(payload);

            return new ValidatedBackupArchive(manifest, payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackupFormatException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.ArchiveLayoutInvalid, exception);
        }
        catch (IOException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.IoFailure, exception);
        }
        finally
        {
            bufferedStream?.Dispose();
        }
    }

    private static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.ArchiveLayoutInvalid, exception);
        }
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count != BackupFormatLimits.RequiredZipEntryCount)
        {
            throw new BackupFormatException(BackupErrorCodes.ArchiveLayoutInvalid);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalLength = 0;
        long totalCompressedLength = 0;
        foreach (var entry in archive.Entries)
        {
            if (!names.Add(entry.FullName)
                || entry.FullName is not ("manifest.json" or "data.json")
                || !string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal)
                || entry.FullName.Contains('/', StringComparison.Ordinal)
                || entry.FullName.Contains('\\', StringComparison.Ordinal)
                || IsDirectoryOrSymbolicLink(entry))
            {
                throw new BackupFormatException(BackupErrorCodes.ArchiveLayoutInvalid);
            }

            var maximumLength = entry.FullName == "manifest.json"
                ? BackupFormatLimits.MaxManifestUncompressedBytes
                : BackupFormatLimits.MaxDataUncompressedBytes;
            if (entry.Length < 0 || entry.Length > maximumLength || entry.CompressedLength < 0)
            {
                throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
            }

            ValidateCompressionRatio(entry.Length, entry.CompressedLength);
            totalLength = checked(totalLength + entry.Length);
            totalCompressedLength = checked(totalCompressedLength + entry.CompressedLength);
        }

        if (totalLength > BackupFormatLimits.MaxTotalUncompressedBytes)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }

        ValidateCompressionRatio(totalLength, totalCompressedLength);
    }

    private static bool IsDirectoryOrSymbolicLink(ZipArchiveEntry entry)
    {
        const int directoryAttribute = 0x10;
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & unixFileTypeMask;
        return (entry.ExternalAttributes & directoryAttribute) != 0
            || unixMode == unixSymbolicLink;
    }

    private static void ValidateCompressionRatio(long uncompressedLength, long compressedLength)
    {
        if (uncompressedLength <= BackupFormatLimits.CompressionRatioMinimumEntryBytes)
        {
            return;
        }

        if (compressedLength <= 0
            || uncompressedLength / (double)compressedLength > BackupFormatLimits.MaxCompressionRatio)
        {
            throw new BackupFormatException(BackupErrorCodes.ArchiveCompressionLimit);
        }
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        using var input = entry.Open();
        using var output = new MemoryStream(
            entry.Length <= int.MaxValue ? checked((int)entry.Length) : 0);
        await CopyBoundedAsync(
            input,
            output,
            maximumLength,
            BackupErrorCodes.LimitExceeded,
            cancellationToken);
        return output.ToArray();
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        Stream output,
        long maximumLength,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total = checked(total + read);
            if (total > maximumLength)
            {
                throw new BackupFormatException(errorCode);
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void VerifyChecksum(string expectedChecksum, ReadOnlySpan<byte> data)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedChecksum["sha256:".Length..]);
        }
        catch (FormatException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.ManifestInvalid, exception);
        }

        var actual = SHA256.HashData(data);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new BackupFormatException(BackupErrorCodes.ChecksumMismatch);
        }
    }

    private static void ValidateNoDuplicateProperties(
        ReadOnlySpan<byte> utf8Json,
        string errorCode)
    {
        try
        {
            var reader = new Utf8JsonReader(
                utf8Json,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = BackupFormatLimits.MaxJsonDepth
                });
            var objectProperties = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    objectProperties.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }
                else if (reader.TokenType == JsonTokenType.EndObject)
                {
                    objectProperties.Pop();
                }
                else if (reader.TokenType == JsonTokenType.PropertyName
                    && (objectProperties.Count == 0
                        || !objectProperties.Peek().Add(reader.GetString()!)))
                {
                    throw new BackupFormatException(errorCode);
                }
            }
        }
        catch (BackupFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new BackupFormatException(errorCode, exception);
        }
    }

    private static void ValidatePortableRecoveryScope(BackupPayload payload)
    {
        if (payload.Workflows.VocabularyReviews.Any(
                workflow => workflow.Status == BackupReviewSessionStatus.Active)
            || payload.Workflows.PreparationBatches.Any(
                workflow => workflow.Status == BackupPreparationSessionStatus.Active)
            || payload.Workflows.LearningSessions.Any(
                workflow => workflow.Status == BackupLearningSessionStatus.Active))
        {
            throw new BackupFormatException(BackupErrorCodes.ActiveWorkflowUnsupported);
        }
    }

    private static void ValidatePortableRecoveryScopeV2(BackupManifestV2 manifest, BackupPayloadV2 payload)
    {
        if (payload.Workflows.VocabularyReviews.Any(
                workflow => workflow.Status == BackupReviewSessionStatus.Active)
            || payload.Workflows.PreparationBatches.Any(
                workflow => workflow.Status == BackupPreparationSessionStatus.Active))
        {
            throw new BackupFormatException(BackupErrorCodes.ActiveWorkflowUnsupported);
        }

        if (manifest.SourceDatabaseSchemaVersion < 10
            && payload.Workflows.LearningSessions.Any(
                workflow => workflow.Status == BackupLearningSessionStatus.Active))
        {
            throw new BackupFormatException(BackupErrorCodes.ActiveWorkflowUnsupported);
        }
    }
}
