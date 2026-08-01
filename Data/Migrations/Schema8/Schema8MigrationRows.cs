using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;
using KnownFirst.Models;

namespace KnownFirst.Data.Migrations.Schema8;

// Plain migration-local row models. None of these are sqlite-net [Table]-mapped entities and none are
// ever registered in DatabaseSchema.InitializeAsync's CreateTableAsync<T>() sequence — they exist only
// to give the migration and its tests a typed shape for raw-SQL reads/writes against the Schema-7 tables
// (read only, via column-name-matched SELECT lists) and the new Schema-8 tables the migration itself
// creates. Kept outside Data/Entities per the binding dormancy boundary (architecture doc §4.8.1).

internal sealed class LegacyWordRow
{
    public int Id { get; set; }
    public string Language { get; set; } = string.Empty;
    public string CanonicalTerm { get; set; } = string.Empty;
    public TokenKind TokenKind { get; set; }
    public WordStatus Status { get; set; }
}

internal sealed class LegacyMeaningRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public string SourceLanguage { get; set; } = string.Empty;
    public string ExplanationLanguage { get; set; } = string.Empty;
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
    public string Source { get; set; } = string.Empty;
    public string SourceProject { get; set; } = string.Empty;
    public string SourcePageTitle { get; set; } = string.Empty;
    public long? SourceRevisionId { get; set; }
    public string Attribution { get; set; } = string.Empty;
    public bool ConfirmedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal sealed class LegacyCardRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public int PreferredMeaningId { get; set; }
    public CardDirection Direction { get; set; }

    // KF-MEANING-001 Slice 4: State drives the mastered compatibility row for a legacy Retired card;
    // CreatedAtUtc is the Required-epoch boundary of the migration-created primary assignment; both
    // CreatedAtUtc and LastReviewedAtUtc are the deterministic (never migration-execution-time) source of
    // the compatibility row's own timestamps.
    public CardState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastReviewedAtUtc { get; set; }
}

internal sealed class LegacyContextRow
{
    public int Id { get; set; }
    public int MeaningId { get; set; }
}

internal sealed class LegacyReviewRow
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public bool WasTypedAnswer { get; set; }
    public bool WasCorrect { get; set; }
}

internal sealed class LegacyQueueRow
{
    public int Id { get; set; }
    public int CardId { get; set; }
}

internal sealed class LegacyWordAutomaticStateRow
{
    public int Id { get; set; }
    public LearningInteractionMode AutomaticInteractionMode { get; set; }
    public int ConsecutiveRecallSuccessCount { get; set; }
    public int ConsecutiveTypingSuccessCount { get; set; }
    public int ConsecutiveTypingFailureCount { get; set; }
    public bool MasteryReviewExtensionScheduled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal sealed class TableColumnInfo
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>One row of <c>Senses</c> (Schema 8, migration-local shape — architecture doc §2.2).</summary>
public sealed class SenseRow
{
    public int Id { get; set; }
    public string StableId { get; set; } = string.Empty;
    public int WordId { get; set; }
    public string SourceLanguage { get; set; } = string.Empty;
    public string ExplanationLanguage { get; set; } = string.Empty;
    public string ProviderSenseId { get; set; } = string.Empty;
    public string TopicOrDomain { get; set; } = string.Empty;
    public string PartOfSpeech { get; set; } = string.Empty;
    public string GrammaticalRelationship { get; set; } = string.Empty;
    public string AcronymExpansion { get; set; } = string.Empty;
    public int? DefaultMeaningId { get; set; }
    public SenseStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One row of <c>AnswerVariants</c> (Schema 8, migration-local shape — architecture doc §2.5).</summary>
public sealed class AnswerVariantRow
{
    public int Id { get; set; }
    public string StableId { get; set; } = string.Empty;
    public int SenseId { get; set; }
    public string AnswerLanguage { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public int? SourceMeaningId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One row of <c>SenseAnswerVariantAssignments</c> (Schema 8 — architecture doc §2.6).</summary>
public sealed class SenseAnswerVariantAssignmentRow
{
    public int Id { get; set; }
    public string StableId { get; set; } = string.Empty;
    public int SenseId { get; set; }
    public CardDirection CardDirection { get; set; }
    public int AnswerVariantId { get; set; }
    public AnswerVariantRequirement Requirement { get; set; }
    public bool IsPreferred { get; set; }

    /// <summary>
    /// KF-MEANING-001 Slice 4. Non-null exactly when <see cref="Requirement"/> is
    /// <see cref="AnswerVariantRequirement.Required"/>. Doubles as the Required-epoch identity: a
    /// replay-owned progress row belongs to the current epoch only when its own <c>CreatedAtUtc</c> is
    /// tick-equal to this value after UTC normalization.
    /// </summary>
    public DateTime? RequiredSinceUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One row of <c>AnswerVariantProgress</c> (Schema 8 — architecture doc §2.9).</summary>
public sealed class AnswerVariantProgressRow
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public int AnswerVariantId { get; set; }
    public LearningInteractionMode InteractionMode { get; set; }
    public int ConsecutiveReadingSuccessCount { get; set; }
    public int ConsecutiveTypingSuccessCount { get; set; }
    public int ConsecutiveTypingFailureCount { get; set; }
    public DateTime? LastAssessedAtUtc { get; set; }
    public bool MasteryReviewExtensionScheduled { get; set; }
    public bool IsMastered { get; set; }
    public int ReplayVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Migrated Schema-8 shape for one <c>LearningCards</c> row (read-only projection for tests).</summary>
public sealed class MigratedCardRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public int? SenseId { get; set; }
    public int PreferredMeaningId { get; set; }
    public CardDirection Direction { get; set; }
}

/// <summary>Migrated Schema-8 shape for one <c>Meanings</c> row (read-only projection for tests).</summary>
public sealed class MigratedMeaningRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public int? SenseId { get; set; }
    public string StableId { get; set; } = string.Empty;
}
