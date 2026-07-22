using SQLite;

namespace KnownFirst.Data.Entities;

[Table("SentenceSpans")]
public sealed class SentenceSpanEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_SentenceSpans_Document_Order", 1, Unique = true)]
    public int DocumentId { get; set; }

    public int StartPosition { get; set; }

    public int Length { get; set; }

    [Indexed("IX_SentenceSpans_Document_Order", 2, Unique = true)]
    public int Order { get; set; }
}
