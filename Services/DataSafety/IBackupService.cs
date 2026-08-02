using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public interface IBackupService
{
    Task CreateBackupAsync(Stream destinationStream, CancellationToken cancellationToken);

    Task CreatePortableArchiveAsync(
        Stream destinationStream,
        CancellationToken cancellationToken);

    Task<BackupPortableArchiveSummary> ValidatePortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Read-only preview (KF-MEANING-001 Slice 9) of what <see cref="ImportPortableArchiveAsync"/> would do
    /// against the target's current state: validates the archive, performs no database mutation, creates no
    /// safety copy, and invokes no merge writer. Does not require <paramref name="sourceStream"/> to be
    /// seekable. The actual import call still independently reopens, re-validates, and re-evaluates the
    /// operation before any mutation — this preview never weakens that guard.
    /// </summary>
    Task<PortableImportPreview> PreviewPortableImportAsync(
        Stream sourceStream,
        CancellationToken cancellationToken);

    Task<PortableImportResult> ImportPortableArchiveAsync(
        Stream sourceStream,
        CancellationToken cancellationToken);
}
