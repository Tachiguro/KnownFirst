using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>
/// A validated Schema-7 database for preparation purposes, proven by reading and checking
/// <c>PRAGMA user_version</c> plus the database's physical shape. Constructible only by
/// <see cref="PreparationSchemaCapability.Resolve"/> (KF-MEANING-001 Slice 3). Deliberately independent of
/// <c>Services/DataSafety/BackupSchemaCapability</c> — preparation must not depend on a backup-specific
/// capability type, even though both ultimately reuse the same <see cref="Schema8ShapeValidator"/>.
/// </summary>
public sealed class ValidatedPreparationSchema7Capability
{
    internal ValidatedPreparationSchema7Capability()
    {
    }

    public const int SchemaVersion = 7;
}

/// <summary>The Schema-8 counterpart of <see cref="ValidatedPreparationSchema7Capability"/>.</summary>
public sealed class ValidatedPreparationSchema8Capability
{
    internal ValidatedPreparationSchema8Capability()
    {
    }

    public const int SchemaVersion = 8;
}

public abstract record PreparationSchemaCapabilityResult;

public sealed record PreparationSchema7CapabilityResult(ValidatedPreparationSchema7Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema8CapabilityResult(ValidatedPreparationSchema8Capability Capability)
    : PreparationSchemaCapabilityResult;

/// <summary>
/// Thrown by <see cref="PreparationSchemaCapability.Resolve"/> for every rejection case: an unsupported
/// <c>PRAGMA user_version</c> value, or a version whose physical shape disagrees with what that version
/// requires (fail-closed — never silently trusts the version number alone). Stable, preparation-specific
/// error codes; never <c>Services.DataSafety.BackupSchemaCapabilityException</c>.
/// </summary>
public sealed class PreparationSchemaCapabilityException : Exception
{
    public PreparationSchemaCapabilityException(int foundVersion, bool shapeMismatch)
        : base(BuildMessage(foundVersion, shapeMismatch))
    {
        FoundVersion = foundVersion;
        ShapeMismatch = shapeMismatch;
    }

    public int FoundVersion { get; }

    public bool ShapeMismatch { get; }

    public string ErrorCode => ShapeMismatch
        ? "preparation-schema-capability-shape-mismatch"
        : "preparation-schema-capability-unsupported-version";

    private static string BuildMessage(int foundVersion, bool shapeMismatch) => shapeMismatch
        ? $"Database reports PRAGMA user_version {foundVersion} but its physical shape does not match that version."
        : $"PRAGMA user_version {foundVersion} is not a supported preparation source/target version; only 7 and 8 are accepted.";
}

/// <summary>
/// Trusted, single-source schema-capability check for the preparation subsystem (KF-MEANING-001 Slice 3).
/// Reads <c>PRAGMA user_version</c>, accepts exactly 7 or 8, validates the expected physical shape for
/// whichever version was reported via the same <see cref="Schema8ShapeValidator"/> the backup subsystem
/// already uses, and fails closed (throws <see cref="PreparationSchemaCapabilityException"/>) if the
/// version and the physical shape disagree, or if any other version is reported. Never infers capability
/// from optional table/column presence alone, and never calls into
/// <c>Services.DataSafety.BackupSchemaCapability</c> — the two capability resolvers are independent
/// call paths that happen to share the same underlying shape validator.
/// </summary>
public static class PreparationSchemaCapability
{
    public static PreparationSchemaCapabilityResult Resolve(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var userVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
        switch (userVersion)
        {
            case ValidatedPreparationSchema7Capability.SchemaVersion:
                if (!Schema8ShapeValidator.IsValidSchema7Shape(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema7CapabilityResult(new ValidatedPreparationSchema7Capability());

            case ValidatedPreparationSchema8Capability.SchemaVersion:
                if (!Schema8ShapeValidator.IsValidShape(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema8CapabilityResult(new ValidatedPreparationSchema8Capability());

            default:
                throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: false);
        }
    }
}
