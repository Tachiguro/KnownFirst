using KnownFirst.Data.Migrations.Schema8;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>
/// A validated Schema-7 database for learning purposes, proven by reading and checking
/// <c>PRAGMA user_version</c> plus the database's physical shape. Constructible only by
/// <see cref="LearningSchemaCapability.Resolve"/> (KF-MEANING-001 Slice 4). Deliberately independent of
/// <see cref="PreparationSchemaCapability"/> and <c>Services/DataSafety/BackupSchemaCapability</c> —
/// learning must not depend on a preparation- or backup-specific capability type, even though all three
/// reuse the same <see cref="Schema8ShapeValidator"/>.
/// </summary>
public sealed class ValidatedLearningSchema7Capability
{
    internal ValidatedLearningSchema7Capability()
    {
    }

    public const int SchemaVersion = 7;
}

/// <summary>The Schema-8 counterpart of <see cref="ValidatedLearningSchema7Capability"/>.</summary>
public sealed class ValidatedLearningSchema8Capability
{
    internal ValidatedLearningSchema8Capability()
    {
    }

    public const int SchemaVersion = 8;
}

public abstract record LearningSchemaCapabilityResult;

public sealed record LearningSchema7CapabilityResult(ValidatedLearningSchema7Capability Capability)
    : LearningSchemaCapabilityResult;

public sealed record LearningSchema8CapabilityResult(ValidatedLearningSchema8Capability Capability)
    : LearningSchemaCapabilityResult;

/// <summary>
/// Thrown by <see cref="LearningSchemaCapability.Resolve"/> for every rejection: an unsupported
/// <c>PRAGMA user_version</c>, or a version whose physical shape disagrees with what that version requires
/// (fail-closed — never trusts the version number alone). Stable, learning-specific error codes.
/// </summary>
public sealed class LearningSchemaCapabilityException : Exception
{
    public LearningSchemaCapabilityException(int foundVersion, bool shapeMismatch, string? shapeDetail = null)
        : base(BuildMessage(foundVersion, shapeMismatch, shapeDetail))
    {
        FoundVersion = foundVersion;
        ShapeMismatch = shapeMismatch;
        ShapeDetail = shapeDetail;
    }

    public int FoundVersion { get; }

    public bool ShapeMismatch { get; }

    public string? ShapeDetail { get; }

    public string ErrorCode => ShapeMismatch
        ? "learning-schema-capability-shape-mismatch"
        : "learning-schema-capability-unsupported-version";

    private static string BuildMessage(int foundVersion, bool shapeMismatch, string? shapeDetail) => shapeMismatch
        ? $"Database reports PRAGMA user_version {foundVersion} but its physical shape does not match that version: {shapeDetail}"
        : $"PRAGMA user_version {foundVersion} is not a supported learning source version; only 7 and 8 are accepted.";
}

/// <summary>
/// Resolves the active learning schema from the validated <c>PRAGMA user_version</c> plus the physical
/// shape (KF-MEANING-001 Slice 4, architecture doc §4.8.2). Schema 8 is never inferred from the presence of
/// a single optional table or column: the shared <see cref="Schema8ShapeValidator"/> decides, and since
/// Slice 4 it additionally requires <c>SenseAnswerVariantAssignments.RequiredSinceUtc</c>, so an incomplete
/// version-8 shape fails here rather than later through a raw "no such column" SQL error.
/// </summary>
public static class LearningSchemaCapability
{
    public static LearningSchemaCapabilityResult Resolve(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var userVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
        switch (userVersion)
        {
            case ValidatedLearningSchema7Capability.SchemaVersion:
                if (!Schema8ShapeValidator.IsValidSchema7Shape(connection, out var schema7Detail))
                {
                    throw new LearningSchemaCapabilityException(userVersion, shapeMismatch: true, schema7Detail);
                }

                return new LearningSchema7CapabilityResult(new ValidatedLearningSchema7Capability());

            case ValidatedLearningSchema8Capability.SchemaVersion:
                if (!Schema8ShapeValidator.IsValidShape(connection, out var schema8Detail))
                {
                    throw new LearningSchemaCapabilityException(userVersion, shapeMismatch: true, schema8Detail);
                }

                return new LearningSchema8CapabilityResult(new ValidatedLearningSchema8Capability());

            default:
                throw new LearningSchemaCapabilityException(userVersion, shapeMismatch: false);
        }
    }
}
