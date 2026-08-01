using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 1 — crash-mid-migration rollback proofs (deterministic fault injection at named
/// checkpoints, never a real bug), retry-after-rollback, and the RENAME COLUMN vs table-rebuild fallback.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema8DormantMigrationFaultInjectionTests
{
    private sealed class InjectedTestFault : Exception
    {
        public InjectedTestFault(string checkpoint) : base($"injected-{checkpoint}")
        {
        }
    }

    [TestMethod]
    public async Task EveryNamedCheckpoint_RollsBackCompletelyAndRetrySucceeds()
    {
        var checkpoints = new[]
        {
            "after-table-creation",
            "after-generation",
            "after-backfill",
            "after-forward-validation",
            "after-old-index-drop",
            "after-new-index-create",
            "after-final-validation"
        };

        foreach (var checkpointToFail in checkpoints)
        {
            await using var fixture = await Schema7Fixture.CreateAsync();
            var seed = await SeedRepresentativeSchema7Async(fixture);
            var before = await fixture.CapturePersistentStateAsync();
            var unchangedRowsBefore = await PersistentDatabaseSnapshot.CaptureTableRowsAsync(
                fixture.Connection,
                UnchangedAuthoritativeTables);
            var transformedRowsBefore = await CaptureTransformedAuthoritativeRowsAsync(fixture.Connection, schema8: false);
            var options = new Schema8MigrationOptions
            {
                FaultInjectionHook = checkpoint =>
                {
                    if (checkpoint == checkpointToFail)
                    {
                        throw new InjectedTestFault(checkpoint);
                    }
                }
            };

            await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));
            Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection), checkpointToFail);
            Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"), checkpointToFail);
            Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"), checkpointToFail);

            await fixture.ReopenAsync();
            var after = await fixture.CapturePersistentStateAsync();
            CollectionAssert.AreEqual(before, after, checkpointToFail);
            Assert.AreEqual(7, await fixture.ReadUserVersionAsync(), checkpointToFail);

            await Schema8DormantMigration.ApplyAsync(fixture.Connection);
            await fixture.ReopenAsync();
            await DatabaseSchema.InitializeAsync(fixture.Connection);
            await AssertRepresentativeDataPreservedAsync(
                fixture,
                seed,
                unchangedRowsBefore,
                transformedRowsBefore,
                checkpointToFail);
        }
    }

    [TestMethod]
    public async Task InjectedCancellation_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seed = await SeedRepresentativeSchema7Async(fixture);
        var before = await fixture.CapturePersistentStateAsync();
        var unchangedRowsBefore = await PersistentDatabaseSnapshot.CaptureTableRowsAsync(
            fixture.Connection,
            UnchangedAuthoritativeTables);
        var transformedRowsBefore = await CaptureTransformedAuthoritativeRowsAsync(fixture.Connection, schema8: false);
        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-backfill")
                {
                    throw new OperationCanceledException("Injected cancellation.");
                }
            }
        };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));
        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));

        await fixture.ReopenAsync();
        var after = await fixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(7, await fixture.ReadUserVersionAsync());

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        await fixture.ReopenAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        await AssertRepresentativeDataPreservedAsync(
            fixture,
            seed,
            unchangedRowsBefore,
            transformedRowsBefore,
            "cancellation");
    }

    private static async Task<(int wordId, int meaningId, int cardId)> SeedOneWordAsync(Schema7Fixture fixture)
    {
        var wordId = await fixture.InsertWordAsync("fault-test");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "fault-test", translation: "Fehlertest");
        var cardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        return (wordId, meaningId, cardId);
    }

    private static async Task<RepresentativeSeed> SeedRepresentativeSchema7Async(Schema7Fixture fixture)
    {
        var timestamp = new DateTime(2034, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var documentId = await fixture.InsertDocumentAsync(
            title: "Rollback evidence",
            content: "representative rollback evidence",
            wordCount: 9,
            importedAt: timestamp);
        var wordId = await fixture.InsertWordAsync(
            "rollback",
            status: WordStatus.Learning,
            totalOccurrenceCount: 23,
            documentCount: 5,
            consecutiveRecallSuccessCount: 4,
            consecutiveTypingSuccessCount: 3,
            consecutiveTypingFailureCount: 2,
            masteryReviewExtensionScheduled: true,
            createdAt: timestamp,
            updatedAt: timestamp.AddMinutes(1));
        var meaningId = await fixture.InsertMeaningAsync(
            wordId,
            displayTerm: "rollback",
            translation: "Zurueckrollen",
            definition: "restore the earlier state",
            createdAt: timestamp,
            updatedAt: timestamp.AddMinutes(2));
        var cardId = await fixture.InsertCardAsync(
            wordId,
            meaningId,
            CardDirection.MeaningToTerm,
            state: CardState.Review,
            dueAtUtc: timestamp.AddDays(1),
            intervalDays: 11,
            easeFactor: 2.2,
            successfulReviewCount: 6,
            lapseCount: 2,
            lastReviewedAtUtc: timestamp.AddDays(-1),
            lastRating: ReviewRating.Good,
            createdAtUtc: timestamp,
            updatedAtUtc: timestamp.AddMinutes(3),
            id: 41);
        await fixture.InsertContextAsync(
            meaningId,
            wordId,
            sourceDocumentId: documentId,
            sourceDocumentTitle: "Rollback evidence",
            text: "representative rollback evidence",
            targetStart: 15,
            targetLength: 8,
            normalizedFingerprint: "rollback-context",
            createdAtUtc: timestamp);
        var learningSessionId = await fixture.InsertLearningSessionAsync(
            status: LearningSessionStatus.Completed,
            startedAtUtc: timestamp,
            updatedAtUtc: timestamp.AddMinutes(4),
            completedAtUtc: timestamp.AddMinutes(5));
        await fixture.InsertReviewAsync(
            cardId,
            learningSessionId,
            rating: ReviewRating.Good,
            wasTypedAnswer: true,
            reviewedAtUtc: timestamp.AddMinutes(5),
            dueAtUtc: timestamp,
            intervalDays: 11,
            easeFactor: 2.2);
        await fixture.InsertQueueItemAsync(
            learningSessionId,
            cardId,
            queueOrder: 0,
            answerRevealed: true,
            spellingChecked: true,
            spellingCorrect: true,
            isCompleted: true,
            rating: ReviewRating.Good,
            completedAtUtc: timestamp.AddMinutes(5));

        await fixture.Connection.ExecuteAsync(
            "INSERT INTO WordForms (WordId, SurfaceForm, OccurrenceCount) VALUES (?, 'rollbacks', 7)", wordId);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO SentenceSpans (DocumentId, StartPosition, Length, \"Order\") VALUES (?, 0, 32, 0)", documentId);
        var sentenceId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO WordOccurrences
                (WordId, DocumentId, SentenceSpanId, StartPosition, Length, SurfaceForm, TechnicalFamily,
                 TechnicalInstanceYear, TechnicalInstanceIdentifier, TechnicalVariant, "Order")
            VALUES (?, ?, ?, 15, 8, 'rollback', 0, NULL, '', '', 0)
            """,
            wordId,
            documentId,
            sentenceId);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO ReviewStates (WordId, ReviewCount, ForgotCount, PartialCount, KnownCount, LastReviewedAt) VALUES (?, 8, 2, 1, 5, ?)",
            wordId,
            timestamp);
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, 1, 1, 1, 0, 1, 0, 1, ?, ?)
            """,
            documentId,
            timestamp,
            timestamp.AddMinutes(1));
        var reviewSessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewCandidates
                (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount,
                 PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
            VALUES (?, ?, 0, 2, 0, 22, 4, ?, 1, 0, ?)
            """,
            reviewSessionId,
            wordId,
            timestamp,
            timestamp.AddMinutes(1));
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LexicalCache
                (CacheKey, SourceLanguage, ExplanationLanguage, NormalizedLemma, LookupMode, TargetLanguage,
                 CanonicalLookupTerm, TokenKind, Provider, ProviderSchemaVersion, ResultJson, SourceProject,
                 PageTitle, RevisionId, Attribution, FetchedAtUtc)
            VALUES ('v2|rollback-sentinel', 'en', 'de', 'rollback', 0, '', 'rollback', 0, 'test', 2,
                    '{}', 'test-project', 'Rollback', 1234, 'test attribution', ?)
            """,
            timestamp);
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO PreparationSessions
                (Status, Method, TotalItems, CompletedItems, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (2, 0, 1, 1, ?, ?, ?)
            """,
            timestamp,
            timestamp.AddMinutes(1),
            timestamp.AddMinutes(1));
        var preparationSessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO PreparationCandidates
                (SessionId, WordId, "Order", Status, ResultJson, SelectedMeaningIndex, LastErrorCode,
                 LookupAttemptCount, UpdatedAtUtc)
            VALUES (?, ?, 0, 2, '{}', 0, '', 1, ?)
            """,
            preparationSessionId,
            wordId,
            timestamp.AddMinutes(1));

        return new RepresentativeSeed(documentId, wordId, meaningId, cardId);
    }

    private static async Task AssertRepresentativeDataPreservedAsync(
        Schema7Fixture fixture,
        RepresentativeSeed seed,
        string[] unchangedRowsBefore,
        string[] transformedRowsBefore,
        string assertionMessage)
    {
        Assert.AreEqual(8, await fixture.ReadUserVersionAsync(), assertionMessage);
        bool validSchema8 = false;
        string? validationFailure = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            validSchema8 = Schema8ShapeValidator.IsValidDatabase(connection, out validationFailure));
        Assert.IsTrue(validSchema8, $"{assertionMessage}: {validationFailure}");

        var unchangedRowsAfter = await PersistentDatabaseSnapshot.CaptureTableRowsAsync(
            fixture.Connection,
            UnchangedAuthoritativeTables);
        CollectionAssert.AreEqual(unchangedRowsBefore, unchangedRowsAfter, assertionMessage);

        var transformedRowsAfter = await CaptureTransformedAuthoritativeRowsAsync(fixture.Connection, schema8: true);
        CollectionAssert.AreEqual(transformedRowsBefore, transformedRowsAfter, assertionMessage);

        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Documents WHERE Id = ?", seed.DocumentId), assertionMessage);
        Assert.AreEqual(23, await fixture.Connection.ExecuteScalarAsync<int>("SELECT TotalOccurrenceCount FROM Words WHERE Id = ?", seed.WordId), assertionMessage);
        Assert.AreEqual(5, await fixture.Connection.ExecuteScalarAsync<int>("SELECT DocumentCount FROM Words WHERE Id = ?", seed.WordId), assertionMessage);
        Assert.AreEqual((int)WordStatus.UnknownBacklog, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", seed.WordId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WordForms WHERE WordId = ?", seed.WordId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WordOccurrences WHERE WordId = ?", seed.WordId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings WHERE Id = ?", seed.MeaningId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ContextSnapshots WHERE WordId = ?", seed.WordId), assertionMessage);
        Assert.AreEqual(seed.CardId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards WHERE Id = ?", seed.CardId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews WHERE CardId = ?", seed.CardId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards WHERE CardId = ?", seed.CardId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LexicalCache WHERE CacheKey = 'v2|rollback-sentinel'"), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ReviewStates WHERE WordId = ?", seed.WordId), assertionMessage);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PreparationCandidates WHERE WordId = ?", seed.WordId), assertionMessage);

        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Senses s JOIN Meanings m ON m.SenseId = s.Id JOIN LearningCards c ON c.SenseId = s.Id AND c.PreferredMeaningId = m.Id JOIN ContextSnapshots x ON x.SenseId = s.Id AND x.MeaningId = m.Id WHERE s.WordId = ? AND m.Id = ? AND c.Id = ? AND x.WordId = ?",
                seed.WordId,
                seed.MeaningId,
                seed.CardId,
                seed.WordId),
            assertionMessage);
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LearningCards c JOIN SenseAnswerVariantAssignments a ON a.SenseId = c.SenseId AND a.CardDirection = c.Direction JOIN AnswerVariants v ON v.Id = a.AnswerVariantId AND v.SenseId = c.SenseId JOIN AnswerVariantProgress p ON p.CardId = c.Id AND p.AnswerVariantId = v.Id WHERE c.Id = ? AND a.Requirement = 0 AND a.RequiredSinceUtc IS NOT NULL",
                seed.CardId),
            assertionMessage);
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LearningReviews r JOIN LearningSessionCards q ON q.CardId = r.CardId AND q.TargetAnswerVariantId = r.TargetAnswerVariantId WHERE r.CardId = ? AND r.TargetAnswerVariantId IS NOT NULL AND r.MatchedAnswerVariantId = r.TargetAnswerVariantId",
                seed.CardId),
            assertionMessage);
    }

    private static async Task<string[]> CaptureTransformedAuthoritativeRowsAsync(
        SQLiteAsyncConnection connection,
        bool schema8)
    {
        var cardMeaningColumn = schema8 ? "PreferredMeaningId" : "MeaningId";
        var projections = new[]
        {
            (Table: "Words", Columns: new[]
            {
                "Id", "Language", "CanonicalTerm", "NormalizedTerm", "TokenKind", "PreparationState",
                "TotalOccurrenceCount", "DocumentCount", "AutomaticInteractionMode",
                "ConsecutiveRecallSuccessCount", "ConsecutiveTypingSuccessCount",
                "ConsecutiveTypingFailureCount", "MasteryReviewExtensionScheduled", "CreatedAt", "UpdatedAt"
            }),
            (Table: "Meanings", Columns: new[]
            {
                "Id", "WordId", "ExplanationLanguage", "SourceLanguage", "DisplayTerm",
                "EncounteredSurfaceForm", "GrammaticalRelationship", "TokenKind", "SelectedMeaningId",
                "AcronymExpansion", "Translation", "Definition", "DictionaryExample", "AdditionalNote",
                "AcceptedAliasesJson", "TranslationOrDefinition", "Source", "SourceProject",
                "SourcePageTitle", "SourceRevisionId", "Attribution", "ConfirmedByUser", "CreatedAt",
                "UpdatedAt", "PreparedAt"
            }),
            (Table: "ContextSnapshots", Columns: new[]
            {
                "Id", "MeaningId", "WordId", "SourceDocumentId", "SourceDocumentTitle", "Text",
                "TargetStart", "TargetLength", "NormalizedFingerprint", "CreatedAtUtc"
            }),
            (Table: "LearningCards", Columns: new[]
            {
                "Id", "WordId", cardMeaningColumn, "Direction", "State", "DueAtUtc", "IntervalDays",
                "EaseFactor", "SuccessfulReviewCount", "LapseCount", "LastReviewedAtUtc", "LastRating",
                "CreatedAtUtc", "UpdatedAtUtc"
            }),
            (Table: "LearningReviews", Columns: new[]
            {
                "Id", "CardId", "SessionId", "Rating", "WasTypedAnswer", "WasCorrect", "ReviewedAtUtc",
                "DueAtUtc", "IntervalDays", "EaseFactor"
            }),
            (Table: "LearningSessionCards", Columns: new[]
            {
                "Id", "SessionId", "CardId", "QueueOrder", "IsDueCard", "IsAgainRepeat", "AnswerRevealed",
                "SpellingChecked", "SpellingCorrect", "IsCompleted", "Rating", "CompletedAtUtc"
            })
        };

        var result = new List<string>();
        foreach (var projection in projections)
        {
            var valueExpression = string.Join(
                " || ':' || ",
                projection.Columns.Select(column => $"hex(CAST(quote(\"{column}\") AS BLOB))"));
            var rows = await connection.QueryScalarsAsync<string>(
                $"SELECT {valueExpression} FROM \"{projection.Table}\" ORDER BY Id");
            result.AddRange(rows.Select((row, ordinal) => $"{projection.Table}|{ordinal}|{row}"));
        }

        return [.. result];
    }

    private static readonly string[] UnchangedAuthoritativeTables =
    [
        "Documents",
        "WordForms",
        "SentenceSpans",
        "WordOccurrences",
        "ReviewStates",
        "ReviewSessions",
        "ReviewCandidates",
        "LexicalCache",
        "PreparationSessions",
        "PreparationCandidates",
        "LearningSessions"
    ];

    private sealed record RepresentativeSeed(int DocumentId, int WordId, int MeaningId, int CardId);

    [TestMethod]
    public async Task FailureAfterTableCreation_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-table-creation")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task FailureAfterBackfill_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-backfill")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task FailureAfterOldIndexRemoval_BeforeVersionUpdate_RollsBackCompletely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-old-index-drop")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, options));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Word_Direction"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Sense_Direction"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task RetryAfterRollback_SucceedsCleanly()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var (wordId, meaningId, cardId) = await SeedOneWordAsync(fixture);

        var failingOptions = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint == "after-backfill")
                {
                    throw new InjectedTestFault(checkpoint);
                }
            }
        };

        await Assert.ThrowsExactlyAsync<InjectedTestFault>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection, failingOptions));
        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));

        var retryResult = await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, retryResult.Outcome);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.AreEqual(
            meaningId,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
    }

    [TestMethod]
    public async Task ForcedColumnRebuildFallback_ProducesIdenticalValidSchema8Shape()
    {
        await using var normalFixture = await Schema7Fixture.CreateAsync();
        var normalSeed = await SeedForcedRebuildSourceAsync(normalFixture);
        var normalResult = await Schema8DormantMigration.ApplyAsync(normalFixture.Connection);

        await using var fixture = await Schema7Fixture.CreateAsync();
        var seed = await SeedForcedRebuildSourceAsync(fixture);
        var result = await Schema8DormantMigration.ApplyAsync(
            fixture.Connection,
            new Schema8MigrationOptions { ForceColumnRebuildFallback = true });

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, normalResult.Outcome);
        Assert.IsFalse(normalResult.UsedColumnRebuildFallback);
        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);
        Assert.IsTrue(result.UsedColumnRebuildFallback);

        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "PreferredMeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Sense_Direction"));

        // Step 1: each database is independently a fully valid Schema 8.
        await AssertValidSchema8Async(normalFixture);
        await AssertValidSchema8Async(fixture);

        // Step 2: each database independently carries the values its own Schema-7 source seeded, so an
        // identical projection can never mean "both paths are wrong in the same way".
        await AssertForcedRebuildExpectationsAsync(normalFixture, normalSeed);
        await AssertForcedRebuildExpectationsAsync(fixture, seed);

        // Step 3: and the normalized logical projections of both learning graphs are identical.
        var normalRows = await CaptureLogicalLearningGraphAsync(normalFixture.Connection);
        var fallbackRows = await CaptureLogicalLearningGraphAsync(fixture.Connection);
        CollectionAssert.AreEqual(
            normalRows,
            fallbackRows,
            "The default rename and forced rebuild paths must preserve the same logical learning graph.");
    }

    private sealed record ForcedRebuildSeed(
        DateTime Timestamp,
        int WordId,
        int MeaningId,
        int CardId,
        int SecondCardId,
        int SessionId,
        int ReviewId,
        int QueueId);

    /// <summary>
    /// Builds the Schema-7 source both paths migrate: one learning session, two cards (one per direction),
    /// a queue row, a typed and correct review — which is exactly the case that backfills a non-null matched
    /// variant — plus non-default automatic counters so per-variant progress rows are produced too. Every
    /// rating, schedule field and timestamp is a distinct fixed value.
    /// </summary>
    private static async Task<ForcedRebuildSeed> SeedForcedRebuildSourceAsync(Schema7Fixture fixture)
    {
        var timestamp = new DateTime(2035, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        var wordId = await fixture.InsertWordAsync(
            "fault-test",
            automaticInteractionMode: LearningInteractionMode.Typing,
            consecutiveRecallSuccessCount: 2,
            consecutiveTypingSuccessCount: 1,
            consecutiveTypingFailureCount: 3,
            masteryReviewExtensionScheduled: true,
            createdAt: timestamp,
            updatedAt: timestamp);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId,
            displayTerm: "fault-test",
            translation: "Fehlertest",
            createdAt: timestamp,
            updatedAt: timestamp);
        var cardId = await fixture.InsertCardAsync(
            wordId,
            meaningId,
            CardDirection.MeaningToTerm,
            state: CardState.Review,
            dueAtUtc: timestamp.AddDays(2),
            intervalDays: 3,
            easeFactor: 2.3,
            successfulReviewCount: 4,
            lapseCount: 1,
            lastReviewedAtUtc: timestamp.AddMinutes(1),
            lastRating: ReviewRating.Good,
            createdAtUtc: timestamp,
            updatedAtUtc: timestamp.AddMinutes(2));
        var secondCardId = await fixture.InsertCardAsync(
            wordId,
            meaningId,
            CardDirection.TermToMeaning,
            state: CardState.Learning,
            dueAtUtc: timestamp.AddDays(5),
            intervalDays: 7,
            easeFactor: 2.7,
            successfulReviewCount: 2,
            lapseCount: 0,
            createdAtUtc: timestamp,
            updatedAtUtc: timestamp.AddMinutes(3));
        const int sessionId = 71;
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningSessions (Id, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) VALUES (?, 1, 1, 1, 0, 0, 1, 0, ?, ?, ?)",
            sessionId,
            timestamp,
            timestamp.AddMinutes(1),
            timestamp.AddMinutes(1));
        var reviewId = await fixture.InsertReviewAsync(
            cardId,
            sessionId,
            rating: ReviewRating.Good,
            wasTypedAnswer: true,
            wasCorrect: true,
            reviewedAtUtc: timestamp.AddMinutes(1),
            dueAtUtc: timestamp.AddDays(2),
            intervalDays: 3,
            easeFactor: 2.3);
        var queueId = await fixture.InsertQueueItemAsync(
            sessionId,
            cardId,
            queueOrder: 4,
            isDueCard: true,
            isAgainRepeat: false,
            answerRevealed: true,
            spellingChecked: true,
            spellingCorrect: true,
            isCompleted: true,
            rating: ReviewRating.Good,
            completedAtUtc: timestamp.AddMinutes(2));

        return new ForcedRebuildSeed(timestamp, wordId, meaningId, cardId, secondCardId, sessionId, reviewId, queueId);
    }

    private static async Task AssertValidSchema8Async(Schema7Fixture fixture)
    {
        var valid = false;
        string? failureDetail = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            valid = Schema8ShapeValidator.IsValidDatabase(connection, out failureDetail));
        Assert.IsTrue(valid, failureDetail);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
    }

    /// <summary>
    /// Asserts one migrated database against the expectations of its own Schema-7 source, independent of the
    /// other path. Every value is read through <c>CAST(... AS INTEGER)</c> where the two paths legitimately
    /// use different SQLite storage classes for the same logical value, so the assertions compare the value
    /// and not its affinity.
    /// </summary>
    private static async Task AssertForcedRebuildExpectationsAsync(Schema7Fixture fixture, ForcedRebuildSeed seed)
    {
        var connection = fixture.Connection;
        var timestamp = seed.Timestamp;

        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariants"));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariantProgress"));

        // Sense / Meaning ownership.
        var senseId = await connection.ExecuteScalarAsync<int>("SELECT Id FROM Senses");
        Assert.AreEqual(seed.WordId, await connection.ExecuteScalarAsync<int>("SELECT WordId FROM Senses WHERE Id = ?", senseId));
        Assert.AreEqual(seed.MeaningId, await connection.ExecuteScalarAsync<int>("SELECT DefaultMeaningId FROM Senses WHERE Id = ?", senseId));
        Assert.AreEqual(senseId, await connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", seed.MeaningId));
        Assert.AreEqual(seed.WordId, await connection.ExecuteScalarAsync<int>("SELECT WordId FROM Meanings WHERE Id = ?", seed.MeaningId));

        // Both directions carry exactly one Required, preferred assignment whose Required epoch is the
        // card's own CreatedAtUtc, over an AnswerVariant that belongs to the same Sense.
        var termVariantId = await connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM AnswerVariants WHERE SenseId = ? AND DisplayText = 'fault-test'", senseId);
        var explanationVariantId = await connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM AnswerVariants WHERE SenseId = ? AND DisplayText = 'Fehlertest'", senseId);
        Assert.AreNotEqual(termVariantId, explanationVariantId);
        Assert.AreEqual(seed.MeaningId, await connection.ExecuteScalarAsync<int>("SELECT SourceMeaningId FROM AnswerVariants WHERE Id = ?", termVariantId));
        Assert.AreEqual(seed.MeaningId, await connection.ExecuteScalarAsync<int>("SELECT SourceMeaningId FROM AnswerVariants WHERE Id = ?", explanationVariantId));
        await AssertPreferredAssignmentAsync(connection, senseId, CardDirection.MeaningToTerm, termVariantId, timestamp);
        await AssertPreferredAssignmentAsync(connection, senseId, CardDirection.TermToMeaning, explanationVariantId, timestamp);

        // Card identity, ownership, direction, state, schedule and timestamps.
        await AssertCardAsync(connection, seed.CardId, seed, senseId, CardDirection.MeaningToTerm, CardState.Review, timestamp.AddDays(2), 3, 2.3, 4, 1, timestamp.AddMinutes(1), (int)ReviewRating.Good, timestamp.AddMinutes(2));
        await AssertCardAsync(connection, seed.SecondCardId, seed, senseId, CardDirection.TermToMeaning, CardState.Learning, timestamp.AddDays(5), 7, 2.7, 2, 0, null, null, timestamp.AddMinutes(3));

        // Session identity, counters and timestamps.
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT Status FROM LearningSessions WHERE Id = ?", seed.SessionId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT TotalCards FROM LearningSessions WHERE Id = ?", seed.SessionId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT CompletedCards FROM LearningSessions WHERE Id = ?", seed.SessionId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT GoodCount FROM LearningSessions WHERE Id = ?", seed.SessionId));
        Assert.AreEqual(timestamp.Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(StartedAtUtc AS INTEGER) FROM LearningSessions WHERE Id = ?", seed.SessionId));
        Assert.AreEqual(timestamp.AddMinutes(1).Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(UpdatedAtUtc AS INTEGER) FROM LearningSessions WHERE Id = ?", seed.SessionId));
        Assert.AreEqual(timestamp.AddMinutes(1).Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(CompletedAtUtc AS INTEGER) FROM LearningSessions WHERE Id = ?", seed.SessionId));

        // Review: session and card ownership, rating, schedule, timestamps, target and matched variant.
        Assert.AreEqual(seed.SessionId, await connection.ExecuteScalarAsync<int>("SELECT SessionId FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(seed.CardId, await connection.ExecuteScalarAsync<int>("SELECT CardId FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual((int)ReviewRating.Good, await connection.ExecuteScalarAsync<int>("SELECT Rating FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT WasTypedAnswer FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT WasCorrect FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>("SELECT IntervalDays FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(2.3, await connection.ExecuteScalarAsync<double>("SELECT EaseFactor FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(timestamp.AddMinutes(1).Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(ReviewedAtUtc AS INTEGER) FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(timestamp.AddDays(2).Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(DueAtUtc AS INTEGER) FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(termVariantId, await connection.ExecuteScalarAsync<int?>("SELECT TargetAnswerVariantId FROM LearningReviews WHERE Id = ?", seed.ReviewId));
        Assert.AreEqual(termVariantId, await connection.ExecuteScalarAsync<int?>("SELECT MatchedAnswerVariantId FROM LearningReviews WHERE Id = ?", seed.ReviewId));

        // Queue row: session and card ownership, order, completion state, rating, target, timestamp.
        Assert.AreEqual(seed.SessionId, await connection.ExecuteScalarAsync<int>("SELECT SessionId FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(seed.CardId, await connection.ExecuteScalarAsync<int>("SELECT CardId FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>("SELECT QueueOrder FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT IsDueCard FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT IsAgainRepeat FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT AnswerRevealed FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT SpellingChecked FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT SpellingCorrect FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT IsCompleted FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual((int)ReviewRating.Good, await connection.ExecuteScalarAsync<int>("SELECT Rating FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(timestamp.AddMinutes(2).Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(CompletedAtUtc AS INTEGER) FROM LearningSessionCards WHERE Id = ?", seed.QueueId));
        Assert.AreEqual(termVariantId, await connection.ExecuteScalarAsync<int?>("SELECT TargetAnswerVariantId FROM LearningSessionCards WHERE Id = ?", seed.QueueId));

        // Per-variant progress: attributed to the right card and variant, with the seeded counters.
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AnswerVariantProgress WHERE CardId = ? AND AnswerVariantId = ? AND InteractionMode = 1 AND ConsecutiveReadingSuccessCount = 2 AND ConsecutiveTypingSuccessCount = 1 AND ConsecutiveTypingFailureCount = 3 AND MasteryReviewExtensionScheduled = 1 AND IsMastered = 0",
                seed.CardId,
                termVariantId));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AnswerVariantProgress WHERE CardId = ? AND AnswerVariantId = ?",
                seed.SecondCardId,
                explanationVariantId));
        Assert.AreEqual(
            timestamp.Ticks,
            await connection.ExecuteScalarAsync<long>(
                "SELECT CAST(CreatedAtUtc AS INTEGER) FROM AnswerVariantProgress WHERE CardId = ?", seed.CardId));
        Assert.AreEqual(
            timestamp.AddMinutes(1).Ticks,
            await connection.ExecuteScalarAsync<long>(
                "SELECT CAST(LastAssessedAtUtc AS INTEGER) FROM AnswerVariantProgress WHERE CardId = ?", seed.CardId));

        // The stable identities that the logical projection deliberately leaves out (they are freshly
        // generated GUIDs per migration run) still exist, are non-empty and are unique in this database.
        foreach (var table in new[] { "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "Meanings" })
        {
            var rowCount = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM \"{table}\"");
            Assert.AreEqual(
                rowCount,
                await connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(DISTINCT StableId) FROM \"{table}\" WHERE StableId IS NOT NULL AND TRIM(StableId) <> ''"),
                $"{table} must carry a unique non-empty StableId per row.");
        }
    }

    private static async Task AssertPreferredAssignmentAsync(
        SQLiteAsyncConnection connection,
        int senseId,
        CardDirection direction,
        int answerVariantId,
        DateTime requiredSinceUtc)
    {
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId WHERE a.SenseId = ? AND a.CardDirection = ? AND a.AnswerVariantId = ? AND a.Requirement = ? AND a.IsPreferred = 1 AND v.SenseId = a.SenseId",
                senseId,
                (int)direction,
                answerVariantId,
                (int)AnswerVariantRequirement.Required));
        Assert.AreEqual(
            requiredSinceUtc.Ticks,
            await connection.ExecuteScalarAsync<long>(
                "SELECT CAST(RequiredSinceUtc AS INTEGER) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ?",
                senseId,
                (int)direction));
    }

    private static async Task AssertCardAsync(
        SQLiteAsyncConnection connection,
        int cardId,
        ForcedRebuildSeed seed,
        int senseId,
        CardDirection direction,
        CardState state,
        DateTime dueAtUtc,
        int intervalDays,
        double easeFactor,
        int successfulReviewCount,
        int lapseCount,
        DateTime? lastReviewedAtUtc,
        int? lastRating,
        DateTime updatedAtUtc)
    {
        Assert.AreEqual(seed.WordId, await connection.ExecuteScalarAsync<int>("SELECT WordId FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(seed.MeaningId, await connection.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(senseId, await connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual((int)direction, await connection.ExecuteScalarAsync<int>("SELECT Direction FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual((int)state, await connection.ExecuteScalarAsync<int>("SELECT State FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(intervalDays, await connection.ExecuteScalarAsync<int>("SELECT IntervalDays FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(easeFactor, await connection.ExecuteScalarAsync<double>("SELECT CAST(EaseFactor AS REAL) FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(successfulReviewCount, await connection.ExecuteScalarAsync<int>("SELECT SuccessfulReviewCount FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(lapseCount, await connection.ExecuteScalarAsync<int>("SELECT LapseCount FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(dueAtUtc.Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(DueAtUtc AS INTEGER) FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(seed.Timestamp.Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(CreatedAtUtc AS INTEGER) FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(updatedAtUtc.Ticks, await connection.ExecuteScalarAsync<long>("SELECT CAST(UpdatedAtUtc AS INTEGER) FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(
            lastReviewedAtUtc?.Ticks,
            await connection.ExecuteScalarAsync<long?>("SELECT CAST(LastReviewedAtUtc AS INTEGER) FROM LearningCards WHERE Id = ?", cardId));
        Assert.AreEqual(
            lastRating,
            await connection.ExecuteScalarAsync<int?>("SELECT LastRating FROM LearningCards WHERE Id = ?", cardId));
    }

    /// <summary>
    /// Normalized logical projection of the complete migrated learning graph. <c>CAST(... AS TEXT)</c> is the
    /// only normalization applied: the forced rebuild recreates <c>LearningCards</c> with declared TEXT/REAL
    /// affinities while the rename path keeps the sqlite-net integer-tick storage, so the same logical value
    /// legitimately arrives in a different storage class. <c>quote()</c> runs on the cast value, so NULL stays
    /// distinguishable from the string <c>'NULL'</c>, every row is emitted (duplicates included) and no id,
    /// owner, target, rating, schedule field or timestamp is dropped. Only the freshly generated
    /// <c>StableId</c> GUIDs and the migration-execution <c>CreatedAtUtc</c>/<c>UpdatedAtUtc</c> stamps of the
    /// rows the migration itself creates are excluded — they are new per run by design, and
    /// <see cref="AssertForcedRebuildExpectationsAsync"/> asserts them separately per database.
    /// </summary>
    private static async Task<string[]> CaptureLogicalLearningGraphAsync(SQLiteAsyncConnection connection)
    {
        var projections = new[]
        {
            (Table: "LearningSessions", Columns: new[]
            {
                "Id", "Status", "TotalCards", "CompletedCards", "AgainCount", "HardCount", "GoodCount",
                "EasyCount", "StartedAtUtc", "UpdatedAtUtc", "CompletedAtUtc"
            }),
            (Table: "LearningCards", Columns: new[]
            {
                "Id", "WordId", "SenseId", "PreferredMeaningId", "Direction", "State", "DueAtUtc",
                "IntervalDays", "EaseFactor", "SuccessfulReviewCount", "LapseCount", "LastReviewedAtUtc",
                "LastRating", "CreatedAtUtc", "UpdatedAtUtc"
            }),
            (Table: "LearningSessionCards", Columns: new[]
            {
                "Id", "SessionId", "CardId", "QueueOrder", "IsDueCard", "IsAgainRepeat", "AnswerRevealed",
                "SpellingChecked", "SpellingCorrect", "IsCompleted", "Rating", "CompletedAtUtc",
                "TargetAnswerVariantId"
            }),
            (Table: "LearningReviews", Columns: new[]
            {
                "Id", "CardId", "SessionId", "Rating", "WasTypedAnswer", "WasCorrect", "ReviewedAtUtc",
                "DueAtUtc", "IntervalDays", "EaseFactor", "TargetAnswerVariantId", "MatchedAnswerVariantId"
            }),
            (Table: "Senses", Columns: new[]
            {
                "Id", "WordId", "SourceLanguage", "ExplanationLanguage", "ProviderSenseId", "TopicOrDomain",
                "PartOfSpeech", "GrammaticalRelationship", "AcronymExpansion", "DefaultMeaningId", "Status"
            }),
            (Table: "Meanings", Columns: new[]
            {
                "Id", "WordId", "SenseId", "ExplanationLanguage", "SourceLanguage", "DisplayTerm",
                "EncounteredSurfaceForm", "GrammaticalRelationship", "TokenKind", "SelectedMeaningId",
                "AcronymExpansion", "Translation", "Definition", "AcceptedAliasesJson",
                "TranslationOrDefinition", "Source", "ConfirmedByUser", "CreatedAt", "UpdatedAt", "PreparedAt"
            }),
            (Table: "AnswerVariants", Columns: new[]
            {
                "Id", "SenseId", "AnswerLanguage", "DisplayText", "NormalizedText", "SourceMeaningId"
            }),
            (Table: "SenseAnswerVariantAssignments", Columns: new[]
            {
                "Id", "SenseId", "CardDirection", "AnswerVariantId", "Requirement", "IsPreferred",
                "RequiredSinceUtc"
            }),
            (Table: "AnswerVariantProgress", Columns: new[]
            {
                "Id", "CardId", "AnswerVariantId", "InteractionMode", "ConsecutiveReadingSuccessCount",
                "ConsecutiveTypingSuccessCount", "ConsecutiveTypingFailureCount", "LastAssessedAtUtc",
                "MasteryReviewExtensionScheduled", "IsMastered", "ReplayVersion", "CreatedAtUtc", "UpdatedAtUtc"
            })
        };

        var result = new List<string>();
        foreach (var projection in projections)
        {
            var valueExpression = string.Join(
                " || ':' || ",
                projection.Columns.Select(column => $"hex(CAST(quote(CAST(\"{column}\" AS TEXT)) AS BLOB))"));
            var rows = await connection.QueryScalarsAsync<string>(
                $"SELECT {valueExpression} FROM \"{projection.Table}\" ORDER BY Id");
            result.AddRange(rows.Select((row, ordinal) => $"{projection.Table}|{ordinal}|{row}"));
        }

        return [.. result];
    }

    [TestMethod]
    public async Task BundledSqliteCapability_IsDetectedAndRenameColumnPathIsUsedByDefault()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedOneWordAsync(fixture);

        string? detectedVersion = null;
        bool supportsRename = false;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            detectedVersion = Schema8SqliteCapabilities.GetSqliteVersion(connection);
            supportsRename = Schema8SqliteCapabilities.SupportsRenameColumn(connection);
        });

        Console.WriteLine($"Bundled SQLite version detected: {detectedVersion}; supports RENAME COLUMN: {supportsRename}");
        Assert.IsTrue(supportsRename, $"Expected bundled SQLite ({detectedVersion}) to support ALTER TABLE ... RENAME COLUMN (>= 3.25.0).");

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        Assert.IsFalse(result.UsedColumnRebuildFallback, "Default migration should take the fast RENAME COLUMN path on this bundled SQLite.");
    }
}
