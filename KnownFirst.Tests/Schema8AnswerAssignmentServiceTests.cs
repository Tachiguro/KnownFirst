using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnownFirst.Core.Learning;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
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
    public async Task SetAssignment_UndefinedRequirement_FailsClosedWithoutMutation()
    {
        const int undefinedRequirementValue = 2;
        var undefinedRequirement = (AnswerVariantRequirement)undefinedRequirementValue;
        Assert.IsFalse(Enum.IsDefined(undefinedRequirement));

        await using var env = await CreateServiceAsync();

        await env.Fixture.InsertSenseAsync(Word10, id: Sense20, status: SenseStatus.Mastered, createdAtUtc: T0, updatedAtUtc: T0);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "existing", id: Var70, createdAtUtc: T0);
        await env.Fixture.InsertAnswerVariantAsync(Sense20, "candidate", id: Var71, createdAtUtc: T0);
        await env.Fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            Card40, Word10, Sense20, 1, (int)CardDirection.MeaningToTerm, (int)CardState.Review,
            T1, 12, 2.3, 4, 1, T0, (int)ReviewRating.Good, T0, T1);
        await env.Fixture.InsertAssignmentAsync(
            Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required,
            isPreferred: true, requiredSinceUtc: T0, createdAtUtc: T0, stableId: "existing-assignment");
        await env.Fixture.InsertProgressAsync(
            Card40, Var70, T0, LearningInteractionMode.Typing,
            consecutiveTypingSuccessCount: 2, lastAssessedAtUtc: T1, updatedAtUtc: T1);
        var sessionId = await env.Fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Active, totalCards: 1, completedCards: 0,
            startedAtUtc: T0, updatedAtUtc: T1);
        var queueItemId = await env.Fixture.InsertQueueItemAsync(sessionId, Card40, queueOrder: 0);
        await env.Fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET TargetAnswerVariantId = ? WHERE Id = ?", Var70, queueItemId);

        var before = await CaptureMutationPersistenceFingerprintAsync(env.Fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(() =>
            env.Service.SetAssignmentAsync(
                Sense20, CardDirection.MeaningToTerm, Var71, undefinedRequirement, isPreferred: true));

        var after = await CaptureMutationPersistenceFingerprintAsync(env.Fixture);
        var assignments = await env.Fixture.ReadAssignmentsAsync(Sense20, CardDirection.MeaningToTerm);
        var progress = await env.Fixture.ReadProgressAsync();
        var card = (await env.Fixture.ReadCardsAsync()).Single(row => row.Id == Card40);

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidAssignmentGraph, exception.Code);
        Assert.AreEqual(before, after);
        Assert.HasCount(1, assignments);
        Assert.AreEqual(Var70, assignments[0].AnswerVariantId);
        Assert.AreEqual(AnswerVariantRequirement.Required, assignments[0].Requirement);
        Assert.IsTrue(assignments[0].IsPreferred);
        Assert.AreEqual(T0, assignments[0].RequiredSinceUtc);
        Assert.HasCount(1, progress);
        Assert.AreEqual(Var70, progress[0].AnswerVariantId);
        Assert.AreEqual(CardState.Review, card.State);
        Assert.AreEqual(T1, card.DueAtUtc);
        Assert.AreEqual(12, card.IntervalDays);
        Assert.AreEqual(2.3, card.EaseFactor);
        Assert.AreEqual(4, card.SuccessfulReviewCount);
        Assert.AreEqual(1, card.LapseCount);
        Assert.AreEqual(T0, card.LastReviewedAtUtc);
        Assert.AreEqual(ReviewRating.Good, card.LastRating);
        Assert.AreEqual(1, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(0, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual((int)SenseStatus.Mastered, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Status FROM Senses WHERE Id = ?", Sense20));
        Assert.AreEqual(2, await env.Fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariants"));
    }

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

        // SetAssignmentAsync atomically clears the prior preferred row, so calling it can never create a
        // duplicate. The corrupt state has to be written behind the service's back.
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: T0);

        // A second preferred row is physically impossible while
        // IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred exists, so the index must be dropped to
        // construct it. That drop is itself a Schema-8 physical-shape violation: since Schema-8 activation,
        // LearningSchemaCapability validates required index definitions and is deliberately the *first* gate,
        // so it fails closed before the logical assignment-graph check is ever reached. Both gates refuse the
        // database; this test pins the outer one and proves the duplicate really exists underneath it.
        await env.Fixture.Connection.ExecuteAsync("DROP INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred");
        await env.Fixture.InsertAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var71, AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: T0);

        Assert.AreEqual(
            2,
            await env.Fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ? AND IsPreferred = 1",
                Sense20,
                (int)CardDirection.MeaningToTerm),
            "The fixture must actually hold two preferred assignments for one (Sense, Direction).");
        var before = await CaptureMutationPersistenceFingerprintAsync(env.Fixture);

        var ex = await Assert.ThrowsExactlyAsync<LearningSchemaCapabilityException>(
            () => env.Service.SetAssignmentAsync(Sense20, CardDirection.MeaningToTerm, Var70, AnswerVariantRequirement.Required, false));

        Assert.IsTrue(ex.ShapeMismatch);
        Assert.AreEqual(8, ex.FoundVersion);
        Assert.AreEqual("learning-schema-capability-shape-mismatch", ex.ErrorCode);
        StringAssert.Contains(ex.ShapeDetail, "IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred");
        Assert.AreEqual(before, await CaptureMutationPersistenceFingerprintAsync(env.Fixture));
    }

    private static async Task<string> CaptureMutationPersistenceFingerprintAsync(Schema7Fixture fixture)
    {
        string? fingerprint = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            static string Rows(SQLite.SQLiteConnection connection, string select) =>
                connection.ExecuteScalar<string?>($"SELECT group_concat(Value, char(10)) FROM ({select})")
                ?? string.Empty;

            fingerprint = string.Join("\n--\n",
                Rows(connection, "SELECT quote(Id)||'|'||quote(StableId)||'|'||quote(SenseId)||'|'||quote(CardDirection)||'|'||quote(AnswerVariantId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(RequiredSinceUtc)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM SenseAnswerVariantAssignments ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(AnswerVariantId)||'|'||quote(InteractionMode)||'|'||quote(ConsecutiveReadingSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(LastAssessedAtUtc)||'|'||quote(MasteryReviewExtensionScheduled)||'|'||quote(IsMastered)||'|'||quote(ReplayVersion)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariantProgress ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(PreferredMeaningId)||'|'||quote(Direction)||'|'||quote(State)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(SuccessfulReviewCount)||'|'||quote(LapseCount)||'|'||quote(LastReviewedAtUtc)||'|'||quote(LastRating)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningCards ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(IsDueCard)||'|'||quote(IsAgainRepeat)||'|'||quote(AnswerRevealed)||'|'||quote(SpellingChecked)||'|'||quote(SpellingCorrect)||'|'||quote(IsCompleted)||'|'||quote(Rating)||'|'||quote(CompletedAtUtc)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(TotalCards)||'|'||quote(CompletedCards)||'|'||quote(AgainCount)||'|'||quote(HardCount)||'|'||quote(GoodCount)||'|'||quote(EasyCount)||'|'||quote(StartedAtUtc)||'|'||quote(UpdatedAtUtc)||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessions ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(Status)||'|'||quote(DefaultMeaningId)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM Senses ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(StableId)||'|'||quote(SenseId)||'|'||quote(AnswerLanguage)||'|'||quote(DisplayText)||'|'||quote(NormalizedText)||'|'||quote(SourceMeaningId)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariants ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(AutomaticInteractionMode)||'|'||quote(ConsecutiveRecallSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(MasteryReviewExtensionScheduled)||'|'||quote(UpdatedAt) AS Value FROM Words ORDER BY Id"),
                Rows(connection, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(SessionId)||'|'||quote(Rating)||'|'||quote(WasTypedAnswer)||'|'||quote(WasCorrect)||'|'||quote(ReviewedAtUtc)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(TargetAnswerVariantId)||'|'||quote(MatchedAnswerVariantId) AS Value FROM LearningReviews ORDER BY Id"));
        });
        return fingerprint!;
    }
}
