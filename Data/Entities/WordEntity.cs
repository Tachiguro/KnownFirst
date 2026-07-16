using KnownFirst.Core.Text;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("Words")]
public sealed class WordEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_Words_Language_NormalizedTerm", 1, Unique = true)]
    public string Language { get; set; } = string.Empty;

    public string CanonicalTerm { get; set; } = string.Empty;

    [Indexed("IX_Words_Language_NormalizedTerm", 2, Unique = true)]
    public string NormalizedTerm { get; set; } = string.Empty;

    [Indexed("IX_Words_Status", 1)]
    public WordStatus Status { get; set; } = WordStatus.Unreviewed;

    public TokenKind TokenKind { get; set; } = TokenKind.Word;

    public int TotalOccurrenceCount { get; set; }

    public int DocumentCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
