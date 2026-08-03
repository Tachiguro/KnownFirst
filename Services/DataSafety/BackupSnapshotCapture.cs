using KnownFirst.Data;
using KnownFirst.Data.Schema8;
using SQLite;

namespace KnownFirst.Services.DataSafety;

/// <summary>Type-safe dual-schema capture entry point (KF-MEANING-001 Slice 2). Resolves
/// <see cref="BackupSchemaCapability"/> internally and dispatches to the matching repository, returning a
/// discriminated envelope that pairs each schema-specific snapshot type with its matching capability
/// type — there is no path by which a <see cref="Schema8BackupSnapshot"/> can reach
/// <see cref="BackupArchiveWriter"/> (v1) or a <see cref="BackupSnapshot"/> can reach
/// <see cref="BackupArchiveWriterV2"/>.</summary>
public abstract record CapturedBackupSnapshotEnvelope;

public sealed record CapturedSchema7SnapshotEnvelope(
    BackupSnapshot Snapshot,
    ValidatedSchema7Capability Capability) : CapturedBackupSnapshotEnvelope;

public sealed record CapturedSchema8SnapshotEnvelope(
    Schema8BackupSnapshot Snapshot,
    ValidatedSchema8Capability Capability) : CapturedBackupSnapshotEnvelope;

/// <summary>The Schema-9 counterpart of <see cref="CapturedSchema8SnapshotEnvelope"/>. Reuses
/// <see cref="Schema8BackupSnapshot"/> unchanged — Schema 9 changes only the <c>ReviewSessions</c> index
/// shape, never the captured snapshot's data model.</summary>
public sealed record CapturedSchema9SnapshotEnvelope(
    Schema8BackupSnapshot Snapshot,
    ValidatedSchema9Capability Capability) : CapturedBackupSnapshotEnvelope;

public static class BackupSnapshotCapture
{
    /// <summary>Portable/user-export capture (workflow-filtered), dispatched by validated schema
    /// capability.</summary>
    public static CapturedBackupSnapshotEnvelope CaptureForExport(SQLiteConnection connection)
    {
        var capability = BackupSchemaCapability.Resolve(connection);
        return capability switch
        {
            Schema7CapabilityResult schema7 => new CapturedSchema7SnapshotEnvelope(
                BackupSnapshotRepository.CapturePortableSnapshot(connection), schema7.Capability),
            Schema8CapabilityResult schema8 => new CapturedSchema8SnapshotEnvelope(
                Schema8BackupSnapshotRepository.CapturePortableSnapshot(connection), schema8.Capability),
            Schema9CapabilityResult schema9 => new CapturedSchema9SnapshotEnvelope(
                Schema8BackupSnapshotRepository.CapturePortableSnapshot(connection), schema9.Capability),
            _ => throw new InvalidOperationException("Unrecognized backup schema capability result.")
        };
    }

    /// <summary>Full/internal backup capture (unfiltered, includes active workflows), dispatched by
    /// validated schema capability.</summary>
    public static CapturedBackupSnapshotEnvelope CaptureFullForBackup(SQLiteConnection connection)
    {
        var capability = BackupSchemaCapability.Resolve(connection);
        return capability switch
        {
            Schema7CapabilityResult schema7 => new CapturedSchema7SnapshotEnvelope(
                BackupSnapshotRepository.CaptureSnapshot(connection), schema7.Capability),
            Schema8CapabilityResult schema8 => new CapturedSchema8SnapshotEnvelope(
                Schema8BackupSnapshotRepository.CaptureSnapshot(connection), schema8.Capability),
            Schema9CapabilityResult schema9 => new CapturedSchema9SnapshotEnvelope(
                Schema8BackupSnapshotRepository.CaptureSnapshot(connection), schema9.Capability),
            _ => throw new InvalidOperationException("Unrecognized backup schema capability result.")
        };
    }
}

/// <summary>Discriminated result of a race-free merge-safety-copy capture attempt — the active-workflow
/// check, schema-capability resolution, and snapshot capture all happen inside one
/// <see cref="IKnownFirstDatabase.ExecuteSnapshotAsync{T}"/> callback, closing the same time-of-check/
/// time-of-use gap the Schema-7-only version already closed.</summary>
public abstract record MergeSafetyCopyCaptureEnvelope;

public sealed record MergeSafetyCopyCaptureBlocked : MergeSafetyCopyCaptureEnvelope;

public sealed record MergeSafetyCopySchema7Captured(
    BackupSnapshot Snapshot,
    ValidatedSchema7Capability Capability) : MergeSafetyCopyCaptureEnvelope;

public sealed record MergeSafetyCopySchema8Captured(
    Schema8BackupSnapshot Snapshot,
    ValidatedSchema8Capability Capability) : MergeSafetyCopyCaptureEnvelope;

/// <summary>The Schema-9 counterpart of <see cref="MergeSafetyCopySchema8Captured"/>. Reuses
/// <see cref="Schema8BackupSnapshot"/> unchanged and captures through the existing meaning-centric
/// Schema-8 snapshot repository — Schema 9 changes only the <c>ReviewSessions</c> index shape, never the
/// snapshot tables or DTO shape — while retaining the honest <see cref="ValidatedSchema9Capability"/>
/// rather than fabricating a <see cref="ValidatedSchema8Capability"/> for a Schema-9 target.</summary>
public sealed record MergeSafetyCopySchema9Captured(
    Schema8BackupSnapshot Snapshot,
    ValidatedSchema9Capability Capability) : MergeSafetyCopyCaptureEnvelope;

public static class BackupMergeSafetyCopySnapshotCapture
{
    public static MergeSafetyCopyCaptureEnvelope CaptureForMergeSafetyCopy(SQLiteConnection connection)
    {
        var capability = BackupSchemaCapability.Resolve(connection);
        switch (capability)
        {
            case Schema7CapabilityResult schema7:
                var v1Result = BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection);
                return v1Result.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow
                    ? new MergeSafetyCopyCaptureBlocked()
                    : new MergeSafetyCopySchema7Captured(v1Result.Snapshot!, schema7.Capability);

            case Schema8CapabilityResult schema8:
                var v2Result = Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection);
                return v2Result.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow
                    ? new MergeSafetyCopyCaptureBlocked()
                    : new MergeSafetyCopySchema8Captured(v2Result.Snapshot!, schema8.Capability);

            case Schema9CapabilityResult schema9:
                var v2ResultForSchema9 = Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(connection);
                return v2ResultForSchema9.Status == PortableSnapshotCaptureStatus.BlockedByActiveWorkflow
                    ? new MergeSafetyCopyCaptureBlocked()
                    : new MergeSafetyCopySchema9Captured(v2ResultForSchema9.Snapshot!, schema9.Capability);

            default:
                throw new InvalidOperationException("Unrecognized backup schema capability result.");
        }
    }
}
