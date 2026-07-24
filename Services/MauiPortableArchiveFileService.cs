using KnownFirst.Localization;
using KnownFirst.Services.DataSafety;
using Microsoft.Extensions.Localization;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services;

public sealed class MauiPortableArchiveFileService(
    IStringLocalizer<SharedResource> localizer) : IPortableArchiveFileService
{
    private static readonly FilePickerFileType ArchiveFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".kfarchive"],
            [DevicePlatform.Android] =
            [
                "application/zip",
                "application/octet-stream",
                "application/vnd.knownfirst.archive"
            ]
        });

    public async Task ExportAsync(
        string suggestedFileName,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeArchive);
        var safeFileName = Path.GetFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(safeFileName)
            || !safeFileName.EndsWith(".kfarchive", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A .kfarchive export file name is required.",
                nameof(suggestedFileName));
        }

        var temporaryPath = Path.Combine(FileSystem.CacheDirectory, safeFileName);
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await writeArchive(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = localizer["Settings_DataExportShareTitle"],
                File = new ShareFile(temporaryPath)
            });
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The operating system may still own the shared cache file.
            }
            catch (UnauthorizedAccessException)
            {
                // The operating system may still own the shared cache file.
            }
        }
    }

    public async Task<IPortableArchiveSelection?> PickImportAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await FilePicker.Default.PickAsync(
            new PickOptions
            {
                PickerTitle = localizer["Settings_DataImportPickerTitle"],
                FileTypes = ArchiveFileTypes
            });
        cancellationToken.ThrowIfCancellationRequested();
        return result is null ? null : new MauiPortableArchiveSelection(result);
    }

    private sealed class MauiPortableArchiveSelection(
        FileResult fileResult) : IPortableArchiveSelection
    {
        public string DisplayName => fileResult.FileName;

        public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = await fileResult.OpenReadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return stream;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
