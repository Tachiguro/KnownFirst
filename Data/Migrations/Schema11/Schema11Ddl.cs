namespace KnownFirst.Data.Migrations.Schema11;

internal static class Schema11Ddl
{
    public const string TableName = "DerivedTermEvidenceEntries";

    public const string OwnerIndexName = "IX_DerivedTermEvidenceEntries_ReviewCandidateId";

    public const string UniqueEvidenceIndexName = "IX_DerivedTermEvidenceEntries_Candidate_Source_Range_Component";

    public const string CreateTable = """
        CREATE TABLE DerivedTermEvidenceEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ReviewCandidateId INTEGER NOT NULL,
            SourceIdentity TEXT NOT NULL,
            SourceSurfaceForm TEXT NOT NULL,
            SourceStartPosition INTEGER NOT NULL,
            SourceLength INTEGER NOT NULL,
            SourceSentenceOrder INTEGER NOT NULL,
            ComponentForm TEXT NOT NULL
        )
        """;

    public const string CreateOwnerIndex =
        $"CREATE INDEX {OwnerIndexName} ON {TableName} (ReviewCandidateId)";

    public const string CreateUniqueEvidenceIndex =
        $"CREATE UNIQUE INDEX {UniqueEvidenceIndexName} ON {TableName} (ReviewCandidateId, SourceIdentity, SourceStartPosition, SourceLength, ComponentForm)";
}
