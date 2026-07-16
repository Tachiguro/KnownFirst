using KnownFirst.Core.Text;
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

    public string SourceLanguage { get; set; } = string.Empty;

    public string DisplayTerm { get; set; } = string.Empty;

    public TokenKind TokenKind { get; set; } = TokenKind.Word;

    public string SelectedMeaningId { get; set; } = string.Empty;

    public string AcronymExpansion { get; set; } = string.Empty;

    public string Translation { get; set; } = string.Empty;

    public string Definition { get; set; } = string.Empty;

    public string DictionaryExample { get; set; } = string.Empty;

    public string AdditionalNote { get; set; } = string.Empty;

    public string AcceptedAliasesJson { get; set; } = "[]";

    public string TranslationOrDefinition { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string SourceProject { get; set; } = string.Empty;

    public string SourcePageTitle { get; set; } = string.Empty;

    public long? SourceRevisionId { get; set; }

    public string Attribution { get; set; } = string.Empty;

    public bool ConfirmedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime PreparedAt { get; set; }
}
