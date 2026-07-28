using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;

namespace KnownFirst.Data.Schema8;

// Plain, migration-local row models for Schema-8 backup capture/restore (KF-MEANING-001 Slice 2). None
// of these are sqlite-net [Table]-mapped entities and none are ever registered in
// DatabaseSchema.InitializeAsync's CreateTableAsync<T>() sequence — they exist only to give backup
// capture/restore a typed shape for raw-SQL reads/writes against the Schema-8 shape of the tables the
// migration changes (Meanings, ContextSnapshots, LearningCards, LearningReviews, LearningSessionCards).
// Kept outside Data/Entities per the binding dormancy boundary (architecture doc §4.8.1) — no existing
// entity class is modified. Tables unaffected by the Schema-8 migration are still captured via the
// existing Data/Entities classes.

public sealed class Schema8MeaningRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public int? SenseId { get; set; }
    public string StableId { get; set; } = string.Empty;
    public string ExplanationLanguage { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string DisplayTerm { get; set; } = string.Empty;
    public string EncounteredSurfaceForm { get; set; } = string.Empty;
    public string GrammaticalRelationship { get; set; } = string.Empty;
    public TokenKind TokenKind { get; set; }
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

public sealed class Schema8ContextRow
{
    public int Id { get; set; }
    public int MeaningId { get; set; }
    public int? SenseId { get; set; }
    public int WordId { get; set; }
    public int SourceDocumentId { get; set; }
    public string SourceDocumentTitle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int TargetStart { get; set; }
    public int TargetLength { get; set; }
    public string NormalizedFingerprint { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Schema8CardRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public int? SenseId { get; set; }
    public int PreferredMeaningId { get; set; }
    public CardDirection Direction { get; set; }
    public CardState State { get; set; }
    public DateTime DueAtUtc { get; set; }
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; }
    public int SuccessfulReviewCount { get; set; }
    public int LapseCount { get; set; }
    public DateTime? LastReviewedAtUtc { get; set; }
    public ReviewRating? LastRating { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class Schema8ReviewRow
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public int SessionId { get; set; }
    public ReviewRating Rating { get; set; }
    public bool WasTypedAnswer { get; set; }
    public bool WasCorrect { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; }
    public int? TargetAnswerVariantId { get; set; }
    public int? MatchedAnswerVariantId { get; set; }
}

public sealed class Schema8QueueRow
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int CardId { get; set; }
    public int QueueOrder { get; set; }
    public bool IsDueCard { get; set; }
    public bool IsAgainRepeat { get; set; }
    public bool AnswerRevealed { get; set; }
    public bool SpellingChecked { get; set; }
    public bool SpellingCorrect { get; set; }
    public bool IsCompleted { get; set; }
    public ReviewRating? Rating { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? TargetAnswerVariantId { get; set; }
}
