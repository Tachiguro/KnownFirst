using SQLite;

namespace KnownFirst.Data.Entities;

[Table("Documents")]
public sealed class DocumentEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string TextLanguage { get; set; } = string.Empty;

    public string ExplanationLanguage { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; }

    public int WordCount { get; set; }
}
