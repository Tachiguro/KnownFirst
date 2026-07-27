using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnownFirst.Data;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public interface IMergeSafetyCopyService
{
    Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
        string? sourceDescription,
        CancellationToken cancellationToken);
}

/// <summary>
/// Creates a validated, private pre-merge recovery archive ("safety copy") of the target database's
/// current portable-recovery-scope state. This is KF-BACKUP-002 Slice 2: it guarantees only that the
/// safety-copy snapshot itself was captured with no active workflow at capture time, and that the
/// resulting archive+metadata pair was reopened and validated from its final path before Success is
/// returned. It performs no merge matching, no database mutation, and no Import routing — a future
/// merge writer must re-check the active-workflow precondition again, immediately before mutation.
/// </summary>
public sealed class MergeSafetyCopyService(
    IKnownFirstDatabase database,
    IBackupPlatformInfo platformInfo,
    IMergeSafetyCopyIdentityProvider? identityProvider = null,
    MergeSafetyCopyFailureInjector? failureInjector = null) : IMergeSafetyCopyService
{
    internal const string DirectoryName = "merge-safety-copies";
    private const string StagingSuffix = ".mergestaging";
    private const string MetadataSuffix = ".metadata.json";
    private const int MaxSourceDescriptionLength = 200;

    private static readonly Regex ArchiveFileNamePattern = new(
        @"^merge-safety-\d{8}T\d{9}Z-[0-9a-f]{6,32}\.kfarchive$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IMergeSafetyCopyIdentityProvider _identityProvider =
        identityProvider ?? new SystemMergeSafetyCopyIdentityProvider();
    private readonly MergeSafetyCopyFailureInjector _failureInjector =
        failureInjector ?? MergeSafetyCopyFailureInjector.None;

    public async Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
        string? sourceDescription,
        CancellationToken cancellationToken)
    {
        var createdFiles = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var storageRoot = ResolveStorageRoot(database.DatabasePath);

            var captureResult = await database.ExecuteSnapshotAsync(connection =>
                BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection));
            cancellationToken.ThrowIfCancellationRequested();

            if (captureResult.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow)
            {
                return MergeSafetyCopyResult.BlockedByActiveWorkflow;
            }

            var snapshot = captureResult.Snapshot
                ?? throw new InvalidOperationException(
                    "Snapshot capture reported success without a snapshot.");

            Directory.CreateDirectory(storageRoot);

            return await WriteSafetyCopyAsync(
                storageRoot,
                snapshot,
                sourceDescription,
                createdFiles,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CleanupAttemptFiles(createdFiles);
            return MergeSafetyCopyResult.Cancelled;
        }
        catch (Exception)
        {
            CleanupAttemptFiles(createdFiles);
            return MergeSafetyCopyResult.Failed;
        }
    }

    private async Task<MergeSafetyCopyResult> WriteSafetyCopyAsync(
        string storageRoot,
        BackupSnapshot snapshot,
        string? sourceDescription,
        List<string> createdFiles,
        CancellationToken cancellationToken)
    {
        var timestampUtc = _identityProvider.UtcNow;
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            timestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        }

        var shortId = _identityProvider.NewShortId();
        var archiveFileName = FormatArchiveFileName(timestampUtc, shortId);
        var metadataFileName = archiveFileName + MetadataSuffix;
        var stagingArchivePath = Path.Combine(storageRoot, archiveFileName + StagingSuffix);
        var stagingMetadataPath = Path.Combine(storageRoot, metadataFileName + StagingSuffix);
        var finalArchivePath = Path.Combine(storageRoot, archiveFileName);
        var finalMetadataPath = Path.Combine(storageRoot, metadataFileName);

        _failureInjector.BeforeArchiveWrite();
        cancellationToken.ThrowIfCancellationRequested();

        var payload = BackupModelMapper.MapToExternal(snapshot);
        await using (var stagingStream = new FileStream(
            stagingArchivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            createdFiles.Add(stagingArchivePath);
            await BackupArchiveWriter.WriteArchiveAsync(
                payload,
                snapshot,
                platformInfo,
                timestampUtc,
                stagingStream,
                cancellationToken);
            await stagingStream.FlushAsync(cancellationToken);
        }

        _failureInjector.AfterArchiveWritten(stagingArchivePath);
        cancellationToken.ThrowIfCancellationRequested();

        var stagedInfo = new FileInfo(stagingArchivePath);
        if (!stagedInfo.Exists
            || stagedInfo.Length <= 0
            || stagedInfo.Length > BackupFormatLimits.MaxArchiveBytes)
        {
            throw new InvalidOperationException(
                "Staged safety-copy archive failed basic verification.");
        }

        ValidatedBackupArchive stagedValidated;
        await using (var stagedReadStream = new FileStream(
            stagingArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true))
        {
            stagedValidated = await BackupArchiveReader.ValidateAsync(stagedReadStream, cancellationToken);
        }

        VerifyRecordCountsMatchSnapshot(stagedValidated.Manifest.RecordCounts, snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        File.Move(stagingArchivePath, finalArchivePath);
        createdFiles.Remove(stagingArchivePath);
        createdFiles.Add(finalArchivePath);

        _failureInjector.AfterArchiveMoved(finalArchivePath);
        cancellationToken.ThrowIfCancellationRequested();

        ValidatedBackupArchive finalValidated;
        await using (var finalReadStream = new FileStream(
            finalArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true))
        {
            finalValidated = await BackupArchiveReader.ValidateAsync(finalReadStream, cancellationToken);
        }

        var finalArchiveSize = new FileInfo(finalArchivePath).Length;

        _failureInjector.BeforeMetadataWrite();
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = new MergeSafetyCopyMetadata(
            MergeSafetyCopyMetadata.CurrentSchemaVersion,
            archiveFileName,
            timestampUtc,
            finalArchiveSize,
            finalValidated.Manifest.RecordCounts,
            SanitizeSourceDescription(sourceDescription),
            finalValidated.Manifest.FormatVersion,
            finalValidated.Manifest.SourceAppVersion,
            finalValidated.Manifest.SourceDatabaseSchemaVersion,
            finalValidated.Manifest.SourcePlatform);

        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(
            metadata,
            MergeSafetyCopyMetadataJsonSerializerContext.Default.MergeSafetyCopyMetadata);

        await using (var metadataStagingStream = new FileStream(
            stagingMetadataPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            createdFiles.Add(stagingMetadataPath);
            await metadataStagingStream.WriteAsync(metadataBytes, cancellationToken);
            await metadataStagingStream.FlushAsync(cancellationToken);
        }

        _failureInjector.AfterMetadataWritten(stagingMetadataPath);
        cancellationToken.ThrowIfCancellationRequested();

        File.Move(stagingMetadataPath, finalMetadataPath);
        createdFiles.Remove(stagingMetadataPath);
        createdFiles.Add(finalMetadataPath);

        var rereadBytes = await File.ReadAllBytesAsync(finalMetadataPath, cancellationToken);
        var rereadMetadata = JsonSerializer.Deserialize(
            rereadBytes,
            MergeSafetyCopyMetadataJsonSerializerContext.Default.MergeSafetyCopyMetadata);
        if (rereadMetadata is null || rereadMetadata != metadata)
        {
            throw new InvalidOperationException(
                "Safety-copy metadata failed re-read validation.");
        }

        // Commit point: both the final archive and the final metadata have now been reopened,
        // validated, and cross-checked successfully. Clear the attempt's file list so that any
        // exception raised after this point (however unlikely) can never cause the catch blocks
        // below to delete the just-committed pair via CleanupAttemptFiles.
        createdFiles.Clear();
        _failureInjector.AfterCommit();

        TryRemoveOlderSafetyCopies(storageRoot, archiveFileName, metadataFileName);

        return MergeSafetyCopyResult.ForSuccess(
            finalArchivePath,
            finalMetadataPath,
            timestampUtc,
            finalArchiveSize,
            finalValidated.Manifest.RecordCounts,
            finalValidated.Manifest);
    }

    private static void VerifyRecordCountsMatchSnapshot(
        BackupRecordCounts manifestCounts,
        BackupSnapshot snapshot)
    {
        var expected = new BackupRecordCounts(
            snapshot.Documents.Count,
            snapshot.SentenceSpans.Count,
            snapshot.Words.Count,
            snapshot.WordForms.Count,
            snapshot.WordOccurrences.Count,
            snapshot.Meanings.Count,
            snapshot.ContextSnapshots.Count,
            snapshot.ReviewStates.Count,
            snapshot.ReviewSessions.Count,
            snapshot.ReviewCandidates.Count,
            snapshot.PreparationSessions.Count,
            snapshot.PreparationCandidates.Count,
            snapshot.LearningCards.Count,
            snapshot.LearningReviews.Count,
            snapshot.LearningSessions.Count,
            snapshot.LearningSessionCards.Count);

        if (!expected.Equals(manifestCounts))
        {
            throw new InvalidOperationException(
                "Validated safety-copy manifest record counts did not match the captured snapshot.");
        }
    }

    private static string FormatArchiveFileName(DateTime timestampUtc, string shortId) =>
        $"merge-safety-{timestampUtc:yyyyMMdd'T'HHmmssfff'Z'}-{shortId}.kfarchive";

    private static string? SanitizeSourceDescription(string? sourceDescription)
    {
        if (string.IsNullOrWhiteSpace(sourceDescription))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(sourceDescription.Length, MaxSourceDescriptionLength));
        foreach (var character in sourceDescription)
        {
            if (builder.Length >= MaxSourceDescriptionLength)
            {
                break;
            }

            if (char.IsControl(character))
            {
                continue;
            }

            builder.Append(character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }

    private static string ResolveStorageRoot(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
        {
            throw new InvalidOperationException(
                "IKnownFirstDatabase.DatabasePath must be an absolute, non-empty path.");
        }

        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrEmpty(databaseDirectory))
        {
            throw new InvalidOperationException(
                "IKnownFirstDatabase.DatabasePath must have a parent directory.");
        }

        return Path.Combine(databaseDirectory, DirectoryName);
    }

    private static bool IsRecognizedArchiveFileName(string fileName) =>
        ArchiveFileNamePattern.IsMatch(fileName);

    private static bool IsRecognizedMetadataFileName(string fileName) =>
        fileName.EndsWith(MetadataSuffix, StringComparison.Ordinal)
        && IsRecognizedArchiveFileName(fileName[..^MetadataSuffix.Length]);

    private static void TryRemoveOlderSafetyCopies(
        string storageRoot,
        string keepArchiveFileName,
        string keepMetadataFileName)
    {
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(storageRoot).ToList();
        }
        catch
        {
            // Non-fatal: enumeration failure must not invalidate the just-finalized new pair.
            return;
        }

        foreach (var filePath in files)
        {
            var name = Path.GetFileName(filePath);
            if (string.Equals(name, keepArchiveFileName, StringComparison.Ordinal)
                || string.Equals(name, keepMetadataFileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsRecognizedArchiveFileName(name) && !IsRecognizedMetadataFileName(name))
            {
                // Never delete a file that does not match the exact safety-copy naming contract.
                continue;
            }

            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Non-fatal: failure to remove an older recognized pair may leave multiple valid copies.
            }
        }
    }

    private static void CleanupAttemptFiles(List<string> createdFiles)
    {
        foreach (var path in createdFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup; the operating system may still hold the file open.
            }
        }
    }
}
