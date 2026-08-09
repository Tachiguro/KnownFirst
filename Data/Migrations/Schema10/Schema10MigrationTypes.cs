namespace KnownFirst.Data.Migrations.Schema10;

public enum Schema10MigrationOutcome
{
    Migrated,
    AlreadyApplied
}

public sealed record Schema10MigrationResult(
    Schema10MigrationOutcome Outcome,
    int SourceVersion,
    int TargetVersion);

/// <summary>
/// Thrown by <see cref="Schema10DormantMigration"/> for every explicit rejection/failure case: an
/// incompatible source version, an unresolvable legacy identity, or fail-closed invariant corruption
/// detected mid-migration. Every case rolls back the enclosing transaction — construction of this
/// exception never happens after a partial commit, so a failed Schema-10 activation always leaves a
/// fully intact Schema-9 database behind.
/// </summary>
public sealed class Schema10MigrationException : Exception
{
    private Schema10MigrationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static Schema10MigrationException FutureVersion(int foundVersion) =>
        new(
            "schema10-migration-future-version",
            $"Database schema version {foundVersion} is newer than this migration's target version {Schema10DormantMigration.TargetVersion}; refusing to guess.");

    public static Schema10MigrationException UnsupportedSourceVersion(int foundVersion) =>
        new(
            "schema10-migration-unsupported-source-version",
            $"Database schema version {foundVersion} is not a supported migration source; only version {Schema10DormantMigration.SourceVersion} can migrate to {Schema10DormantMigration.TargetVersion}.");

    /// <summary>
    /// Two rows bootstrapped to one identity. This can only happen when two rows are indistinguishable
    /// under the frozen bootstrap material, which the unique index forbids. Failing closed is the same
    /// choice <c>MergeWriterTargetIndex</c> already makes for a colliding review-session identity: a
    /// silent merge of two rows would destroy history, and inventing a disambiguator would break the
    /// determinism the bootstrap exists to provide.
    /// </summary>
    public static Schema10MigrationException DuplicateBootstrapIdentity(string table, string stableId) =>
        new(
            "schema10-migration-duplicate-bootstrap-identity",
            $"Two {table} rows bootstrapped to the same StableId '{stableId}'; refusing to merge or disambiguate them.");

    /// <summary>A learning card the migration must derive a semantic identity for has no resolvable Sense.</summary>
    public static Schema10MigrationException UnresolvableCardIdentity(int cardId) =>
        new(
            "schema10-migration-unresolvable-card-identity",
            $"LearningCard {cardId} has no resolvable Sense, so no deterministic learning-workflow identity can be derived from it.");

    public static Schema10MigrationException InvariantViolation(string detail) =>
        new("schema10-migration-invariant-violation", $"Migration invariant violated: {detail}");

    public static Schema10MigrationException AlreadyAppliedShapeInvalid(string detail) =>
        new(
            "schema10-migration-already-applied-shape-invalid",
            $"Database reports schema version {Schema10DormantMigration.TargetVersion} but its Schema-10 shape is invalid: {detail}");
}
