using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;
using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServiceDailyLimitTests
{
    private static readonly DateTime Day1Utc = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2Utc = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task GetOrStart_AdmitsAtMostTheConfiguredNewWordsPerDay()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var result = await service.GetOrStartAsync();

        Assert.IsNotNull(result.Card);
        var admittedCount = await fixture.Connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT c.WordId)
            FROM LearningSessionCards sc
            JOIN LearningCards c ON sc.CardId = c.Id
            WHERE sc.IsCompleted = 0
            """);

        Assert.AreEqual(5, admittedCount);

        var grantCount = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(5, grantCount);
    }

    [TestMethod]
    public async Task GetOrStart_MultipleSessionsSameDay_DoesNotAdmitMoreThanNWords()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Session 1: loads and rates all 5 words
        var loadResult = await service.GetOrStartAsync();
        while (loadResult.Card is not null)
        {
            await service.RevealAnswerAsync(loadResult.Card.QueueItemId);
            loadResult = await service.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);
        }

        Assert.IsNotNull(loadResult.CompletedSummary);
        Assert.AreEqual(5, loadResult.CompletedSummary.CardsReviewed);

        // Session 2 on same day: should NOT admit the remaining 3 new words
        var session2Result = await service.GetOrStartAsync();
        Assert.IsNull(session2Result.Card, "No new words should be admitted once daily limit is reached on the same day");
        Assert.IsNotNull(session2Result.CompletedSummary);

        var totalGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(5, totalGrants);
    }

    [TestMethod]
    public async Task GetOrStart_DayRollover_CarryOverConsumesSlotsFirst_ThenFillsRemainingN()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Day 1: start session (admits words 1..5, grant slots 0..4).
        // Rate 2 cards (words 1 and 2), leaving words 3, 4, 5 untouched (incomplete).
        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        await service.RevealAnswerAsync(load.Card.QueueItemId);
        load = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);

        Assert.IsNotNull(load.Card);
        await service.RevealAnswerAsync(load.Card.QueueItemId);
        load = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);

        // Advance clock to Day 2
        clock.UtcNow = Day2Utc;

        // GetOrStartAsync on Day 2 with active session:
        // Carry-over words (3, 4, 5) consume Day 2 slots 0, 1, 2.
        // Fresh words (words 6, 7) consume remaining Day 2 slots 3, 4.
        var day2Result = await service.GetOrStartAsync();
        Assert.IsNotNull(day2Result.Card);

        var day2Grants = await fixture.Connection.QueryAsync<LearningDayGrantEntity>(
            "SELECT * FROM LearningDayGrants WHERE DayOrdinal = 2 ORDER BY SlotOrdinal");

        Assert.AreEqual(5, day2Grants.Count);
        // Words 3, 4, 5 (the carry-over words) have slot ordinals 0, 1, 2
        var carryOverWordIds = day2Grants.Take(3).Select(g => g.WordId).ToHashSet();
        Assert.IsTrue(carryOverWordIds.Contains(3));
        Assert.IsTrue(carryOverWordIds.Contains(4));
        Assert.IsTrue(carryOverWordIds.Contains(5));

        // Fresh words 6, 7 have slot ordinals 3, 4
        var freshWordIds = day2Grants.Skip(3).Select(g => g.WordId).ToHashSet();
        Assert.IsTrue(freshWordIds.Contains(6));
        Assert.IsTrue(freshWordIds.Contains(7));
    }

    [TestMethod]
    public async Task GetOrStart_LimitReduction_PreservesQueueAndGrants_AppliesPresentationPrefix()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 10; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 10 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        // Day 1: limit 10 admits all 10 words (slots 0..9)
        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);

        var initialGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(10, initialGrants);

        // User reduces limit to 5
        appSettings.PreparationLimit = 5;

        // Rate the first 5 cards (words 1..5)
        for (var i = 1; i <= 5; i++)
        {
            var current = await service.GetOrStartAsync();
            Assert.IsNotNull(current.Card);
            await service.RevealAnswerAsync(current.Card.QueueItemId);
            await service.RateAsync(current.Card.QueueItemId, ReviewRating.Good);
        }

        // Now words 1..5 are completed. Words 6..10 have slot ordinals 5..9 >= 5 (effective limit).
        // They must NOT be presentable!
        var blockedLoad = await service.GetOrStartAsync();
        Assert.IsNull(blockedLoad.Card, "Cards with SlotOrdinal >= current limit N should not be presented");

        // Queue rows and grants in DB remain intact (10 rows, 10 grants)
        var totalQueueRows = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards");
        Assert.AreEqual(10, totalQueueRows);
        var remainingGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(10, remainingGrants);

        // If user increases limit back to 10, remaining cards become presentable again
        appSettings.PreparationLimit = 10;
        var unblockedLoad = await service.GetOrStartAsync();
        Assert.IsNotNull(unblockedLoad.Card, "Cards become presentable again when limit is restored");
    }

    [TestMethod]
    public async Task GetOrStart_OldWorkDueCardsAndLearnedSiblingCards_UnconstrainedByLimit()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        // 5 genuinely-new words
        for (var i = 1; i <= 5; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"new-word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }

        // 2 due review cards (words 50, 51)
        for (var i = 1; i <= 2; i++)
        {
            await SeedCardAsync(fixture, cardId: 200 + i, term: $"due-word-{i}", ordinal: 10 + i, CardState.Review, atUtc: Day1Utc, dueAtUtc: Day1Utc.AddHours(-1));
        }

        // 1 learned word (word 60) with a sibling New card (cardId 301)
        var seededPast = Day1Utc.AddDays(-2);
        var word60 = await fixture.InsertWordAsync("sibling-word", totalOccurrenceCount: 1, createdAt: seededPast, updatedAt: seededPast);
        var m60 = await fixture.InsertMeaningAsync(word60, displayTerm: "sibling-word", translation: "answer", createdAt: seededPast, updatedAt: seededPast);
        var card60Learned = await fixture.InsertCardAsync(word60, m60, CardDirection.MeaningToTerm, CardState.Review, dueAtUtc: Day1Utc.AddDays(5), createdAtUtc: seededPast, updatedAtUtc: seededPast, id: 300);
        await fixture.InsertCardAsync(word60, m60, CardDirection.TermToMeaning, CardState.New, dueAtUtc: Day1Utc, createdAtUtc: seededPast, updatedAtUtc: seededPast, id: 301);

        // Add a completed session and review to record that word 60 has ever been learned
        var pastSessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: seededPast, updatedAtUtc: seededPast, completedAtUtc: seededPast);
        await fixture.InsertReviewAsync(card60Learned, pastSessionId, ReviewRating.Good, true, true, Day1Utc.AddDays(-1), Day1Utc.AddDays(5), 1, 2.5);

        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var result = await service.GetOrStartAsync();
        Assert.IsNotNull(result.Card);

        var totalSessionCards = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards");
        // 5 genuinely-new + 2 due + 1 sibling new = 8 total cards
        Assert.AreEqual(8, totalSessionCards);

        var genuinelyNewGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(5, genuinelyNewGrants);
    }

    [TestMethod]
    public async Task GetOrStart_BridgePhase_BlocksGenuinelyNewWords_AllowsDueCards()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        // 3 new words
        for (var i = 1; i <= 3; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"new-word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }

        // 2 due cards
        for (var i = 1; i <= 2; i++)
        {
            await SeedCardAsync(fixture, cardId: 200 + i, term: $"due-word-{i}", ordinal: 10 + i, CardState.Review, atUtc: Day1Utc, dueAtUtc: Day1Utc.AddHours(-1));
        }

        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        // Put DayState into Bridge phase
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningDayState (Id, Phase, DayOrdinal, ActiveDayStartUtc, ActiveDayEndUtc, FrozenTimeZoneId, FrozenCutoffMinutes, BridgeStartedUtc, BridgeTargetTimeZoneId, BridgeTargetCutoffMinutes, BridgeTargetUtc, UpdatedAtUtc)
            VALUES (1, ?, 1, ?, ?, 'UTC', 0, ?, 'UTC', 240, ?, ?)
            """,
            (int)LearningDayPhase.Bridge, Day1Utc.AddDays(-1), Day1Utc.AddHours(-4), Day1Utc.AddHours(-4), Day1Utc.AddHours(4), Day1Utc);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var result = await service.GetOrStartAsync();

        // Due cards can be loaded and studied
        Assert.IsNotNull(result.Card);
        Assert.IsTrue(result.Card.QueueItemId > 0);

        var sessionCardCount = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards");
        // Only 2 due cards are admitted, 0 genuinely-new cards admitted
        Assert.AreEqual(2, sessionCardCount);

        var grantCount = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants");
        Assert.AreEqual(0, grantCount);
    }

    [TestMethod]
    public async Task Rate_NoSameDayReplacement_DoesNotReopenSlot()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);

        // Rate the first word
        await service.RevealAnswerAsync(load.Card.QueueItemId);
        var afterRate = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);

        // Total grants must remain 5, not 6
        var grantCount = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(5, grantCount);
    }

    [TestMethod]
    public async Task EnsureDayState_FrozenCutoffAndTimezone_PersistsUntilRollover()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedCardAsync(fixture, cardId: 101, term: "word-1", ordinal: 1, CardState.New, atUtc: Day1Utc);
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService
        {
            PreparationLimit = 5,
            LearningDayCutoffMinutes = 0,
            LearningTimezoneMode = LearningTimezoneMode.Explicit,
            ExplicitLearningTimezoneId = "UTC"
        };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        await service.GetOrStartAsync();

        var state1 = (await fixture.Connection.QueryAsync<LearningDayStateEntity>("SELECT * FROM LearningDayState")).First();
        Assert.AreEqual("UTC", state1.FrozenTimeZoneId);
        Assert.AreEqual(0, state1.FrozenCutoffMinutes);

        // User changes settings mid-day
        appSettings.LearningDayCutoffMinutes = 240; // 04:00
        appSettings.ExplicitLearningTimezoneId = OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo";

        // Query service again on same day
        await service.GetOrStartAsync();

        var state2 = (await fixture.Connection.QueryAsync<LearningDayStateEntity>("SELECT * FROM LearningDayState")).First();
        Assert.AreEqual("UTC", state2.FrozenTimeZoneId, "Frozen timezone should not change mid-day");
        Assert.AreEqual(0, state2.FrozenCutoffMinutes, "Frozen cutoff should not change mid-day");
    }

    [TestMethod]
    public async Task MarkPermanentlyKnown_DoesNotReopenCurrentDayNewWordSlot()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);

        var initialGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(5, initialGrants);

        var permanentlyKnownSuccess = await service.MarkPermanentlyKnownAsync(1, confirmed: true);
        Assert.IsTrue(permanentlyKnownSuccess);

        var afterMark = await service.GetOrStartAsync();

        var totalGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(5, totalGrants, "Permanently Known must not reopen or create new grants on the same day");

        var word6Grant = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1 AND WordId = 6");
        Assert.AreEqual(0, word6Grant);
    }

    [TestMethod]
    public async Task BothDirections_OneWordIdConsumesOneNewWordSlot()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("bidirectional-word", totalOccurrenceCount: 10, createdAt: Day1Utc, updatedAt: Day1Utc);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "bidirectional-word", translation: "translation", createdAt: Day1Utc, updatedAt: Day1Utc);

        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, CardState.New, dueAtUtc: Day1Utc, createdAtUtc: Day1Utc, updatedAtUtc: Day1Utc, id: 101);
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, CardState.New, dueAtUtc: Day1Utc, createdAtUtc: Day1Utc, updatedAtUtc: Day1Utc, id: 102);

        var wordId2 = await fixture.InsertWordAsync("second-word", totalOccurrenceCount: 5, createdAt: Day1Utc, updatedAt: Day1Utc);
        var meaningId2 = await fixture.InsertMeaningAsync(wordId2, displayTerm: "second-word", translation: "translation2", createdAt: Day1Utc, updatedAt: Day1Utc);
        await fixture.InsertCardAsync(wordId2, meaningId2, CardDirection.TermToMeaning, CardState.New, dueAtUtc: Day1Utc, createdAtUtc: Day1Utc, updatedAtUtc: Day1Utc, id: 103);

        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);

        var totalSessionCards = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards");
        Assert.AreEqual(3, totalSessionCards, "All 3 cards (2 for word 1, 1 for word 2) should be admitted");

        var distinctWordCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT c.WordId) FROM LearningSessionCards sc JOIN LearningCards c ON sc.CardId = c.Id");
        Assert.AreEqual(2, distinctWordCount);

        var totalGrants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(2, totalGrants, "Only 2 grants should be created for the 2 distinct WordIds");

        var word1Grants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1 AND WordId = ?", wordId);
        Assert.AreEqual(1, word1Grants, "Word 1 with both directions must consume exactly 1 grant slot");
    }

    [TestMethod]
    public async Task MultipleSenses_OneWordIdConsumesOneNewWordSlot()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("polysemous-word", totalOccurrenceCount: 10, createdAt: Day1Utc, updatedAt: Day1Utc);

        var m1Id = await fixture.InsertMeaningAsync(wordId, displayTerm: "polysemous-word", translation: "sense 1 answer", createdAt: Day1Utc, updatedAt: Day1Utc);
        await fixture.InsertCardAsync(wordId, m1Id, CardDirection.TermToMeaning, CardState.New, dueAtUtc: Day1Utc, createdAtUtc: Day1Utc, updatedAtUtc: Day1Utc, id: 201);

        var m2Id = await fixture.InsertMeaningAsync(wordId, displayTerm: "polysemous-word", translation: "sense 2 answer", createdAt: Day1Utc, updatedAt: Day1Utc);
        await fixture.InsertCardAsync(wordId, m2Id, CardDirection.MeaningToTerm, CardState.New, dueAtUtc: Day1Utc, createdAtUtc: Day1Utc, updatedAtUtc: Day1Utc, id: 202);

        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);

        var totalSessionCards = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards");
        Assert.AreEqual(2, totalSessionCards, "Both sense cards should be in session queue");

        var distinctSenses = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT SenseId) FROM LearningCards WHERE WordId = ?", wordId);
        Assert.AreEqual(2, distinctSenses, "Word should have 2 distinct Senses");

        var grants = await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1");
        Assert.AreEqual(1, grants, "Word with multiple senses must consume exactly 1 grant slot");
    }

    [TestMethod]
    public async Task ActiveSession_CompletedHistoricalRow_DoesNotBlockSameCardWhenDueAgain()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedCardAsync(fixture, cardId: 101, term: "word-1", ordinal: 1, CardState.New, atUtc: Day1Utc);
        await SeedCardAsync(fixture, cardId: 102, term: "word-2", ordinal: 2, CardState.New, atUtc: Day1Utc);
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(101, load.Card.CardId);

        await service.RevealAnswerAsync(load.Card.QueueItemId);
        var afterRate1 = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);

        var card101 = await fixture.Connection.GetAsync<LearningCardEntity>(101);
        clock.UtcNow = card101.DueAtUtc.AddMinutes(5);

        var afterReconcile = await service.GetOrStartAsync();
        Assert.IsNotNull(afterReconcile.Card);

        var queueRowsFor101 = await fixture.Connection.QueryAsync<LearningSessionCardEntity>(
            "SELECT * FROM LearningSessionCards WHERE CardId = 101 ORDER BY QueueOrder");

        Assert.AreEqual(2, queueRowsFor101.Count, "Card 101 should have original completed row and newly appended incomplete row");
        Assert.IsTrue(queueRowsFor101[0].IsCompleted);
        Assert.IsFalse(queueRowsFor101[1].IsCompleted);
    }

    [TestMethod]
    public async Task ActiveSession_IncompleteAgainRepeat_PreventsDuplicateDueAppend()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedCardAsync(fixture, cardId: 101, term: "word-1", ordinal: 1, CardState.New, atUtc: Day1Utc);
        await SeedCardAsync(fixture, cardId: 102, term: "word-2", ordinal: 2, CardState.New, atUtc: Day1Utc);
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(101, load.Card.CardId);

        await service.RevealAnswerAsync(load.Card.QueueItemId);
        var afterAgain = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Again);

        var beforeReconcileCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE CardId = 101 AND IsCompleted = 0");
        Assert.AreEqual(1, beforeReconcileCount);

        var reloaded = await service.GetOrStartAsync();

        var afterReconcileCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE CardId = 101 AND IsCompleted = 0");
        Assert.AreEqual(1, afterReconcileCount, "Incomplete row must prevent duplicate append during reconciliation");
    }

    [TestMethod]
    public async Task ActiveSession_PartialFill_RestartDoesNotDuplicateGrantsOrQueueRows()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        await service.RevealAnswerAsync(load.Card!.QueueItemId);
        load = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);
        await service.RevealAnswerAsync(load.Card!.QueueItemId);
        load = await service.RateAsync(load.Card.QueueItemId, ReviewRating.Good);

        clock.UtcNow = Day2Utc;

        var day2First = await service.GetOrStartAsync();
        Assert.IsNotNull(day2First.Card);

        var grantsAfterFirst = await fixture.Connection.QueryAsync<LearningDayGrantEntity>(
            "SELECT * FROM LearningDayGrants WHERE DayOrdinal = 2 ORDER BY SlotOrdinal");
        Assert.AreEqual(5, grantsAfterFirst.Count);

        var queueCountAfterFirst = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsCompleted = 0");

        var day2Second = await service.GetOrStartAsync();
        Assert.IsNotNull(day2Second.Card);

        var grantsAfterSecond = await fixture.Connection.QueryAsync<LearningDayGrantEntity>(
            "SELECT * FROM LearningDayGrants WHERE DayOrdinal = 2 ORDER BY SlotOrdinal");
        Assert.AreEqual(5, grantsAfterSecond.Count, "Restart must not create duplicate grants");

        var queueCountAfterSecond = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsCompleted = 0");
        Assert.AreEqual(queueCountAfterFirst, queueCountAfterSecond, "Restart must not duplicate queue rows");
    }

    [TestMethod]
    public async Task GetOrStart_DayRollover_CarryOverGreaterOrEqualToN_AdmitsNoFreshWords()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var i = 1; i <= 8; i++)
        {
            await SeedCardAsync(fixture, cardId: 100 + i, term: $"word-{i}", ordinal: i, CardState.New, atUtc: Day1Utc);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var appSettings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(Day1Utc);
        var service = CreateService(fixture, clock, appSettings);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);

        clock.UtcNow = Day2Utc;

        var day2Load = await service.GetOrStartAsync();
        Assert.IsNotNull(day2Load.Card);

        var day2Grants = await fixture.Connection.QueryAsync<LearningDayGrantEntity>(
            "SELECT * FROM LearningDayGrants WHERE DayOrdinal = 2 ORDER BY SlotOrdinal");
        Assert.AreEqual(5, day2Grants.Count);

        var day2WordIds = day2Grants.Select(g => g.WordId).ToHashSet();
        for (var i = 1; i <= 5; i++)
        {
            Assert.IsTrue(day2WordIds.Contains(i), $"Carry-over word {i} should be granted");
        }

        Assert.IsFalse(day2WordIds.Contains(6), "Fresh word 6 should NOT be admitted");
        Assert.IsFalse(day2WordIds.Contains(7), "Fresh word 7 should NOT be admitted");
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
