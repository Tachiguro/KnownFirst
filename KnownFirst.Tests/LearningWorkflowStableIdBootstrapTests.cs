using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Models;

namespace KnownFirst.Tests;

/// <summary>
/// KF-BACKUP-005A: the legacy stable-identity bootstrap, split by what pre-Schema-10 data could actually
/// be transported.
///
/// <para><b>Completed</b> learning sessions have been portable since Beta 10, so the same completed
/// history can genuinely exist on two installations and must bootstrap deterministically to one identity.
/// <b>Active</b> sessions were never portable through any supported path, so they simply receive a fresh
/// immutable identity once, at migration, and keep it forever.</para>
///
/// <para>Every test asserts <c>PRAGMA user_version = 10</c> before reading a Schema-10-only column, so the
/// pre-implementation failure is behavioural rather than a missing-column SQL error.</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LearningWorkflowStableIdBootstrapTests
{
    private const int Schema10 = 10;

    [TestMethod]
    public async Task EquivalentCompletedHistories_OnTwoInstallations_BootstrapToIdenticalStableIds()
    {
        // Two installations holding the same completed learning history under different local row ids —
        // the state two devices reach after exchanging that history through a real portable archive.
        await using var installationA = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await using var installationB = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync(localIdOffset: 3);

        await HistoricalMigrationFixture.UpgradeToSchema10Async(installationA.Connection);
        await HistoricalMigrationFixture.UpgradeToSchema10Async(installationB.Connection);

        Assert.AreEqual(10, await installationA.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(10, await installationB.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var sessionsA = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(installationA);
        var sessionsB = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(installationB);
        Assert.HasCount(1, sessionsA);
        Assert.HasCount(1, sessionsB);

        Assert.AreNotEqual(
            sessionsA[0].Id,
            sessionsB[0].Id,
            "The fixtures must genuinely disagree about local row ids, or the determinism assertion proves nothing.");
        Assert.AreEqual(
            sessionsA[0].StableId,
            sessionsB[0].StableId,
            "Equivalent historically portable Completed sessions must bootstrap to one deterministic identity.");

        var queueA = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(installationA);
        var queueB = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(installationB);
        Assert.HasCount(1, queueA);
        Assert.HasCount(1, queueB);
        Assert.AreEqual(
            queueA[0].StableId,
            queueB[0].StableId,
            "Queue identity cascades from the deterministic completed-session identity.");
    }

    [TestMethod]
    public async Task CompletedBootstrap_ProducesTheDeterministicSha256Shape()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await HistoricalMigrationFixture.UpgradeToSchema10Async(fixture.Connection);
        Assert.AreEqual(10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var session = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture)).Single();
        Assert.AreEqual(
            64,
            session.StableId?.Length,
            "A deterministic legacy Completed bootstrap is a 64-character lowercase SHA-256 hex digest.");
        Assert.IsTrue(Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(session.StableId));
    }

    [TestMethod]
    public async Task DistinctCompletedSessionsSharingStartedAtUtc_RemainDistinct()
    {
        // Nothing in the physical Schema-9 shape forbids two sessions sharing one StartedAtUtc instant:
        // LearningSessions carries no index and no uniqueness constraint on that column. A bootstrap that
        // collapsed them would be a false merge, which is worse than a conservative split.
        await using var fixture = await Schema10LegacyLearningFixtures.CreateTwoCompletedSessionsSharingStartedAtUtcAsync();

        var sharedTimestampCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessions WHERE StartedAtUtc = (SELECT StartedAtUtc FROM LearningSessions ORDER BY Id LIMIT 1)");
        Assert.AreEqual(2, sharedTimestampCount, "The fixture must genuinely contain two sessions at one StartedAtUtc.");

        await HistoricalMigrationFixture.UpgradeToSchema10Async(fixture.Connection);
        Assert.AreEqual(10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var sessions = await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture);
        Assert.HasCount(2, sessions);
        Assert.AreNotEqual(
            sessions[0].StableId,
            sessions[1].StableId,
            "Two distinct Completed sessions must never collapse onto one identity merely because StartedAtUtc matches.");
    }

    [TestMethod]
    public async Task LegacyActiveSession_ReceivesOneGuidFormIdentity_AndKeepsItThroughCompletion()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();

        await HistoricalMigrationFixture.UpgradeToSchema10Async(fixture.Connection);
        Assert.AreEqual(10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var assigned = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture)).Single();
        Assert.AreEqual(
            32,
            assigned.StableId?.Length,
            "A legacy Active session had no portable identity to reconstruct, so migration assigns a fresh GUID-form id.");
        Assert.IsTrue(Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(assigned.StableId));

        // Advance the workflow exactly as ordinary use would: complete the queue rows, record ratings and
        // counters, and finish the session. None of that may disturb the identity.
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET IsCompleted = 1, Rating = ?, CompletedAtUtc = ? WHERE SessionId = ?",
            (int)ReviewRating.Good, Schema10LegacyLearningFixtures.SessionCompletedAtUtc, assigned.Id);
        await fixture.Connection.ExecuteAsync(
            """
            UPDATE LearningSessions
            SET Status = ?, CompletedCards = 2, GoodCount = 2, UpdatedAtUtc = ?, CompletedAtUtc = ?
            WHERE Id = ?
            """,
            (int)LearningSessionStatus.Completed,
            Schema10LegacyLearningFixtures.SessionCompletedAtUtc,
            Schema10LegacyLearningFixtures.SessionCompletedAtUtc,
            assigned.Id);

        var afterCompletion = (await Schema10LegacyLearningFixtures.LoadSessionStableIdsAsync(fixture)).Single();
        Assert.AreEqual(
            assigned.StableId,
            afterCompletion.StableId,
            "An assigned StableId must survive the Active -> Completed transition unchanged.");
    }

    [TestMethod]
    public async Task LegacyActiveQueueRows_KeepTheirIdentities_AsMutableFieldsAdvance()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();

        await HistoricalMigrationFixture.UpgradeToSchema10Async(fixture.Connection);
        Assert.AreEqual(10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var before = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);
        Assert.HasCount(2, before);
        Assert.AreEqual(
            2,
            before.Select(row => row.StableId).Distinct(StringComparer.Ordinal).Count(),
            "Two surviving queue rows must receive two distinct identities.");

        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1, IsCompleted = 1, Rating = ?, CompletedAtUtc = ?",
            (int)ReviewRating.Hard, Schema10LegacyLearningFixtures.SessionCompletedAtUtc);

        var after = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);
        CollectionAssert.AreEqual(
            before.Select(row => row.StableId).ToList(),
            after.Select(row => row.StableId).ToList(),
            "Queue identity must not depend on completion, rating, reveal or spelling state.");
    }

    [TestMethod]
    public async Task AgainRepeatQueueRow_ReceivesItsOwnIdentity_AndNeverCopiesItsSourceRow()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();
        await HistoricalMigrationFixture.UpgradeToSchema10Async(fixture.Connection);
        Assert.AreEqual(10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var before = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);
        var sourceRow = before[0];

        await fixture.Connection.RunInTransactionAsync(connection =>
            KnownFirst.Data.Schema8.Schema8LearningRepository.InsertAgainRepeatQueueRow(
                connection, sourceRow.Id, queueOrder: 7));

        var after = await Schema10LegacyLearningFixtures.LoadQueueStableIdsAsync(fixture);
        Assert.HasCount(before.Count + 1, after);

        var repeat = after.Single(row => row.QueueOrder == 7);
        Assert.IsTrue(
            Schema10LegacyLearningFixtures.IsAcceptedStableIdShape(repeat.StableId),
            "An Again repeat row must carry its own valid StableId.");
        Assert.AreNotEqual(
            sourceRow.StableId,
            repeat.StableId,
            "The INSERT ... SELECT Again-repeat path must never copy the source row's identity.");
        Assert.AreEqual(
            after.Count,
            after.Select(row => row.StableId).Distinct(StringComparer.Ordinal).Count(),
            "Queue identities must remain unique within the table.");
    }
}
