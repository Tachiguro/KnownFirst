using System.Collections.Concurrent;
using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Review;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningTargetRuntimeCutoverTests
{
    private static readonly DateTime TestStartTime = new(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Slice4_GetOrStartAsync_OnSchema14_ReturnsTargetCentricCardWithMetadataLabel()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(database, clock);

        var appSettings = new TestAppSettings(LearningMode.Automatic);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            appSettings);

        var loadResult = await learningService.GetOrStartAsync();

        Assert.IsNotNull(loadResult.Card, "Session must return an active card view.");
        Assert.IsNotNull(loadResult.Card.TargetId, "CardView must carry a valid TargetId on Schema 14.");
        Assert.IsTrue(loadResult.Card.TargetId > 0, "TargetId must be positive.");
        Assert.IsNotNull(loadResult.Card.TargetKind, "CardView must carry TargetKind on Schema 14.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(loadResult.Card.TargetMetadataLabel), "CardView must carry a non-empty TargetMetadataLabel.");

        // Reveal and rate the target and verify target FSRS persistence
        await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
        var ratedResult = await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        await database.ReadAsync(async conn =>
        {
            var targetReviewsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetReviews");
            Assert.AreEqual(1, targetReviewsCount, "TargetReviews must record exactly 1 review event.");

            var historyCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries");
            Assert.AreEqual(1, historyCount, "TargetFsrsReviewHistoryEntries must record exactly 1 history entry.");

            var targetFsrs = await conn.QueryAsync<TargetFsrsRow>(
                "SELECT TargetId, State, Stability, Difficulty, LastReviewedAtUtc, DueAtUtc FROM TargetFsrsStates WHERE TargetId = ?",
                loadResult.Card.TargetId.Value);
            Assert.AreEqual(1, targetFsrs.Count, "TargetFsrsStates must contain exactly 1 row for the target.");
            Assert.AreEqual((int)Fsrs6CardState.Review, targetFsrs[0].State, "State must transition to Review after initial Good rating.");
            Assert.IsNotNull(targetFsrs[0].Stability);
            Assert.IsNotNull(targetFsrs[0].Difficulty);
            Assert.IsNotNull(targetFsrs[0].DueAtUtc);
            return true;
        });
    }

    [TestMethod]
    public async Task Slice4_CheckSpellingAsync_OnSchema14_ValidatesTargetAnswerVariantsAndHandlesProgression()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(database, clock);

        var appSettings = new TestAppSettings(LearningMode.Typing);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            appSettings);

        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card);

        // Incorrect spelling check should fail and record Again
        var incorrectCheck = await learningService.CheckSpellingAsync(loadResult.Card.QueueItemId, "completely_wrong_answer");
        Assert.IsFalse(incorrectCheck.IsCorrect, "Incorrect answer must not pass spelling check.");
        Assert.IsTrue(incorrectCheck.RatingWasPersisted, "Again rating must be persisted on incorrect spelling.");

        await database.ReadAsync(async conn =>
        {
            var targetReviewsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetReviews WHERE Rating = 0");
            Assert.AreEqual(1, targetReviewsCount, "TargetReviews must record Again on incorrect typed answer.");

            var targetFsrs = await conn.QueryAsync<TargetFsrsRow>(
                "SELECT TargetId, State FROM TargetFsrsStates WHERE TargetId = ?",
                loadResult.Card.TargetId!.Value);
            Assert.AreEqual(1, targetFsrs.Count);
            Assert.AreEqual((int)Fsrs6CardState.Learning, targetFsrs[0].State, "State must transition to Learning after initial Again rating.");
            return true;
        });
    }

    [TestMethod]
    public async Task Slice4_CheckSpellingAsync_MatchedAlias_RecordsMatchedVariantId()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(database, clock);

        var appSettings = new TestAppSettings(LearningMode.Typing);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            appSettings);

        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card);

        // Submit accepted alias "home"
        var aliasCheck = await learningService.CheckSpellingAsync(loadResult.Card.QueueItemId, "home");
        Assert.IsTrue(aliasCheck.IsCorrect, "Accepted alias must pass spelling check.");
        Assert.IsFalse(aliasCheck.RatingWasPersisted, "Rating must not be persisted yet for correct answer.");
        Assert.IsNotNull(aliasCheck.MatchedAnswerVariantId, "MatchedAnswerVariantId must be set.");

        // Now rate Good
        var ratedResult = await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        await database.ReadAsync(async conn =>
        {
            var reviews = await conn.QueryAsync<TargetReviewRow>(
                "SELECT TargetId, Rating, MatchedAnswerVariantId FROM TargetReviews WHERE TargetId = ?",
                loadResult.Card.TargetId!.Value);
            Assert.AreEqual(1, reviews.Count);
            Assert.AreEqual((int)ReviewRating.Good, reviews[0].Rating);
            Assert.AreEqual(aliasCheck.MatchedAnswerVariantId, reviews[0].MatchedAnswerVariantId);
            return true;
        });
    }

    [TestMethod]
    public async Task Slice4_MarkPermanentlyKnown_PreservesTargetFactsAndGraph()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        var targetDueAtUtc = new DateTimeOffset(TestStartTime.AddMinutes(-30), TimeSpan.Zero);
        await SeedWorkflowTargetAndLegacyStateAsync(database, targetDueAtUtc);

        var appSettings = new TestAppSettings(LearningMode.Automatic);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            appSettings);

        // Session start creates active session with due target
        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card);
        var wordId = loadResult.Card.WordId;

        await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
        await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        // Verify pre-conditions: 1 review event, 1 history entry, 2 targets, 2 FSRS states
        await database.ReadAsync(async conn =>
        {
            Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningTargets"));
            Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetAnswerVariants"));
            Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsStates"));
            Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries"));
            Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetReviews"));
            return true;
        });

        // Mark permanently known
        var marked = await learningService.MarkPermanentlyKnownAsync(wordId, confirmed: true);
        Assert.IsTrue(marked, "MarkPermanentlyKnownAsync must return true.");

        await database.ReadAsync(async conn =>
        {
            var wordControlCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM WordLearningControls WHERE WordId = ?", wordId);
            Assert.AreEqual(1, wordControlCount, "WordLearningControls must record 1 row for the known word.");

            var targetCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningTargets");
            Assert.AreEqual(2, targetCount, "LearningTargets must be preserved after permanently known.");

            var variantCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetAnswerVariants");
            Assert.AreEqual(2, variantCount, "TargetAnswerVariants must be preserved after permanently known.");

            var fsrsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsStates");
            Assert.AreEqual(2, fsrsCount, "TargetFsrsStates must be preserved after permanently known.");

            var historyCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries");
            Assert.AreEqual(1, historyCount, "TargetFsrsReviewHistoryEntries must be preserved after permanently known.");

            var reviewCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetReviews");
            Assert.AreEqual(1, reviewCount, "TargetReviews must be preserved after permanently known.");

            var incompleteQueueCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM LearningSessionCards q
                JOIN LearningTargets t ON t.Id = q.CardId
                JOIN Senses s ON s.Id = t.SenseId
                WHERE s.WordId = ? AND q.IsCompleted = 0
                """, wordId);
            Assert.AreEqual(0, incompleteQueueCount, "Incomplete queue items must be cleared.");

            var fkErrors = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check");
            Assert.AreEqual(0, fkErrors, "Foreign key integrity must remain valid.");

            return true;
        });

        // LearningService must not return the word for active learning
        var nextLoad = await learningService.GetOrStartAsync();
        Assert.IsNull(nextLoad.Card, "Word must not yield active target learning work while AlreadyKnown is set.");

        // WorkflowState must report 0 due and 0 new items for the word
        var workflowState = await new WorkflowStateService(database, clock).GetSnapshotAsync();
        Assert.AreEqual(0, workflowState.DueCardCount, "Workflow state must count 0 due targets.");
        Assert.AreEqual(0, workflowState.PreparedNewItemCount, "Workflow state must count 0 new targets.");
    }

    [TestMethod]
    public async Task Slice4_AutomaticProgression_TransitionsReadingToTypingAfterTwoSuccesses()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(database, clock);

        var appSettings = new TestAppSettings(LearningMode.Automatic);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            appSettings);

        // Review 1: Reading mode -> Good
        var session1 = await learningService.GetOrStartAsync();
        Assert.IsNotNull(session1.Card);
        Assert.AreEqual(LearningInteractionMode.Reading, session1.Card.InteractionMode);
        await learningService.RevealAnswerAsync(session1.Card.QueueItemId);
        await learningService.RateAsync(session1.Card.QueueItemId, ReviewRating.Good);

        // Advance clock past review 1 due date (FSRS initial Good interval is ~3 days)
        clock.Advance(TimeSpan.FromDays(4));

        // Review 2: Reading mode -> Good -> qualifies for typing
        var session2 = await learningService.GetOrStartAsync();
        Assert.IsNotNull(session2.Card);
        Assert.AreEqual(LearningInteractionMode.Reading, session2.Card.InteractionMode);
        await learningService.RevealAnswerAsync(session2.Card.QueueItemId);
        await learningService.RateAsync(session2.Card.QueueItemId, ReviewRating.Good);

        // Advance clock past review 2 due date (ensure target is due for review 3)
        clock.Advance(TimeSpan.FromDays(60));

        // Review 3: Should now be in Typing mode under Automatic progression
        var session3 = await learningService.GetOrStartAsync();
        Assert.IsNotNull(session3.Card);
        Assert.AreEqual(LearningInteractionMode.Typing, session3.Card.InteractionMode,
            "Target should progress to Typing interaction mode after 2 successful scheduled recall reviews.");
    }

    [TestMethod]
    public async Task Slice4_WorkflowStateService_ReportsTargetDueAndNewCountsOnSchema14()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var targetDueAtUtc = new DateTimeOffset(TestStartTime.AddMinutes(-30), TimeSpan.Zero);
        await SeedWorkflowTargetAndLegacyStateAsync(database, targetDueAtUtc);

        var snapshot = await new WorkflowStateService(database, new FakeClock(TestStartTime)).GetSnapshotAsync();

        Assert.AreEqual(1, snapshot.DueCardCount,
            "Exactly the due target must be counted; the two legacy card directions must not be counted.");
        Assert.AreEqual(1, snapshot.PreparedNewItemCount,
            "The new sibling target must contribute its word exactly once.");
        Assert.AreEqual(targetDueAtUtc.UtcDateTime, snapshot.NextDueAtUtc,
            "NextDueAtUtc must come from the target FSRS state rather than either legacy card direction.");
    }

    [TestMethod]
    public async Task Schema14_ZeroTargets_DoesNotInvokeLegacyScheduler()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedLegacyCardAsync(database, TestStartTime, CardDirection.TermToMeaning);

        var observableScheduler = new ObservableLegacyScheduler();
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            observableScheduler,
            new SpellingAnswerComparer(),
            clock,
            new TestAppSettings(LearningMode.Automatic),
            fsrs6SchedulingService: fsrs);

        var loadResult = await learningService.GetOrStartAsync();
        if (loadResult.Card is not null)
        {
            await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
            await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);
        }

        Assert.AreEqual(0, observableScheduler.InvocationCount,
            "Legacy scheduler must not be invoked on Schema 14 even when target count is zero.");
    }

    [TestMethod]
    public async Task Schema14_ZeroTargets_GetOrStartAsync_DoesNotDispatchToLegacyScheduler()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedLegacyCardAsync(database, TestStartTime, CardDirection.TermToMeaning);
        var observableScheduler = new ObservableLegacyScheduler();
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            observableScheduler,
            new SpellingAnswerComparer(),
            clock,
            new TestAppSettings(LearningMode.Automatic),
            fsrs6SchedulingService: fsrs);

        var result = await learningService.GetOrStartAsync();
        Assert.IsNull(result.Card,
            "Schema-14 target authority must not return the valid legacy-only active queue item.");
        Assert.AreEqual(0, observableScheduler.InvocationCount,
            "The zero-target GetOrStart boundary must not reach the legacy runtime.");
    }

    [TestMethod]
    public async Task Schema14_ZeroTargets_RevealAnswerAsync_UsesTargetRuntimeValidation()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        var seeded = await SeedLegacyCardAsync(database, TestStartTime, CardDirection.TermToMeaning);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            new TestAppSettings(LearningMode.Automatic));

        var ex = await CaptureSchema8LearningDataExceptionAsync(
            () => learningService.RevealAnswerAsync(seeded.QueueItemId));
        var answerRevealed = await database.ReadAsync(connection =>
            connection.ExecuteScalarAsync<int>(
                "SELECT AnswerRevealed FROM LearningSessionCards WHERE Id = ?", seeded.QueueItemId));

        Assert.AreEqual(0, answerRevealed,
            "Target dispatch must not let the legacy reveal path mutate the queue row.");
        Assert.IsNotNull(ex, "The legacy-only queue item must be rejected by target authority.");
        Assert.AreEqual(Schema8LearningDataErrorCode.CardNotFound, ex.Code);
    }

    [TestMethod]
    public async Task Schema14_ZeroTargets_CheckSpellingAsync_UsesTargetRuntimeValidation()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        var seeded = await SeedLegacyCardAsync(database, TestStartTime, CardDirection.MeaningToTerm);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            new TestAppSettings(LearningMode.Typing));

        var ex = await CaptureSchema8LearningDataExceptionAsync(
            () => learningService.CheckSpellingAsync(seeded.QueueItemId, "Schnitt"));
        var state = await database.ReadAsync(async connection => new
        {
            SpellingChecked = await connection.ExecuteScalarAsync<int>(
                "SELECT SpellingChecked FROM LearningSessionCards WHERE Id = ?", seeded.QueueItemId),
            AnswerRevealed = await connection.ExecuteScalarAsync<int>(
                "SELECT AnswerRevealed FROM LearningSessionCards WHERE Id = ?", seeded.QueueItemId),
            ReviewCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews")
        });

        Assert.AreEqual(0, state.SpellingChecked,
            "Target dispatch must not let the legacy spelling path consume the queue row.");
        Assert.AreEqual(0, state.AnswerRevealed,
            "Target dispatch must not let the legacy spelling path reveal the answer.");
        Assert.AreEqual(0, state.ReviewCount);
        Assert.IsNotNull(ex, "The legacy-only queue item must be rejected by target authority.");
        Assert.AreEqual(Schema8LearningDataErrorCode.CardNotFound, ex.Code);
    }

    [TestMethod]
    public async Task Schema14_ZeroTargets_RateAsync_UsesTargetRuntimeValidation()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        var seeded = await SeedLegacyCardAsync(
            database,
            TestStartTime,
            CardDirection.TermToMeaning,
            answerRevealed: true);
        var observableScheduler = new ObservableLegacyScheduler();
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            observableScheduler,
            new SpellingAnswerComparer(),
            clock,
            new TestAppSettings(LearningMode.Automatic),
            fsrs6SchedulingService: fsrs);

        var ex = await CaptureSchema8LearningDataExceptionAsync(
            () => learningService.RateAsync(seeded.QueueItemId, ReviewRating.Good));
        var state = await database.ReadAsync(async connection => new
        {
            CardState = await connection.ExecuteScalarAsync<int>(
                "SELECT State FROM LearningCards WHERE Id = ?", seeded.CardId),
            QueueCompleted = await connection.ExecuteScalarAsync<int>(
                "SELECT IsCompleted FROM LearningSessionCards WHERE Id = ?", seeded.QueueItemId),
            ReviewCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews")
        });

        Assert.AreEqual(0, observableScheduler.InvocationCount,
            "Target dispatch must not invoke the legacy scheduler.");
        Assert.AreEqual((int)CardState.New, state.CardState,
            "Target dispatch must leave legacy scheduling state unchanged.");
        Assert.AreEqual(0, state.QueueCompleted,
            "Target dispatch must not complete the legacy queue row.");
        Assert.AreEqual(0, state.ReviewCount,
            "Target dispatch must not append legacy review history.");
        Assert.IsNotNull(ex, "The legacy-only queue item must be rejected by target authority.");
        Assert.AreEqual(Schema8LearningDataErrorCode.CardNotFound, ex.Code);
    }

    [TestMethod]
    public async Task Schema14_ZeroTargets_MarkPermanentlyKnownAsync_UsesTargetRuntimeAuthority()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        var seeded = await SeedLegacyCardAsync(database, TestStartTime, CardDirection.TermToMeaning);
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            database,
            new SpellingAnswerComparer(),
            clock,
            fsrs,
            new TestAppSettings(LearningMode.Automatic));

        var marked = await learningService.MarkPermanentlyKnownAsync(seeded.WordId, confirmed: true);
        var state = await database.ReadAsync(async connection => new
        {
            WordControlCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM WordLearningControls WHERE WordId = ?", seeded.WordId),
            LegacyCardCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LearningCards WHERE Id = ?", seeded.CardId),
            LegacyQueueCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LearningSessionCards WHERE Id = ?", seeded.QueueItemId),
            LegacyVariantCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AnswerVariants WHERE Id = ?", seeded.AnswerVariantId)
        });

        Assert.IsTrue(marked);
        Assert.AreEqual(1, state.WordControlCount,
            "Target authority records the clean WordLearningControls row.");
        Assert.AreEqual(1, state.LegacyCardCount,
            "Target authority must not delete the non-authoritative legacy card graph.");
        Assert.AreEqual(1, state.LegacyQueueCount,
            "Target authority must not consume or delete a legacy-only queue row.");
        Assert.AreEqual(1, state.LegacyVariantCount,
            "Target authority must not delete legacy answer variants.");
    }

    private sealed class ObservableLegacyScheduler : ISpacedRepetitionScheduler
    {
        public int InvocationCount { get; private set; }

        public CardSchedule Schedule(CardSchedule current, ReviewRating rating, DateTime reviewedAtUtc)
        {
            InvocationCount++;
            return new CardSchedule(
                current.State,
                reviewedAtUtc.AddDays(1),
                1,
                2.5,
                current.SuccessfulReviewCount + 1,
                current.LapseCount,
                reviewedAtUtc,
                rating);
        }
    }

    private sealed class TargetReviewRow
    {
        public int TargetId { get; set; }
        public int Rating { get; set; }
        public int? MatchedAnswerVariantId { get; set; }
    }

    private sealed record LegacyQueueSeed(
        int WordId,
        int SenseId,
        int MeaningId,
        int CardId,
        int AnswerVariantId,
        int SessionId,
        int QueueItemId);

    private static async Task<Schema8LearningDataException?> CaptureSchema8LearningDataExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Schema8LearningDataException ex)
        {
            return ex;
        }
    }

    private static async Task<LegacyQueueSeed> SeedLegacyCardAsync(
        IKnownFirstDatabase database,
        DateTime now,
        CardDirection direction,
        bool answerRevealed = false)
    {
        return await database.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO Words (
                    Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                    TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                    ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                    MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
                VALUES ('en', 'cutover', 'cutover', 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
                """,
                now,
                now);
            var wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO Senses (
                    StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('cutover-sense', ?, 'en', 'en', 0, ?, ?)
                """,
                wordId,
                now,
                now);
            var senseId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO Meanings (
                    WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                    GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                    AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                    ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
                VALUES (?, ?, 'de', 'en', 'cutover', 'cutover', '', 0, 'Schnitt', 'a production cutover', '', '',
                        '[]', 'Schnitt', 'test', 'test', 'test', 'test', 1, ?, ?, ?, 'cutover-meaning')
                """,
                wordId,
                senseId,
                now,
                now,
                now);
            var meaningId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

            connection.Execute(
                """
                INSERT INTO LearningCards (
                    WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                    EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, 0, ?, 0, 2.5, 0, 0, ?, ?)
                """,
                wordId,
                senseId,
                meaningId,
                (int)direction,
                now,
                now,
                now);
            var cardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO AnswerVariants (
                    StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                    CreatedAtUtc, UpdatedAtUtc)
                VALUES ('cutover-answer', ?, 'de', 'Schnitt', 'schnitt', ?, ?, ?)
                """,
                senseId,
                meaningId,
                now,
                now);
            var variantId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                """
                INSERT INTO SenseAnswerVariantAssignments (
                    StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                    RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('cutover-assignment', ?, ?, ?, 0, 1, ?, ?, ?)
                """,
                senseId,
                (int)direction,
                variantId,
                now,
                now,
                now);
            Schema13LearningRepository.InsertCleanNewState(connection, cardId);

            connection.Execute(
                """
                INSERT INTO LearningSessions (
                    Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                    StartedAtUtc, UpdatedAtUtc, CompletedAtUtc, StableId)
                VALUES (?, 1, 0, 0, 0, 0, 0, ?, ?, NULL, ?)
                """,
                (int)LearningSessionStatus.Active,
                now,
                now,
                Guid.NewGuid().ToString("N"));
            var sessionId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO LearningSessionCards (
                    SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed,
                    SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc,
                    TargetAnswerVariantId, StableId)
                VALUES (?, ?, 0, 0, 0, ?, 0, 0, 0, NULL, NULL, ?, ?)
                """,
                sessionId,
                cardId,
                answerRevealed ? 1 : 0,
                variantId,
                Guid.NewGuid().ToString("N"));
            var queueItemId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            return new LegacyQueueSeed(
                wordId,
                senseId,
                meaningId,
                cardId,
                variantId,
                sessionId,
                queueItemId);
        });
    }

    private static async Task SeedWorkflowTargetAndLegacyStateAsync(
        IKnownFirstDatabase database,
        DateTimeOffset targetDueAtUtc)
    {
        var legacy = await SeedLegacyCardAsync(
            database,
            TestStartTime,
            CardDirection.TermToMeaning);

        await database.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM LearningSessionCards WHERE SessionId = ?", legacy.SessionId);
            connection.Execute("DELETE FROM LearningSessions WHERE Id = ?", legacy.SessionId);

            connection.Execute(
                """
                INSERT INTO LearningCards (
                    WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                    EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, 0, 2.5, 0, 0, ?, ?)
                """,
                legacy.WordId,
                legacy.SenseId,
                legacy.MeaningId,
                (int)CardDirection.MeaningToTerm,
                (int)CardState.New,
                TestStartTime,
                TestStartTime,
                TestStartTime);
            var siblingLegacyCardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                """
                INSERT INTO SenseAnswerVariantAssignments (
                    StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                    RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, 1, ?, ?, ?)
                """,
                Guid.NewGuid().ToString("N"),
                legacy.SenseId,
                (int)CardDirection.MeaningToTerm,
                legacy.AnswerVariantId,
                (int)AnswerVariantRequirement.Required,
                TestStartTime,
                TestStartTime,
                TestStartTime);
            Schema13LearningRepository.InsertCleanNewState(connection, siblingLegacyCardId);

            var dueTarget = LearningTargetRepository.CreateTarget(
                connection,
                legacy.SenseId,
                LearningTarget.CreateTranslation("en", "de"),
                TestStartTime,
                Guid.NewGuid().ToString("N"));
            LearningTargetRepository.AddAnswerVariant(
                connection,
                dueTarget.Id,
                new TargetAnswerVariantDraft(
                    "de",
                    "Schnitt",
                    AnswerVariantRequirement.Required,
                    IsPreferred: true,
                    SourceMeaningId: legacy.MeaningId),
                TestStartTime,
                Guid.NewGuid().ToString("N"));
            TargetFsrsStateRepository.Save(
                connection,
                dueTarget.Id,
                Fsrs6Card.Review(
                    stability: 4,
                    difficulty: 5,
                    lastReviewedAtUtc: targetDueAtUtc.AddDays(-4),
                    dueAtUtc: targetDueAtUtc));

            var newSiblingTarget = LearningTargetRepository.CreateTarget(
                connection,
                legacy.SenseId,
                LearningTarget.CreateDefinition("en", "en"),
                TestStartTime,
                Guid.NewGuid().ToString("N"));
            LearningTargetRepository.AddAnswerVariant(
                connection,
                newSiblingTarget.Id,
                new TargetAnswerVariantDraft(
                    "en",
                    "a production cutover",
                    AnswerVariantRequirement.Required,
                    IsPreferred: true,
                    SourceMeaningId: legacy.MeaningId),
                TestStartTime,
                Guid.NewGuid().ToString("N"));
            TargetFsrsStateRepository.Save(connection, newSiblingTarget.Id, Fsrs6Card.New());

            return true;
        });
    }

    private static async Task SeedPreparedWordWithTranslationAsync(
        IKnownFirstDatabase database,
        FakeClock clock)
    {
        var provider = new MutableProvider(clock);
        var review = CreateTextReviewService(database);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus steht dort.", "Haus", "de", LexicalLookupMode.Translation, "en");

        var sessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        Assert.IsTrue(sessionId > 0);

        var item = await preparation.GetCurrentAsync();
        Assert.IsNotNull(item);

        var input = new PreparedMeaningInput(
            SelectedMeaningId: null,
            AcronymExpansion: null,
            Translation: "house",
            Definition: string.Empty,
            DictionaryExample: null,
            AdditionalNote: null,
            AcceptedAliases: ["building", "home"],
            ProviderName: "Wiktionary",
            SourceProject: "wiktionary",
            SourcePageTitle: "Haus",
            SourceRevisionId: 12345,
            Attribution: "CC BY-SA",
            ManualInputMode: LexicalLookupMode.Translation);

        await preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);
    }

    private static TextReviewService CreateTextReviewService(IKnownFirstDatabase database) =>
        new(
            database,
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

    private static PreparationService CreatePreparationService(
        IKnownFirstDatabase database,
        MutableProvider provider,
        FakeClock clock) =>
        new(
            database,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(database),
                new LexicalLookupProviderResolver([provider])),
            clock,
            diagnosticLog: null,
            faultInjector: null);

    private static async Task<int> ImportWithOnlyThisWordUnknownAsync(
        TextReviewService review,
        string content,
        string unknownTerm,
        string sourceLanguage,
        LexicalLookupMode lookupMode,
        string? targetLanguage = null)
    {
        var request = new ImportTextRequest(
            $"Document {Guid.NewGuid():N}",
            content,
            sourceLanguage,
            lookupMode,
            targetLanguage);
        var result = await review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private sealed class TargetFsrsRow
    {
        public int TargetId { get; set; }
        public int State { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public string? DueAtUtc { get; set; }
    }

    private sealed class DisabledEnhancedRecognitionSettings : IAppSettingsService
    {
        public int PreparationLimit => PreparationLimitPolicy.DefaultLimit;
        public IReadOnlyList<int> SupportedPreparationLimits => [PreparationLimitPolicy.DefaultLimit];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => LearningMode.Automatic;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => false;
        public LearningTimezoneMode LearningTimezoneMode => LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId => null;
        public int LearningDayCutoffMinutes => LearningDayConfiguration.DefaultCutoffMinutes;
        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool enabled) => throw new NotSupportedException();
        public void SetLearningTimezoneMode(LearningTimezoneMode mode) => throw new NotSupportedException();
        public void SetExplicitLearningTimezoneId(string? timezoneId) => throw new NotSupportedException();
        public void SetLearningDayCutoffMinutes(int minutes) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class TestAppSettings(LearningMode mode) : IAppSettingsService
    {
        public int PreparationLimit => PreparationLimitPolicy.DefaultLimit;
        public IReadOnlyList<int> SupportedPreparationLimits => [PreparationLimitPolicy.DefaultLimit];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => mode;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => false;
        public LearningTimezoneMode LearningTimezoneMode => LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId => null;
        public int LearningDayCutoffMinutes => LearningDayConfiguration.DefaultCutoffMinutes;
        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode m) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool enabled) => throw new NotSupportedException();
        public void SetLearningTimezoneMode(LearningTimezoneMode m) => throw new NotSupportedException();
        public void SetExplicitLearningTimezoneId(string? timezoneId) => throw new NotSupportedException();
        public void SetLearningDayCutoffMinutes(int minutes) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class MutableProvider(FakeClock clock) : IDictionaryLookupProvider
    {
        private readonly ConcurrentQueue<LexicalLookupRequest> _requests = new();

        public Func<LexicalLookupRequest, IReadOnlyList<LexicalMeaning>> MeaningsFactory { get; set; } =
            _ => [new LexicalMeaning("primary", "noun", "Ein Gebäude zum Wohnen", "house", null, ["building", "home"])];

        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public int RequestCount => _requests.Count;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
            var result = new LexicalResult(
                LexicalLookupStatus.Success,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                MeaningsFactory(request),
                ProviderName,
                "en.wiktionary.org",
                request.Term,
                1,
                "Wiktionary contributors",
                clock.UtcNow,
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage);
            return Task.FromResult(result);
        }
    }
}
