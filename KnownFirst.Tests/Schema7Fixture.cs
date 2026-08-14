using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Builds an isolated, temporary Schema-7 SQLite migration source without invoking the now-active
/// <see cref="DatabaseSchema.InitializeAsync"/>. Every insert
/// helper writes raw parameterized SQL matching the live Schema-7 shape so tests can construct arbitrary
/// — including deliberately corrupt — fixtures without depending on production service classes.
/// </summary>
internal sealed class Schema7Fixture : IAsyncDisposable
{
    private Schema7Fixture(string rootDirectory, string path, SQLiteAsyncConnection connection)
    {
        RootDirectory = rootDirectory;
        DatabasePath = path;
        Connection = connection;
    }

    internal string RootDirectory { get; }

    public string DatabasePath { get; }

    public SQLiteAsyncConnection Connection { get; private set; }

    public static async Task<Schema7Fixture> CreateAsync()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "kf-schema7-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, "knownfirst.db3");
        var connection = new SQLiteAsyncConnection(path);
        await InitializeEmptyAsync(connection);
        return new Schema7Fixture(rootDirectory, path, connection);
    }

    internal static async Task InitializeEmptyAsync(SQLiteAsyncConnection connection)
    {
        await connection.CreateTableAsync<DocumentEntity>();
        await connection.CreateTableAsync<WordEntity>();
        await connection.CreateTableAsync<WordFormEntity>();
        await connection.CreateTableAsync<SentenceSpanEntity>();
        await connection.CreateTableAsync<WordOccurrenceEntity>();
        await connection.CreateTableAsync<MeaningEntity>();
        await connection.CreateTableAsync<ReviewStateEntity>();
        await connection.CreateTableAsync<ReviewSessionEntity>();
        await connection.CreateTableAsync<ReviewCandidateEntity>();
        await connection.CreateTableAsync<LexicalCacheEntity>();
        await connection.CreateTableAsync<PreparationSessionEntity>();
        await connection.CreateTableAsync<PreparationCandidateEntity>();
        await connection.CreateTableAsync<ContextSnapshotEntity>();
        await connection.CreateTableAsync<LearningCardEntity>();
        await connection.CreateTableAsync<LearningReviewEntity>();
        await connection.CreateTableAsync<LearningSessionEntity>();
        await connection.CreateTableAsync<LearningSessionCardEntity>();
        await connection.ExecuteAsync($"PRAGMA user_version = {Schema8DormantMigration.SourceVersion}");
    }

    /// <summary>Closes and reopens the connection against the same file — simulates app restart / retry.</summary>
    public async Task ReopenAsync()
    {
        await Connection.CloseAsync();
        Connection = new SQLiteAsyncConnection(DatabasePath);
    }

    public async Task<string[]> CapturePersistentStateAsync()
    {
        string[]? snapshot = null;
        await Connection.RunInTransactionAsync(connection => snapshot = CapturePersistentState(connection));
        return snapshot!;
    }

    private static string[] CapturePersistentState(SQLiteConnection connection)
    {
        var result = new List<string>
        {
            $"user_version|{connection.ExecuteScalar<int>("PRAGMA user_version")}",
        };
        var tables = connection.Query<PersistentTableRow>(
            "SELECT name, COALESCE(sql, '') AS Sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        foreach (var table in tables)
        {
            result.Add($"table|{table.Name}|{table.Sql}");
            var escapedTable = EscapeIdentifier(table.Name);
            var columns = connection.Query<PersistentColumnRow>($"PRAGMA table_info(\"{escapedTable}\")")
                .OrderBy(column => column.ColumnId)
                .ToArray();
            result.AddRange(columns.Select(column =>
                $"column|{table.Name}|{column.ColumnId}|{column.Name}|{column.Type}|{column.NotNull}|{column.DefaultValue}|{column.PrimaryKey}"));

            if (columns.Length > 0)
            {
                var rowExpression = string.Join(
                    " || char(31) || ",
                    columns.Select(column => $"COALESCE(quote(\"{EscapeIdentifier(column.Name)}\"), 'NULL')"));
                var rows = connection.Query<PersistentValueRow>(
                    $"SELECT {rowExpression} AS Value FROM \"{escapedTable}\" ORDER BY rowid");
                result.AddRange(rows.Select((row, ordinal) => $"row|{table.Name}|{ordinal}|{row.Value}"));
            }

            var indexes = connection.Query<PersistentIndexListRow>($"PRAGMA index_list(\"{escapedTable}\")")
                .OrderBy(index => index.Name, StringComparer.Ordinal)
                .ToArray();
            foreach (var index in indexes)
            {
                var sql = connection.ExecuteScalar<string?>(
                    "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = ?", index.Name) ?? string.Empty;
                result.Add($"index|{table.Name}|{index.Name}|{index.Unique}|{index.Origin}|{index.Partial}|{sql}");
                var escapedIndex = EscapeIdentifier(index.Name);
                var indexColumns = connection.Query<PersistentIndexColumnRow>($"PRAGMA index_xinfo(\"{escapedIndex}\")")
                    .OrderBy(column => column.SequenceNumber);
                result.AddRange(indexColumns.Select(column =>
                    $"index-column|{index.Name}|{column.SequenceNumber}|{column.ColumnId}|{column.Name}|{column.Descending}|{column.Collation}|{column.Key}"));
            }
        }

        return [.. result];
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    public async ValueTask DisposeAsync()
    {
        await TemporaryDatabaseFiles.CloseAndDeleteAsync(Connection, DatabasePath);
        if (!string.IsNullOrWhiteSpace(RootDirectory) && Directory.Exists(RootDirectory))
        {
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class PersistentTableRow
    {
        public string Name { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }

    private sealed class PersistentColumnRow
    {
        [Column("cid")]
        public int ColumnId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        [Column("notnull")]
        public int NotNull { get; set; }
        [Column("dflt_value")]
        public string? DefaultValue { get; set; }
        [Column("pk")]
        public int PrimaryKey { get; set; }
    }

    private sealed class PersistentIndexListRow
    {
        public string Name { get; set; } = string.Empty;
        [Column("unique")]
        public int Unique { get; set; }
        [Column("origin")]
        public string Origin { get; set; } = string.Empty;
        [Column("partial")]
        public int Partial { get; set; }
    }

    private sealed class PersistentIndexColumnRow
    {
        [Column("seqno")]
        public int SequenceNumber { get; set; }
        [Column("cid")]
        public int ColumnId { get; set; }
        public string? Name { get; set; }
        [Column("desc")]
        public int Descending { get; set; }
        [Column("coll")]
        public string? Collation { get; set; }
        [Column("key")]
        public int Key { get; set; }
    }

    private sealed class PersistentValueRow
    {
        public string Value { get; set; } = string.Empty;
    }

    public async Task<int> InsertDocumentAsync(
        string title = "Doc",
        string textLanguage = "en",
        string explanationLanguage = "de",
        string content = "some document text",
        string? contentFingerprint = null,
        int wordCount = 1,
        DateTime? importedAt = null)
    {
        // Real production content fingerprints are a SHA-256 hex digest of Content (verified against
        // BackupSourceMaterial.ContentSha256 by BackupModelContract(V2).ValidateContentChecksum) —
        // a default must match that shape, not an arbitrary GUID.
        var fingerprint = contentFingerprint ?? Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        await Connection.ExecuteAsync(
            """
            INSERT INTO Documents
                (Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content,
                 ContentFingerprint, ImportedAt, WordCount)
            VALUES (?, ?, ?, 0, '', ?, ?, ?, ?)
            """,
            title, textLanguage, explanationLanguage, content,
            fingerprint, importedAt ?? DateTime.UtcNow, wordCount);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertWordAsync(
        string canonicalTerm,
        string language = "en",
        WordStatus status = WordStatus.Prepared,
        TokenKind tokenKind = TokenKind.Word,
        PreparationState preparationState = PreparationState.Prepared,
        int totalOccurrenceCount = 1,
        int documentCount = 1,
        LearningInteractionMode automaticInteractionMode = LearningInteractionMode.Reading,
        int consecutiveRecallSuccessCount = 0,
        int consecutiveTypingSuccessCount = 0,
        int consecutiveTypingFailureCount = 0,
        bool masteryReviewExtensionScheduled = false,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var now = DateTime.UtcNow;
        await Connection.ExecuteAsync(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            language, canonicalTerm, canonicalTerm.ToLowerInvariant(), (int)status, (int)tokenKind, (int)preparationState,
            totalOccurrenceCount, documentCount, (int)automaticInteractionMode, consecutiveRecallSuccessCount,
            consecutiveTypingSuccessCount, consecutiveTypingFailureCount, masteryReviewExtensionScheduled,
            createdAt ?? now, updatedAt ?? now);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertMeaningAsync(
        int wordId,
        string sourceLanguage = "en",
        string explanationLanguage = "de",
        string displayTerm = "word",
        string translation = "Wort",
        string definition = "",
        string encounteredSurfaceForm = "",
        string grammaticalRelationship = "",
        string selectedMeaningId = "",
        string acronymExpansion = "",
        string dictionaryExample = "",
        string additionalNote = "",
        string acceptedAliasesJson = "[]",
        bool confirmedByUser = true,
        string source = "Manual",
        string sourceProject = "",
        string sourcePageTitle = "",
        long? sourceRevisionId = null,
        string attribution = "",
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var now = DateTime.UtcNow;
        await Connection.ExecuteAsync(
            """
            INSERT INTO Meanings
                (WordId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                 GrammaticalRelationship, TokenKind, SelectedMeaningId, AcronymExpansion, Translation,
                 Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition,
                 Source, SourceProject, SourcePageTitle, SourceRevisionId, Attribution, ConfirmedByUser,
                 CreatedAt, UpdatedAt, PreparedAt)
            VALUES (?, ?, ?, ?, ?, ?, 0, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            wordId, explanationLanguage, sourceLanguage, displayTerm, encounteredSurfaceForm,
            grammaticalRelationship, selectedMeaningId, acronymExpansion, translation,
            definition, dictionaryExample, additionalNote, acceptedAliasesJson, string.IsNullOrEmpty(translation) ? definition : translation,
            source, sourceProject, sourcePageTitle, sourceRevisionId, attribution, confirmedByUser,
            createdAt ?? now, updatedAt ?? now, createdAt ?? now);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Inserts a Schema-7 learning card. <paramref name="id"/> writes an explicit primary key so a test can
    /// use the approved fixed card ids (40/41/42) instead of depending on autoincrement order.
    /// </summary>
    public async Task<int> InsertCardAsync(
        int wordId,
        int meaningId,
        CardDirection direction,
        CardState state = CardState.New,
        DateTime? dueAtUtc = null,
        int intervalDays = 0,
        double easeFactor = 2.5,
        int successfulReviewCount = 0,
        int lapseCount = 0,
        DateTime? lastReviewedAtUtc = null,
        ReviewRating? lastRating = null,
        DateTime? createdAtUtc = null,
        DateTime? updatedAtUtc = null,
        int? id = null)
    {
        var now = DateTime.UtcNow;
        if (id.HasValue)
        {
            await Connection.ExecuteAsync(
                """
                INSERT INTO LearningCards
                    (Id, WordId, MeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount,
                     LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                id.Value, wordId, meaningId, (int)direction, (int)state, dueAtUtc ?? now, intervalDays, easeFactor,
                successfulReviewCount, lapseCount, lastReviewedAtUtc, (int?)lastRating, createdAtUtc ?? now, updatedAtUtc ?? now);
            return id.Value;
        }

        await Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (WordId, MeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount,
                 LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            wordId, meaningId, (int)direction, (int)state, dueAtUtc ?? now, intervalDays, easeFactor,
            successfulReviewCount, lapseCount, lastReviewedAtUtc, (int?)lastRating, createdAtUtc ?? now, updatedAtUtc ?? now);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertContextAsync(
        int meaningId,
        int wordId,
        int sourceDocumentId = 1,
        string sourceDocumentTitle = "Doc",
        string text = "some context text",
        int targetStart = 0,
        int targetLength = 4,
        string? normalizedFingerprint = null,
        DateTime? createdAtUtc = null)
    {
        await Connection.ExecuteAsync(
            """
            INSERT INTO ContextSnapshots
                (MeaningId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart, TargetLength,
                 NormalizedFingerprint, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            meaningId, wordId, sourceDocumentId, sourceDocumentTitle, text, targetStart, targetLength,
            normalizedFingerprint ?? Guid.NewGuid().ToString("N"), createdAtUtc ?? DateTime.UtcNow);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertLearningSessionAsync(
        LearningSessionStatus status = LearningSessionStatus.Completed,
        int totalCards = 1,
        int completedCards = 1,
        DateTime? startedAtUtc = null,
        DateTime? updatedAtUtc = null,
        DateTime? completedAtUtc = null)
    {
        var now = DateTime.UtcNow;
        await Connection.ExecuteAsync(
            """
            INSERT INTO LearningSessions
                (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                 StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, ?, ?, 0, 0, 0, 0, ?, ?, ?)
            """,
            (int)status, totalCards, completedCards, startedAtUtc ?? now, updatedAtUtc ?? now, completedAtUtc);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertReviewAsync(
        int cardId,
        int sessionId = 1,
        ReviewRating rating = ReviewRating.Good,
        bool wasTypedAnswer = false,
        bool wasCorrect = true,
        DateTime? reviewedAtUtc = null,
        DateTime? dueAtUtc = null,
        int intervalDays = 1,
        double easeFactor = 2.5)
    {
        var now = DateTime.UtcNow;
        await Connection.ExecuteAsync(
            """
            INSERT INTO LearningReviews
                (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            cardId, sessionId, (int)rating, wasTypedAnswer, wasCorrect, reviewedAtUtc ?? now, dueAtUtc ?? now,
            intervalDays, easeFactor);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertQueueItemAsync(
        int sessionId,
        int cardId,
        int queueOrder,
        bool isDueCard = true,
        bool isAgainRepeat = false,
        bool answerRevealed = false,
        bool spellingChecked = false,
        bool spellingCorrect = false,
        bool isCompleted = false,
        ReviewRating? rating = null,
        DateTime? completedAtUtc = null)
    {
        await Connection.ExecuteAsync(
            """
            INSERT INTO LearningSessionCards
                (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                 SpellingCorrect, IsCompleted, Rating, CompletedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            sessionId, cardId, queueOrder, isDueCard, isAgainRepeat, answerRevealed, spellingChecked,
            spellingCorrect, isCompleted, (int?)rating, completedAtUtc);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    // ---- KF-MEANING-001 Slice 4: Schema-8 helpers ----
    // Policy-free raw-SQL conveniences only. No production policy lives here: every helper writes or reads
    // exactly what the caller supplies, so a test can construct arbitrary — including deliberately corrupt —
    // Schema-8 fixtures without going through a production service.

    /// <summary>Runs the dormant migration against this fixture and asserts nothing; callers assert.</summary>
    public Task<Schema8MigrationResult> MigrateToSchema8Async(Schema8MigrationOptions? options = null) =>
        Schema8DormantMigration.ApplyAsync(Connection, options);

    public Task<int> ReadUserVersionAsync() =>
        Connection.ExecuteScalarAsync<int>("PRAGMA user_version");

    public async Task<int> InsertSenseAsync(
        int wordId,
        int? id = null,
        string sourceLanguage = "en",
        string explanationLanguage = "de",
        string providerSenseId = "",
        int? defaultMeaningId = null,
        SenseStatus status = SenseStatus.Learning,
        DateTime? createdAtUtc = null,
        DateTime? updatedAtUtc = null)
    {
        var now = createdAtUtc ?? DateTime.UtcNow;
        var columns = id.HasValue ? "Id, " : string.Empty;
        var placeholders = id.HasValue ? "?, " : string.Empty;
        var args = new List<object?>();
        if (id.HasValue)
        {
            args.Add(id.Value);
        }

        args.AddRange([
            Guid.NewGuid().ToString("N"), wordId, sourceLanguage, explanationLanguage, providerSenseId,
            defaultMeaningId, (int)status, now, updatedAtUtc ?? now
        ]);

        await Connection.ExecuteAsync(
            $"""
            INSERT INTO Senses
                ({columns}StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES ({placeholders}?, ?, ?, ?, ?, '', '', '', '', ?, ?, ?, ?)
            """,
            [.. args]);

        return id ?? await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertAnswerVariantAsync(
        int senseId,
        string displayText,
        string answerLanguage = "en",
        int? id = null,
        string? normalizedText = null,
        int? sourceMeaningId = null,
        DateTime? createdAtUtc = null)
    {
        var now = createdAtUtc ?? DateTime.UtcNow;
        var columns = id.HasValue ? "Id, " : string.Empty;
        var placeholders = id.HasValue ? "?, " : string.Empty;
        var args = new List<object?>();
        if (id.HasValue)
        {
            args.Add(id.Value);
        }

        args.AddRange([
            Guid.NewGuid().ToString("N"), senseId, answerLanguage, displayText,
            normalizedText ?? displayText, sourceMeaningId, now, now
        ]);

        await Connection.ExecuteAsync(
            $"""
            INSERT INTO AnswerVariants
                ({columns}StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES ({placeholders}?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.. args]);

        return id ?? await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Inserts an assignment exactly as supplied — including a deliberately invariant-violating combination —
    /// so fail-closed behaviour can be tested.
    /// </summary>
    public async Task<int> InsertAssignmentAsync(
        int senseId,
        CardDirection direction,
        int answerVariantId,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTime? requiredSinceUtc,
        DateTime? createdAtUtc = null,
        string? stableId = null)
    {
        var now = createdAtUtc ?? DateTime.UtcNow;
        await Connection.ExecuteAsync(
            """
            INSERT INTO SenseAnswerVariantAssignments
                (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId ?? Guid.NewGuid().ToString("N"), senseId, (int)direction, answerVariantId,
            (int)requirement, isPreferred, requiredSinceUtc, now, now);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public async Task<int> InsertProgressAsync(
        int cardId,
        int answerVariantId,
        DateTime createdAtUtc,
        LearningInteractionMode interactionMode = LearningInteractionMode.Reading,
        int consecutiveReadingSuccessCount = 0,
        int consecutiveTypingSuccessCount = 0,
        int consecutiveTypingFailureCount = 0,
        DateTime? lastAssessedAtUtc = null,
        bool masteryReviewExtensionScheduled = false,
        bool isMastered = false,
        int replayVersion = 1,
        DateTime? updatedAtUtc = null)
    {
        await Connection.ExecuteAsync(
            """
            INSERT INTO AnswerVariantProgress
                (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                 MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            cardId, answerVariantId, (int)interactionMode, consecutiveReadingSuccessCount,
            consecutiveTypingSuccessCount, consecutiveTypingFailureCount, lastAssessedAtUtc,
            masteryReviewExtensionScheduled, isMastered, replayVersion, createdAtUtc,
            updatedAtUtc ?? createdAtUtc);

        return await Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public Task<List<Schema8AssignmentReadRow>> ReadAssignmentsAsync(int senseId, CardDirection direction) =>
        Connection.QueryAsync<Schema8AssignmentReadRow>(
            """
            SELECT a.Id, a.StableId, a.SenseId, a.CardDirection, a.AnswerVariantId, a.Requirement,
                   a.IsPreferred, a.RequiredSinceUtc, a.CreatedAtUtc, a.UpdatedAtUtc, v.NormalizedText,
                   v.AnswerLanguage
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            ORDER BY a.Id
            """,
            senseId, (int)direction);

    public Task<List<Schema8CardReadRow>> ReadCardsAsync() =>
        Connection.QueryAsync<Schema8CardReadRow>(
            """
            SELECT Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                   SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc
            FROM LearningCards ORDER BY Id
            """);

    public Task<List<Schema8ProgressReadRow>> ReadProgressAsync() =>
        Connection.QueryAsync<Schema8ProgressReadRow>(
            """
            SELECT Id, CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                   ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                   MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc
            FROM AnswerVariantProgress ORDER BY CardId, AnswerVariantId
            """);

    public Task<int> GetTableCountAsync(string tableName) =>
        Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=?", tableName);

    public Task<Data.Entities.LearningCardEntity> ReadCardAsync(int id) =>
        Connection.Table<Data.Entities.LearningCardEntity>().FirstAsync(x => x.Id == id);

    public Task<Data.Entities.LearningSessionEntity> ReadSessionAsync(int id) =>
        Connection.Table<Data.Entities.LearningSessionEntity>().FirstAsync(x => x.Id == id);

    public Task<Data.Entities.LearningSessionCardEntity> ReadQueueRowAsync(int id) =>
        Connection.Table<Data.Entities.LearningSessionCardEntity>().FirstAsync(x => x.Id == id);

    public Task<List<Data.Entities.LearningReviewEntity>> ReadReviewsAsync(int cardId) =>
        Connection.Table<Data.Entities.LearningReviewEntity>().Where(x => x.CardId == cardId).ToListAsync();


    /// <summary>
    /// Portably rebuilds <c>SenseAnswerVariantAssignments</c> without the <c>RequiredSinceUtc</c> column, so an
    /// incomplete <c>user_version = 8</c> physical shape can be constructed without depending on
    /// <c>ALTER TABLE ... DROP COLUMN</c> (SQLite 3.35+). Copies every compatible row, drops the original,
    /// renames the replacement and recreates the required indexes.
    /// </summary>
    public async Task RebuildAssignmentsTableWithoutRequiredSinceUtcAsync()
    {
        await Connection.ExecuteAsync(
            """
            CREATE TABLE SenseAnswerVariantAssignments_NoBoundary (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StableId TEXT NOT NULL,
                SenseId INTEGER NOT NULL,
                CardDirection INTEGER NOT NULL,
                AnswerVariantId INTEGER NOT NULL,
                Requirement INTEGER NOT NULL,
                IsPreferred INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            )
            """);
        await Connection.ExecuteAsync(
            """
            INSERT INTO SenseAnswerVariantAssignments_NoBoundary
                (Id, StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc)
            SELECT Id, StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc
            FROM SenseAnswerVariantAssignments
            """);
        await Connection.ExecuteAsync("DROP TABLE SenseAnswerVariantAssignments");
        await Connection.ExecuteAsync(
            "ALTER TABLE SenseAnswerVariantAssignments_NoBoundary RENAME TO SenseAnswerVariantAssignments");
        await Connection.ExecuteAsync(
            "CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_StableId ON SenseAnswerVariantAssignments (StableId)");
        await Connection.ExecuteAsync(
            "CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Variant ON SenseAnswerVariantAssignments (SenseId, CardDirection, AnswerVariantId)");
        await Connection.ExecuteAsync(
            """
            CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred
            ON SenseAnswerVariantAssignments (SenseId, CardDirection)
            WHERE IsPreferred = 1
            """);
    }
}

/// <summary>Assignment projection joined with its variant text, for Slice-4 migration and preparation tests.</summary>
internal sealed class Schema8AssignmentReadRow
{
    public int Id { get; set; }
    public string StableId { get; set; } = string.Empty;
    public int SenseId { get; set; }
    public CardDirection CardDirection { get; set; }
    public int AnswerVariantId { get; set; }
    public AnswerVariantRequirement Requirement { get; set; }
    public bool IsPreferred { get; set; }
    public DateTime? RequiredSinceUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string NormalizedText { get; set; } = string.Empty;
    public string AnswerLanguage { get; set; } = string.Empty;
}

internal sealed class Schema8CardReadRow
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

internal sealed class Schema8ProgressReadRow
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
