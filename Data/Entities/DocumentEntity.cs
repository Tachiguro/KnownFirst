using SQLite;
using KnownFirst.Core.Preparation;

namespace KnownFirst.Data.Entities;

[Table("Documents")]
public sealed class DocumentEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string TextLanguage { get; set; } = string.Empty;

    public string ExplanationLanguage { get; set; } = string.Empty;

    public LexicalLookupMode LookupMode { get; set; }

    public string TargetLanguage { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [Indexed]
    public string ContentFingerprint { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; }

    public int WordCount { get; set; }
}
