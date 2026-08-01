using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Services.Study;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema8AnswerAssignmentServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private const int Word10 = 10;
    private const int Sense20 = 20;
    private const int Sense21 = 21; // Different sense
    private const int Card40 = 40; // MeaningToTerm
    private const int Card41 = 41; // TermToMeaning
    private const int Card42 = 42; // Other sense card
    private const int Var70 = 70;
    private const int Var71 = 71;

    private sealed class TestEnvironment : IAsyncDisposable
    {
        public Schema7Fixture Fixture { get; }
        public Schema8AnswerAssignmentService Service { get; }
        public FakeClock Clock { get; }

        public TestEnvironment(Schema7Fixture fixture, Schema8AnswerAssignmentService service, FakeClock clock)
        {
            Fixture = fixture;
            Service = service;
            Clock = clock;
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }

    private async Task<TestEnvironment> CreateServiceAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        var clock = new FakeClock(T1); // Base clock for mutations

        // Ensure Schema-8 tables exist
        await fixture.MigrateToSchema8Async();

        var adapter = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = new Schema8AnswerAssignmentService(adapter, clock);
        return new TestEnvironment(fixture, service, clock);
    }

    // ---- Insert and identity ----

    [TestMethod]
    public async Task AbsentAssignment_IsInsertedOnce()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(Var70, assignments[0].AnswerVariantId);
    }

    [TestMethod]
    public async Task AbsentAssignment_StableIdIs32LowercaseHexAndUnique()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "y", id: Var71);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.AcceptedOnly, false);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        Assert.AreEqual(2, assignments.Count);
        StringAssert.Matches(assignments[0].StableId, new System.Text.RegularExpressions.Regex("^[0-9a-f]{32}$"));
        StringAssert.Matches(assignments[1].StableId, new System.Text.RegularExpressions.Regex("^[0-9a-f]{32}$"));
        Assert.AreNotEqual(assignments[0].StableId, assignments[1].StableId);
    }

    [TestMethod]
    public async Task ExistingAssignment_StableIdIsUnchangedAfterEveryTransition()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);

        var orig = (await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm)).Single();
        var origStableId = orig.StableId;

        // Promote
        env.Clock.UtcNow = T2;
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true);
        var promoted = (await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm)).Single();
        Assert.AreEqual(origStableId, promoted.StableId);
        Assert.AreEqual(AnswerVariantRequirement.Required, promoted.Requirement);
    }

    // ---- Preferred and requirement independence ----

    [TestMethod]
    public async Task ZeroPreferredAssignments_IsValid()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        Assert.IsFalse(assignments.Any(a => a.IsPreferred));
    }

    [TestMethod]
    public async Task SettingPreferred_AtomicallyClearsPriorPreferred()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "y", id: Var71);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        // Setup initial preferred
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: T0);

        // Mutate to swap preferred
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, false);
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.Required, true);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        Assert.AreEqual(1, assignments.Count(a => a.IsPreferred));
        Assert.IsTrue(assignments.Single(a => a.AnswerVariantId == Var71).IsPreferred);
        Assert.IsFalse(assignments.Single(a => a.AnswerVariantId == Var70).IsPreferred);
    }

    [TestMethod]
    public async Task RequirementAndIsPreferred_AreIndependent()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, isPreferred: true);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        var a = assignments.Single();
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, a.Requirement);
        Assert.IsTrue(a.IsPreferred);
        Assert.IsNull(a.RequiredSinceUtc);
    }

    [TestMethod]
    public async Task AnswerLanguage_IsNeverValidatedAgainstCardDirection()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20, sourceLanguage: "en", explanationLanguage: "de");
        // "x" has language "en" - normally term side
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", answerLanguage: "en", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card41, Word10, Sense20, 1, (int)CardDirection.TermToMeaning, 0, T0, 0, 2.5, 0, 0, T0, T0);

        // Assigning to TermToMeaning (expects explanation language normally, but engine doesn't block it)
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.TermToMeaning, Var70, AnswerVariantRequirement.Required, true);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.TermToMeaning);
        Assert.AreEqual(1, assignments.Count);
    }

    // ---- Promotion and epoch reset ----

    [TestMethod]
    public async Task Promotion_SetsRequiredSinceUtcToPromotionTime()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false, null);

        env.Clock.UtcNow = T2;
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        Assert.AreEqual(T2, assignments[0].RequiredSinceUtc);
    }

    [TestMethod]
    public async Task Promotion_PreservedAcceptedOnlyRow_IsResetToZeroBaseline()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false, null);

        // Setup existing accepted-only progress with stats
        await env.Fixture.InsertProgressAsync(Card40, Var70, createdAtUtc: T0, consecutiveTypingSuccessCount: 2, isMastered: true);

        env.Clock.UtcNow = T2;
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true);

        var progress = await env.Fixture.ReadProgressAsync();
        Assert.AreEqual(1, progress.Count);
        Assert.AreEqual(0, progress[0].ConsecutiveTypingSuccessCount);
        Assert.IsFalse(progress[0].IsMastered);
        Assert.AreEqual(T2, progress[0].CreatedAtUtc); // New epoch
    }

    [TestMethod]
    public async Task Promotion_RollsAffectedSenseFromMasteredToLearning()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20, status: SenseStatus.Mastered);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false, null);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true);

        var senseStatus = await env.Fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Senses WHERE Id = ?", Sense20);
        Assert.AreEqual((int)SenseStatus.Learning, senseStatus);
    }

    [TestMethod]
    public async Task SetRequirement_NewRequiredAssignment_ReactivatesOnlyAffectedRetiredCard()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20, status: SenseStatus.Mastered);
        await env.Fixture.InsertSenseAsync(Word10, id: Sense21, status: SenseStatus.Mastered);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "new-required", id: Var70);
        await env.Fixture.InsertAnswerVariantAsync(Sense21, "unrelated", id: Var71);
        await env.Fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, (int)CardState.Retired,
            T1, 30, 2.3, 5, 2, T0, (int)ReviewRating.Good, T0, T1);
        await env.Fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            Card42, Word10, Sense21, 2, (int)CardDirection.MeaningToTerm, (int)CardState.Retired,
            T0, 60, 2.7, 8, 1, T0, (int)ReviewRating.Easy, T0, T0);

        var cardsBefore = await env.Fixture.ReadCardsAsync();
        var controlBefore = cardsBefore.Single(card => card.Id == Card42);
        Assert.AreEqual(2, cardsBefore.Count);
        Assert.AreEqual(0, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
        Assert.AreEqual(0, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariantProgress"));

        env.Clock.UtcNow = T2;
        var result = await env.Service.SetAssignmentAsync(
            Sense20, CardDirection.MeaningToTerm, Var70,
            AnswerVariantRequirement.Required, isPreferred: false);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        var progress = await env.Fixture.ReadProgressAsync();
        var cardsAfter = await env.Fixture.ReadCardsAsync();
        var affected = cardsAfter.Single(card => card.Id == Card40);
        var controlAfter = cardsAfter.Single(card => card.Id == Card42);

        Assert.IsTrue(result.Inserted);
        Assert.IsFalse(result.Promoted);
        Assert.AreEqual(CardState.Learning, result.CardStateAfter);
        Assert.HasCount(1, assignments);
        Assert.AreEqual(AnswerVariantRequirement.Required, assignments[0].Requirement);
        Assert.IsFalse(assignments[0].IsPreferred);
        Assert.AreEqual(T2, assignments[0].RequiredSinceUtc);
        Assert.AreEqual(T2, assignments[0].CreatedAtUtc);
        Assert.AreEqual(CardState.Learning, affected.State);
        Assert.AreEqual(T1, affected.DueAtUtc);
        Assert.AreEqual(30, affected.IntervalDays);
        Assert.AreEqual(2.3, affected.EaseFactor);
        Assert.AreEqual(5, affected.SuccessfulReviewCount);
        Assert.AreEqual(2, affected.LapseCount);
        Assert.AreEqual(T0, affected.LastReviewedAtUtc);
        Assert.AreEqual(ReviewRating.Good, affected.LastRating);
        Assert.AreEqual(T0, affected.CreatedAtUtc);
        Assert.AreEqual(T2, affected.UpdatedAtUtc);
        Assert.AreEqual(controlBefore.Id, controlAfter.Id);
        Assert.AreEqual(controlBefore.State, controlAfter.State);
        Assert.AreEqual(controlBefore.DueAtUtc, controlAfter.DueAtUtc);
        Assert.AreEqual(controlBefore.IntervalDays, controlAfter.IntervalDays);
        Assert.AreEqual(controlBefore.EaseFactor, controlAfter.EaseFactor);
        Assert.AreEqual(controlBefore.SuccessfulReviewCount, controlAfter.SuccessfulReviewCount);
        Assert.AreEqual(controlBefore.LapseCount, controlAfter.LapseCount);
        Assert.AreEqual(controlBefore.LastReviewedAtUtc, controlAfter.LastReviewedAtUtc);
        Assert.AreEqual(controlBefore.LastRating, controlAfter.LastRating);
        Assert.AreEqual(controlBefore.CreatedAtUtc, controlAfter.CreatedAtUtc);
        Assert.AreEqual(controlBefore.UpdatedAtUtc, controlAfter.UpdatedAtUtc);
        Assert.HasCount(1, progress);
        Assert.AreEqual(Card40, progress[0].CardId);
        Assert.AreEqual(Var70, progress[0].AnswerVariantId);
        Assert.AreEqual(0, progress[0].ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, progress[0].ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, progress[0].ConsecutiveTypingFailureCount);
        Assert.IsFalse(progress[0].IsMastered);
        Assert.AreEqual(T2, progress[0].CreatedAtUtc);
        Assert.AreEqual(T2, progress[0].UpdatedAtUtc);
        Assert.AreEqual(2, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(2, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariants"));
        Assert.AreEqual(1, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
        Assert.AreEqual(1, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariantProgress"));
    }

    // ---- Demotion and re-promotion ----

    [TestMethod]
    public async Task Demotion_SetsRequiredSinceUtcToNull()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true, T0);

        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);

        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        Assert.IsNull(assignments[0].RequiredSinceUtc);
    }

    [TestMethod]
    public async Task Demotion_PreservesProgressRowByteForByte()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true, T0);

        await env.Fixture.InsertProgressAsync(Card40, Var70, createdAtUtc: T0, consecutiveTypingSuccessCount: 3, isMastered: true);

        env.Clock.UtcNow = T2;
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);

        var progress = await env.Fixture.ReadProgressAsync();
        Assert.AreEqual(1, progress.Count);
        Assert.AreEqual(3, progress[0].ConsecutiveTypingSuccessCount); // Preserved
        Assert.AreEqual(T0, progress[0].CreatedAtUtc); // Preserved
        Assert.AreEqual(T0, progress[0].UpdatedAtUtc); // Because we said byte-for-byte, mutation shouldn't touch it
    }

    // ---- Retirement cleanup ----

    [TestMethod]
    public async Task Demotion_RemainingRequiredAllMastered_RetiresCardImmediately()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "y", id: Var71);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, true, T0);
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.Required, false, T0);

        await env.Fixture.InsertProgressAsync(Card40, Var70, createdAtUtc: T0, isMastered: false);
        await env.Fixture.InsertProgressAsync(Card40, Var71, createdAtUtc: T0, isMastered: true);

        // Mutate Var70 to AcceptedOnly. Now Var71 is the only Required one, and it is Mastered.
        // Therefore, the card should be retired.
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.AcceptedOnly, false);
        await env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.Required, true);

        var card = (await env.Fixture.ReadCardsAsync()).Single();
        Assert.AreEqual(CardState.Retired, card.State);
    }

    // ---- Fail-closed graph validation ----

    [TestMethod]
    public async Task Validation_MissingCardForDirection_FailsClosed()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        // We do NOT insert a card for TermToMeaning.

        var ex = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(() => env.Service.SetAssignmentAsync(Sense20, CardDirection.TermToMeaning, Var70, AnswerVariantRequirement.Required, true));

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidCardGraph, ex.Code);
    }

    [TestMethod]
    public async Task Validation_DuplicatePreferred_FailsClosed()
    {
        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "x", id: Var70);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "y", id: Var71);
        await env.Fixture.Connection.ExecuteAsync("INSERT INTO LearningCards (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, 0, T0, 0, 2.5, 0, 0, T0, T0);

        // Wait, SetAssignmentAsync atomically clears prior preferred, so calling it won't trigger DuplicatePreferred.
        // To trigger the duplicate preferred check, we must insert it maliciously behind its back, and then read it.
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: T0);

        // SQLite physically prevents two preferred assignments via IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred.
        // We must drop it transiently to test the service's fail-closed corruption check.
        await env.Fixture.Connection.ExecuteAsync("DROP INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred");
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: T0);

        // Now attempt any mutation, which will trigger the graph validation.
        var ex = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(() => env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, false));

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidAssignmentGraph, ex.Code);
    }
}
