using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models.Backup;
using SQLite;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Builds the V1/V2 source-side Schema-13 semantic projection by executing the same empty-target import
/// and <see cref="Schema13LearningBootstrap"/> oracle used by Slice 3. The private temporary database is
/// never the target database and is removed after projection.
/// </summary>
internal static class Schema13LegacySourceProjector
{
    public static async Task<BackupPayloadV3> ProjectAsync(
        BackupPayloadV2 payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"KnownFirst-schema13-preflight-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(temporaryPath);
        try
        {
            await DatabaseSchema.InitializeAsync(connection).ConfigureAwait(false);
            await Schema13DormantMigration.ApplyAsync(connection).ConfigureAwait(false);

            Schema13BackupSnapshot? snapshot = null;
            await connection.RunInTransactionAsync(sqliteConnection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capability = BackupSchemaCapability.Resolve(sqliteConnection) as Schema13CapabilityResult
                    ?? throw new BackupSchemaCapabilityException(13, shapeMismatch: true);
                Schema13BackupImportRepository.AdaptLegacyIntoEmptyDatabase(
                    sqliteConnection,
                    capability.Capability,
                    payload,
                    cancellationToken);
                snapshot = Schema13BackupSnapshotRepository.CapturePortableSnapshot(sqliteConnection);
            }).ConfigureAwait(false);

            return BackupModelMapperV3.MapToExternal(
                snapshot ?? throw new InvalidOperationException("Legacy Schema-13 projection produced no snapshot."));
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
            DeleteTemporaryFile(temporaryPath);
            DeleteTemporaryFile(temporaryPath + "-wal");
            DeleteTemporaryFile(temporaryPath + "-shm");
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a private, non-user projection fixture.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a private, non-user projection fixture.
        }
    }
}
