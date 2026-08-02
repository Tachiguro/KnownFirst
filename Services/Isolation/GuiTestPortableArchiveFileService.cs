using KnownFirst.Services.DataSafety;

namespace KnownFirst.Services.Isolation;

#if KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED

/// <summary>
/// Debug/GUI-test-only replacement for the interactive native Save/Open file-picker boundary that
/// <c>WindowsPortableArchiveFileService</c> normally drives (diagnostics-export-import repair GUI
/// hardening). This resolver decides only <em>whether</em> that replacement should be wired into
/// production DI, and fails exactly like <see cref="GuiTestFaultInjection"/>: an env var that is set but
/// the isolated GUI-test profile is not active throws rather than silently activating (or not activating)
/// a file-system seam that could otherwise reach a real user file.
/// </summary>
public static class GuiTestPortableArchive
{
    public const string EnvironmentVariableName = "KNOWNFIRST_GUI_TEST_ARCHIVE_SEAM";

    private const string ArchiveFileName = "gui-test-portable-archive.kfarchive";

    public static string? Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!GuiTestProfile.IsActive)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set, but the isolated GUI test profile is not active. " +
                "Failing closed instead of risking a file-picker seam against a non-test profile.");
        }

        return raw.Trim();
    }

    public static bool IsActive => Resolve() is not null;

    /// <summary>
    /// The single deterministic archive file this seam ever reads or writes — always directly under the
    /// current run's isolated <see cref="GuiTestProfile.RootPath"/>, never anywhere else. Export and
    /// self-import both resolve to this exact path, so a self-import scenario always reads back the exact
    /// bytes the preceding export wrote.
    /// </summary>
    public static string ArchivePath => Path.Combine(
        IsActive ? GuiTestProfile.RootPath : throw new InvalidOperationException(
            "The GUI test portable-archive seam is not active."),
        ArchiveFileName);
}

/// <summary>
/// See <see cref="GuiTestPortableArchive"/>. Only <em>this</em> interactive picker boundary is replaced —
/// every other step (real snapshot capture, real external model mapping, real JSON/ZIP archive writer,
/// real archive validator, real import preview/import) still runs through the genuine
/// <see cref="BackupService"/> exactly as in production, because <see cref="ExportAsync"/> and
/// <see cref="PickImportAsync"/> only ever supply a <see cref="Stream"/> — they never touch archive
/// contents themselves. Wired into DI in <c>MauiProgram.cs</c> in place of
/// <c>WindowsPortableArchiveFileService</c> only when <see cref="GuiTestPortableArchive.IsActive"/>
/// is true.
/// </summary>
public sealed class GuiTestPortableArchiveFileService : IPortableArchiveFileService
{
    public async Task<PortableArchiveSaveStatus> ExportAsync(
        string suggestedFileName,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeArchive);
        if (!GuiTestPortableArchive.IsActive)
        {
            throw new InvalidOperationException(
                $"{nameof(GuiTestPortableArchiveFileService)}.{nameof(ExportAsync)} was invoked without " +
                "the GUI-test portable-archive seam active. Failing closed instead of touching a real destination.");
        }

        PortableArchiveExportGuard.ValidateArchiveFileName(suggestedFileName);
        var path = GuiTestPortableArchive.ArchivePath;

        await using (var output = File.Create(path))
        {
            await writeArchive(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        PortableArchiveExportGuard.VerifySavedArchive(path);
        return PortableArchiveSaveStatus.Saved;
    }

    public Task<IPortableArchiveSelection?> PickImportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GuiTestPortableArchive.IsActive)
        {
            throw new InvalidOperationException(
                $"{nameof(GuiTestPortableArchiveFileService)}.{nameof(PickImportAsync)} was invoked without " +
                "the GUI-test portable-archive seam active. Failing closed instead of touching a real source.");
        }

        var path = GuiTestPortableArchive.ArchivePath;
        if (!File.Exists(path))
        {
            // Mirrors the real picker's "user cancelled" contract: no file selected yet.
            return Task.FromResult<IPortableArchiveSelection?>(null);
        }

        return Task.FromResult<IPortableArchiveSelection?>(new FileArchiveSelection(path));
    }

    private sealed class FileArchiveSelection(string path) : IPortableArchiveSelection
    {
        public string DisplayName => Path.GetFileName(path);

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(File.OpenRead(path));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

#endif
