using SQLite;

namespace KnownFirst.Data.Entities;

[Table("WordForms")]
public sealed class WordFormEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_WordForms_WordId", 1)]
    public int WordId { get; set; }

    public string SurfaceForm { get; set; } = string.Empty;

    public int OccurrenceCount { get; set; }
}
