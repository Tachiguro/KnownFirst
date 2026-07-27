namespace KnownFirst.Services.DataSafety;

public static class PortableArchiveExportGuard
{
    public static string ValidateArchiveFileName(string suggestedFileName)
    {
        var safeFileName = Path.GetFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(safeFileName)
            || !safeFileName.EndsWith(".kfarchive", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A .kfarchive export file name is required.",
                nameof(suggestedFileName));
        }

        return safeFileName;
    }

    public static void VerifySavedArchive(string path)
    {
        if (!File.Exists(path))
        {
            throw new IOException("The saved archive could not be verified at its destination.");
        }

        if (new FileInfo(path).Length == 0)
        {
            throw new IOException("The saved archive is unexpectedly empty.");
        }
    }

    // Reads one byte instead of checking Stream.Length/Position, which non-seekable
    // content-provider streams (e.g. Android) can throw on even after a successful write.
    public static async Task VerifySavedArchiveAsync(
        Func<Stream?> openVerificationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openVerificationStream);

        var verifyStream = openVerificationStream();
        if (verifyStream is null)
        {
            throw new IOException("The saved archive could not be verified at its destination.");
        }

        await using var _ = verifyStream;
        var buffer = new byte[1];
        var bytesRead = await verifyStream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
        if (bytesRead == 0)
        {
            throw new IOException("The saved archive is unexpectedly empty.");
        }
    }
}
