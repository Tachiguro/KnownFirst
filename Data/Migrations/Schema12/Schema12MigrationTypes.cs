namespace KnownFirst.Data.Migrations.Schema12;

public enum Schema12MigrationOutcome
{
    Migrated,
    AlreadyApplied
}

public sealed record Schema12MigrationResult(
    Schema12MigrationOutcome Outcome,
    int SourceVersion,
    int TargetVersion);

public sealed class Schema12MigrationException : Exception
{
    private Schema12MigrationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static Schema12MigrationException FutureVersion(int foundVersion) =>
        new(
            "schema12-migration-future-version",
            $"Database schema version {foundVersion} is newer than this migration's target version {Schema12DormantMigration.TargetVersion}; refusing to guess.");

    public static Schema12MigrationException UnsupportedSourceVersion(int foundVersion) =>
        new(
            "schema12-migration-unsupported-source-version",
            $"Database schema version {foundVersion} is not a supported migration source; only version {Schema12DormantMigration.SourceVersion} can migrate to {Schema12DormantMigration.TargetVersion}.");

    public static Schema12MigrationException InvariantViolation(string detail) =>
        new("schema12-migration-invariant-violation", $"Migration invariant violated: {detail}");

    public static Schema12MigrationException AlreadyAppliedShapeInvalid(string detail) =>
        new(
            "schema12-migration-already-applied-shape-invalid",
            $"Database reports schema version {Schema12DormantMigration.TargetVersion} but its Schema-12 shape is invalid: {detail}");
}
