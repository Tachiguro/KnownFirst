using SQLite;

namespace KnownFirst.Data.Entities;

[Table("Meanings")]
public sealed class MeaningEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_Meanings_WordId", 1)]
    public int WordId { get; set; }

    public string ExplanationLanguage { get; set; } = string.Empty;

    public string TranslationOrDefinition { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public bool ConfirmedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
