namespace KnownFirst.Data.Migrations.Schema9;

public enum Schema9MigrationOutcome
{
    Migrated,
    AlreadyApplied
}

public sealed record Schema9MigrationResult(
    Schema9MigrationOutcome Outcome,
    int SourceVersion,
    int TargetVersion);

/// <summary>
/// Thrown by <see cref="Schema9DormantMigration"/> for every explicit rejection/failure case: an
/// incompatible source version, an unresolvable legacy <c>ReviewSessions(DocumentId)</c> index, or
/// fail-closed invariant corruption detected mid-migration. Every case rolls back the enclosing
/// transaction — construction of this exception never happens after a partial commit.
/// </summary>
public sealed class Schema9MigrationException : Exception
{
    private Schema9MigrationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static Schema9MigrationException FutureVersion(int foundVersion) =>
        new(
            "schema9-migration-future-version",
            $"Database schema version {foundVersion} is newer than this migration's target version {Schema9DormantMigration.TargetVersion}; refusing to guess.");

    public static Schema9MigrationException UnsupportedSourceVersion(int foundVersion) =>
        new(
            "schema9-migration-unsupported-source-version",
            $"Database schema version {foundVersion} is not a supported migration source; only version {Schema9DormantMigration.SourceVersion} can migrate to {Schema9DormantMigration.TargetVersion}.");

    public static Schema9MigrationException MissingLegacyIndex() =>
        new(
            "schema9-migration-missing-legacy-index",
            "No non-partial unique single-column ReviewSessions(DocumentId) index was found on the Schema-8 source; refusing to guess which index to replace.");

    public static Schema9MigrationException AmbiguousLegacyIndex(int matchCount) =>
        new(
            "schema9-migration-ambiguous-legacy-index",
            $"{matchCount} non-partial unique single-column ReviewSessions(DocumentId) indexes were found on the Schema-8 source; refusing to guess which one to replace.");

    public static Schema9MigrationException InvariantViolation(string detail) =>
        new("schema9-migration-invariant-violation", $"Migration invariant violated: {detail}");

    public static Schema9MigrationException AlreadyAppliedShapeInvalid(string detail) =>
        new(
            "schema9-migration-already-applied-shape-invalid",
            $"Database reports schema version {Schema9DormantMigration.TargetVersion} but its Schema-9 shape is invalid: {detail}");
}
