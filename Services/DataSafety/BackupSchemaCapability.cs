using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Migrations.Schema10;
using SQLite;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// A validated Schema-7 database, proven by reading and checking <c>PRAGMA user_version</c> plus the
/// database's physical shape. Constructible only by <see cref="BackupSchemaCapability.Resolve"/> — it
/// is deliberately not claimed to be unforgeable across the whole assembly (an <c>internal</c>
/// constructor is visible to every file compiled into this assembly, including tests); the guarantee
/// this type actually provides is that <see cref="BackupArchiveWriter.WriteArchiveAsync"/> and
/// <see cref="BackupArchiveWriterV2.WriteArchiveAsync"/> have statically different, non-interchangeable
/// signatures, so passing a Schema-8-derived capability to the v1 writer (or vice versa) is a compile
/// error, not a runtime possibility to defend against.
/// </summary>
public sealed class ValidatedSchema7Capability
{
    internal ValidatedSchema7Capability()
    {
    }

    public const int SchemaVersion = 7;
}

/// <summary>The Schema-8 counterpart of <see cref="ValidatedSchema7Capability"/>.</summary>
public sealed class ValidatedSchema8Capability
{
    internal ValidatedSchema8Capability()
    {
    }

    public const int SchemaVersion = 8;
}

/// <summary>The Schema-9 counterpart of <see cref="ValidatedSchema8Capability"/> (index-only Schema 8 -&gt;
/// 9 activation). Schema 9 shares Schema 8's meaning-centric data model and payload-v2 code paths exactly
/// — only the <c>ReviewSessions</c> index shape differs.</summary>
public sealed class ValidatedSchema9Capability
{
    internal ValidatedSchema9Capability()
    {
    }

    public const int SchemaVersion = 9;
}

/// <summary>The Schema-10 counterpart of <see cref="ValidatedSchema9Capability"/> (KF-BACKUP-005A).
/// Schema 10 keeps Schema 9's meaning-centric data model and payload-v2 code paths exactly; it adds
/// persistent <c>StableId</c> identity to <c>LearningSessions</c> and <c>LearningSessionCards</c>, which
/// is why it is an honest capability of its own rather than a Schema-9 proof object — an archive written
/// from a Schema-10 database must record source schema 10 and must carry those identities.</summary>
public sealed class ValidatedSchema10Capability
{
    internal ValidatedSchema10Capability()
    {
    }

    public const int SchemaVersion = 10;
}

public abstract record BackupSchemaCapabilityResult;

public sealed record Schema7CapabilityResult(ValidatedSchema7Capability Capability) : BackupSchemaCapabilityResult;

public sealed record Schema8CapabilityResult(ValidatedSchema8Capability Capability) : BackupSchemaCapabilityResult;

public sealed record Schema9CapabilityResult(ValidatedSchema9Capability Capability) : BackupSchemaCapabilityResult;

public sealed record Schema10CapabilityResult(ValidatedSchema10Capability Capability) : BackupSchemaCapabilityResult;

/// <summary>
/// Thrown by <see cref="BackupSchemaCapability.Resolve"/> for every rejection case: an unsupported
/// <c>PRAGMA user_version</c> value, or a version whose physical shape disagrees with what that
/// version requires (fail-closed — never silently trusts the version number alone).
/// </summary>
public sealed class BackupSchemaCapabilityException : Exception
{
    public BackupSchemaCapabilityException(int foundVersion, bool shapeMismatch)
        : base(BuildMessage(foundVersion, shapeMismatch))
    {
        FoundVersion = foundVersion;
        ShapeMismatch = shapeMismatch;
    }

    public int FoundVersion { get; }

    public bool ShapeMismatch { get; }

    public string ErrorCode => ShapeMismatch
        ? "backup-schema-capability-shape-mismatch"
        : "backup-schema-capability-unsupported-version";

    private static string BuildMessage(int foundVersion, bool shapeMismatch) => shapeMismatch
        ? $"Database reports PRAGMA user_version {foundVersion} but its physical shape does not match that version."
        : $"PRAGMA user_version {foundVersion} is not a supported backup source/target version; only 7, 8, 9, and 10 are accepted.";
}

/// <summary>
/// Trusted, single-source schema-capability check for the backup/restore subsystem (KF-MEANING-001
/// Slice 2, architecture doc §4.8.2). Reads <c>PRAGMA user_version</c>, accepts exactly 7 or 8,
/// validates the expected physical shape for whichever version was reported, and fails closed
/// (throws <see cref="BackupSchemaCapabilityException"/>) if the version and the physical shape
/// disagree, or if any other version is reported. Never infers capability from optional table or
/// column presence alone — <c>PRAGMA user_version</c> is always read first and is authoritative,
/// physical shape is a secondary corroborating check, not the primary signal.
/// </summary>
public static class BackupSchemaCapability
{
    public static BackupSchemaCapabilityResult Resolve(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var userVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
        switch (userVersion)
        {
            case ValidatedSchema7Capability.SchemaVersion:
                if (!Schema8ShapeValidator.IsValidSchema7Shape(connection, out _))
                {
                    throw new BackupSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new Schema7CapabilityResult(new ValidatedSchema7Capability());

            case ValidatedSchema8Capability.SchemaVersion:
                if (!Schema8ShapeValidator.IsValidShape(connection, out _))
                {
                    throw new BackupSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new Schema8CapabilityResult(new ValidatedSchema8Capability());

            case ValidatedSchema9Capability.SchemaVersion:
                if (!Schema9ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new BackupSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new Schema9CapabilityResult(new ValidatedSchema9Capability());

            case ValidatedSchema10Capability.SchemaVersion:
                if (!Schema10ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new BackupSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new Schema10CapabilityResult(new ValidatedSchema10Capability());

            default:
                throw new BackupSchemaCapabilityException(userVersion, shapeMismatch: false);
        }
    }
}
