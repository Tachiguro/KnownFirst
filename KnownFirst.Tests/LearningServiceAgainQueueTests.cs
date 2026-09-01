using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;
using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServiceAgainQueueTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task RateAsync_Again_AppendsExactlyOneTailRepeatAndPresentsItBeforeSchedulerDue()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new FakeClock(Now);
        var service = CreateService(fixture, clock);
        var original = (await service.GetOrStartAsync()).Card!;
        var originalRow = (await ReadQueueAsync(fixture)).Single();

        await service.RevealAnswerAsync(original.QueueItemId);
        var result = await service.RateAsync(original.QueueItemId, ReviewRating.Again);

        var rows = await ReadQueueAsync(fixture);
        var repeat = rows.Single(row => row.IsAgainRepeat);
        var dueAtUtc = await fixture.Connection.ExecuteScalarAsync<DateTime>(
            "SELECT DueAtUtc FROM LearningCards WHERE Id = ?", original.CardId);
        var session = await ReadSessionAsync(fixture, original.SessionId);

        Assert.HasCount(2, rows);
        Assert.IsTrue(rows[0].IsCompleted);
        Assert.AreEqual(originalRow.Id, rows[0].Id);
        Assert.AreEqual(originalRow.CardId, repeat.CardId);
        Assert.AreEqual(originalRow.TargetAnswerVariantId, repeat.TargetAnswerVariantId);
        Assert.AreEqual(1, repeat.QueueOrder);
        Assert.IsGreaterThan(Now, dueAtUtc);
        Assert.IsNotNull(result.Card);
        Assert.AreEqual(repeat.Id, result.Card.QueueItemId);
        Assert.IsTrue(result.Card.IsAgainRepeat);
        Assert.AreEqual(1, result.Card.CompletedCards);
        Assert.AreEqual(2, result.Card.TotalCards);
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
        Assert.AreEqual(2, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.AgainCount);
    }

    [TestMethod]
    public async Task RateAsync_AgainOnRepeat_AppendsOneNewTailRepeat()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture, new FakeClock(Now));
        var original = (await service.GetOrStartAsync()).Card!;

        await service.RevealAnswerAsync(original.QueueItemId);
        await service.RateAsync(original.QueueItemId, ReviewRating.Again);
        var firstRepeat = (await ReadQueueAsync(fixture)).Single(row => row.IsAgainRepeat);

        await service.RevealAnswerAsync(firstRepeat.Id);
        var result = await service.RateAsync(firstRepeat.Id, ReviewRating.Again);

        var rows = await ReadQueueAsync(fixture);
        var nextRepeat = rows.Single(row => row.QueueOrder == 2);
        var session = await ReadSessionAsync(fixture, original.SessionId);

        Assert.HasCount(3, rows);
        Assert.IsTrue(rows.Single(row => row.Id == firstRepeat.Id).IsCompleted);
        Assert.IsTrue(nextRepeat.IsAgainRepeat);
        Assert.AreEqual(firstRepeat.CardId, nextRepeat.CardId);
        Assert.AreEqual(firstRepeat.TargetAnswerVariantId, nextRepeat.TargetAnswerVariantId);
        Assert.IsNotNull(result.Card);
        Assert.AreEqual(nextRepeat.Id, result.Card.QueueItemId);
        Assert.AreEqual(3, session.TotalCards);
        Assert.AreEqual(2, session.CompletedCards);
        Assert.AreEqual(2, session.AgainCount);
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
    }

    [TestMethod]
    public async Task RateAsync_RepeatedAgain_ChainsOneRowPerActionWithoutCap()
    {
        const int actionCount = 10;
        await using var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture, new FakeClock(Now));
        var current = (await service.GetOrStartAsync()).Card!;
        var sessionId = current.SessionId;

        for (var action = 1; action <= actionCount; action++)
        {
            await service.RevealAnswerAsync(current.QueueItemId);
            var result = await service.RateAsync(current.QueueItemId, ReviewRating.Again);
            var rows = await ReadQueueAsync(fixture);

            Assert.HasCount(action + 1, rows, $"Action {action} must append exactly one queue row.");
            CollectionAssert.AreEqual(
                Enumerable.Range(0, action + 1).ToArray(),
                rows.Select(row => row.QueueOrder).ToArray(),
                $"Queue ordering must remain contiguous after action {action}.");
            Assert.AreEqual(action, rows.Count(row => row.IsCompleted));
            Assert.AreEqual(1, rows.Count(row => !row.IsCompleted));
            Assert.IsNotNull(result.Card);
            current = result.Card;
            Assert.AreEqual(rows[^1].Id, current.QueueItemId);
            Assert.IsTrue(current.IsAgainRepeat);
        }

        var finalRows = await ReadQueueAsync(fixture);
        var session = await ReadSessionAsync(fixture, sessionId);
        Assert.HasCount(actionCount, finalRows.Where(row => row.IsAgainRepeat).ToArray());
        Assert.AreEqual(actionCount, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningReviews WHERE SessionId = ?", sessionId));
        Assert.AreEqual(actionCount + 1, session.TotalCards);
        Assert.AreEqual(actionCount, session.CompletedCards);
        Assert.AreEqual(actionCount, session.AgainCount);
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
    }

    [TestMethod]
    public async Task GetOrStart_RecreatedService_ResumesPendingAgainRepeatBeforeSchedulerDue()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new FakeClock(Now);
        var firstService = CreateService(fixture, clock);
        var original = (await firstService.GetOrStartAsync()).Card!;

        await firstService.RevealAnswerAsync(original.QueueItemId);
        await firstService.RateAsync(original.QueueItemId, ReviewRating.Again);
        var repeat = (await ReadQueueAsync(fixture)).Single(row => row.IsAgainRepeat);
        var persistedDueAtUtc = await fixture.Connection.ExecuteScalarAsync<DateTime>(
            "SELECT DueAtUtc FROM LearningCards WHERE Id = ?", original.CardId);

        var recreatedService = CreateService(fixture, clock);
        var resumed = await recreatedService.GetOrStartAsync();

        Assert.IsGreaterThan(clock.UtcNow, persistedDueAtUtc);
        Assert.IsNotNull(resumed.Card);
        Assert.AreEqual(repeat.Id, resumed.Card.QueueItemId);
        Assert.IsTrue(resumed.Card.IsAgainRepeat);
        Assert.AreEqual(original.SessionId, resumed.Card.SessionId);
    }

    [TestMethod]
    public async Task RateAsync_Again_DoesNotConsumeDailyGrantChangeTargetOrOpenReplacementSlot()
    {
        await using var fixture = await CreateFixtureAsync(cardCount: 6);
        var settings = new TestAppSettingsService { PreparationLimit = 5 };
        var service = CreateService(fixture, new FakeClock(Now), settings);
        var original = (await service.GetOrStartAsync()).Card!;
        var initialRows = await ReadQueueAsync(fixture);

        Assert.HasCount(5, initialRows);
        Assert.AreEqual(5, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1"));

        await service.RevealAnswerAsync(original.QueueItemId);
        var result = await service.RateAsync(original.QueueItemId, ReviewRating.Again);

        var rows = await ReadQueueAsync(fixture);
        var repeat = rows.Single(row => row.IsAgainRepeat);
        Assert.HasCount(6, rows);
        Assert.AreEqual(5, repeat.QueueOrder);
        Assert.AreEqual(5, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningDayGrants WHERE DayOrdinal = 1"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE CardId = 45"));
        Assert.AreEqual(5, settings.PreparationLimit);
        Assert.IsNotNull(result.Card);
        Assert.AreEqual(initialRows[1].Id, result.Card.QueueItemId,
            "Earlier incomplete work must remain ahead of the tail repeat.");
    }

    [TestMethod]
    public async Task RateAsync_DuplicateCompletedQueueSubmission_CreatesNoAdditionalReviewOrRepeat()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture, new FakeClock(Now));
        var original = (await service.GetOrStartAsync()).Card!;

        await service.RevealAnswerAsync(original.QueueItemId);
        await service.RateAsync(original.QueueItemId, ReviewRating.Again);
        var before = await fixture.CapturePersistentStateAsync();

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(original.QueueItemId, ReviewRating.Again));
        var after = await fixture.CapturePersistentStateAsync();

        Assert.AreEqual(Schema8LearningDataErrorCode.DuplicateSubmission, exception.Code);
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsAgainRepeat = 1"));
    }

    [TestMethod]
    public async Task RateAsync_Again_RollsBackBeforeRepeatInsertion()
    {
        await AssertRatingRollbackAsync(Schema8LearningMutationCheckpoint.AfterReviewTargetMatchedUpdate);
    }

    [TestMethod]
    public async Task RateAsync_Again_RollsBackAfterRepeatAndCounterMutation()
    {
        await AssertRatingRollbackAsync(Schema8LearningMutationCheckpoint.DuringProgressReplacement);
    }

    [TestMethod]
    public async Task GetOrStart_FutureDueNonRepeatCardRemainsSuppressed()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new FakeClock(Now);
        var initial = await CreateService(fixture, clock).GetOrStartAsync();
        Assert.IsNotNull(initial.Card);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET State = ?, DueAtUtc = ? WHERE Id = ?",
            (int)CardState.Review, Now.AddHours(1), initial.Card.CardId);

        var result = await CreateService(fixture, clock).GetOrStartAsync();

        Assert.IsNull(result.Card);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT IsAgainRepeat FROM LearningSessionCards"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT IsCompleted FROM LearningSessionCards"));
    }

    private static async Task AssertRatingRollbackAsync(Schema8LearningMutationCheckpoint checkpoint)
    {
        await using var fixture = await CreateFixtureAsync();
        var service = CreateService(
            fixture,
            new FakeClock(Now),
            failureInjector: new ThrowAtCheckpointInjector(checkpoint));
        var original = (await service.GetOrStartAsync()).Card!;
        await service.RevealAnswerAsync(original.QueueItemId);
        var before = await fixture.CapturePersistentStateAsync();

        await Assert.ThrowsExactlyAsync<InjectedRatingFailureException>(
            () => service.RateAsync(original.QueueItemId, ReviewRating.Again));
        var after = await fixture.CapturePersistentStateAsync();

        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE IsAgainRepeat = 1"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT CompletedCards FROM LearningSessions WHERE Id = ?", original.SessionId));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT TotalCards FROM LearningSessions WHERE Id = ?", original.SessionId));
        Assert.AreEqual((int)CardState.New, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT State FROM LearningCards WHERE Id = ?", original.CardId));
    }

    private static async Task<Schema7Fixture> CreateFixtureAsync(int cardCount = 1)
    {
        var fixture = await Schema7Fixture.CreateAsync();
        for (var index = 0; index < cardCount; index++)
        {
            var wordId = await fixture.InsertWordAsync(
                $"again-{index}", totalOccurrenceCount: cardCount - index,
                createdAt: Now.AddMinutes(index), updatedAt: Now.AddMinutes(index));
            var meaningId = await fixture.InsertMeaningAsync(
                wordId, displayTerm: $"again-{index}", translation: $"wieder-{index}",
                createdAt: Now.AddMinutes(index), updatedAt: Now.AddMinutes(index));
            await fixture.InsertCardAsync(
                wordId, meaningId, CardDirection.MeaningToTerm, CardState.New,
                dueAtUtc: Now, createdAtUtc: Now.AddMinutes(index), updatedAtUtc: Now.AddMinutes(index),
                id: 40 + index);
        }

        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        return fixture;
    }

    private static LearningService CreateService(
        Schema7Fixture fixture,
        FakeClock clock,
        IAppSettingsService? appSettings = null,
        ISchema8LearningFailureInjector? failureInjector = null) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        clock,
        appSettings,
        failureInjector,
        new LearningTimezoneResolver());

    private static Task<List<QueueRow>> ReadQueueAsync(Schema7Fixture fixture) =>
        fixture.Connection.QueryAsync<QueueRow>(
            "SELECT Id, SessionId, CardId, QueueOrder, IsAgainRepeat, IsCompleted, TargetAnswerVariantId, StableId FROM LearningSessionCards ORDER BY QueueOrder, Id");

    private static async Task<SessionRow> ReadSessionAsync(Schema7Fixture fixture, int sessionId) =>
        (await fixture.Connection.QueryAsync<SessionRow>(
            "SELECT Id, Status, TotalCards, CompletedCards, AgainCount FROM LearningSessions WHERE Id = ?", sessionId)).Single();

    private sealed class QueueRow
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int CardId { get; set; }
        public int QueueOrder { get; set; }
        public bool IsAgainRepeat { get; set; }
        public bool IsCompleted { get; set; }
        public int? TargetAnswerVariantId { get; set; }
        public string StableId { get; set; } = string.Empty;
    }

    private sealed class SessionRow
    {
        public int Id { get; set; }
        public LearningSessionStatus Status { get; set; }
        public int TotalCards { get; set; }
        public int CompletedCards { get; set; }
        public int AgainCount { get; set; }
    }

    private sealed class ThrowAtCheckpointInjector(Schema8LearningMutationCheckpoint checkpoint)
        : ISchema8LearningFailureInjector
    {
        public void AtCheckpoint(Schema8LearningMutationCheckpoint current)
        {
            if (current == checkpoint)
            {
                throw new InjectedRatingFailureException();
            }
        }
    }

    private sealed class InjectedRatingFailureException : Exception
    {
    }

    private sealed class TestAppSettingsService : IAppSettingsService
    {
        public int PreparationLimit { get; set; } = 5;
        public IReadOnlyList<int> SupportedPreparationLimits => [1, 5, 10, 20, 30, 50];
        public CardDirectionPreference CardDirection { get; set; } = CardDirectionPreference.Both;
        public LearningMode LearningMode { get; set; } = LearningMode.Reading;
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
        public void Reset() { }
    }
}
