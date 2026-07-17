using KnownFirst.Core.Text;
using KnownFirst.Core.Preparation;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LexicalCache")]
public sealed class LexicalCacheEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Unique = true)]
    public string CacheKey { get; set; } = string.Empty;

    public string SourceLanguage { get; set; } = string.Empty;

    public string ExplanationLanguage { get; set; } = string.Empty;

    public string NormalizedLemma { get; set; } = string.Empty;

    public LexicalLookupMode LookupMode { get; set; }

    public string TargetLanguage { get; set; } = string.Empty;

    public string CanonicalLookupTerm { get; set; } = string.Empty;

    public TokenKind TokenKind { get; set; }

    public string Provider { get; set; } = string.Empty;

    public int ProviderSchemaVersion { get; set; }

    public string ResultJson { get; set; } = string.Empty;

    public string SourceProject { get; set; } = string.Empty;

    public string PageTitle { get; set; } = string.Empty;

    public long? RevisionId { get; set; }

    public string Attribution { get; set; } = string.Empty;

    public DateTime FetchedAtUtc { get; set; }
}
