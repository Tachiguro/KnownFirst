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
    public async Task Slice4_MarkPermanentlyKnown_CascadesAcrossTargetTables()
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
        Assert.IsNotNull(loadResult.Card);
        await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
        await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        // Mark permanently known
        var marked = await learningService.MarkPermanentlyKnownAsync(loadResult.Card.WordId, confirmed: true);
        Assert.IsTrue(marked, "MarkPermanentlyKnownAsync must return true.");

        await database.ReadAsync(async conn =>
        {
            var targetCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningTargets");
            Assert.AreEqual(0, targetCount, "LearningTargets must be empty after permanently known.");

            var variantCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetAnswerVariants");
            Assert.AreEqual(0, variantCount, "TargetAnswerVariants must be empty after permanently known.");

            var fsrsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsStates");
            Assert.AreEqual(0, fsrsCount, "TargetFsrsStates must be empty after permanently known.");

            var historyCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries");
            Assert.AreEqual(0, historyCount, "TargetFsrsReviewHistoryEntries must be empty after permanently known.");

            var reviewCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TargetReviews");
            Assert.AreEqual(0, reviewCount, "TargetReviews must be empty after permanently known.");
            return true;
        });
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

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(database, clock);

        var workflowStateService = new WorkflowStateService(database, clock);
        var snapshot = await workflowStateService.GetSnapshotAsync();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(1, snapshot.PreparedNewItemCount, "PreparedNewItemCount must report the prepared word target on Schema 14.");
        Assert.AreEqual(0, snapshot.DueCardCount, "DueCardCount should be 0 before initial review.");
    }

    private sealed class TargetReviewRow
    {
        public int TargetId { get; set; }
        public int Rating { get; set; }
        public int? MatchedAnswerVariantId { get; set; }
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
