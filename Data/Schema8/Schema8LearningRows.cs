using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;

namespace KnownFirst.Data.Schema8;

// Plain, policy-free row projections for the Schema-8 learning paths (KF-MEANING-001 Slice 4). None of
// these are sqlite-net [Table]-mapped entities and none are ever registered in
// DatabaseSchema.InitializeAsync's CreateTableAsync<T>() sequence. The projections that already exist for
// Schema-8 backup capture (Schema8CardRow, Schema8ReviewRow, Schema8QueueRow) and for the migration
// (SenseRow, AnswerVariantRow, SenseAnswerVariantAssignmentRow, AnswerVariantProgressRow) are reused rather
// than duplicated; only genuinely new shapes live here.

/// <summary>
/// Single source of truth for comparing persisted Schema-8 timestamps (KF-MEANING-001 Slice 4).
/// <para>
/// sqlite-net stores <see cref="DateTime"/> as int64 ticks by default and returns values with
/// <see cref="DateTimeKind.Unspecified"/>, so equality must be evaluated on ticks after normalizing both
/// operands to UTC semantics — never as a SQL-level comparison of the raw column and never with a
/// wall-clock tolerance. This mirrors the established <c>EnsureUtc</c> pattern in
/// <c>Services/DataSafety/BackupModelMapperV2</c>.
/// </para>
/// </summary>
public static class Schema8Utc
{
    public static DateTime Normalize(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? Normalize(DateTime? value) =>
        value.HasValue ? Normalize(value.Value) : null;

    /// <summary>Exact tick equality after UTC normalization. Two nulls are equal; null never equals a value.</summary>
    public static bool AreSameInstant(DateTime? left, DateTime? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return !left.HasValue && !right.HasValue;
        }

        return Normalize(left.Value).Ticks == Normalize(right.Value).Ticks;
    }

    public static bool AreSameInstant(DateTime left, DateTime right) =>
        Normalize(left).Ticks == Normalize(right).Ticks;
}

/// <summary>One learning-queue row joined with the card reference and its frozen answer target.</summary>
public sealed class Schema8QueueTargetRow
{
    public int Id { get; set; }
    public string? StableId { get; set; }
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

/// <summary>
/// One assignment of a <c>(SenseId, CardDirection)</c> joined with its answer variant, i.e. the complete
/// attribution surface for one card direction.
/// </summary>
public sealed class Schema8AttributionCandidateRow
{
    public int AssignmentId { get; set; }
    public string AssignmentStableId { get; set; } = string.Empty;
    public int SenseId { get; set; }
    public CardDirection CardDirection { get; set; }
    public int AnswerVariantId { get; set; }
    public AnswerVariantRequirement Requirement { get; set; }
    public bool IsPreferred { get; set; }
    public DateTime? RequiredSinceUtc { get; set; }
    public string AnswerLanguage { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;

    public bool IsRequired => Requirement == AnswerVariantRequirement.Required;
}

/// <summary>Learning-session counters read without mapping <c>LearningSessionEntity</c>.</summary>
public sealed class Schema8SessionCounterRow
{
    public int Id { get; set; }
    public LearningSessionStatus Status { get; set; }
    public int TotalCards { get; set; }
    public int CompletedCards { get; set; }
    public int AgainCount { get; set; }
    public int HardCount { get; set; }
    public int GoodCount { get; set; }
    public int EasyCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>Word fields required to preserve the Schema-7 new-card ordering for Schema-8 cards.</summary>
public sealed class Schema8QueueWordRow
{
    public int Id { get; set; }
    public string CanonicalTerm { get; set; } = string.Empty;
    public int TotalOccurrenceCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Minimal Sense projection for the affected-Sense status rollup.</summary>
public sealed class Schema8SenseStatusRow
{
    public int Id { get; set; }
    public int WordId { get; set; }
    public SenseStatus Status { get; set; }
}

/// <summary>Scalar helper row for a single nullable timestamp projection.</summary>
public sealed class Schema8NullableTimestampRow
{
    public DateTime? Value { get; set; }
}

/// <summary>Scalar integer projection for raw Schema-8 relationship queries.</summary>
public sealed class Schema8IdRow
{
    public int Id { get; set; }
}

public sealed class Schema12LearningDayStateRow
{
    public int Id { get; set; }
    public LearningDayPhase Phase { get; set; }
    public int DayOrdinal { get; set; }
    public DateTime ActiveDayStartUtc { get; set; }
    public DateTime ActiveDayEndUtc { get; set; }
    public string FrozenTimeZoneId { get; set; } = string.Empty;
    public int FrozenCutoffMinutes { get; set; }
    public DateTime? BridgeStartedUtc { get; set; }
    public string? BridgeTargetTimeZoneId { get; set; }
    public int? BridgeTargetCutoffMinutes { get; set; }
    public DateTime? BridgeTargetUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class Schema12LearningDayGrantRow
{
    public int Id { get; set; }
    public int DayOrdinal { get; set; }
    public int WordId { get; set; }
    public int SlotOrdinal { get; set; }
    public DateTime GrantedAtUtc { get; set; }
}
