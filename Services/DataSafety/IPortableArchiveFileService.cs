namespace KnownFirst.Services.DataSafety;

public enum PortableArchiveSaveStatus
{
    Saved,
    Cancelled
}

public interface IPortableArchiveFileService
{
    Task<PortableArchiveSaveStatus> ExportAsync(
        string suggestedFileName,
        Func<Stream, CancellationToken, Task> writeArchive,
        CancellationToken cancellationToken);

    Task<IPortableArchiveSelection?> PickImportAsync(CancellationToken cancellationToken);
}

public interface IPortableArchiveSelection : IAsyncDisposable
{
    string DisplayName { get; }

    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}
