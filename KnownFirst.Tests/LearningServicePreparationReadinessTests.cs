using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;
using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServicePreparationReadinessTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task GetOrStart_CardlessWordDoesNotConsumeGrantAndEligibleWordsFillCapacity()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var cardlessWordId = await fixture.InsertWordAsync(
            "cardless",
            status: WordStatus.UnknownBacklog,
            preparationState: PreparationState.Unprepared,
            totalOccurrenceCount: 100,
            createdAt: NowUtc.AddHours(-2),
            updatedAt: NowUtc.AddHours(-2));

        var eligibleWordIds = new List<int>();
        for (var index = 1; index <= 5; index++)
        {
            eligibleWordIds.Add(await SeedNewCardAsync(
                fixture, cardId: 100 + index, term: $"eligible-{index}", frequency: 10 - index));
        }

        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var service = CreateService(
            fixture,
            new FakeClock(NowUtc),
            new TestAppSettingsService { PreparationLimit = 5 });

        var result = await service.GetOrStartAsync();

        Assert.IsNotNull(result.Card);
        var grantedWordIds = (await fixture.Connection.QueryAsync<GrantedWordRow>(
                "SELECT WordId FROM LearningDayGrants WHERE DayOrdinal = 1 ORDER BY SlotOrdinal"))
            .Select(row => row.WordId)
            .ToList();

        Assert.IsFalse(grantedWordIds.Contains(cardlessWordId),
            "A bare Word row without a queueable New card must not consume a genuinely-new grant.");
        CollectionAssert.AreEquivalent(eligibleWordIds, grantedWordIds,
            "All available capacity must be filled by distinct Words that own queueable New cards.");

        var grantedWordsWithoutQueueableNewCard = await fixture.Connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM LearningDayGrants g
            WHERE g.DayOrdinal = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM LearningCards c
                  WHERE c.WordId = g.WordId AND c.State = ?)
            """,
            (int)CardState.New);

        Assert.AreEqual(0, grantedWordsWithoutQueueableNewCard);
    }

    [TestMethod]
    [DataRow(4, false)]
    [DataRow(5, true)]
    [DataRow(6, true)]
    public async Task Readiness_RequiresEligibleBacklogToFillAllOpenDemand(
        int eligibleWordCount,
        bool expectedTransition)
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var index = 1; index <= eligibleWordCount; index++)
        {
            await SeedNewCardAsync(fixture, 200 + index, $"eligible-{index}", frequency: 10 - index);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var service = CreateService(
            fixture,
            new FakeClock(NowUtc),
            new TestAppSettingsService { PreparationLimit = 5 });

        var readiness = await service.GetPreparationReadinessAsync();

        Assert.AreEqual(expectedTransition, readiness.ShouldTransitionToLearning);
        Assert.AreEqual(LearningDayPhase.ActiveBudgetDay, readiness.Phase);
        Assert.AreEqual(5, readiness.RemainingFreshWordDemand);
        Assert.AreEqual(eligibleWordCount, readiness.EligibleFreshWordCount);
        await AssertNoAdmissionSideEffectsAsync(fixture);
    }

    [TestMethod]
    public async Task Readiness_ExistingGrantReducesDemandWithoutBeingRewritten()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var index = 1; index <= 4; index++)
        {
            await SeedNewCardAsync(fixture, 300 + index, $"eligible-{index}", frequency: 10 - index);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var service = CreateService(
            fixture,
            new FakeClock(NowUtc),
            new TestAppSettingsService { PreparationLimit = 5 });
        await service.GetPreparationReadinessAsync();
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (1, 900, 0, ?)",
            NowUtc.AddMinutes(-5));

        var readiness = await service.GetPreparationReadinessAsync();

        Assert.IsTrue(readiness.ShouldTransitionToLearning);
        Assert.AreEqual(4, readiness.RemainingFreshWordDemand);
        Assert.AreEqual(4, readiness.EligibleFreshWordCount);
        var grant = (await fixture.Connection.QueryAsync<GrantSnapshot>(
            "SELECT WordId, SlotOrdinal, GrantedAtUtc FROM LearningDayGrants WHERE DayOrdinal = 1")).Single();
        Assert.AreEqual(900, grant.WordId);
        Assert.AreEqual(0, grant.SlotOrdinal);
        Assert.AreEqual(NowUtc.AddMinutes(-5), grant.GrantedAtUtc);
    }

    [TestMethod]
    public async Task Readiness_FullyConsumedOrReducedLimit_IsFalseAndPreservesGrants()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var settings = new TestAppSettingsService { PreparationLimit = 5 };
        var service = CreateService(fixture, new FakeClock(NowUtc), settings);
        await service.GetPreparationReadinessAsync();
        for (var slot = 0; slot < 5; slot++)
        {
            await fixture.Connection.ExecuteAsync(
                "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (1, ?, ?, ?)",
                900 + slot, slot, NowUtc.AddMinutes(-slot));
        }

        var fullyConsumed = await service.GetPreparationReadinessAsync();
        Assert.IsFalse(fullyConsumed.ShouldTransitionToLearning);
        Assert.AreEqual(0, fullyConsumed.RemainingFreshWordDemand);

        settings.PreparationLimit = 1;
        var reduced = await service.GetPreparationReadinessAsync();
        Assert.IsFalse(reduced.ShouldTransitionToLearning);
        Assert.AreEqual(0, reduced.RemainingFreshWordDemand);
        Assert.AreEqual(5, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1"));
    }

    [TestMethod]
    public async Task Readiness_BridgePhase_IsFalse()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var index = 1; index <= 5; index++)
        {
            await SeedNewCardAsync(fixture, 400 + index, $"eligible-{index}", frequency: index);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningDayState
                (Id, Phase, DayOrdinal, ActiveDayStartUtc, ActiveDayEndUtc, FrozenTimeZoneId,
                 FrozenCutoffMinutes, BridgeStartedUtc, BridgeTargetTimeZoneId,
                 BridgeTargetCutoffMinutes, BridgeTargetUtc, UpdatedAtUtc)
            VALUES (1, ?, 1, ?, ?, 'UTC', 0, ?, 'UTC', 240, ?, ?)
            """,
            (int)LearningDayPhase.Bridge,
            NowUtc.AddDays(-1),
            NowUtc.AddHours(-4),
            NowUtc.AddHours(-4),
            NowUtc.AddHours(4),
            NowUtc);

        var readiness = await CreateService(
                fixture,
                new FakeClock(NowUtc),
                new TestAppSettingsService { PreparationLimit = 5 })
            .GetPreparationReadinessAsync();

        Assert.IsFalse(readiness.ShouldTransitionToLearning);
        Assert.AreEqual(LearningDayPhase.Bridge, readiness.Phase);
        Assert.AreEqual(0, readiness.RemainingFreshWordDemand);
        await AssertNoAdmissionSideEffectsAsync(fixture);
    }

    [TestMethod]
    public async Task CardlessUnknownKnownAndIgnoredWords_NeitherSatisfyReadinessNorReceiveGrants()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.InsertWordAsync(
            "unprepared", status: WordStatus.UnknownBacklog, preparationState: PreparationState.Unprepared,
            totalOccurrenceCount: 30, createdAt: NowUtc, updatedAt: NowUtc);
        await fixture.InsertWordAsync(
            "known", status: WordStatus.Known, preparationState: PreparationState.Unprepared,
            totalOccurrenceCount: 20, createdAt: NowUtc, updatedAt: NowUtc);
        await fixture.InsertWordAsync(
            "ignored", status: WordStatus.Ignored, preparationState: PreparationState.Unprepared,
            totalOccurrenceCount: 10, createdAt: NowUtc, updatedAt: NowUtc);
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var service = CreateService(
            fixture,
            new FakeClock(NowUtc),
            new TestAppSettingsService { PreparationLimit = 1 });

        var readiness = await service.GetPreparationReadinessAsync();
        var load = await service.GetOrStartAsync();

        Assert.IsFalse(readiness.ShouldTransitionToLearning);
        Assert.AreEqual(0, readiness.EligibleFreshWordCount);
        Assert.IsNull(load.Card);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessions"));
    }

    [TestMethod]
    public async Task Readiness_DueOldWorkAndLearnedSiblingDoNotConsumeOrSatisfyFreshDemand()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var learnedAt = NowUtc.AddDays(-2);
        var wordId = await fixture.InsertWordAsync(
            "old-work", totalOccurrenceCount: 10, createdAt: learnedAt, updatedAt: learnedAt);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "old-work", translation: "answer", createdAt: learnedAt, updatedAt: learnedAt);
        var dueCardId = await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, CardState.Review,
            dueAtUtc: NowUtc.AddHours(-1), createdAtUtc: learnedAt, updatedAtUtc: learnedAt, id: 501);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.TermToMeaning, CardState.New,
            dueAtUtc: NowUtc, createdAtUtc: learnedAt, updatedAtUtc: learnedAt, id: 502);
        var pastSessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: learnedAt, updatedAtUtc: learnedAt, completedAtUtc: learnedAt);
        await fixture.InsertReviewAsync(
            dueCardId, pastSessionId, ReviewRating.Good, reviewedAtUtc: learnedAt,
            dueAtUtc: NowUtc.AddHours(-1));
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var service = CreateService(
            fixture,
            new FakeClock(NowUtc),
            new TestAppSettingsService { PreparationLimit = 1 });
        var readiness = await service.GetPreparationReadinessAsync();

        Assert.IsFalse(readiness.ShouldTransitionToLearning);
        Assert.AreEqual(1, readiness.RemainingFreshWordDemand);
        Assert.AreEqual(0, readiness.EligibleFreshWordCount);

        var load = await service.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants"));
        Assert.AreEqual(2, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards"));
    }

    [TestMethod]
    public async Task Readiness_ActiveCarryOverConsumesCapacityBeforeCardBackedFreshCandidates()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var carryOverWordId = await SeedNewCardAsync(fixture, 601, "carry-over", frequency: 1000);
        var cardlessWordId = await fixture.InsertWordAsync(
            "cardless",
            status: WordStatus.UnknownBacklog,
            preparationState: PreparationState.Unprepared,
            totalOccurrenceCount: 100,
            createdAt: NowUtc,
            updatedAt: NowUtc);
        var freshWordIds = new List<int>();
        for (var index = 1; index <= 4; index++)
        {
            freshWordIds.Add(await SeedNewCardAsync(
                fixture, 610 + index, $"fresh-{index}", frequency: 10 - index));
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var settings = new TestAppSettingsService { PreparationLimit = 1 };
        var clock = new FakeClock(NowUtc);
        var service = CreateService(fixture, clock, settings);
        var day1 = await service.GetOrStartAsync();
        Assert.IsNotNull(day1.Card);
        Assert.AreEqual(carryOverWordId, day1.Card.WordId);

        clock.UtcNow = NowUtc.AddDays(1);
        settings.PreparationLimit = 5;
        var readiness = await service.GetPreparationReadinessAsync();

        Assert.IsTrue(readiness.ShouldTransitionToLearning);
        Assert.AreEqual(4, readiness.RemainingFreshWordDemand);
        Assert.AreEqual(4, readiness.EligibleFreshWordCount);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 2"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessions WHERE Status = ?", (int)LearningSessionStatus.Active));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards"));

        await service.GetOrStartAsync();
        var day2GrantedWordIds = (await fixture.Connection.QueryAsync<GrantedWordRow>(
                "SELECT WordId FROM LearningDayGrants WHERE DayOrdinal = 2 ORDER BY SlotOrdinal"))
            .Select(row => row.WordId)
            .ToList();
        Assert.HasCount(5, day2GrantedWordIds);
        Assert.IsTrue(day2GrantedWordIds.Contains(carryOverWordId));
        Assert.IsFalse(day2GrantedWordIds.Contains(cardlessWordId));
        foreach (var freshWordId in freshWordIds)
        {
            Assert.IsTrue(day2GrantedWordIds.Contains(freshWordId));
        }
    }

    [TestMethod]
    public async Task Readiness_MultipleDirectionsForOneWordConsumeOneSlot()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync(
            "bidirectional", totalOccurrenceCount: 10, createdAt: NowUtc, updatedAt: NowUtc);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bidirectional", translation: "answer", createdAt: NowUtc, updatedAt: NowUtc);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.TermToMeaning, CardState.New,
            dueAtUtc: NowUtc, createdAtUtc: NowUtc, updatedAtUtc: NowUtc, id: 701);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, CardState.New,
            dueAtUtc: NowUtc, createdAtUtc: NowUtc, updatedAtUtc: NowUtc, id: 702);
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var settings = new TestAppSettingsService { PreparationLimit = 2 };
        var service = CreateService(fixture, new FakeClock(NowUtc), settings);

        var demandTwo = await service.GetPreparationReadinessAsync();
        Assert.IsFalse(demandTwo.ShouldTransitionToLearning);
        Assert.AreEqual(1, demandTwo.EligibleFreshWordCount);

        settings.PreparationLimit = 1;
        var demandOne = await service.GetPreparationReadinessAsync();
        Assert.IsTrue(demandOne.ShouldTransitionToLearning);
        Assert.AreEqual(1, demandOne.EligibleFreshWordCount);
        await AssertNoAdmissionSideEffectsAsync(fixture);
    }

    [TestMethod]
    public async Task Readiness_RecreatedServiceReturnsSameDecisionWithoutAdmissionSideEffects()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        for (var index = 1; index <= 5; index++)
        {
            await SeedNewCardAsync(fixture, 800 + index, $"eligible-{index}", frequency: index);
        }
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);

        var settings = new TestAppSettingsService { PreparationLimit = 5 };
        var clock = new FakeClock(NowUtc);
        var first = await CreateService(fixture, clock, settings).GetPreparationReadinessAsync();
        var second = await CreateService(fixture, clock, settings).GetPreparationReadinessAsync();

        Assert.AreEqual(first, second);
        Assert.IsTrue(second.ShouldTransitionToLearning);
        await AssertNoAdmissionSideEffectsAsync(fixture);
    }

    [TestMethod]
    public async Task Readiness_InvalidEligibleCardGraphFailsClosedWithoutAdmissionSideEffects()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedNewCardAsync(fixture, 901, "invalid-graph", frequency: 10);
        await SeedNewCardAsync(fixture, 902, "other-word", frequency: 1);
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        await fixture.Connection.ExecuteAsync(
            """
            UPDATE LearningCards
            SET PreferredMeaningId = (SELECT PreferredMeaningId FROM LearningCards WHERE Id = 902)
            WHERE Id = 901
            """);

        await Assert.ThrowsExactlyAsync<LearningSchemaCapabilityException>(() =>
            CreateService(
                    fixture,
                    new FakeClock(NowUtc),
                    new TestAppSettingsService { PreparationLimit = 1 })
                .GetPreparationReadinessAsync());

        await AssertNoAdmissionSideEffectsAsync(fixture);
    }

    private static async Task AssertNoAdmissionSideEffectsAsync(Schema7Fixture fixture)
    {
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards"));
    }

    private static LearningService CreateService(
        Schema7Fixture fixture,
        IClock clock,
        IAppSettingsService appSettings) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        clock,
        appSettings,
        timezoneResolver: new LearningTimezoneResolver());

    private static async Task<int> SeedNewCardAsync(
        Schema7Fixture fixture,
        int cardId,
        string term,
        int frequency)
    {
        var wordId = await fixture.InsertWordAsync(
            term, totalOccurrenceCount: frequency, createdAt: NowUtc.AddHours(-1), updatedAt: NowUtc.AddHours(-1));
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: term, translation: $"answer-{term}", createdAt: NowUtc, updatedAt: NowUtc);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, CardState.New, dueAtUtc: NowUtc,
            createdAtUtc: NowUtc, updatedAtUtc: NowUtc, id: cardId);
        return wordId;
    }

    private sealed class GrantedWordRow
    {
        public int WordId { get; set; }
    }

    private sealed class GrantSnapshot
    {
        public int WordId { get; set; }
        public int SlotOrdinal { get; set; }
        public DateTime GrantedAtUtc { get; set; }
    }

    private sealed class TestAppSettingsService : IAppSettingsService
    {
        public int PreparationLimit { get; set; } = 10;
        public IReadOnlyList<int> SupportedPreparationLimits => PreparationLimitPolicy.Presets;
        public CardDirectionPreference CardDirection { get; set; } = CardDirectionPreference.Both;
        public LearningMode LearningMode { get; set; } = LearningMode.Automatic;
        public bool HasOnlineLookupConsent { get; set; }
        public bool EnhancedTermRecognitionEnabled { get; set; }
        public LearningTimezoneMode LearningTimezoneMode { get; set; } = LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId { get; set; }
        public int LearningDayCutoffMinutes { get; set; }

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
