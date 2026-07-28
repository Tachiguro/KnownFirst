using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Builds an isolated, temporary, real Schema-7 SQLite database (via the unmodified
/// <see cref="DatabaseSchema.InitializeAsync"/>) for KF-MEANING-001 Slice 1 migration tests. Every insert
/// helper writes raw parameterized SQL matching the live Schema-7 shape so tests can construct arbitrary
/// — including deliberately corrupt — fixtures without depending on production service classes.
/// </summary>
internal sealed class Schema7Fixture : IAsyncDisposable
{
    private Schema7Fixture(string path, SQLiteAsyncConnection connection)
    {
        DatabasePath = path;
        Connection = connection;
    }

    public string DatabasePath { get; }

    public SQLiteAsyncConnection Connection { get; private set; }

    public static async Task<Schema7Fixture> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"knownfirst-schema8-migration-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);
        await DatabaseSchema.InitializeAsync(connection);
        return new Schema7Fixture(path, connection);
    }

    /// <summary>Closes and reopens the connection against the same file — simulates app restart / retry.</summary>
    public async Task ReopenAsync()
    {
        await Connection.CloseAsync();
        Connection = new SQLiteAsyncConnection(DatabasePath);
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        SQLiteAsyncConnection.ResetPool();
        foreach (var file in new[] { DatabasePath, $"{DatabasePath}-wal", $"{DatabasePath}-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
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
        DateTime? updatedAtUtc = null)
    {
        var now = DateTime.UtcNow;
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
}
