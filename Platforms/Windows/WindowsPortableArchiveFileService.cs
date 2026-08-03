using KnownFirst.Localization;
using KnownFirst.Services.DataSafety;
using Microsoft.Extensions.Localization;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace KnownFirst.Services;

public sealed class WindowsPortableArchiveFileService(
    IStringLocalizer<SharedResource> localizer) : IPortableArchiveFileService
{
    public async Task<PortableArchiveSaveStatus> ExportAsync(
        string suggestedFileName,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeArchive);
        var safeFileName = PortableArchiveExportGuard.ValidateArchiveFileName(suggestedFileName);

        var savePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(safeFileName)
        };
        savePicker.FileTypeChoices.Add(
            localizer["Settings_PortableArchiveFileTypeLabel"],
            new List<string> { ".kfarchive" });

        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, GetActiveWindowHandle());

        var destinationFile = await savePicker.PickSaveFileAsync().AsTask(cancellationToken);
        if (destinationFile is null)
        {
            return PortableArchiveSaveStatus.Cancelled;
        }

        // The native Windows FileSavePicker may already have materialized an empty (or otherwise
        // pre-existing) entry at destinationFile.Path before this call returns it, so a File.Exists
        // check here cannot tell a brand-new user-chosen name apart from a pre-existing file the user
        // deliberately picked to overwrite. Do not attempt to delete this path on failure: that could
        // destroy a legitimate pre-existing file. The guarantee that matters is provided below by
        // StagedPortableArchiveExporter, which never opens, truncates, or deletes whatever already
        // exists at this path until a fully validated replacement is ready for atomic finalization.
        await StagedPortableArchiveExporter.ExportAsync(destinationFile.Path, writeArchive, cancellationToken);
        return PortableArchiveSaveStatus.Saved;
    }

    public async Task<IPortableArchiveSelection?> PickImportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var openPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        openPicker.FileTypeFilter.Add(".kfarchive");

        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, GetActiveWindowHandle());

        var file = await openPicker.PickSingleFileAsync().AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return file is null ? null : new WindowsPortableArchiveSelection(file);
    }

    private static nint GetActiveWindowHandle()
    {
        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "The active window could not be resolved for the file picker.");
        var nativeWindow = mauiWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException(
                "The active window could not be resolved for the file picker.");
        return WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
    }

    private sealed class WindowsPortableArchiveSelection(StorageFile file) : IPortableArchiveSelection
    {
        public string DisplayName => file.Name;

        public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = await file.OpenStreamForReadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return stream;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
