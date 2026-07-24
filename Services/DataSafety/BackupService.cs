using KnownFirst.Data;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public sealed class BackupService(
    IKnownFirstDatabase database,
    IBackupPlatformInfo platformInfo,
    IBackupImportFailureInjector? failureInjector = null) : IBackupService
{
    public async Task CreateBackupAsync(Stream destinationStream, CancellationToken cancellationToken)
    {
        var snapshot = await database.ExecuteSnapshotAsync(connection =>
            BackupSnapshotRepository.CaptureSnapshot(connection));

        await WriteArchiveAsync(snapshot, destinationStream, cancellationToken);
    }

    public async Task CreatePortableArchiveAsync(
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        var snapshot = await database.ExecuteSnapshotAsync(connection =>
            BackupSnapshotRepository.CapturePortableSnapshot(connection));

        await WriteValidatedPortableArchiveAsync(
            snapshot,
            destinationStream,
            cancellationToken);
    }

    public async Task<BackupManifest> ValidatePortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        var validated = await BackupArchiveReader.ValidateAsync(
            sourceStream,
            cancellationToken);
        return validated.Manifest;
    }

    public async Task<PortableImportResult> ImportPortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        try
        {
            var validated = await BackupArchiveReader.ValidateAsync(
                sourceStream,
                cancellationToken);

            return await database.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (BackupImportRepository.HasDurableUserData(connection))
                {
                    return new PortableImportResult(
                        PortableImportStatus.TargetNotEmpty,
                        BackupErrorCodes.TargetNotEmpty);
                }

                BackupImportRepository.ImportIntoEmptyDatabase(
                    connection,
                    validated.Payload,
                    cancellationToken,
                    failureInjector);
                return new PortableImportResult(PortableImportStatus.Success, null);
            });
        }
        catch (OperationCanceledException)
        {
            return new PortableImportResult(
                PortableImportStatus.Cancelled,
                BackupErrorCodes.OperationCancelled);
        }
        catch (BackupFormatException exception)
        {
            return new PortableImportResult(
                PortableImportStatus.ValidationFailed,
                exception.Code);
        }
        catch
        {
            return new PortableImportResult(
                PortableImportStatus.Failed,
                BackupErrorCodes.RestoreFailed);
        }
    }

    private async Task WriteArchiveAsync(
        BackupSnapshot snapshot,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        var payload = BackupModelMapper.MapToExternal(snapshot);

        await BackupArchiveWriter.WriteArchiveAsync(
            payload,
            snapshot,
            platformInfo,
            DateTime.UtcNow,
            destinationStream,
            cancellationToken);
    }

    private async Task WriteValidatedPortableArchiveAsync(
        BackupSnapshot snapshot,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.GetTempFileName();
        try
        {
            await using (var staging = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await WriteArchiveAsync(snapshot, staging, cancellationToken);
                await staging.FlushAsync(cancellationToken);
                if (staging.Length > BackupFormatLimits.MaxArchiveBytes)
                {
                    throw new BackupFormatException(BackupErrorCodes.ArchiveTooLarge);
                }

                staging.Position = 0;
                await BackupArchiveReader.ValidateAsync(staging, cancellationToken);
                staging.Position = 0;
                await staging.CopyToAsync(destinationStream, cancellationToken);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the private staging file.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of the private staging file.
            }
        }
    }
}
