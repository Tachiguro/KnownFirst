using SQLite;
using KnownFirst.Core.Text;

namespace KnownFirst.Data.Entities;

[Table("WordOccurrences")]
public sealed class WordOccurrenceEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_WordOccurrences_WordId", 1)]
    public int WordId { get; set; }

    [Indexed("IX_WordOccurrences_DocumentId", 1)]
    public int DocumentId { get; set; }

    [Indexed("IX_WordOccurrences_Sentence_Order", 1)]
    public int SentenceSpanId { get; set; }

    public int StartPosition { get; set; }

    public int Length { get; set; }

    public string SurfaceForm { get; set; } = string.Empty;

    public TechnicalTokenFamily TechnicalFamily { get; set; }

    public int? TechnicalInstanceYear { get; set; }

    public string TechnicalInstanceIdentifier { get; set; } = string.Empty;

    public string TechnicalVariant { get; set; } = string.Empty;

    [Indexed("IX_WordOccurrences_Sentence_Order", 2)]
    public int Order { get; set; }
}
