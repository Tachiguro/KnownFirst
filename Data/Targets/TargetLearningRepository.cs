namespace KnownFirst.Data.Targets;

using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema8;
using SQLite;

public sealed record TargetLearningRow(
    int Id,
    string StableId,
    int SenseId,
    int WordId,
    string CanonicalTerm,
    TokenKind TokenKind,
    LearningTargetKind TargetKind,
    string SourceLanguage,
    string TargetLanguage,
    bool TypingOptOut,
    Fsrs6CardState State,
    double? Stability,
    double? Difficulty,
    DateTimeOffset? LastReviewedAtUtc,
    int? StepIndex,
    DateTimeOffset? DueAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public static class TargetLearningRepository
{
    private const string TargetProjection =
        "t.Id, t.StableId, t.SenseId, s.WordId, w.CanonicalTerm, w.TokenKind, t.TargetKind, " +
        "t.SourceLanguage, t.TargetLanguage, t.TypingOptOut, f.State AS FsrsState, f.Stability, " +
        "f.Difficulty, f.LastReviewedAtUtc AS FsrsLastReviewedAtUtc, f.StepIndex, " +
        "f.DueAtUtc AS FsrsDueAtUtc, t.CreatedAtUtc, t.UpdatedAtUtc";

    public static int CountTargets(SQLiteConnection connection) =>
        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets");

    public static int CountDueTargets(SQLiteConnection connection, DateTimeOffset nowUtc)
    {
        var nowStr = Schema13TimestampCodec.FormatUtc(nowUtc);
        return connection.ExecuteScalar<int>(
            """
            SELECT COUNT(DISTINCT t.Id)
            FROM LearningTargets t
            JOIN TargetFsrsStates f ON f.TargetId = t.Id
            WHERE f.State IN (1, 2, 3)
              AND f.DueAtUtc IS NOT NULL
              AND f.DueAtUtc <= ?
              AND EXISTS (
                  SELECT 1 FROM TargetAnswerVariants v
                  WHERE v.TargetId = t.Id AND v.Requirement = 0
              )
            """,
            nowStr);
    }

    public static int CountNewWords(SQLiteConnection connection) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(DISTINCT s.WordId)
            FROM LearningTargets t
            JOIN Senses s ON s.Id = t.SenseId
            JOIN TargetFsrsStates f ON f.TargetId = t.Id
            WHERE f.State = 0
              AND EXISTS (
                  SELECT 1 FROM TargetAnswerVariants v
                  WHERE v.TargetId = t.Id AND v.Requirement = 0
              )
            """);

    public static DateTimeOffset? SelectNextDueAtUtc(SQLiteConnection connection)
    {
        var minDueStr = connection.ExecuteScalar<string?>(
            """
            SELECT MIN(f.DueAtUtc)
            FROM LearningTargets t
            JOIN TargetFsrsStates f ON f.TargetId = t.Id
            WHERE f.State IN (1, 2, 3)
              AND f.DueAtUtc IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM TargetAnswerVariants v
                  WHERE v.TargetId = t.Id AND v.Requirement = 0
              )
            """);

        return string.IsNullOrWhiteSpace(minDueStr)
            ? null
            : Schema13TimestampCodec.ParseUtcDateTimeOffset(minDueStr);
    }

    public static IReadOnlyList<TargetLearningRow> LoadActiveLearningTargets(SQLiteConnection connection)
    {
        var probes = connection.Query<TargetLearningProbe>(
            $"""
            SELECT {TargetProjection}
            FROM LearningTargets t
            JOIN Senses s ON s.Id = t.SenseId
            JOIN Words w ON w.Id = s.WordId
            JOIN TargetFsrsStates f ON f.TargetId = t.Id
            WHERE EXISTS (
                SELECT 1 FROM TargetAnswerVariants v
                WHERE v.TargetId = t.Id AND v.Requirement = 0
            )
            ORDER BY t.Id
            """);

        var wordControls = new Dictionary<int, WordLearningControl>();
        var senseControls = new Dictionary<int, SenseLearningControl>();
        var result = new List<TargetLearningRow>(probes.Count);

        foreach (var probe in probes)
        {
            if (!wordControls.TryGetValue(probe.WordId, out var wordControl))
            {
                wordControl = WordLearningControlRepository.Load(connection, probe.WordId);
                wordControls.Add(probe.WordId, wordControl);
            }

            if (!senseControls.TryGetValue(probe.SenseId, out var senseControl))
            {
                senseControl = SenseLearningControlRepository.Load(connection, probe.SenseId);
                senseControls.Add(probe.SenseId, senseControl);
            }

            if (ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseControl))
            {
                result.Add(MapProbe(probe));
            }
        }

        return result;
    }

    public static TargetLearningRow? LoadTargetById(SQLiteConnection connection, int targetId)
    {
        var probe = connection.Query<TargetLearningProbe>(
            $"""
            SELECT {TargetProjection}
            FROM LearningTargets t
            JOIN Senses s ON s.Id = t.SenseId
            JOIN Words w ON w.Id = s.WordId
            JOIN TargetFsrsStates f ON f.TargetId = t.Id
            WHERE t.Id = ?
            """,
            targetId).FirstOrDefault();

        return probe is null ? null : MapProbe(probe);
    }

    public static bool HasEverBeenLearned(SQLiteConnection connection, int wordId) =>
        connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM TargetReviews r
            JOIN LearningTargets t ON r.TargetId = t.Id
            JOIN Senses s ON s.Id = t.SenseId
            WHERE s.WordId = ?
            """,
            wordId) > 0;

    public static HashSet<int> LoadEverLearnedWordIds(SQLiteConnection connection) =>
        connection.Query<Schema8IdRow>(
            """
            SELECT DISTINCT s.WordId AS Id
            FROM TargetReviews r
            JOIN LearningTargets t ON r.TargetId = t.Id
            JOIN Senses s ON s.Id = t.SenseId
            ORDER BY s.WordId
            """)
        .Select(row => row.Id)
        .ToHashSet();

    private static TargetLearningRow MapProbe(TargetLearningProbe probe) =>
        new(
            probe.Id,
            probe.StableId,
            probe.SenseId,
            probe.WordId,
            probe.CanonicalTerm,
            (TokenKind)probe.TokenKind,
            (LearningTargetKind)probe.TargetKind,
            probe.SourceLanguage,
            probe.TargetLanguage,
            probe.TypingOptOut == 1,
            probe.FsrsState,
            probe.Stability,
            probe.Difficulty,
            probe.FsrsLastReviewedAtUtc is null ? null : Schema13TimestampCodec.ParseUtcDateTimeOffset(probe.FsrsLastReviewedAtUtc),
            probe.StepIndex,
            probe.FsrsDueAtUtc is null ? null : Schema13TimestampCodec.ParseUtcDateTimeOffset(probe.FsrsDueAtUtc),
            Schema13TimestampCodec.ParseUtcDateTime(probe.CreatedAtUtc),
            Schema13TimestampCodec.ParseUtcDateTime(probe.UpdatedAtUtc));

    private sealed class TargetLearningProbe
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
        public int SenseId { get; set; }
        public int WordId { get; set; }
        public string CanonicalTerm { get; set; } = string.Empty;
        public int TokenKind { get; set; }
        public int TargetKind { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public int TypingOptOut { get; set; }
        public Fsrs6CardState FsrsState { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? FsrsLastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? FsrsDueAtUtc { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }
}
