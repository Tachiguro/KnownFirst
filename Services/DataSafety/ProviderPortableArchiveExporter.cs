namespace KnownFirst.Services.DataSafety;

/// <summary>
/// A destination acquired from a platform picker (e.g. the Android Storage Access Framework or the
/// Windows FileSavePicker). <see cref="ProviderPortableArchiveExporter"/> only writes to it and later
/// re-reads it for verification; it never deletes, renames, moves, or replaces it. A picker-returned
/// document is not provably new — <c>Intent.ACTION_CREATE_DOCUMENT</c> may legitimately return an
/// existing document the user chose to overwrite — so this abstraction deliberately exposes no
/// operation that could destroy user-owned data.
/// </summary>
internal interface IPortableArchiveExportDestination : IAsyncDisposable
{
    Task<Stream> OpenWriteAsync(CancellationToken cancellationToken);

    Task<Stream> OpenReadBackAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Platform-independent seam extracted from <c>AndroidPortableArchiveFileService</c> (KF data-safety
/// gap fix): the caller's archive is written to an app-private staging file, flushed, closed, reopened,
/// and strictly validated through <see cref="BackupArchiveReader.ValidateVersionedAsync"/> before
/// <c>acquireDestinationAsync</c> is ever invoked — so a picker is never shown for a writer
/// that failed, produced a truncated archive, or produced a corrupt-but-non-empty archive. The validated
/// staged bytes are then copied to the acquired destination, the destination is reopened, and the same
/// strict validator is applied to the readback before <see cref="PortableArchiveSaveStatus.Saved"/> is
/// ever returned — closing the prior one-byte-non-empty-only acceptance gap. The exact private staging
/// artifact this call created is always removed, on every outcome (success, cancellation, or failure).
/// The optional <c>stagingFileNameFactory</c> parameter is an internal test seam only; production always
/// resolves to <see cref="DefaultStagingFileName"/>.
/// </summary>
internal static class ProviderPortableArchiveExporter
{
    private const int MaxStagingNameAttempts = 8;
    private const int StreamBufferSize = 81920;

    internal static Task<PortableArchiveSaveStatus> ExportAsync(
        Func<Stream, CancellationToken, Task> writeArchive,
        Func<CancellationToken, Task<IPortableArchiveExportDestination?>> acquireDestinationAsync,
        CancellationToken cancellationToken,
        Func<int, string>? stagingFileNameFactory = null)
    {
        ArgumentNullException.ThrowIfNull(writeArchive);
        ArgumentNullException.ThrowIfNull(acquireDestinationAsync);

        return ExportCoreAsync(
            writeArchive,
            acquireDestinationAsync,
            cancellationToken,
            stagingFileNameFactory ?? DefaultStagingFileName);
    }

    private static async Task<PortableArchiveSaveStatus> ExportCoreAsync(
        Func<Stream, CancellationToken, Task> writeArchive,
        Func<CancellationToken, Task<IPortableArchiveExportDestination?>> acquireDestinationAsync,
        CancellationToken cancellationToken,
        Func<int, string> stagingFileNameFactory)
    {
        var (stagingPath, staging) = CreateExclusiveStagingFile(stagingFileNameFactory);
        try
        {
            await using (staging)
            {
                await writeArchive(staging, cancellationToken);
                await staging.FlushAsync(cancellationToken);
            }

            await using (var verification = OpenStagingForRead(stagingPath))
            {
                await BackupArchiveReader.ValidateVersionedAsync(verification, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var destination = await acquireDestinationAsync(cancellationToken);
            if (destination is null)
            {
                return PortableArchiveSaveStatus.Cancelled;
            }

            await using (destination)
            {
                await using (var stagedForCopy = OpenStagingForRead(stagingPath))
                await using (var destinationWrite = await destination.OpenWriteAsync(cancellationToken))
                {
                    await stagedForCopy.CopyToAsync(destinationWrite, StreamBufferSize, cancellationToken);
                    await destinationWrite.FlushAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                await using var readBack = await destination.OpenReadBackAsync(cancellationToken);
                await BackupArchiveReader.ValidateVersionedAsync(readBack, cancellationToken);
            }

            return PortableArchiveSaveStatus.Saved;
        }
        finally
        {
            TryDeleteStagingFile(stagingPath);
        }
    }

    private static (string Path, FileStream Stream) CreateExclusiveStagingFile(
        Func<int, string> stagingFileNameFactory)
    {
        var directory = Path.GetTempPath();

        IOException? lastFailure = null;
        for (var attempt = 0; attempt < MaxStagingNameAttempts; attempt++)
        {
            var candidatePath = Path.Combine(directory, stagingFileNameFactory(attempt));
            try
            {
                var stream = new FileStream(
                    candidatePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    StreamBufferSize,
                    useAsync: true);
                return (candidatePath, stream);
            }
            catch (IOException exception)
            {
                lastFailure = exception;
            }
        }

        throw new IOException(
            "A unique private export staging file could not be created after multiple attempts.", lastFailure);
    }

    private static FileStream OpenStagingForRead(string stagingPath) => new(
        stagingPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        StreamBufferSize,
        useAsync: true);

    private static void TryDeleteStagingFile(string stagingPath)
    {
        try
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of this operation's own staging file.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of this operation's own staging file.
        }
    }

    private static string DefaultStagingFileName(int attempt) =>
        $"knownfirst-export-{Guid.NewGuid():N}.kfexport-stage";
}
