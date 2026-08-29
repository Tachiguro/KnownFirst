using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Data.Migrations.Schema13;

public sealed class LegacyWordKnownRow
{
    public int Id { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class LegacyCardRow
{
    public int CardId { get; set; }
    public int? SenseId { get; set; }
    public string? SenseStableId { get; set; }
    public int Direction { get; set; }
    public int State { get; set; }
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; }
    public int SuccessfulReviewCount { get; set; }
    public int LapseCount { get; set; }
    public DateTime? LastReviewedAtUtc { get; set; }
    public int? LastRating { get; set; }
    public DateTime DueAtUtc { get; set; }
}

public sealed class LegacyReviewRow
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public int Rating { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
}

public sealed record MigratedWordLearningControl(
    int WordId,
    string DecidedAtUtc);

public sealed record MigratedReviewHistoryEntry(
    string StableId,
    int CardId,
    int SequenceNumber,
    int Rating,
    string ReviewedAtUtc);

public sealed record MigratedCardState(
    int CardId,
    Fsrs6Card Card);

public sealed record Schema13BootstrapPlan(
    IReadOnlyList<MigratedWordLearningControl> WordControls,
    IReadOnlyList<MigratedReviewHistoryEntry> ReviewHistory,
    IReadOnlyList<MigratedCardState> CardStates);
