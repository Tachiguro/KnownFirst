using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Builds physically valid Schema-9 databases carrying legacy learning-workflow history, for the
/// KF-BACKUP-005A Schema-10 stable-identity tests. Every fixture starts from
/// <see cref="Schema8BackupFixtureBuilders.CreateSlice4BoundarySchema8FixtureAsync"/>, whose Sense /
/// AnswerVariant / assignment StableIds are fixed constants — so two independently built fixtures
/// describe the <em>same</em> semantic content and therefore the same content-derived card identity,
/// exactly as two installations that exchanged that history through a real portable archive would.
///
/// <para>The learning rows themselves are written with direct SQL against the Schema-9 physical shape.
/// This is legacy data that predates Schema 10 by construction: it is the input a Schema-9 -&gt; Schema-10
/// migration must bootstrap, so it deliberately cannot be produced through a Schema-10 code path.</para>
/// </summary>
internal static class Schema10LegacyLearningFixtures
{
    public static readonly DateTime SessionStartedAtUtc = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime SessionCompletedAtUtc = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>A Schema-9 fixture holding exactly one <em>Completed</em> learning session over the
    /// fixture's single card, with one completed queue row.</summary>
    public static async Task<Schema7Fixture> CreateCompletedSessionSchema9FixtureAsync(int localIdOffset = 0)
    {
        var fixture = await CreateSchema9BaseAsync(localIdOffset);
        var cardId = await SingleCardIdAsync(fixture);

        var sessionId = await InsertSessionAsync(
            fixture,
            LearningSessionStatus.Completed,
            totalCards: 1,
            completedCards: 1,
            startedAtUtc: SessionStartedAtUtc,
            completedAtUtc: SessionCompletedAtUtc,
            goodCount: 1);

        await InsertQueueRowAsync(
            fixture, sessionId, cardId, queueOrder: 0, isAgainRepeat: false, isCompleted: true,
            rating: ReviewRating.Good, completedAtUtc: SessionCompletedAtUtc);

        return fixture;
    }

    /// <summary>A Schema-9 fixture holding two <em>distinct</em> Completed sessions that deliberately share
    /// one <c>StartedAtUtc</c> instant. Nothing in the physical Schema-9 shape forbids this: the
    /// <c>LearningSessions</c> table has no index and no uniqueness constraint on <c>StartedAtUtc</c>.</summary>
    public static async Task<Schema7Fixture> CreateTwoCompletedSessionsSharingStartedAtUtcAsync()
    {
        var fixture = await CreateSchema9BaseAsync(localIdOffset: 0);
        var cardId = await SingleCardIdAsync(fixture);

        var firstSessionId = await InsertSessionAsync(
            fixture, LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: SessionStartedAtUtc, completedAtUtc: SessionCompletedAtUtc, goodCount: 1);
        await InsertQueueRowAsync(
            fixture, firstSessionId, cardId, queueOrder: 0, isAgainRepeat: false, isCompleted: true,
            rating: ReviewRating.Good, completedAtUtc: SessionCompletedAtUtc);

        // Same StartedAtUtc, genuinely different history: a later completion instant and a different rating.
        var secondSessionId = await InsertSessionAsync(
            fixture, LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
            startedAtUtc: SessionStartedAtUtc, completedAtUtc: SessionCompletedAtUtc.AddHours(4), hardCount: 1);
        await InsertQueueRowAsync(
            fixture, secondSessionId, cardId, queueOrder: 0, isAgainRepeat: false, isCompleted: true,
            rating: ReviewRating.Hard, completedAtUtc: SessionCompletedAtUtc.AddHours(4));

        return fixture;
    }

    /// <summary>A Schema-9 fixture holding exactly one <em>Active</em> learning session with two incomplete
    /// queue rows over the fixture's single card — the shape an interrupted session leaves behind.</summary>
    public static async Task<Schema7Fixture> CreateActiveSessionSchema9FixtureAsync()
    {
        var fixture = await CreateSchema9BaseAsync(localIdOffset: 0);
        var cardId = await SingleCardIdAsync(fixture);

        var sessionId = await InsertSessionAsync(
            fixture, LearningSessionStatus.Active, totalCards: 2, completedCards: 0,
            startedAtUtc: SessionStartedAtUtc, completedAtUtc: null);

        await InsertQueueRowAsync(
            fixture, sessionId, cardId, queueOrder: 0, isAgainRepeat: false, isCompleted: false,
            rating: null, completedAtUtc: null);
        await InsertQueueRowAsync(
            fixture, sessionId, cardId, queueOrder: 1, isAgainRepeat: false, isCompleted: false,
            rating: null, completedAtUtc: null);

        return fixture;
    }

