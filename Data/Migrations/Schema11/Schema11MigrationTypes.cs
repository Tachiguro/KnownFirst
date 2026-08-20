namespace KnownFirst.Data.Migrations.Schema11;

public enum Schema11MigrationOutcome
{
    Migrated,
    AlreadyApplied
}

public sealed record Schema11MigrationResult(
    Schema11MigrationOutcome Outcome,
    int SourceVersion,
    int TargetVersion);

public sealed class Schema11MigrationException : Exception
{
    private Schema11MigrationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static Schema11MigrationException FutureVersion(int foundVersion) =>
        new(
            "schema11-migration-future-version",
            $"Database schema version {foundVersion} is newer than this migration's target version {Schema11DormantMigration.TargetVersion}; refusing to guess.");

    public static Schema11MigrationException UnsupportedSourceVersion(int foundVersion) =>
        new(
            "schema11-migration-unsupported-source-version",
            $"Database schema version {foundVersion} is not a supported migration source; only version {Schema11DormantMigration.SourceVersion} can migrate to {Schema11DormantMigration.TargetVersion}.");

    public static Schema11MigrationException InvariantViolation(string detail) =>
        new("schema11-migration-invariant-violation", $"Migration invariant violated: {detail}");

    public static Schema11MigrationException AlreadyAppliedShapeInvalid(string detail) =>
        new(
            "schema11-migration-already-applied-shape-invalid",
            $"Database reports schema version {Schema11DormantMigration.TargetVersion} but its Schema-11 shape is invalid: {detail}");
}
