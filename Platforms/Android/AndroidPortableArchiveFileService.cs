using Android.Content;
using KnownFirst.Services.DataSafety;
using Microsoft.Maui.ApplicationModel;
using AndroidUri = Android.Net.Uri;

namespace KnownFirst.Services;

public sealed class AndroidPortableArchiveFileService : IPortableArchiveFileService
{
    private const int CreateDocumentRequestCode = 42001;
    private const int OpenDocumentRequestCode = 42002;
    private const string ArchiveMimeType = "application/octet-stream";

    private static TaskCompletionSource<AndroidUri?>? _pendingCreateDocument;
    private static TaskCompletionSource<AndroidUri?>? _pendingOpenDocument;

    // Exclusive write/truncate mode, not a read-write mode. Per the official ContentResolver contract,
    // opening with the exclusive "r" or "w" modes lets the returned ParcelFileDescriptor be a pipe or
    // socket pair to enable streaming, whereas a read-write mode implies a file on disk that supports
    // seeking. Nothing in this export path reads back through the write handle, so the exclusive mode
    // is used to give the provider the most implementation flexibility.
    private const string DestinationWriteMode = "wt";

    public async Task<PortableArchiveSaveStatus> ExportAsync(
        string suggestedFileName,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeArchive);
        var safeFileName = PortableArchiveExportGuard.ValidateArchiveFileName(suggestedFileName);

        return await ProviderPortableArchiveExporter.ExportAsync(
            writeArchive,
            token => AcquireDestinationAsync(safeFileName, token),
            cancellationToken);
    }

    // Resolves the current Activity and launches the ACTION_CREATE_DOCUMENT picker only when invoked
    // by ProviderPortableArchiveExporter, which calls this only after the staged archive has already
    // been strictly validated — so no Activity is captured or retained during archive generation, and
    // the picker is never shown for a writer that failed or produced an invalid archive. Returns null
    // (never throws) when the user cancels the picker.
    private static async Task<IPortableArchiveExportDestination?> AcquireDestinationAsync(
        string safeFileName, CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "No active Android activity is available for the save picker.");

        var completionSource = new TaskCompletionSource<AndroidUri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCreateDocument = completionSource;

        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(ArchiveMimeType);
        intent.PutExtra(Intent.ExtraTitle, safeFileName);
        activity.StartActivityForResult(intent, CreateDocumentRequestCode);

        AndroidUri? destinationUri;
        await using (cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken)))
        {
            destinationUri = await completionSource.Task;
        }

        if (destinationUri is null)
        {
            return null;
        }

        var contentResolver = Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("The content resolver is unavailable.");

        return new AndroidProviderDocumentDestination(contentResolver, destinationUri);
    }

    // Writes and re-reads the picker-returned document only. Deliberately exposes (and performs) no
    // delete, rename, move, or replace operation — see IPortableArchiveExportDestination for why.
    private sealed class AndroidProviderDocumentDestination(
        ContentResolver contentResolver, AndroidUri uri) : IPortableArchiveExportDestination
    {
        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = contentResolver.OpenOutputStream(uri, DestinationWriteMode)
                ?? throw new IOException("The selected destination could not be opened for writing.");
            return Task.FromResult(stream);
        }

        public Task<Stream> OpenReadBackAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = contentResolver.OpenInputStream(uri)
                ?? throw new IOException("The saved archive could not be reopened for verification.");
            return Task.FromResult(stream);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public async Task<IPortableArchiveSelection?> PickImportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "No active Android activity is available for the open picker.");

        var completionSource = new TaskCompletionSource<AndroidUri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOpenDocument = completionSource;

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { ArchiveMimeType, "application/zip" });
        activity.StartActivityForResult(intent, OpenDocumentRequestCode);

        AndroidUri? selectedUri;
        await using (cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken)))
        {
            selectedUri = await completionSource.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return selectedUri is null ? null : new AndroidPortableArchiveSelection(selectedUri);
    }

    internal static void HandleActivityResult(int requestCode, Android.App.Result resultCode, Intent? data)
    {
        var uri = resultCode == Android.App.Result.Ok ? data?.Data : null;
        switch (requestCode)
        {
            case CreateDocumentRequestCode:
                _pendingCreateDocument?.TrySetResult(uri);
                _pendingCreateDocument = null;
                break;
            case OpenDocumentRequestCode:
                _pendingOpenDocument?.TrySetResult(uri);
                _pendingOpenDocument = null;
                break;
        }
    }

    private sealed class AndroidPortableArchiveSelection(AndroidUri uri) : IPortableArchiveSelection
    {
        public string DisplayName => QueryDisplayName(uri);

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contentResolver = Android.App.Application.Context.ContentResolver
                ?? throw new InvalidOperationException("The content resolver is unavailable.");
            var stream = contentResolver.OpenInputStream(uri)
                ?? throw new IOException("The selected archive could not be opened for reading.");
            return Task.FromResult(stream);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static string QueryDisplayName(AndroidUri uri)
        {
            var contentResolver = Android.App.Application.Context.ContentResolver;
            if (contentResolver is null)
            {
                return uri.LastPathSegment ?? "archive.kfarchive";
            }

#pragma warning disable CS0618 // Android.Provider.OpenableColumns is deprecated in favor of the
                               // IOpenableColumns interface, which does not yet expose the constant
                               // as a static member usable from C#. The column name itself is stable.
            using var cursor = contentResolver.Query(
                uri,
                new[] { Android.Provider.OpenableColumns.DisplayName },
                null,
                null,
                null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var nameIndex = cursor.GetColumnIndex(Android.Provider.OpenableColumns.DisplayName);
                if (nameIndex >= 0)
                {
                    return cursor.GetString(nameIndex) ?? uri.LastPathSegment ?? "archive.kfarchive";
                }
            }
#pragma warning restore CS0618

            return uri.LastPathSegment ?? "archive.kfarchive";
        }
    }
}
