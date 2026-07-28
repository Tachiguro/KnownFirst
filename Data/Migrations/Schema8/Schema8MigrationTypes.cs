namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// The Sense-level learning progression tier (KF-MEANING-001 architecture doc §2.2). Distinct from
/// <see cref="KnownFirst.Models.WordStatus"/>, which after migration represents only global word review
/// judgment and never Sense progression.
/// </summary>
public enum SenseStatus
{
    Prepared = 0,
    Learning = 1,
    Mastered = 2,
    Suspended = 3
}

/// <summary>
/// Direction-specific mastery requirement for one Sense/AnswerVariant assignment (architecture doc §2.6).
/// </summary>
public enum AnswerVariantRequirement
{
    Required = 0,
    AcceptedOnly = 1
}

public enum Schema8MigrationOutcome
{
    Migrated,
    AlreadyApplied
}

public sealed record Schema8MigrationResult(
    Schema8MigrationOutcome Outcome,
    int SourceVersion,
    int TargetVersion,
    bool UsedColumnRebuildFallback);

/// <summary>
/// Thrown by <see cref="Schema8DormantMigration"/> for every explicit rejection/failure case: an
/// incompatible source version, or fail-closed referential/invariant corruption detected mid-migration.
/// Every case rolls back the enclosing transaction — construction of this exception never happens after
/// a partial commit.
/// </summary>
public sealed class Schema8MigrationException : Exception
{
    private Schema8MigrationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static Schema8MigrationException FutureVersion(int foundVersion) =>
        new(
            "schema8-migration-future-version",
            $"Database schema version {foundVersion} is newer than this migration's target version {Schema8DormantMigration.TargetVersion}; refusing to guess.");

    public static Schema8MigrationException UnsupportedSourceVersion(int foundVersion) =>
        new(
            "schema8-migration-unsupported-source-version",
            $"Database schema version {foundVersion} is not a supported migration source; only version {Schema8DormantMigration.SourceVersion} can migrate to {Schema8DormantMigration.TargetVersion}.");

    public static Schema8MigrationException ReferentialCorruption(string detail) =>
        new("schema8-migration-referential-corruption", $"Migration aborted (fail-closed): {detail}");

    public static Schema8MigrationException InvariantViolation(string detail) =>
        new("schema8-migration-invariant-violation", $"Migration invariant violated: {detail}");

    public static Schema8MigrationException AlreadyAppliedShapeInvalid(string detail) =>
        new(
            "schema8-migration-already-applied-shape-invalid",
            $"Database reports schema version {Schema8DormantMigration.TargetVersion} but its Schema-8 shape is invalid: {detail}");

    public static Schema8MigrationException FaultInjected(string checkpoint) =>
        new("schema8-migration-fault-injected", $"Test fault injected at checkpoint '{checkpoint}'.");
}

/// <summary>
/// Test-only knobs for <see cref="Schema8DormantMigration.ApplyAsync"/>. Never referenced by production
/// code — the default (<c>new Schema8MigrationOptions()</c>) is exactly ordinary production behavior.
/// </summary>
public sealed class Schema8MigrationOptions
{
    /// <summary>
    /// Invoked at named checkpoints between migration steps. A test can throw from this hook to prove
    /// crash-mid-migration rollback at that exact boundary. Never invoked outside the migration's own
    /// transaction, so a thrown exception always rolls back the whole migration.
    /// </summary>
    public Action<string>? FaultInjectionHook { get; init; }

    /// <summary>
    /// Forces the portable table-rebuild fallback for the <c>LearningCards.MeaningId -&gt;
    /// PreferredMeaningId</c> rename, even when the bundled SQLite supports
    /// <c>ALTER TABLE ... RENAME COLUMN</c>. Lets the fallback path be proven correct without needing an
    /// old SQLite build.
    /// </summary>
    public bool ForceColumnRebuildFallback { get; init; }
}
