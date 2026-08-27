using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;
using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningNextDueAvailabilityTests
{
    private static readonly DateTime Day1Utc = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task BudgetBlockedNewCard_DoesNotBecomeNextDueAtUtc()
    {
        // Scenario 1:
        // Genuinely-new queueable vocabulary exists;
        // Daily admission capacity is exhausted;
        // At least one additional valid New card remains with persisted DueAtUtc;
        // No Learning/Review/Relearning card exists that should supply a scheduled due time.
        // Expected future behavior: completed-session summary NextDueAtUtc is null.
        // Current RED cause: current summary logic reports the New card's raw DueAtUtc.
        await using var fixture = await Schema7Fixture.CreateAsync();

        // Seed 1 genuinely-new queueable card
        await SeedCardAsync(fixture, cardId: 101, term: "blocked-word", ordinal: 1, CardState.New, atUtc: Day1Utc, dueAtUtc: Day1Utc);

        // Record a previously completed session
        var pastSessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: Day1Utc.AddHours(-2), updatedAtUtc: Day1Utc.AddHours(-2), completedAtUtc: Day1Utc.AddHours(-2));

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 1 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Exhaust daily admission capacity for Day 1 (1 grant for slot 0)
        await service.GetPreparationReadinessAsync();
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (1, 999, 0, ?)",
            Day1Utc.AddHours(-1));

        var result = await service.GetOrStartAsync();

        Assert.IsNull(result.Card, "No active session card should be returned when daily limit is exhausted and no due cards exist.");
        Assert.IsNotNull(result.CompletedSummary, "Completed summary should be returned from latest completed session.");
        Assert.AreEqual(pastSessionId, result.CompletedSummary.SessionId);
        Assert.IsNull(result.CompletedSummary.NextDueAtUtc, "Budget-blocked New card must not supply NextDueAtUtc.");
    }

    [TestMethod]
    public async Task BlockedNewTimestamp_DoesNotOutrankFutureScheduledReview()
    {
        // Scenario 2:
        // One valid blocked genuinely-new New card with an earlier raw DueAtUtc;
        // One valid scheduled card in Learning, Review, or Relearning with a later future due timestamp.
        // Expected future behavior: NextDueAtUtc equals the scheduled card's due timestamp exactly;
        // does not equal the New card's seed timestamp.
        // Current RED cause: current minimum query selects the New card.
        await using var fixture = await Schema7Fixture.CreateAsync();

        // Seed blocked New card with earlier raw DueAtUtc
        await SeedCardAsync(fixture, cardId: 101, term: "blocked-new-word", ordinal: 1, CardState.New, atUtc: Day1Utc, dueAtUtc: Day1Utc);

        // Seed scheduled Review card with future due timestamp
        var futureScheduledDueUtc = Day1Utc.AddDays(3);
        await SeedCardAsync(fixture, cardId: 201, term: "scheduled-review-word", ordinal: 2, CardState.Review, atUtc: Day1Utc.AddHours(-1), dueAtUtc: futureScheduledDueUtc);

        // Record a previously completed session
        var pastSessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: Day1Utc.AddHours(-2), updatedAtUtc: Day1Utc.AddHours(-2), completedAtUtc: Day1Utc.AddHours(-2));

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 1 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Exhaust daily budget for Day 1
        await service.GetPreparationReadinessAsync();
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (1, 999, 0, ?)",
            Day1Utc.AddHours(-1));

        var result = await service.GetOrStartAsync();

        Assert.IsNull(result.Card, "No active session card should be returned.");
        Assert.IsNotNull(result.CompletedSummary, "Completed summary should be returned.");
        Assert.AreEqual(futureScheduledDueUtc, result.CompletedSummary.NextDueAtUtc, "NextDueAtUtc must match the scheduled review card, not the blocked New card.");
        Assert.AreNotEqual(Day1Utc, result.CompletedSummary.NextDueAtUtc, "NextDueAtUtc must not equal the New card's seed timestamp.");
    }

    [TestMethod]
    public async Task ScheduledReview_DueAfterSessionCompletion_ReturnedByLaterAuthoritativeReload()
    {
        // Scenario 3 (Characterization):
        // Card A available in the initial session;
        // Valid scheduled card B due several minutes in the future;
        // Complete card A;
        // Advance fake clock beyond card B's due instant;
        // Invoke GetOrStartAsync() again.
        // Expected behavior: card B is returned in a new active learning session.
        await using var fixture = await Schema7Fixture.CreateAsync();

        // Card A: queueable New card
        await SeedCardAsync(fixture, cardId: 101, term: "card-a", ordinal: 1, CardState.New, atUtc: Day1Utc, dueAtUtc: Day1Utc);

        // Card B: scheduled Review card due in 10 minutes
        var cardBDueUtc = Day1Utc.AddMinutes(10);
        await SeedCardAsync(fixture, cardId: 201, term: "card-b", ordinal: 2, CardState.Review, atUtc: Day1Utc.AddHours(-1), dueAtUtc: cardBDueUtc);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Initial session: card A is admitted and served
        var session1 = await service.GetOrStartAsync();
        Assert.IsNotNull(session1.Card);
        Assert.AreEqual(101, session1.Card.CardId);

        // Complete card A
        await service.RevealAnswerAsync(session1.Card.QueueItemId);
        var completed = await service.RateAsync(session1.Card.QueueItemId, ReviewRating.Good);
        Assert.IsNull(completed.Card);
        Assert.IsNotNull(completed.CompletedSummary);

        // Advance fake clock beyond card B's due instant
        clock.UtcNow = Day1Utc.AddMinutes(15);

        // Reload authoritative session
        var session2 = await service.GetOrStartAsync();
        Assert.IsNotNull(session2.Card, "Card B should now be served in a new active session.");
        Assert.AreEqual(201, session2.Card.CardId, "Card B must be the scheduled card returned.");
    }

    [TestMethod]
    public async Task ScheduledReview_ZeroRequiredAssignments_DoesNotSupplyNextDueAtUtc()
    {
        // Scenario A:
        // Scheduled card in Review with a valid scheduled DueAtUtc;
        // Its Sense + Direction has zero active Required assignments (demoted to AcceptedOnly);
        // Valid non-corrupt Schema-12 state;
        // No other queueable scheduled review exists.
        // Expected future behavior: NextDueAtUtc is null.
        // Required current RED cause: current state-only query returns the nonqueueable card's DueAtUtc.
        await using var fixture = await Schema7Fixture.CreateAsync();

        var cardDueUtc = Day1Utc.AddHours(2);
        await SeedCardAsync(fixture, cardId: 101, term: "zero-required-word", ordinal: 1, CardState.Review, atUtc: Day1Utc, dueAtUtc: cardDueUtc);

        var pastSessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: Day1Utc.AddHours(-2), updatedAtUtc: Day1Utc.AddHours(-2), completedAtUtc: Day1Utc.AddHours(-2));

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        // Demote the only assignment to AcceptedOnly, leaving zero Required assignments for this Sense + Direction
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", 101);
        await fixture.Connection.ExecuteAsync(
            """
            UPDATE SenseAnswerVariantAssignments
            SET Requirement = ?, RequiredSinceUtc = NULL
            WHERE SenseId = ? AND CardDirection = ?
            """,
            (int)AnswerVariantRequirement.AcceptedOnly, senseId, (int)CardDirection.MeaningToTerm);

        var appSettings = new TestAppSettingsService { PreparationLimit = 1 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Exhaust daily admission capacity
        await service.GetPreparationReadinessAsync();
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (1, 999, 0, ?)",
            Day1Utc.AddHours(-1));

        var result = await service.GetOrStartAsync();

        Assert.IsNull(result.Card, "No active card should be served.");
        Assert.IsNotNull(result.CompletedSummary, "Completed summary should be returned.");
        Assert.AreEqual(pastSessionId, result.CompletedSummary.SessionId);
        Assert.IsNull(result.CompletedSummary.NextDueAtUtc,
            "Scheduled card with zero Required assignments must not supply NextDueAtUtc.");

        // Characterization: verify that once due instant arrives, the card is skipped rather than failing as malformed data
        clock.UtcNow = cardDueUtc.AddMinutes(5);
        var sessionAfterDue = await service.GetOrStartAsync();
        Assert.IsNull(sessionAfterDue.Card, "Zero-Required card must be cleanly skipped by queue target selection.");
        Assert.IsNotNull(sessionAfterDue.CompletedSummary);
        Assert.IsNull(sessionAfterDue.CompletedSummary.NextDueAtUtc);
    }

    [TestMethod]
    public async Task NonqueueableScheduledReview_DoesNotOutrankLaterQueueableScheduledReview()
    {
        // Scenario B:
        // Card A: scheduled review with earlier DueAtUtc but zero Required assignments;
        // Card B: scheduled review with later DueAtUtc and at least one valid Required assignment.
        // Expected future behavior: NextDueAtUtc equals card B's timestamp exactly.
        // Required current RED cause: current query selects card A.
        await using var fixture = await Schema7Fixture.CreateAsync();

        var cardADueUtc = Day1Utc.AddHours(1);
        var cardBDueUtc = Day1Utc.AddHours(4);

        await SeedCardAsync(fixture, cardId: 101, term: "card-a-zero-req", ordinal: 1, CardState.Review, atUtc: Day1Utc, dueAtUtc: cardADueUtc);
        await SeedCardAsync(fixture, cardId: 201, term: "card-b-valid-req", ordinal: 2, CardState.Review, atUtc: Day1Utc, dueAtUtc: cardBDueUtc);

        var pastSessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: Day1Utc.AddHours(-2), updatedAtUtc: Day1Utc.AddHours(-2), completedAtUtc: Day1Utc.AddHours(-2));

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        // Demote Card A's assignment to AcceptedOnly, leaving Card A with zero Required assignments
        var senseAId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", 101);
        await fixture.Connection.ExecuteAsync(
            """
            UPDATE SenseAnswerVariantAssignments
            SET Requirement = ?, RequiredSinceUtc = NULL
            WHERE SenseId = ? AND CardDirection = ?
            """,
            (int)AnswerVariantRequirement.AcceptedOnly, senseAId, (int)CardDirection.MeaningToTerm);

        var appSettings = new TestAppSettingsService { PreparationLimit = 1 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Exhaust daily admission capacity
        await service.GetPreparationReadinessAsync();
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (1, 999, 0, ?)",
            Day1Utc.AddHours(-1));

        var result = await service.GetOrStartAsync();

        Assert.IsNull(result.Card, "No active session card should be returned.");
        Assert.IsNotNull(result.CompletedSummary, "Completed summary should be returned.");
        Assert.AreEqual(cardBDueUtc, result.CompletedSummary.NextDueAtUtc,
            "NextDueAtUtc must match Card B's timestamp, not Card A's nonqueueable timestamp.");
        Assert.AreNotEqual(cardADueUtc, result.CompletedSummary.NextDueAtUtc,
            "NextDueAtUtc must not equal nonqueueable Card A's timestamp.");

        // Characterization: verify Card A is skipped when its due instant passes, and Card B is served when Card B is due
        clock.UtcNow = cardADueUtc.AddMinutes(5);
        var sessionAtCardA = await service.GetOrStartAsync();
        Assert.IsNull(sessionAtCardA.Card, "Card A has zero Required assignments and must be skipped.");
        Assert.AreEqual(cardBDueUtc, sessionAtCardA.CompletedSummary?.NextDueAtUtc,
            "Card B remains the next valid scheduled review.");

        clock.UtcNow = cardBDueUtc.AddMinutes(5);
        var sessionAtCardB = await service.GetOrStartAsync();
        Assert.IsNotNull(sessionAtCardB.Card, "Card B has active Required assignment and must be served.");
        Assert.AreEqual(201, sessionAtCardB.Card.CardId, "Card B must be the card returned.");
    }

    [TestMethod]
    public async Task SummaryNextDue_Schema7_ExcludesNewCards()
    {
        // Legacy consistency:
        // Schema-7 summary path must exclude CardState.New and reflect only scheduled cards.
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-schema7-summary-next-due");
        await database.InitializeAsync();

        // Seed Card 1 (New, to be learned in session 1)
        var card1 = await SeedSchema7CardAsync(database, "schema7-word-1", CardState.New, Day1Utc);

        var clock = new FakeClock(Day1Utc);
        var service = new LearningService(
            database,
            new SimpleSpacedRepetitionScheduler(),
            new SpellingAnswerComparer(),
            clock);

        // Start session 1 containing Card 1
        var session1 = await service.GetOrStartAsync();
        Assert.IsNotNull(session1.Card);
        Assert.AreEqual(card1.CardId, session1.Card.CardId);

        // Seed Card 2 (New) with an earlier DueAtUtc seed timestamp after session 1 has already started
        var card2DueUtc = Day1Utc.AddHours(-1);
        await SeedSchema7CardAsync(database, "schema7-word-2", CardState.New, card2DueUtc);

        // Reveal and rate Card 1 Good, completing session 1
        await service.RevealAnswerAsync(session1.Card.QueueItemId);
        var completion = await service.RateAsync(session1.Card.QueueItemId, ReviewRating.Good);

        Assert.IsNull(completion.Card);
        Assert.IsNotNull(completion.CompletedSummary);

        // Query persisted Card 1 to find its scheduled DueAtUtc
        var storedCard1 = await database.ReadAsync(conn =>
            conn.Table<LearningCardEntity>().Where(c => c.Id == card1.CardId).FirstAsync());
        Assert.AreEqual(CardState.Review, storedCard1.State);
        Assert.IsTrue(storedCard1.DueAtUtc > Day1Utc, "Rated card should be scheduled into the future.");

        // Expected future behavior: NextDueAtUtc equals storedCard1.DueAtUtc, NOT card 2's earlier seed timestamp
        Assert.AreEqual(storedCard1.DueAtUtc, completion.CompletedSummary.NextDueAtUtc,
            "Schema-7 NextDueAtUtc must represent scheduled review timestamp, not unadmitted New card's DueAtUtc.");
        Assert.AreNotEqual(card2DueUtc, completion.CompletedSummary.NextDueAtUtc,
            "Schema-7 NextDueAtUtc must not be the New card's seed DueAtUtc.");
    }

    private static LearningService CreateService(
        Schema7Fixture fixture,
        IClock clock,
        IAppSettingsService? appSettings = null) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        clock,
        appSettings,
        timezoneResolver: new LearningTimezoneResolver());

    private static async Task SeedCardAsync(
        Schema7Fixture fixture,
        int cardId,
        string term,
        int ordinal,
        CardState state,
        DateTime atUtc,
        DateTime? dueAtUtc = null,
        int frequency = 1)
    {
        var createdAt = atUtc.AddHours(-1).AddMinutes(ordinal);
        var wordId = await fixture.InsertWordAsync(
            term, totalOccurrenceCount: frequency, createdAt: createdAt, updatedAt: createdAt);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: term, translation: $"answer-{term}", createdAt: createdAt, updatedAt: createdAt);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state, dueAtUtc: dueAtUtc ?? atUtc,
            createdAtUtc: createdAt, updatedAtUtc: createdAt, id: cardId);
    }

    private static Task<(int WordId, int MeaningId, int CardId)> SeedSchema7CardAsync(
        TemporaryKnownFirstDatabase database,
        string term,
        CardState state,
        DateTime dueAtUtc) =>
        database.RunInTransactionAsync(connection =>
        {
            var word = new WordEntity
            {
                CanonicalTerm = term,
                NormalizedTerm = term,
                Language = "de",
                Status = WordStatus.UnknownBacklog,
                PreparationState = PreparationState.Prepared,
                TokenKind = TokenKind.Word,
                TotalOccurrenceCount = 1,
                DocumentCount = 1,
                CreatedAt = dueAtUtc,
                UpdatedAt = dueAtUtc
            };
            connection.Insert(word);

            var meaning = new MeaningEntity
            {
                WordId = word.Id,
                DisplayTerm = term,
                Translation = $"answer-{term}",
                AcceptedAliasesJson = "[]",
                CreatedAt = dueAtUtc,
                UpdatedAt = dueAtUtc
            };
            connection.Insert(meaning);

            var card = new LearningCardEntity
            {
                WordId = word.Id,
                MeaningId = meaning.Id,
                Direction = CardDirection.TermToMeaning,
                State = state,
                DueAtUtc = dueAtUtc,
                CreatedAtUtc = dueAtUtc,
                UpdatedAtUtc = dueAtUtc
            };
            connection.Insert(card);
            return (word.Id, meaning.Id, card.Id);
        });

    private sealed class TestAppSettingsService : IAppSettingsService
    {
        public int PreparationLimit { get; set; } = 10;
        public IReadOnlyList<int> SupportedPreparationLimits => [5, 10, 20, 30, 50];
        public CardDirectionPreference CardDirection { get; set; } = CardDirectionPreference.Both;
        public LearningMode LearningMode { get; set; } = LearningMode.Automatic;
        public bool HasOnlineLookupConsent { get; set; } = false;
        public bool EnhancedTermRecognitionEnabled { get; set; } = false;
        public LearningTimezoneMode LearningTimezoneMode { get; set; } = LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId { get; set; } = null;
        public int LearningDayCutoffMinutes { get; set; } = 0;

        public void SetPreparationLimit(int limit) => PreparationLimit = limit;
        public void SetCardDirection(CardDirectionPreference preference) => CardDirection = preference;
        public void SetLearningMode(LearningMode mode) => LearningMode = mode;
        public void GrantOnlineLookupConsent() => HasOnlineLookupConsent = true;
        public void RevokeOnlineLookupConsent() => HasOnlineLookupConsent = false;
        public void SetEnhancedTermRecognitionEnabled(bool value) => EnhancedTermRecognitionEnabled = value;
        public void SetLearningTimezoneMode(LearningTimezoneMode mode) => LearningTimezoneMode = mode;
        public void SetExplicitLearningTimezoneId(string? timezoneId) => ExplicitLearningTimezoneId = timezoneId;
        public void SetLearningDayCutoffMinutes(int minutes) => LearningDayCutoffMinutes = minutes;
        public void Reset()
        {
            PreparationLimit = 10;
            CardDirection = CardDirectionPreference.Both;
            LearningMode = LearningMode.Automatic;
            HasOnlineLookupConsent = false;
            EnhancedTermRecognitionEnabled = false;
            LearningTimezoneMode = LearningTimezoneMode.System;
            ExplicitLearningTimezoneId = null;
            LearningDayCutoffMinutes = 0;
        }
    }
}