    public static async Task<int> SingleCardIdAsync(Schema7Fixture fixture) =>
        await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM LearningCards ORDER BY Id LIMIT 1");

    public static async Task<int> InsertSessionAsync(
        Schema7Fixture fixture,
        LearningSessionStatus status,
        int totalCards,
        int completedCards,
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        int againCount = 0,
        int hardCount = 0,
        int goodCount = 0,
        int easyCount = 0)
    {
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningSessions
                (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                 StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (int)status, totalCards, completedCards, againCount, hardCount, goodCount, easyCount,
            startedAtUtc, completedAtUtc ?? startedAtUtc, completedAtUtc);
        return await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    public static async Task<int> InsertQueueRowAsync(
        Schema7Fixture fixture,
        int sessionId,
        int cardId,
        int queueOrder,
        bool isAgainRepeat,
        bool isCompleted,
        ReviewRating? rating,
        DateTime? completedAtUtc)
    {
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningSessionCards
                (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                 SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
            VALUES (?, ?, ?, 1, ?, 0, 0, 0, ?, ?, ?, NULL)
            """,
            sessionId, cardId, queueOrder, isAgainRepeat, isCompleted,
            rating is null ? null : (int?)rating.Value, completedAtUtc);
        return await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Slice-4 boundary content migrated up to Schema 9. <paramref name="localIdOffset"/> burns that many
    /// <c>LearningSessions</c> and <c>LearningSessionCards</c> rowids before any real history is written, so
    /// two fixtures holding identical semantic content genuinely disagree about local numeric ids — which is
    /// what makes a cross-installation determinism assertion meaningful rather than accidental.
    /// </summary>
    private static async Task<Schema7Fixture> CreateSchema9BaseAsync(int localIdOffset)
    {
        var fixture = await Schema8BackupFixtureBuilders.CreateSlice4BoundarySchema8FixtureAsync();
        var result = await Schema9DormantMigration.ApplyAsync(fixture.Connection);
        if (result.Outcome is not (Schema9MigrationOutcome.Migrated or Schema9MigrationOutcome.AlreadyApplied))
        {
            throw new InvalidOperationException("Expected the fixture to reach Schema 9.");
        }

        var cardId = await SingleCardIdAsync(fixture);
        for (var burn = 0; burn < localIdOffset; burn++)
        {
            var throwawaySessionId = await InsertSessionAsync(
                fixture, LearningSessionStatus.Completed, totalCards: 1, completedCards: 1,
                startedAtUtc: SessionStartedAtUtc.AddDays(-100 - burn),
                completedAtUtc: SessionStartedAtUtc.AddDays(-100 - burn));
            await InsertQueueRowAsync(
                fixture, throwawaySessionId, cardId, queueOrder: 0, isAgainRepeat: false, isCompleted: true,
                rating: ReviewRating.Good, completedAtUtc: SessionStartedAtUtc.AddDays(-100 - burn));

            await fixture.Connection.ExecuteAsync("DELETE FROM LearningSessionCards WHERE SessionId = ?", throwawaySessionId);
            await fixture.Connection.ExecuteAsync("DELETE FROM LearningSessions WHERE Id = ?", throwawaySessionId);
        }

        return fixture;
    }

    // ---- Assertion helpers over the Schema-10 identity columns ----

    public static Task<List<StableIdRow>> LoadSessionStableIdsAsync(Schema7Fixture fixture) =>
        fixture.Connection.QueryAsync<StableIdRow>(
            "SELECT Id, StableId FROM LearningSessions ORDER BY StartedAtUtc, Id");

    public static Task<List<QueueStableIdRow>> LoadQueueStableIdsAsync(Schema7Fixture fixture) =>
        fixture.Connection.QueryAsync<QueueStableIdRow>(
            "SELECT Id, SessionId, QueueOrder, IsAgainRepeat, StableId FROM LearningSessionCards ORDER BY SessionId, QueueOrder, Id");

    public static bool IsAcceptedStableIdShape(string? value) =>
        value is not null
        && (value.Length == 32 || value.Length == 64)
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public sealed class StableIdRow
    {
        public int Id { get; set; }

        public string? StableId { get; set; }
    }

    public sealed class QueueStableIdRow
    {
        public int Id { get; set; }

        public int SessionId { get; set; }

        public int QueueOrder { get; set; }

        public bool IsAgainRepeat { get; set; }

        public string? StableId { get; set; }
    }
}
