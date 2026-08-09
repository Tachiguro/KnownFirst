using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Schema8ActivationTests
{
    [TestMethod]
    public async Task FreshInitialization_FinishesAsValidatedCurrentSchema()
    {
        var path = TemporaryPath("fresh");
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);

            Assert.AreEqual(DatabaseSchema.CurrentVersion, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual(0, await ColumnCountAsync(connection, "LearningCards", "MeaningId"));
            Assert.AreEqual(1, await ColumnCountAsync(connection, "LearningCards", "PreferredMeaningId"));
            Assert.AreEqual(1, await IndexCountAsync(connection, "IX_LearningCards_Sense_Direction"));
            Assert.AreEqual(0, await IndexCountAsync(connection, "IX_LearningCards_Word_Direction"));

            BackupSchemaCapabilityResult? capability = null;
            await connection.RunInTransactionAsync(sqlite => capability = BackupSchemaCapability.Resolve(sqlite));
            Assert.IsInstanceOfType<Schema10CapabilityResult>(capability);
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task PopulatedSchema7_NormalInitializationPreservesHistoryAndIsIdempotent()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var documentId = await fixture.InsertDocumentAsync(content: "preserved evidence");
        var wordId = await fixture.InsertWordAsync(
            "history",
            status: WordStatus.Mastered,
            totalOccurrenceCount: 17,
            documentCount: 3,
            consecutiveRecallSuccessCount: 4,
            consecutiveTypingSuccessCount: 5,
            consecutiveTypingFailureCount: 2);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "history", translation: "Geschichte");
        var cardCreated = new DateTime(2031, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var cardId = await fixture.InsertCardAsync(
            wordId,
            meaningId,
            CardDirection.MeaningToTerm,
            state: CardState.Review,
            intervalDays: 12,
            successfulReviewCount: 7,
            createdAtUtc: cardCreated,
            id: 40);
        await fixture.InsertContextAsync(meaningId, wordId, sourceDocumentId: documentId);
        var sessionId = await fixture.InsertLearningSessionAsync();
        var reviewId = await fixture.InsertReviewAsync(cardId, sessionId, intervalDays: 12);
        var queueId = await fixture.InsertQueueItemAsync(sessionId, cardId, 0, isCompleted: true, rating: ReviewRating.Good);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        Assert.AreEqual(DatabaseSchema.CurrentVersion, await fixture.ReadUserVersionAsync());
        Assert.AreEqual(40, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards WHERE Id = 40"));
        Assert.AreEqual(meaningId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE Id = 40"));
        Assert.AreEqual(12, await fixture.Connection.ExecuteScalarAsync<int>("SELECT IntervalDays FROM LearningCards WHERE Id = 40"));
        Assert.AreEqual(reviewId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningReviews WHERE Id = ?", reviewId));
        Assert.AreEqual(queueId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningSessionCards WHERE Id = ?", queueId));
        Assert.AreEqual(17, await fixture.Connection.ExecuteScalarAsync<int>("SELECT TotalOccurrenceCount FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(3, await fixture.Connection.ExecuteScalarAsync<int>("SELECT DocumentCount FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ContextSnapshots WHERE WordId = ?", wordId));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariantProgress WHERE CardId = ?", cardId));

        var stableIdsBefore = await fixture.Connection.QueryAsync<StableIdRow>(
            "SELECT StableId FROM Senses UNION ALL SELECT StableId FROM Meanings UNION ALL SELECT StableId FROM AnswerVariants ORDER BY StableId");
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        var stableIdsAfter = await fixture.Connection.QueryAsync<StableIdRow>(
            "SELECT StableId FROM Senses UNION ALL SELECT StableId FROM Meanings UNION ALL SELECT StableId FROM AnswerVariants ORDER BY StableId");

        CollectionAssert.AreEqual(stableIdsBefore.Select(row => row.StableId).ToArray(), stableIdsAfter.Select(row => row.StableId).ToArray());
        Assert.AreEqual(0, await ColumnCountAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.AreEqual(0, await IndexCountAsync(fixture.Connection, "IX_LearningCards_Word_Direction"));
    }

    [TestMethod]
    public Task MalformedSchema8_ReopenFailsClosedWithoutRepair() =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            "malformed-v8",
            connection => connection.ExecuteAsync("DROP INDEX IX_LearningCards_Sense_Direction"),
            "IX_LearningCards_Sense_Direction");

    [TestMethod]
    public Task MalformedSchema8_MissingDocumentsTable_RemainsUnchangedAfterReopen() =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            "missing-documents",
            connection => connection.ExecuteAsync("DROP TABLE Documents"),
            "Documents");

    [TestMethod]
    public Task MalformedSchema8_MissingAnswerVariantsNormalizedText_RemainsUnchangedAfterReopen() =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            "missing-normalized-text",
            async connection =>
            {
                await connection.ExecuteAsync("DROP INDEX IX_AnswerVariants_Sense_Language_Text");
                await connection.ExecuteAsync("ALTER TABLE AnswerVariants DROP COLUMN NormalizedText");
            },
            "NormalizedText");

    [TestMethod]
    public Task MalformedSchema8_MissingLearningSessionsTable_RemainsUnchangedAfterReopen() =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            "missing-learning-sessions",
            connection => connection.ExecuteAsync("DROP TABLE LearningSessions"),
            "LearningSessions");

    [TestMethod]
    public Task MalformedSchema8_SameNamedIndexWithWrongColumns_RemainsUnchangedAfterReopen() =>
        AssertMalformedIndexRemainsUnchangedAsync(
            "wrong-index-columns",
            "IX_LearningCards_Sense_Direction",
            "CREATE UNIQUE INDEX IX_LearningCards_Sense_Direction ON LearningCards (WordId, Direction)",
            "ordered columns");

    [TestMethod]
    public Task MalformedSchema8_SameNamedIndexWithReversedColumnOrder_RemainsUnchangedAfterReopen() =>
        AssertMalformedIndexRemainsUnchangedAsync(
            "reversed-index-columns",
            "IX_LearningCards_Sense_Direction",
            "CREATE UNIQUE INDEX IX_LearningCards_Sense_Direction ON LearningCards (Direction, SenseId)",
            "ordered columns");

    [TestMethod]
    public Task MalformedSchema8_SameNamedIndexWithWrongUniqueness_RemainsUnchangedAfterReopen() =>
        AssertMalformedIndexRemainsUnchangedAsync(
            "wrong-index-uniqueness",
            "IX_LearningCards_Sense_Direction",
            "CREATE INDEX IX_LearningCards_Sense_Direction ON LearningCards (SenseId, Direction)",
            "uniqueness");

    [TestMethod]
    public Task MalformedSchema8_PartialIndexWithoutPredicate_RemainsUnchangedAfterReopen() =>
        AssertMalformedIndexRemainsUnchangedAsync(
            "missing-index-predicate",
            "IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred",
            "CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred ON SenseAnswerVariantAssignments (SenseId, CardDirection)",
            "partial-index setting");

    [TestMethod]
    public Task MalformedSchema8_PartialIndexWithWrongPredicate_RemainsUnchangedAfterReopen() =>
        AssertMalformedIndexRemainsUnchangedAsync(
            "wrong-index-predicate",
            "IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred",
            "CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred ON SenseAnswerVariantAssignments (SenseId, CardDirection) WHERE IsPreferred = 0",
            "partial-index predicate");

    [DataTestMethod]
    [DataRow("Documents", "LookupMode", "Document row")]
    [DataRow("Words", "Status", "Word row")]
    [DataRow("Words", "TokenKind", "Word row")]
    [DataRow("Words", "PreparationState", "Word row")]
    [DataRow("Words", "AutomaticInteractionMode", "Word row")]
    [DataRow("WordOccurrences", "TechnicalFamily", "WordOccurrence row")]
    [DataRow("Meanings", "TokenKind", "Meaning row")]
    [DataRow("ReviewSessions", "Status", "ReviewSession row")]
    [DataRow("ReviewCandidates", "Status", "ReviewCandidate row")]
    [DataRow("ReviewCandidates", "PreviousWordStatus", "ReviewCandidate row")]
    [DataRow("LexicalCache", "LookupMode", "LexicalCache row")]
    [DataRow("LexicalCache", "TokenKind", "LexicalCache row")]
    [DataRow("PreparationSessions", "Status", "PreparationSession row")]
    [DataRow("PreparationSessions", "Method", "PreparationSession row")]
    [DataRow("PreparationCandidates", "Status", "PreparationCandidate row")]
    [DataRow("LearningCards", "Direction", "LearningCard row")]
    [DataRow("LearningCards", "State", "LearningCard row")]
    [DataRow("LearningReviews", "Rating", "LearningReview row")]
    [DataRow("LearningSessions", "Status", "LearningSession row")]
    // The four Schema-8 tables declare their enum columns NOT NULL, so persisting a NULL there requires
    // rebuilding the column as nullable — which the physical nullability gate rejects before the logical
    // enum gate is reached. MalformedSchema8_UndefinedRequiredEnum_... still covers the logical gate for
    // exactly these columns.
    [DataRow("Senses", "Status", "Senses.Status must be declared NOT NULL")]
    [DataRow("SenseAnswerVariantAssignments", "CardDirection", "SenseAnswerVariantAssignments.CardDirection must be declared NOT NULL")]
    [DataRow("SenseAnswerVariantAssignments", "Requirement", "SenseAnswerVariantAssignments.Requirement must be declared NOT NULL")]
    [DataRow("SenseAnswerVariantAssignments", "IsPreferred", "SenseAnswerVariantAssignments.IsPreferred must be declared NOT NULL")]
    [DataRow("AnswerVariantProgress", "InteractionMode", "AnswerVariantProgress.InteractionMode must be declared NOT NULL")]
    public Task MalformedSchema8_NullPersistedEnum_RemainsCompletelyUnchangedAfterReopen(
        string table,
        string column,
        string expectedFailureDetail) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"null-{table}-{column}",
            connection => RebuildTableWithNullableNullAsync(connection, table, column),
            expectedFailureDetail);

    [DataTestMethod]
    [DataRow("Documents", "LookupMode", "Document row")]
    [DataRow("Words", "Status", "Word row")]
    [DataRow("Words", "TokenKind", "Word row")]
    [DataRow("Words", "PreparationState", "Word row")]
    [DataRow("Words", "AutomaticInteractionMode", "Word row")]
    [DataRow("WordOccurrences", "TechnicalFamily", "WordOccurrence row")]
    [DataRow("Meanings", "TokenKind", "Meaning row")]
    [DataRow("ReviewSessions", "Status", "ReviewSession row")]
    [DataRow("ReviewCandidates", "Status", "ReviewCandidate row")]
    [DataRow("ReviewCandidates", "PreviousWordStatus", "ReviewCandidate row")]
    [DataRow("LexicalCache", "LookupMode", "LexicalCache row")]
    [DataRow("LexicalCache", "TokenKind", "LexicalCache row")]
    [DataRow("PreparationSessions", "Status", "PreparationSession row")]
    [DataRow("PreparationSessions", "Method", "PreparationSession row")]
    [DataRow("PreparationCandidates", "Status", "PreparationCandidate row")]
    [DataRow("LearningCards", "Direction", "LearningCard row")]
    [DataRow("LearningCards", "State", "LearningCard row")]
    [DataRow("LearningReviews", "Rating", "LearningReview row")]
    [DataRow("LearningSessions", "Status", "LearningSession row")]
    [DataRow("Senses", "Status", "Sense row")]
    [DataRow("SenseAnswerVariantAssignments", "CardDirection", "assignment row")]
    [DataRow("SenseAnswerVariantAssignments", "Requirement", "assignment row")]
    [DataRow("SenseAnswerVariantAssignments", "IsPreferred", "assignment row")]
    [DataRow("AnswerVariantProgress", "InteractionMode", "progress row")]
    public Task MalformedSchema8_UndefinedRequiredEnum_RemainsCompletelyUnchangedAfterReopen(
        string table,
        string column,
        string expectedFailureDetail) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"undefined-{table}-{column}",
            connection => connection.ExecuteAsync($"UPDATE \"{table}\" SET \"{column}\" = 99"),
            expectedFailureDetail);

    [DataTestMethod]
    [DataRow("WordForms", "WordId", "WordForm row")]
    [DataRow("SentenceSpans", "DocumentId", "SentenceSpan row")]
    [DataRow("WordOccurrences", "WordId", "WordOccurrence Word")]
    [DataRow("WordOccurrences", "DocumentId", "WordOccurrence Document")]
    [DataRow("WordOccurrences", "SentenceSpanId", "WordOccurrence SentenceSpan")]
    [DataRow("ReviewStates", "WordId", "ReviewState row")]
    [DataRow("ReviewSessions", "DocumentId", "ReviewSession row")]
    [DataRow("ReviewCandidates", "SessionId", "ReviewCandidate Session")]
    [DataRow("ReviewCandidates", "WordId", "ReviewCandidate Word")]
    [DataRow("PreparationCandidates", "SessionId", "PreparationCandidate Session")]
    [DataRow("PreparationCandidates", "WordId", "PreparationCandidate Word")]
    [DataRow("ContextSnapshots", "SourceDocumentId", "ContextSnapshot source Document")]
    [DataRow("LearningReviews", "SessionId", "LearningReview Session")]
    [DataRow("LearningSessionCards", "SessionId", "LearningSessionCard Session")]
    public Task MalformedSchema8_OrphanedRequiredRelationship_RemainsCompletelyUnchangedAfterReopen(
        string table,
        string column,
        string expectedFailureDetail) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"orphan-{table}-{column}",
            connection => connection.ExecuteAsync($"UPDATE \"{table}\" SET \"{column}\" = 999999"),
            expectedFailureDetail);

    [TestMethod]
    public Task MalformedSchema8_WordOccurrenceSentenceBelongsToDifferentDocument_RemainsCompletelyUnchangedAfterReopen() =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            "occurrence-sentence-wrong-document",
            async connection =>
            {
                await connection.ExecuteAsync(
                    "INSERT INTO Documents (Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content, ContentFingerprint, ImportedAt, WordCount) VALUES (999998, 'Other', 'en', 'de', 0, '', 'other', 'other-document', ?, 1)",
                    new DateTime(2042, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                await connection.ExecuteAsync("UPDATE WordOccurrences SET DocumentId = 999998");
            },
            "WordOccurrence SentenceSpan");

    [DataTestMethod]
    [DataRow("LearningReviews")]
    [DataRow("LearningSessionCards")]
    public Task MalformedSchema8_NonNullMissingTarget_RemainsCompletelyUnchangedAfterReopen(string table) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"missing-target-{table}",
            connection => connection.ExecuteAsync($"UPDATE \"{table}\" SET TargetAnswerVariantId = 999999"),
            table == "LearningReviews" ? "LearningReview target" : "learning queue target");

    [DataTestMethod]
    [DataRow("LearningReviews", 0)]
    [DataRow("LearningSessionCards", 0)]
    [DataRow("LearningReviews", 1)]
    [DataRow("LearningSessionCards", 1)]
    public Task MalformedSchema8_NonNullTargetForWrongSenseOrDirection_RemainsCompletelyUnchangedAfterReopen(
        string table,
        int mismatchKind) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"target-mismatch-{table}-{mismatchKind}",
            connection => SeedMismatchedTargetAsync(connection, table, mismatchKind),
            table == "LearningReviews" ? "LearningReview target" : "learning queue target");

    [DataTestMethod]
    [DataRow("LearningCards", "LastRating", "LearningCard row")]
    [DataRow("LearningSessionCards", "Rating", "LearningSessionCard row")]
    public Task MalformedSchema8_InvalidOptionalRating_RemainsCompletelyUnchangedAfterReopen(
        string table,
        string column,
        string expectedFailureDetail) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"invalid-{table}-{column}",
            connection => connection.ExecuteAsync($"UPDATE \"{table}\" SET \"{column}\" = 99"),
            expectedFailureDetail);

    /// <summary>
    /// Every row rebuilds one column that the real Schema-8 DDL declares <c>NOT NULL</c> as nullable while
    /// leaving all row values valid and non-null, so the only remaining defect is the schema declaration
    /// itself. Proves the nullability gate reads <c>PRAGMA table_info</c> rather than inferring anything
    /// from the current contents, and that a failed initialization still mutates nothing.
    /// </summary>
    [DataTestMethod]
    [DataRow("Senses", "StableId")]
    [DataRow("Senses", "SourceLanguage")]
    [DataRow("Senses", "ProviderSenseId")]
    [DataRow("AnswerVariants", "DisplayText")]
    [DataRow("AnswerVariants", "NormalizedText")]
    [DataRow("Senses", "Status")]
    [DataRow("SenseAnswerVariantAssignments", "Requirement")]
    [DataRow("SenseAnswerVariantAssignments", "IsPreferred")]
    [DataRow("AnswerVariantProgress", "InteractionMode")]
    [DataRow("AnswerVariantProgress", "ReplayVersion")]
    [DataRow("AnswerVariantProgress", "IsMastered")]
    [DataRow("Senses", "WordId")]
    [DataRow("AnswerVariants", "SenseId")]
    [DataRow("SenseAnswerVariantAssignments", "SenseId")]
    [DataRow("SenseAnswerVariantAssignments", "AnswerVariantId")]
    [DataRow("AnswerVariantProgress", "CardId")]
    [DataRow("AnswerVariantProgress", "AnswerVariantId")]
    [DataRow("Senses", "CreatedAtUtc")]
    [DataRow("Senses", "UpdatedAtUtc")]
    [DataRow("AnswerVariants", "UpdatedAtUtc")]
    [DataRow("SenseAnswerVariantAssignments", "CreatedAtUtc")]
    [DataRow("AnswerVariantProgress", "CreatedAtUtc")]
    public Task MalformedSchema8_RequiredColumnRedeclaredNullable_RemainsCompletelyUnchangedAfterReopen(
        string table,
        string column) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"nullable-declaration-{table}-{column}",
            connection => RebuildColumnDeclarationAsync(
                connection, table, column, ColumnDeclarationChange.DropNotNull, requireChange: true),
            $"{table}.{column} must be declared NOT NULL");

    /// <summary>
    /// The mirror direction: a column the real DDL declares nullable on purpose must not silently become
    /// <c>NOT NULL</c>. Values stay non-null so only the declaration differs.
    /// </summary>
    [DataTestMethod]
    [DataRow("Senses", "DefaultMeaningId")]
    [DataRow("AnswerVariants", "SourceMeaningId")]
    [DataRow("SenseAnswerVariantAssignments", "RequiredSinceUtc")]
    [DataRow("AnswerVariantProgress", "LastAssessedAtUtc")]
    [DataRow("LearningCards", "SenseId")]
    public Task MalformedSchema8_IntentionallyNullableColumnRedeclaredNotNull_RemainsCompletelyUnchangedAfterReopen(
        string table,
        string column) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"notnull-declaration-{table}-{column}",
            connection => RebuildColumnDeclarationAsync(
                connection, table, column, ColumnDeclarationChange.AddNotNull, requireChange: true),
            $"{table}.{column} must remain declared nullable");

    /// <summary>
    /// Primary-key semantics are validated by primary-key position and INTEGER (rowid alias) affinity, not by
    /// the <c>notnull</c> flag, so a rebuilt table that keeps the column but loses its primary key is rejected
    /// for both DDL families — the raw Schema-8 tables (reported notnull = 0) and the sqlite-net baseline
    /// <c>LearningCards</c> (reported notnull = 1).
    /// </summary>
    [DataTestMethod]
    [DataRow("Senses")]
    [DataRow("AnswerVariants")]
    [DataRow("SenseAnswerVariantAssignments")]
    [DataRow("AnswerVariantProgress")]
    [DataRow("LearningCards")]
    [DataRow("Documents")]
    public Task MalformedSchema8_PrimaryKeyRebuiltAsPlainColumn_RemainsCompletelyUnchangedAfterReopen(string table) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            $"dropped-primary-key-{table}",
            connection => RebuildColumnDeclarationAsync(
                connection, table, "Id", ColumnDeclarationChange.DropPrimaryKey, requireChange: true),
            $"{table}.Id must be the table's INTEGER PRIMARY KEY");

    /// <summary>
    /// Pins the expected nullability metadata against the DDL a freshly initialized Schema-8 database really
    /// produces, including the two legitimate primary-key <c>notnull</c> reportings: the sqlite-net baseline
    /// DDL appends an explicit <c>not null</c> (reported 1) while the raw Schema-8
    /// <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> does not (reported 0), even though neither can hold NULL.
    /// </summary>
    [DataTestMethod]
    [DataRow("Documents", "Id", 1, 1)]
    [DataRow("Documents", "Title", 0, 0)]
    [DataRow("Words", "Id", 1, 1)]
    [DataRow("Words", "Status", 0, 0)]
    [DataRow("ContextSnapshots", "SenseId", 0, 0)]
    [DataRow("Meanings", "SenseId", 0, 0)]
    [DataRow("Meanings", "StableId", 0, 0)]
    [DataRow("LearningCards", "Id", 1, 1)]
    [DataRow("LearningCards", "WordId", 0, 0)]
    [DataRow("LearningCards", "SenseId", 0, 0)]
    [DataRow("LearningReviews", "TargetAnswerVariantId", 0, 0)]
    [DataRow("LearningReviews", "MatchedAnswerVariantId", 0, 0)]
    [DataRow("LearningSessionCards", "TargetAnswerVariantId", 0, 0)]
    [DataRow("Senses", "Id", 0, 1)]
    [DataRow("Senses", "StableId", 1, 0)]
    [DataRow("Senses", "WordId", 1, 0)]
    [DataRow("Senses", "Status", 1, 0)]
    [DataRow("Senses", "CreatedAtUtc", 1, 0)]
    [DataRow("Senses", "DefaultMeaningId", 0, 0)]
    [DataRow("AnswerVariants", "Id", 0, 1)]
    [DataRow("AnswerVariants", "SenseId", 1, 0)]
    [DataRow("AnswerVariants", "DisplayText", 1, 0)]
    [DataRow("AnswerVariants", "SourceMeaningId", 0, 0)]
    [DataRow("SenseAnswerVariantAssignments", "Id", 0, 1)]
    [DataRow("SenseAnswerVariantAssignments", "AnswerVariantId", 1, 0)]
    [DataRow("SenseAnswerVariantAssignments", "Requirement", 1, 0)]
    [DataRow("SenseAnswerVariantAssignments", "RequiredSinceUtc", 0, 0)]
    [DataRow("AnswerVariantProgress", "Id", 0, 1)]
    [DataRow("AnswerVariantProgress", "CardId", 1, 0)]
    [DataRow("AnswerVariantProgress", "CreatedAtUtc", 1, 0)]
    [DataRow("AnswerVariantProgress", "LastAssessedAtUtc", 0, 0)]
    public async Task FreshSchema8_DeclaredColumnNullabilityMatchesRealDdl(
        string table,
        string column,
        int expectedNotNull,
        int expectedPrimaryKey)
    {
        var path = TemporaryPath($"declared-nullability-{table}-{column}");
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);

            var declared = (await connection.QueryAsync<RebuildColumnRow>($"PRAGMA table_info(\"{table}\")"))
                .Single(item => string.Equals(item.Name, column, StringComparison.OrdinalIgnoreCase));

            Assert.AreEqual(expectedNotNull, declared.NotNull);
            Assert.AreEqual(expectedPrimaryKey, declared.PrimaryKey);
            if (expectedPrimaryKey == 1)
            {
                Assert.IsTrue(
                    string.Equals(declared.Type, "INTEGER", StringComparison.OrdinalIgnoreCase),
                    $"{table}.{column} must be an INTEGER rowid alias but is declared '{declared.Type}'.");
            }
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public Task ValidSchema8_IntentionallyNullableColumnsHoldingNull_ReopenInitializesWithoutMutation() =>
        AssertValidSchema8RemainsUnchangedAfterReopenAsync(
            "valid-intentionally-nullable-columns",
            async connection =>
            {
                await connection.ExecuteAsync("UPDATE Senses SET DefaultMeaningId = NULL");
                await connection.ExecuteAsync("UPDATE AnswerVariants SET SourceMeaningId = NULL");
                await connection.ExecuteAsync("UPDATE SenseAnswerVariantAssignments SET Requirement = 1, RequiredSinceUtc = NULL");
                await connection.ExecuteAsync("UPDATE AnswerVariantProgress SET LastAssessedAtUtc = NULL");
                await connection.ExecuteAsync("UPDATE LearningCards SET LastReviewedAtUtc = NULL, LastRating = NULL");
                await connection.ExecuteAsync("UPDATE Meanings SET SourceRevisionId = NULL");
                await connection.ExecuteAsync("UPDATE WordOccurrences SET TechnicalInstanceYear = NULL");
            });

    [TestMethod]
    public async Task ValidSchema8_NullOptionalRatingsRemainAcceptedAndUnchangedAfterReopen()
    {
        var path = TemporaryPath("valid-null-optional-ratings");
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await SeedRepresentativeSchema8DataAsync(connection);
            await AssignCurrentSchemaLearningWorkflowStableIdsAsync(connection);
            await connection.ExecuteAsync("UPDATE LearningCards SET LastRating = NULL");
            await connection.ExecuteAsync("UPDATE LearningSessionCards SET Rating = NULL");
            await connection.CloseAsync();
            connection = null;
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await connection.CloseAsync();
            connection = null;
            var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            CollectionAssert.AreEqual(before, after);
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public Task ValidSchema8_NullLearningReviewTarget_ReopenInitializesWithoutMutation() =>
        AssertValidSchema8RemainsUnchangedAfterReopenAsync(
            "valid-null-review-target",
            connection => connection.ExecuteAsync("UPDATE LearningReviews SET TargetAnswerVariantId = NULL"));

    [TestMethod]
    public Task ValidSchema8_NullLearningSessionCardTarget_ReopenInitializesWithoutMutation() =>
        AssertValidSchema8RemainsUnchangedAfterReopenAsync(
            "valid-null-queue-target",
            connection => connection.ExecuteAsync("UPDATE LearningSessionCards SET TargetAnswerVariantId = NULL"));

    [TestMethod]
    public Task ValidSchema8_EnumAndNullableBoundaries_ReopenInitializesWithoutMutation() =>
        AssertValidSchema8RemainsUnchangedAfterReopenAsync(
            "valid-enum-nullable-boundaries",
            async connection =>
            {
                await connection.ExecuteAsync("UPDATE SenseAnswerVariantAssignments SET Requirement = 0");
                await connection.ExecuteAsync("UPDATE LearningCards SET State = 5, LastRating = NULL");
                await connection.ExecuteAsync("UPDATE Senses SET Status = 3");
                await connection.ExecuteAsync("UPDATE LearningSessionCards SET Rating = NULL, TargetAnswerVariantId = NULL");
                await connection.ExecuteAsync("UPDATE LearningReviews SET TargetAnswerVariantId = NULL, MatchedAnswerVariantId = NULL");
            });

    [TestMethod]
    public async Task FailedMigration_DoesNotCleanCacheAndRemainsRetryableSchema7()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.Connection.ExecuteAsync("INSERT INTO LexicalCache (CacheKey) VALUES ('legacy|preserve')");
        var wordId = await fixture.InsertWordAsync("broken");
        await fixture.InsertMeaningAsync(wordId + 999, displayTerm: "broken", translation: "kaputt");

        await Assert.ThrowsExactlyAsync<Schema8MigrationException>(() => DatabaseSchema.InitializeAsync(fixture.Connection));

        Assert.AreEqual(7, await fixture.ReadUserVersionAsync());
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LexicalCache WHERE CacheKey = 'legacy|preserve'"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Senses'"));
        Assert.AreEqual(1, await ColumnCountAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task Reset_RecreatesFreshValidatedSchema8()
    {
        await using var database = new TemporarySchema8Database("schema8-activation-reset");
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState, TotalOccurrenceCount, DocumentCount, CreatedAt, UpdatedAt) VALUES ('en', 'temporary', 'temporary', 1, 0, 0, 0, 0, ?, ?)",
                DateTime.UtcNow,
                DateTime.UtcNow);
            return true;
        });

        await database.ResetAsync();

        var result = await database.ReadAsync(async connection => new
        {
            Version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            WordCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"),
            LegacyColumnCount = await ColumnCountAsync(connection, "LearningCards", "MeaningId")
        });
        Assert.AreEqual(8, result.Version);
        Assert.AreEqual(0, result.WordCount);
        Assert.AreEqual(0, result.LegacyColumnCount);
    }

    [TestMethod]
    public async Task NormallyMigratedDatabase_ExportsV2AndRestoresIntoFreshActiveSchema8()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var documentId = await sourceFixture.InsertDocumentAsync(content: "archive evidence");
        var wordId = await sourceFixture.InsertWordAsync("archive", totalOccurrenceCount: 9, documentCount: 1);
        var meaningId = await sourceFixture.InsertMeaningAsync(wordId, displayTerm: "archive", translation: "Archiv");
        var cardId = await sourceFixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, id: 77);
        await sourceFixture.InsertContextAsync(meaningId, wordId, sourceDocumentId: documentId);
        await Schema8DormantMigration.ApplyAsync(sourceFixture.Connection);

        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var sourceService = new BackupService(sourceDatabase, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var archive = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archive, CancellationToken.None);
        archive.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(archive, CancellationToken.None);
        Assert.AreEqual(2, validated.FormatVersion);
        Assert.AreEqual(8, validated.V2!.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(1, validated.V2.Payload.Senses.Count);

        await using var targetDatabase = new TemporarySchema8Database("schema8-activation-restore");
        await targetDatabase.InitializeAsync();
        archive.Position = 0;
        var restore = await new BackupService(targetDatabase, new Schema8BackupFixtureBuilders.FakePlatformInfo())
            .ImportPortableArchiveAsync(archive, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, restore.Status);
        var restored = await targetDatabase.ReadAsync(async connection => new
        {
            Version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            WordCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"),
            SenseCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"),
            MeaningCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings"),
            CardCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"),
            ContextCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ContextSnapshots"),
            PreferredMeaningCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LearningCards c JOIN Meanings m ON m.Id = c.PreferredMeaningId JOIN Senses s ON s.Id = c.SenseId WHERE m.SenseId = s.Id AND m.WordId = c.WordId")
        });
        Assert.AreEqual(8, restored.Version);
        Assert.AreEqual(1, restored.WordCount);
        Assert.AreEqual(1, restored.SenseCount);
        Assert.AreEqual(1, restored.MeaningCount);
        Assert.AreEqual(1, restored.CardCount);
        Assert.AreEqual(1, restored.ContextCount);
        Assert.AreEqual(1, restored.PreferredMeaningCount);
        Assert.AreEqual(77, cardId);
    }

    [TestMethod]
    public async Task NormallyActivatedDatabase_DispatchesLearningWorkflowAndPermanentKnown()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("activation");
        var meaningId = await fixture.InsertMeaningAsync(
            wordId,
            displayTerm: "activation",
            translation: "Aktivierung");
        await fixture.InsertCardAsync(
            wordId,
            meaningId,
            CardDirection.MeaningToTerm,
            state: CardState.Review,
            dueAtUtc: DateTime.UtcNow.AddDays(-1));
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        object? learningCapability = null;
        object? preparationCapability = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            learningCapability = LearningSchemaCapability.Resolve(connection);
            preparationCapability = PreparationSchemaCapability.Resolve(connection);
        });
        Assert.IsInstanceOfType<LearningSchema10CapabilityResult>(learningCapability);
        Assert.IsInstanceOfType<PreparationSchema10CapabilityResult>(preparationCapability);

        var clock = new FakeClock(DateTime.UtcNow.AddDays(1));
        var learning = new LearningService(
            database,
            new SimpleSpacedRepetitionScheduler(),
            new SpellingAnswerComparer(),
            clock,
            new TypingAppSettings());
        var workflow = new WorkflowStateService(database, clock);

        var snapshot = await workflow.GetSnapshotAsync();
        Assert.AreEqual(1, snapshot.DueCardCount);
        var load = await learning.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(LearningInteractionMode.Typing, load.Card.InteractionMode);
        var spelling = await learning.CheckSpellingAsync(load.Card.QueueItemId, "activation");
        Assert.IsTrue(spelling.IsCorrect);
        var rated = await learning.RateAsync(load.Card.QueueItemId, ReviewRating.Good);
        Assert.IsNotNull(rated.CompletedSummary);
        await learning.RunMaintenanceAsync();

        Assert.IsTrue(await learning.MarkPermanentlyKnownAsync(wordId, confirmed: true));
        Assert.AreEqual((int)WordStatus.Known, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE WordId = ?", wordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings WHERE WordId = ?", wordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
    }

    private static Task<int> ColumnCountAsync(SQLiteAsyncConnection connection, string table, string column) =>
        connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = ?", column);

    private static Task<int> IndexCountAsync(SQLiteAsyncConnection connection, string index) =>
        connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", index);

    private static Task AssertMalformedIndexRemainsUnchangedAsync(
        string suffix,
        string indexName,
        string replacementSql,
        string expectedFailureDetail) =>
        AssertMalformedSchema8RemainsUnchangedAsync(
            suffix,
            async connection =>
            {
                await connection.ExecuteAsync($"DROP INDEX \"{indexName}\"");
                await connection.ExecuteAsync(replacementSql);
            },
            expectedFailureDetail);

    private static async Task AssertMalformedSchema8RemainsUnchangedAsync(
        string suffix,
        Func<SQLiteAsyncConnection, Task> corrupt,
        string expectedFailureDetail)
    {
        var path = TemporaryPath(suffix);
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await Schema7Fixture.InitializeEmptyAsync(connection);
            await Schema8DormantMigration.ApplyAsync(connection);
            await SeedRepresentativeSchema8DataAsync(connection);
            await Schema8DormantMigration.ApplyAsync(connection);
            await connection.ExecuteAsync(
                "INSERT INTO LexicalCache (CacheKey, SourceLanguage, ExplanationLanguage, NormalizedLemma, LookupMode, TargetLanguage, CanonicalLookupTerm, TokenKind, Provider, ProviderSchemaVersion, ResultJson, SourceProject, PageTitle, Attribution, FetchedAtUtc) "
                + "VALUES ('legacy|malformed-preserve', 'en', 'de', 'preserve', 0, '', 'preserve', 0, 'test', 2, '{}', 'test-project', 'Preserve', 'preserve attribution', ?)",
                new DateTime(2041, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            await corrupt(connection);
            await connection.ExecuteAsync(
                "CREATE TABLE ActivationSentinel (Value TEXT NOT NULL, OptionalValue TEXT NULL, Payload BLOB NOT NULL)");
            await connection.ExecuteAsync(
                "INSERT INTO ActivationSentinel (Value, OptionalValue, Payload) VALUES ('unchanged', NULL, X'0001FF')");
            await connection.CloseAsync();
            connection = null;
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            connection = new SQLiteAsyncConnection(path);
            var exception = await Assert.ThrowsExactlyAsync<Schema8MigrationException>(
                () => DatabaseSchema.InitializeAsync(connection));
            StringAssert.Contains(exception.Message, expectedFailureDetail);

            await connection.CloseAsync();
            connection = null;
            var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            CollectionAssert.AreEqual(before, after);
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    private static async Task AssertValidSchema8RemainsUnchangedAfterReopenAsync(
        string suffix,
        Func<SQLiteAsyncConnection, Task> arrange)
    {
        var path = TemporaryPath(suffix);
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await SeedRepresentativeSchema8DataAsync(connection);
            await AssignCurrentSchemaLearningWorkflowStableIdsAsync(connection);
            await arrange(connection);
            await connection.CloseAsync();
            connection = null;
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await connection.CloseAsync();
            connection = null;
            var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(path);

            CollectionAssert.AreEqual(before, after);
        }
        finally
        {
            await CloseAndDeleteAsync(connection, path);
        }
    }

    private static async Task SeedMismatchedTargetAsync(
        SQLiteAsyncConnection connection,
        string targetTable,
        int mismatchKind)
    {
        const int mismatchSenseId = 901;
        const int mismatchMeaningId = 902;
        const int mismatchVariantId = 903;
        var timestamp = new DateTime(2043, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        if (mismatchKind == 0)
        {
            await connection.ExecuteAsync(
                "INSERT INTO Senses (Id, StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, 'sense-target-mismatch', 102, 'en', 'de', '', '', '', '', '', NULL, 0, ?, ?)",
                mismatchSenseId,
                timestamp,
                timestamp);
            await connection.ExecuteAsync(
                "INSERT INTO Meanings (Id, WordId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId, AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, SenseId, StableId) VALUES (?, 102, 'de', 'en', 'other', 'other', '', 0, '', '', 'andere', '', '', '', '[]', 'andere', 'test', '', '', NULL, '', 1, ?, ?, ?, ?, 'meaning-target-mismatch')",
                mismatchMeaningId,
                timestamp,
                timestamp,
                timestamp,
                mismatchSenseId);
            await connection.ExecuteAsync("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", mismatchMeaningId, mismatchSenseId);
            await connection.ExecuteAsync(
                "INSERT INTO AnswerVariants (Id, StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc) VALUES (?, 'variant-target-mismatch', ?, 'en', 'other', 'other', ?, ?, ?)",
                mismatchVariantId,
                mismatchSenseId,
                mismatchMeaningId,
                timestamp,
                timestamp);
            await connection.ExecuteAsync(
                "INSERT INTO SenseAnswerVariantAssignments (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc) VALUES ('assignment-target-mismatch', ?, 0, ?, 0, 1, ?, ?, ?)",
                mismatchSenseId,
                mismatchVariantId,
                timestamp,
                timestamp,
                timestamp);
        }
        else
        {
            await connection.ExecuteAsync(
                "INSERT INTO AnswerVariants (Id, StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc) VALUES (?, 'variant-direction-mismatch', 106, 'de', 'bewahren', 'bewahren', 107, ?, ?)",
                mismatchVariantId,
                timestamp,
                timestamp);
            await connection.ExecuteAsync(
                "INSERT INTO SenseAnswerVariantAssignments (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc) VALUES ('assignment-direction-mismatch', 106, 1, ?, 0, 1, ?, ?, ?)",
                mismatchVariantId,
                timestamp,
                timestamp,
                timestamp);
        }

        await connection.ExecuteAsync($"UPDATE \"{targetTable}\" SET TargetAnswerVariantId = ?", mismatchVariantId);
    }

    /// <summary>
    /// Deterministic canonical learning-workflow identities for the two representative rows
    /// <see cref="SeedRepresentativeSchema8DataAsync"/> inserts with raw SQL. Each is exactly 32 lowercase
    /// hex characters (the GUID-form shape <c>LearningWorkflowStableId</c> accepts), distinct from the
    /// other, and unique within its own table — the row's fixture Id is encoded in the trailing digits only
    /// to keep the pairing readable, never as a semantic dependency.
    /// </summary>
    private const string RepresentativeSessionStableId = "5e551000000000000000000000000119";

    private const string RepresentativeQueueRowStableId = "9ca4d000000000000000000000000121";

    /// <summary>
    /// Gives the two representative learning-workflow rows the identities a current-schema (Schema-10)
    /// database requires. Called only where the fixture was built through
    /// <see cref="DatabaseSchema.InitializeAsync"/> — i.e. where the <c>StableId</c> columns physically
    /// exist — and never on the pinned physical Schema-8 malformed-shape path, whose whole purpose is a
    /// database that predates them. Assignment is explicit rather than generated so the seeded values are
    /// byte-stable across runs, and it happens at seed time rather than at reopen because
    /// <c>Schema10ShapeValidator</c> deliberately fails closed on a missing identity instead of repairing
    /// one.
    /// </summary>
    private static async Task AssignCurrentSchemaLearningWorkflowStableIdsAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync(
            "UPDATE LearningSessions SET StableId = ? WHERE Id = 119", RepresentativeSessionStableId);
        await connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET StableId = ? WHERE Id = 121", RepresentativeQueueRowStableId);
    }

    private static async Task SeedRepresentativeSchema8DataAsync(SQLiteAsyncConnection connection)
    {
        var timestamp = new DateTime(2040, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await connection.RunInTransactionAsync(sqlite =>
        {
            sqlite.Execute(
                "INSERT INTO Documents (Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content, ContentFingerprint, ImportedAt, WordCount) "
                + "VALUES (101, 'Malformed evidence', 'en', 'de', 1, 'de', 'persistent malformed evidence', 'malformed-document', ?, 3)",
                timestamp);
            sqlite.Execute(
                "INSERT INTO Words (Id, Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState, TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt) "
                + "VALUES (102, 'en', 'preserve', 'preserve', 2, 0, 2, 17, 3, 1, 4, 5, 2, 1, ?, ?)",
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute("INSERT INTO WordForms (Id, WordId, SurfaceForm, OccurrenceCount) VALUES (103, 102, 'preserves', 6)");
            sqlite.Execute("INSERT INTO SentenceSpans (Id, DocumentId, StartPosition, Length, \"Order\") VALUES (104, 101, 0, 29, 0)");
            sqlite.Execute(
                "INSERT INTO WordOccurrences (Id, WordId, DocumentId, SentenceSpanId, StartPosition, Length, SurfaceForm, TechnicalFamily, TechnicalInstanceYear, TechnicalInstanceIdentifier, TechnicalVariant, \"Order\") "
                + "VALUES (105, 102, 101, 104, 11, 8, 'preserve', 0, NULL, '', '', 0)");
            sqlite.Execute(
                "INSERT INTO Senses (Id, StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status, CreatedAtUtc, UpdatedAtUtc) "
                + "VALUES (106, 'sense-malformed-preserve', 102, 'en', 'de', 'provider-sense', 'testing', 'verb', '', '', NULL, 0, ?, ?)",
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO Meanings (Id, WordId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId, AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, SenseId, StableId) "
                + "VALUES (107, 102, 'de', 'en', 'preserve', 'preserve', '', 0, 'provider-meaning', '', 'bewahren', 'keep unchanged', 'preserve this row', 'note', '[]', 'bewahren', 'test', 'test-project', 'Preserve', 9876, 'test attribution', 1, ?, ?, ?, 106, 'meaning-malformed-preserve')",
                timestamp,
                timestamp.AddMinutes(1),
                timestamp.AddMinutes(2));
            sqlite.Execute("UPDATE Senses SET DefaultMeaningId = 107 WHERE Id = 106");
            sqlite.Execute(
                "INSERT INTO ReviewStates (Id, WordId, ReviewCount, ForgotCount, PartialCount, KnownCount, LastReviewedAt) VALUES (108, 102, 8, 2, 1, 5, ?)",
                timestamp);
            sqlite.Execute(
                "INSERT INTO ReviewSessions (Id, DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount, DecisionSequence, StartedAt, CompletedAt) "
                + "VALUES (109, 101, 1, 1, 1, 0, 1, 0, 1, ?, ?)",
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO ReviewCandidates (Id, SessionId, WordId, \"Order\", Status, PreviousWordStatus, PreviousTotalOccurrenceCount, PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt) "
                + "VALUES (110, 109, 102, 0, 2, 0, 16, 2, ?, 1, 0, ?)",
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO LexicalCache (Id, CacheKey, SourceLanguage, ExplanationLanguage, NormalizedLemma, LookupMode, TargetLanguage, CanonicalLookupTerm, TokenKind, Provider, ProviderSchemaVersion, ResultJson, SourceProject, PageTitle, RevisionId, Attribution, FetchedAtUtc) "
                + "VALUES (111, 'v2|malformed-preserve', 'en', 'de', 'preserve', 1, 'de', 'preserve', 0, 'test', 2, '{}', 'test-project', 'Preserve', 9876, 'test attribution', ?)",
                timestamp);
            sqlite.Execute(
                "INSERT INTO PreparationSessions (Id, Status, Method, TotalItems, CompletedItems, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) VALUES (112, 1, 0, 1, 1, ?, ?, ?)",
                timestamp,
                timestamp.AddMinutes(1),
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO PreparationCandidates (Id, SessionId, WordId, \"Order\", Status, ResultJson, SelectedMeaningIndex, LastErrorCode, LookupAttemptCount, UpdatedAtUtc) "
                + "VALUES (113, 112, 102, 0, 2, '{}', 0, '', 1, ?)",
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO ContextSnapshots (Id, MeaningId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart, TargetLength, NormalizedFingerprint, CreatedAtUtc, SenseId) "
                + "VALUES (114, 107, 102, 101, 'Malformed evidence', 'persistent malformed evidence', 11, 8, 'malformed-context', ?, 106)",
                timestamp);
            sqlite.Execute(
                "INSERT INTO AnswerVariants (Id, StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc) "
                + "VALUES (115, 'variant-malformed-preserve', 106, 'en', 'preserve', 'preserve', 107, ?, ?)",
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc) "
                + "VALUES (116, 102, 106, 107, 0, 2, ?, 11, 2.2, 6, 2, ?, 2, ?, ?)",
                timestamp.AddDays(1),
                timestamp.AddDays(-1),
                timestamp,
                timestamp.AddMinutes(3));
            sqlite.Execute(
                "INSERT INTO SenseAnswerVariantAssignments (Id, StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc) "
                + "VALUES (117, 'assignment-malformed-preserve', 106, 0, 115, 0, 1, ?, ?, ?)",
                timestamp,
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO AnswerVariantProgress (Id, CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc, MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc) "
                + "VALUES (118, 116, 115, 1, 4, 5, 2, ?, 1, 0, 1, ?, ?)",
                timestamp,
                timestamp,
                timestamp.AddMinutes(1));
            sqlite.Execute(
                "INSERT INTO LearningSessions (Id, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) "
                + "VALUES (119, 1, 1, 1, 0, 0, 1, 0, ?, ?, ?)",
                timestamp,
                timestamp.AddMinutes(5),
                timestamp.AddMinutes(5));
            sqlite.Execute(
                "INSERT INTO LearningReviews (Id, CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId) "
                + "VALUES (120, 116, 119, 2, 1, 1, ?, ?, 11, 2.2, 115, 115)",
                timestamp.AddMinutes(5),
                timestamp);
            sqlite.Execute(
                "INSERT INTO LearningSessionCards (Id, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId) "
                + "VALUES (121, 119, 116, 0, 1, 0, 1, 1, 1, 1, 2, ?, 115)",
                timestamp.AddMinutes(5));
        });
    }

    private static async Task RebuildTableWithNullableNullAsync(
        SQLiteAsyncConnection connection,
        string tableName,
        string columnName)
    {
        await RebuildColumnDeclarationAsync(
            connection, tableName, columnName, ColumnDeclarationChange.DropNotNull, requireChange: false);
        await connection.ExecuteAsync(
            $"UPDATE \"{EscapeIdentifier(tableName)}\" SET \"{EscapeIdentifier(columnName)}\" = NULL");
    }

    /// <summary>
    /// Rebuilds <paramref name="tableName"/> so that exactly one column's declaration changes — dropping or
    /// adding <c>NOT NULL</c>, or dropping its <c>PRIMARY KEY</c> — while preserving every other column
    /// declaration, default, row value, and index. <paramref name="requireChange"/> makes the helper fail loudly
    /// when the column does not actually carry the declaration under test, so a nullability test can never pass
    /// against an unchanged database.
    /// </summary>
    private static Task RebuildColumnDeclarationAsync(
        SQLiteAsyncConnection connection,
        string tableName,
        string columnName,
        ColumnDeclarationChange change,
        bool requireChange) =>
        connection.RunInTransactionAsync(sqlite =>
        {
            var escapedTable = EscapeIdentifier(tableName);
            var columns = sqlite.Query<RebuildColumnRow>($"PRAGMA table_info(\"{escapedTable}\")")
                .OrderBy(column => column.ColumnId)
                .ToArray();
            var target = columns.SingleOrDefault(column =>
                string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                throw new InvalidOperationException($"Expected required column {tableName}.{columnName} was not found.");
            }

            var alreadyInTargetState = change switch
            {
                ColumnDeclarationChange.DropNotNull => target.NotNull == 0,
                ColumnDeclarationChange.AddNotNull => target.NotNull == 1,
                _ => target.PrimaryKey == 0
            };
            if (alreadyInTargetState)
            {
                if (requireChange)
                {
                    throw new InvalidOperationException(
                        $"{tableName}.{columnName} does not carry the declaration this rebuild is meant to change "
                        + $"(notnull = {target.NotNull}, pk = {target.PrimaryKey}).");
                }

                return;
            }

            var indexes = sqlite.Query<RebuildIndexRow>(
                "SELECT name, sql AS Sql FROM sqlite_master WHERE type = 'index' AND tbl_name = ? AND sql IS NOT NULL ORDER BY name",
                tableName);
            var originalSql = sqlite.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?", tableName);
            var usesAutoIncrement = originalSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase);
            var malformedTable = $"{tableName}__RedeclaredColumn";
            var definitions = columns.Select(column =>
            {
                var isTarget = string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase);
                var definition = $"\"{EscapeIdentifier(column.Name)}\" {column.Type}";
                var keepsPrimaryKey = column.PrimaryKey != 0
                                      && !(isTarget && change == ColumnDeclarationChange.DropPrimaryKey);
                if (keepsPrimaryKey)
                {
                    definition += " PRIMARY KEY";
                    if (usesAutoIncrement
                        && string.Equals(column.Type, "INTEGER", StringComparison.OrdinalIgnoreCase))
                    {
                        definition += " AUTOINCREMENT";
                    }
                }

                var isNotNull = isTarget
                    ? change switch
                    {
                        ColumnDeclarationChange.DropNotNull => false,
                        ColumnDeclarationChange.AddNotNull => true,
                        _ => column.NotNull == 1
                    }
                    : column.NotNull == 1;
                if (isNotNull)
                {
                    definition += " NOT NULL";
                }

                if (column.DefaultValue is not null)
                {
                    definition += $" DEFAULT {column.DefaultValue}";
                }

                return definition;
            });
            var columnList = string.Join(", ", columns.Select(column => $"\"{EscapeIdentifier(column.Name)}\""));

            sqlite.Execute($"CREATE TABLE \"{EscapeIdentifier(malformedTable)}\" ({string.Join(", ", definitions)})");
            sqlite.Execute(
                $"INSERT INTO \"{EscapeIdentifier(malformedTable)}\" ({columnList}) SELECT {columnList} FROM \"{escapedTable}\"");
            sqlite.Execute($"DROP TABLE \"{escapedTable}\"");
            sqlite.Execute($"ALTER TABLE \"{EscapeIdentifier(malformedTable)}\" RENAME TO \"{escapedTable}\"");
            foreach (var index in indexes)
            {
                sqlite.Execute(index.Sql);
            }
        });

    private enum ColumnDeclarationChange
    {
        DropNotNull,
        AddNotNull,
        DropPrimaryKey
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    private static string TemporaryPath(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-schema8-activation-{suffix}-{Guid.NewGuid():N}.db3");

    private static Task CloseAndDeleteAsync(SQLiteAsyncConnection? connection, string path) =>
        TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);

    private sealed class StableIdRow
    {
        public string StableId { get; set; } = string.Empty;
    }

    private sealed class RebuildColumnRow
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

    private sealed class RebuildIndexRow
    {
        public string Name { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }

    private sealed class TypingAppSettings : IAppSettingsService
    {
        public int PreparationLimit => 20;
        public IReadOnlyList<int> SupportedPreparationLimits => [20];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => LearningMode.Typing;
        public bool HasOnlineLookupConsent => false;

        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }
}
