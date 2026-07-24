namespace KnownFirst.Services.DataSafety;

public interface IPortableArchiveFileService
{
    Task ExportAsync(
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
