namespace KnownFirst.Data.Migrations.Schema13;

public enum Schema13MigrationOutcome
{
    Migrated,
    AlreadyApplied
}

public sealed record Schema13MigrationResult(
    Schema13MigrationOutcome Outcome,
    int SourceVersion,
    int TargetVersion);

public sealed class Schema13MigrationException : Exception
{
    public string ErrorCode { get; }

    public Schema13MigrationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public static Schema13MigrationException FutureVersion(int foundVersion) =>
        new(
            "schema13-migration-future-version",
            $"Database schema version {foundVersion} is newer than this migration's target version 13; refusing to guess.");

    public static Schema13MigrationException UnsupportedSourceVersion(int foundVersion) =>
        new(
            "schema13-migration-unsupported-source-version",
            $"Database schema version {foundVersion} is not a supported migration source; only version 12 can migrate to 13.");

    public static Schema13MigrationException InvariantViolation(string detail) =>
        new("schema13-migration-invariant-violation", $"Migration invariant violated: {detail}");

    public static Schema13MigrationException AlreadyAppliedShapeInvalid(string detail) =>
        new(
            "schema13-migration-already-applied-shape-invalid",
            $"Database reports schema version 13 but its Schema-13 shape is invalid: {detail}");

    public static Schema13MigrationException CorruptReviewRating(int reviewId, int foundRating) =>
        new(
            "schema13-migration-corrupt-review-rating",
            $"Review {reviewId} has an invalid rating {foundRating}; expected 0..3.");

    public static Schema13MigrationException MissingReviewHistory(int cardId, string detail) =>
        new(
            "schema13-migration-missing-review-history",
            $"Card {cardId} shows prior learning progress but has no surviving review history: {detail}");
}
