namespace KnownFirst.Data.Targets;

using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using SQLite;

public sealed class LearningTargetRepository
{
    private readonly IKnownFirstDatabase _database;

    public LearningTargetRepository(IKnownFirstDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<PersistedLearningTarget> CreateTargetAsync(
        int senseId,
        LearningTarget target,
        DateTime nowUtc,
        string? stableId = null) =>
        _database.RunInTransactionAsync(conn => CreateTarget(conn, senseId, target, nowUtc, stableId));

    public Task<PersistedLearningTarget?> GetTargetByIdAsync(int targetId) =>
        _database.RunInTransactionAsync(conn => GetTargetById(conn, targetId));

    public Task<PersistedLearningTarget?> FindTargetByIdentityAsync(int senseId, LearningTargetIdentity identity) =>
        _database.RunInTransactionAsync(conn => FindTargetByIdentity(conn, senseId, identity));

    public Task<PersistedLearningTarget> GetOrCreateTargetAsync(
        int senseId,
        LearningTarget target,
        DateTime nowUtc) =>
        _database.RunInTransactionAsync(conn => GetOrCreateTarget(conn, senseId, target, nowUtc));

    public Task<PersistedTargetAnswerVariant> AddAnswerVariantAsync(
        int targetId,
        TargetAnswerVariantDraft draft,
        DateTime nowUtc,
        string? stableId = null) =>
        _database.RunInTransactionAsync(conn => AddAnswerVariant(conn, targetId, draft, nowUtc, stableId));

    public Task<IReadOnlyList<PersistedTargetAnswerVariant>> GetAnswerVariantsAsync(int targetId) =>
        _database.RunInTransactionAsync(conn => GetAnswerVariants(conn, targetId));

    public Task<PersistedTargetAnswerVariant?> GetPreferredVariantAsync(int targetId) =>
        _database.RunInTransactionAsync(conn => GetPreferredVariant(conn, targetId));

    public Task<IReadOnlyList<PersistedTargetAnswerVariant>> GetRequiredVariantsAsync(int targetId) =>
        _database.RunInTransactionAsync(conn => GetRequiredVariants(conn, targetId));

    public Task<(PersistedLearningTarget Target, IReadOnlyList<PersistedTargetAnswerVariant> Variants)> CreateTargetWithVariantsAsync(
        int senseId,
        LearningTarget target,
        IReadOnlyList<TargetAnswerVariantDraft> variants,
        DateTime nowUtc) =>
        _database.RunInTransactionAsync(conn => CreateTargetWithVariants(conn, senseId, target, variants, nowUtc));

    public Task<IReadOnlyList<PersistedLearningTarget>> GetTargetsForSenseAsync(int senseId) =>
        _database.RunInTransactionAsync(conn => GetTargetsForSense(conn, senseId));

    public Task SetTypingOptOutAsync(int targetId, bool typingOptOut, DateTime nowUtc) =>
        _database.RunInTransactionAsync(conn =>
        {
            SetTypingOptOut(conn, targetId, typingOptOut, nowUtc);
            return true;
        });

    public static PersistedLearningTarget CreateTarget(
        SQLiteConnection connection,
        int senseId,
        LearningTarget target,
        DateTime nowUtc,
        string? stableId = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (senseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senseId), senseId, "SenseId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(target);
        ValidateUtc(nowUtc);

        var senseExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Senses WHERE Id = ?", senseId) > 0;
        if (!senseExists)
        {
            throw new InvalidOperationException($"Sense with Id {senseId} does not exist.");
        }

        var sid = string.IsNullOrWhiteSpace(stableId) ? Guid.NewGuid().ToString("N") : stableId.Trim();
        var nowFormatted = Schema13TimestampCodec.FormatUtc(nowUtc);

        connection.Execute(
            """
            INSERT INTO LearningTargets
                (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            sid,
            senseId,
            (int)target.Kind,
            target.SourceLanguage,
            target.TargetLanguage,
            target.TypingOptOut ? 1 : 0,
            nowFormatted,
            nowFormatted);

        var id = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
        return new PersistedLearningTarget(
            id,
            sid,
            senseId,
            target.Kind,
            target.SourceLanguage,
            target.TargetLanguage,
            target.TypingOptOut,
            nowUtc,
            nowUtc);
    }

    public static PersistedLearningTarget? GetTargetById(SQLiteConnection connection, int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }

        var row = connection.Query<TargetRow>(
            """
            SELECT Id, StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc
            FROM LearningTargets
            WHERE Id = ?
            """,
            targetId).FirstOrDefault();

        return row is null ? null : MapTarget(row);
    }

    public static PersistedLearningTarget? FindTargetByIdentity(
        SQLiteConnection connection,
        int senseId,
        LearningTargetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (senseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senseId), senseId, "SenseId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(identity);

        var row = connection.Query<TargetRow>(
            """
            SELECT Id, StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc
            FROM LearningTargets
            WHERE SenseId = ? AND TargetKind = ? AND SourceLanguage = ? AND TargetLanguage = ?
            """,
            senseId,
            (int)identity.Kind,
            identity.SourceLanguage,
            identity.TargetLanguage).FirstOrDefault();

        return row is null ? null : MapTarget(row);
    }

    public static PersistedLearningTarget GetOrCreateTarget(
        SQLiteConnection connection,
        int senseId,
        LearningTarget target,
        DateTime nowUtc)
    {
        var existing = FindTargetByIdentity(connection, senseId, target.Identity);
        return existing ?? CreateTarget(connection, senseId, target, nowUtc);
    }

    public static PersistedTargetAnswerVariant AddAnswerVariant(
        SQLiteConnection connection,
        int targetId,
        TargetAnswerVariantDraft draft,
        DateTime nowUtc,
        string? stableId = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.AnswerLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.DisplayText);
        ValidateUtc(nowUtc);

        var targetExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningTargets WHERE Id = ?", targetId) > 0;
        if (!targetExists)
        {
            throw new InvalidOperationException($"LearningTarget with Id {targetId} does not exist.");
        }

        if (draft.SourceMeaningId.HasValue)
        {
            var meaningExists = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Meanings WHERE Id = ?", draft.SourceMeaningId.Value) > 0;
            if (!meaningExists)
            {
                throw new InvalidOperationException($"Meaning with Id {draft.SourceMeaningId.Value} does not exist.");
            }
        }

        var sid = string.IsNullOrWhiteSpace(stableId) ? Guid.NewGuid().ToString("N") : stableId.Trim();
        var answerLang = draft.AnswerLanguage.Trim().ToLowerInvariant();
        var displayText = draft.DisplayText.Normalize(System.Text.NormalizationForm.FormC).Trim();
        var normalizedText = string.IsNullOrWhiteSpace(draft.NormalizedText)
            ? NormalizeAnswerText(displayText)
            : NormalizeAnswerText(draft.NormalizedText);

        var nowFormatted = Schema13TimestampCodec.FormatUtc(nowUtc);
        string? requiredSinceUtc = draft.Requirement == AnswerVariantRequirement.Required
            ? nowFormatted
            : null;

        connection.Execute(
            """
            INSERT INTO TargetAnswerVariants
                (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            sid,
            targetId,
            answerLang,
            displayText,
            normalizedText,
            (int)draft.Requirement,
            draft.IsPreferred ? 1 : 0,
            requiredSinceUtc,
            draft.SourceMeaningId,
            nowFormatted,
            nowFormatted);

        var id = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
        return new PersistedTargetAnswerVariant(
            id,
            sid,
            targetId,
            answerLang,
            displayText,
            normalizedText,
            draft.Requirement,
            draft.IsPreferred,
            draft.Requirement == AnswerVariantRequirement.Required ? nowUtc : null,
            draft.SourceMeaningId,
            nowUtc,
            nowUtc);
    }

    public static IReadOnlyList<PersistedTargetAnswerVariant> GetAnswerVariants(SQLiteConnection connection, int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }

        var rows = connection.Query<VariantRow>(
            """
            SELECT Id, StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc
            FROM TargetAnswerVariants
            WHERE TargetId = ?
            ORDER BY IsPreferred DESC, Id ASC
            """,
            targetId);

        return rows.Select(MapVariant).ToList();
    }

    public static PersistedTargetAnswerVariant? GetPreferredVariant(SQLiteConnection connection, int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }

        var row = connection.Query<VariantRow>(
            """
            SELECT Id, StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc
            FROM TargetAnswerVariants
            WHERE TargetId = ? AND IsPreferred = 1
            LIMIT 1
            """,
            targetId).FirstOrDefault();

        return row is null ? null : MapVariant(row);
    }

