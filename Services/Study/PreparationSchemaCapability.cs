using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Migrations.Schema10;
using KnownFirst.Data.Migrations.Schema11;
using KnownFirst.Data.Migrations.Schema12;
using KnownFirst.Data.Migrations.Schema13;
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

/// <summary>The Schema-9 counterpart of <see cref="ValidatedPreparationSchema8Capability"/> (index-only
/// Schema 8 -&gt; 9 activation; the preparation-relevant data model is unchanged).</summary>
public sealed class ValidatedPreparationSchema9Capability
{
    internal ValidatedPreparationSchema9Capability()
    {
    }

    public const int SchemaVersion = 9;
}

/// <summary>The Schema-10 counterpart of <see cref="ValidatedPreparationSchema9Capability"/>
/// (KF-BACKUP-005A). The preparation-relevant data model is untouched by Schema 10 — only the
/// learning-workflow tables gain identity columns — so preparation keeps working exactly as before.</summary>
public sealed class ValidatedPreparationSchema10Capability
{
    internal ValidatedPreparationSchema10Capability()
    {
    }

    public const int SchemaVersion = 10;
}

/// <summary>The Schema-11 counterpart of <see cref="ValidatedPreparationSchema10Capability"/>
/// (German enhanced term recognition derivation evidence persistence). The preparation-relevant
/// data model is untouched by Schema 11 — only review provenance gain evidence entries — so
/// preparation keeps working exactly as before.</summary>
public sealed class ValidatedPreparationSchema11Capability
{
    internal ValidatedPreparationSchema11Capability()
    {
    }

    public const int SchemaVersion = 11;
}

public sealed class ValidatedPreparationSchema12Capability
{
    internal ValidatedPreparationSchema12Capability()
    {
    }

    public const int SchemaVersion = 12;
}

public sealed class ValidatedPreparationSchema13Capability
{
    internal ValidatedPreparationSchema13Capability()
    {
    }

    public const int SchemaVersion = 13;
}

public abstract record PreparationSchemaCapabilityResult;

public sealed record PreparationSchema7CapabilityResult(ValidatedPreparationSchema7Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema8CapabilityResult(ValidatedPreparationSchema8Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema9CapabilityResult(ValidatedPreparationSchema9Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema10CapabilityResult(ValidatedPreparationSchema10Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema11CapabilityResult(ValidatedPreparationSchema11Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema12CapabilityResult(ValidatedPreparationSchema12Capability Capability)
    : PreparationSchemaCapabilityResult;

public sealed record PreparationSchema13CapabilityResult(ValidatedPreparationSchema13Capability Capability)
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
        : $"PRAGMA user_version {foundVersion} is not a supported preparation source/target version; only 7 through 13 are accepted.";
}

/// <summary>
/// Trusted, single-source schema-capability check for the preparation subsystem (KF-MEANING-001 Slice 3).
/// Reads <c>PRAGMA user_version</c>, accepts exactly versions 7 through 13, validates the expected physical shape for
/// whichever version was reported via the same shape validators the backup subsystem
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

            case ValidatedPreparationSchema9Capability.SchemaVersion:
                if (!Schema9ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema9CapabilityResult(new ValidatedPreparationSchema9Capability());

            case ValidatedPreparationSchema10Capability.SchemaVersion:
                if (!Schema10ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema10CapabilityResult(new ValidatedPreparationSchema10Capability());

            case ValidatedPreparationSchema11Capability.SchemaVersion:
                if (!Schema11ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema11CapabilityResult(new ValidatedPreparationSchema11Capability());

            case ValidatedPreparationSchema12Capability.SchemaVersion:
                if (!Schema12ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema12CapabilityResult(new ValidatedPreparationSchema12Capability());

            case ValidatedPreparationSchema13Capability.SchemaVersion:
                if (!Schema13ShapeValidator.IsValidDatabase(connection, out _))
                {
                    throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: true);
                }

                return new PreparationSchema13CapabilityResult(new ValidatedPreparationSchema13Capability());

            default:
                throw new PreparationSchemaCapabilityException(userVersion, shapeMismatch: false);
        }
    }
}
