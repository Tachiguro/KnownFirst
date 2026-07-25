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
}