    public static IReadOnlyList<PersistedTargetAnswerVariant> GetRequiredVariants(SQLiteConnection connection, int targetId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }

        var rows = connection.Query<VariantRow>(
            """
            SELECT Id, StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc
            FROM TargetAnswerVariants
            WHERE TargetId = ? AND Requirement = 0
            ORDER BY IsPreferred DESC, Id ASC
            """,
            targetId);

        return rows.Select(MapVariant).ToList();
    }

    public static (PersistedLearningTarget Target, IReadOnlyList<PersistedTargetAnswerVariant> Variants) CreateTargetWithVariants(
        SQLiteConnection connection,
        int senseId,
        LearningTarget target,
        IReadOnlyList<TargetAnswerVariantDraft> variants,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(variants);

        if (variants.Count > 0)
        {
            var preferredCount = variants.Count(v => v.IsPreferred);
            if (preferredCount != 1)
            {
                throw new InvalidOperationException(
                    $"Target variant collection must have exactly one preferred variant (found {preferredCount}).");
            }
        }

        var createdTarget = CreateTarget(connection, senseId, target, nowUtc);
        var createdVariants = new List<PersistedTargetAnswerVariant>(variants.Count);

        foreach (var draft in variants)
        {
            createdVariants.Add(AddAnswerVariant(connection, createdTarget.Id, draft, nowUtc));
        }

        return (createdTarget, createdVariants);
    }

    public static IReadOnlyList<PersistedLearningTarget> GetTargetsForSense(SQLiteConnection connection, int senseId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (senseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senseId), senseId, "SenseId must be a positive integer.");
        }

        var rows = connection.Query<TargetRow>(
            """
            SELECT Id, StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc
            FROM LearningTargets
            WHERE SenseId = ?
            ORDER BY Id ASC
            """,
            senseId);

        return rows.Select(MapTarget).ToList();
    }

    public static void SetTypingOptOut(
        SQLiteConnection connection,
        int targetId,
        bool typingOptOut,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (targetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId must be a positive integer.");
        }
        ValidateUtc(nowUtc);

        var updated = connection.Execute(
            "UPDATE LearningTargets SET TypingOptOut = ?, UpdatedAtUtc = ? WHERE Id = ?",
            typingOptOut ? 1 : 0,
            Schema13TimestampCodec.FormatUtc(nowUtc),
            targetId);

        if (updated != 1)
        {
            throw new InvalidOperationException($"LearningTarget with Id {targetId} does not exist.");
        }
    }

    private static void ValidateUtc(DateTime dateTime)
    {
        if (dateTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", nameof(dateTime));
        }
    }

    private static PersistedLearningTarget MapTarget(TargetRow row) =>
        new(
            row.Id,
            row.StableId,
            row.SenseId,
            (LearningTargetKind)row.TargetKind,
            row.SourceLanguage,
            row.TargetLanguage,
            row.TypingOptOut == 1,
            Schema13TimestampCodec.ParseUtcDateTime(row.CreatedAtUtc),
            Schema13TimestampCodec.ParseUtcDateTime(row.UpdatedAtUtc));

    private static PersistedTargetAnswerVariant MapVariant(VariantRow row) =>
        new(
            row.Id,
            row.StableId,
            row.TargetId,
            row.AnswerLanguage,
            row.DisplayText,
            row.NormalizedText,
            (AnswerVariantRequirement)row.Requirement,
            row.IsPreferred == 1,
            string.IsNullOrWhiteSpace(row.RequiredSinceUtc) ? null : Schema13TimestampCodec.ParseUtcDateTime(row.RequiredSinceUtc),
            row.SourceMeaningId,
            Schema13TimestampCodec.ParseUtcDateTime(row.CreatedAtUtc),
            Schema13TimestampCodec.ParseUtcDateTime(row.UpdatedAtUtc));

    private sealed class TargetRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
        public int SenseId { get; set; }
        public int TargetKind { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public int TypingOptOut { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class VariantRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
        public int TargetId { get; set; }
        public string AnswerLanguage { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;
        public int Requirement { get; set; }
        public int IsPreferred { get; set; }
        public string? RequiredSinceUtc { get; set; }
        public int? SourceMeaningId { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    public static string NormalizeAnswerText(string? text) =>
        text is null ? string.Empty : text.Trim().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
}
