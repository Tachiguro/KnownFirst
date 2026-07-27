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

    public async Task<PortableArchiveSaveStatus> ExportAsync(
        string suggestedFileName,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeArchive);
        var safeFileName = PortableArchiveExportGuard.ValidateArchiveFileName(suggestedFileName);

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
            return PortableArchiveSaveStatus.Cancelled;
        }

        var contentResolver = Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("The content resolver is unavailable.");

        await using (var output = contentResolver.OpenOutputStream(destinationUri, "rwt")
            ?? throw new IOException("The selected destination could not be opened for writing."))
        {
            await writeArchive(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        await PortableArchiveExportGuard.VerifySavedArchiveAsync(
            () => contentResolver.OpenInputStream(destinationUri),
            cancellationToken);

        return PortableArchiveSaveStatus.Saved;
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
