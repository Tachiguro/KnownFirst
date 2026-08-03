namespace KnownFirst.Data.Migrations.Schema9;

/// <summary>
/// Raw DDL for the Schema-9 <c>ReviewSessions</c> index shape. Issued only from inside
/// <see cref="Schema9DormantMigration"/> — never from <c>DatabaseSchema.InitializeAsync</c>.
/// </summary>
internal static class Schema9Ddl
{
    public const string NormalIndexName = "IX_ReviewSessions_DocumentId";

    public const string ActiveIndexName = "IX_ReviewSessions_DocumentId_Active";

    /// <summary>The exact predicate the partial unique Active-session index declares.</summary>
    public const string ActivePredicateSql = "Status = 0";

    public const string CreateNormalIndex =
        $"CREATE INDEX {NormalIndexName} ON ReviewSessions (DocumentId)";

    public const string CreateActiveIndex = $"""
        CREATE UNIQUE INDEX {ActiveIndexName}
        ON ReviewSessions (DocumentId)
        WHERE {ActivePredicateSql}
        """;
}
