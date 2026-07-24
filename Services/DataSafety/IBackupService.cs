using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public interface IBackupService
{
    Task CreateBackupAsync(Stream destinationStream, CancellationToken cancellationToken);

    Task CreatePortableArchiveAsync(
        Stream destinationStream,
        CancellationToken cancellationToken);

    Task<BackupManifest> ValidatePortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken);

    Task<PortableImportResult> ImportPortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken);
}
