namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Failure-safe staged export used by <c>WindowsPortableArchiveFileService</c>: the archive is written to
/// a collision-safe temporary file in the destination's own directory, fully flushed and closed, reopened
/// and validated through the real production archive validator (<see cref="BackupArchiveReader"/>), and
/// only then atomically committed at the final destination. A pre-existing destination is never opened,
/// truncated, or deleted before that commit point, so any failure up to and including cancellation leaves
/// it byte-for-byte unchanged; a new destination stays absent until the same commit point succeeds.
/// The optional <c>finalizeOverride</c> and <c>temporaryFileNameFactory</c> parameters are internal test
/// seams only: production always resolves to the real <see cref="FinalizeDestination"/> and
/// <see cref="DefaultTemporaryFileName"/> implementations.
/// </summary>
internal static class StagedPortableArchiveExporter
{
    private const int MaxTemporaryNameAttempts = 8;
    private const int StreamBufferSize = 81920;

    internal static Task ExportAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken,
        Func<int, string>? temporaryFileNameFactory = null,
        Action<string, string>? finalizeOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeArchive);

        return ExportCoreAsync(
            destinationPath,
            writeArchive,
            cancellationToken,
            temporaryFileNameFactory ?? (_ => DefaultTemporaryFileName(destinationPath)),
            finalizeOverride ?? FinalizeDestination);
    }

    private static async Task ExportCoreAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken,
        Func<int, string> temporaryFileNameFactory,
        Action<string, string> finalizeDestination)
    {
        var (temporaryPath, staged) = CreateExclusiveTemporaryFile(destinationPath, temporaryFileNameFactory);
        try
        {
            await using (staged)
            {
                await writeArchive(staged, cancellationToken);
                await staged.FlushAsync(cancellationToken);
            }

            await using (var verification = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                useAsync: true))
            {
                await BackupArchiveReader.ValidateVersionedAsync(verification, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            finalizeDestination(temporaryPath, destinationPath);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static (string Path, FileStream Stream) CreateExclusiveTemporaryFile(
        string destinationPath,
        Func<int, string> temporaryFileNameFactory)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException("The export destination directory could not be determined.");
        }

        IOException? lastFailure = null;
        for (var attempt = 0; attempt < MaxTemporaryNameAttempts; attempt++)
        {
            var candidatePath = Path.Combine(directory, temporaryFileNameFactory(attempt));
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
            "A unique temporary export file could not be created after multiple attempts.", lastFailure);
    }

    private static void FinalizeDestination(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
        }
        else
        {
            File.Move(temporaryPath, destinationPath);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
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

    private static string DefaultTemporaryFileName(string destinationPath) =>
        $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.kfexport-tmp";
}
