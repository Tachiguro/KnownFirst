using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13MigrationBootstrapTests
{
    private static async Task<Schema7Fixture> CreateValidSchema12DatabaseAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        return fixture;
    }

    private static (int wordId, int senseId, int cardId) SeedGraph(
        SQLiteConnection conn,
        string term = "apple",
        WordStatus status = WordStatus.Unreviewed,
        DateTime? updatedAt = null)
    {
        var now = updatedAt ?? new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        conn.Execute(
            """
            INSERT INTO Words (
                Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
            VALUES ('en', ?, ?, ?, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            term, term, (int)status, now, now);
        var wordId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        conn.Execute(
            """
            INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, 'en', 'en', 0, ?, ?)
            """,
            $"s-{Guid.NewGuid():N}", wordId, now, now);
        var senseId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        conn.Execute(
            """
            INSERT INTO Meanings (
                WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
            VALUES (?, ?, 'en', 'en', ?, ?, '', 0, 'meaning', 'definition', 'example', '', '[]', 'meaning', 'test', 'test', 'title', 'attribution', 1, ?, ?, ?, ?)
            """,
            wordId, senseId, term, term, now, now, now, $"m-{Guid.NewGuid():N}");
        var meaningId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

        conn.Execute(
            """
            INSERT INTO LearningCards (
                WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, 0, 0, ?, 0, 2.5, 0, 0, ?, ?)
            """,
            wordId, senseId, meaningId, now, now, now);
        var cardId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        return (wordId, senseId, cardId);
    }

    private static int SeedSession(SQLiteConnection conn, DateTime now)
    {
        var stableId = Guid.NewGuid().ToString("N").ToLowerInvariant();
        conn.Execute(
            "INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) VALUES (?, 1, 1, 1, 0, 0, 1, 0, ?, ?, ?)",
            stableId, now, now, now);
        return conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static void ApplySchema13TargetDdl(SQLiteConnection conn)
    {
        conn.Execute(Schema13Ddl.CreateFsrsCardStatesTable);
        conn.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);
        conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
        conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
        conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
        conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
        conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
        conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
    }

    private static void MaterializeBootstrapPlan(SQLiteConnection conn, Schema13BootstrapPlan plan)
    {
        foreach (var control in plan.WordControls)
        {
            conn.Execute(
                "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)",
                control.WordId, control.DecidedAtUtc);
        }

        foreach (var history in plan.ReviewHistory)
        {
            conn.Execute(
                "INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES (?, ?, ?, ?, ?)",
                history.StableId, history.CardId, history.SequenceNumber, history.Rating, history.ReviewedAtUtc);
        }

        foreach (var state in plan.CardStates)
        {
            conn.Execute(
                """
                INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                state.CardId,
                (int)state.Card.State,
                state.Card.Stability,
                state.Card.Difficulty,
                state.Card.LastReviewedAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(state.Card.LastReviewedAtUtc.Value) : null,
                state.Card.StepIndex,
                state.Card.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(state.Card.DueAtUtc.Value) : null);
        }
    }

    #region A. StableId Policy

    [TestMethod]
    public void HistoricalReviewStableId_DistinguishesIdenticalFactualEventsByMultiplicityOrdinal()
    {
        var timestamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var id0 = Schema13HistoricalReviewStableIdPolicy.Compute(
            "s-001",
            CardDirection.TermToMeaning,
            timestamp,
            ReviewRating.Good,
            multiplicityOrdinal: 0);

        var id1 = Schema13HistoricalReviewStableIdPolicy.Compute(
            "s-001",
            CardDirection.TermToMeaning,
            timestamp,
            ReviewRating.Good,
            multiplicityOrdinal: 1);

        Assert.IsNotNull(id0);
        Assert.IsNotNull(id1);
        Assert.AreNotEqual(id0, id1);
    }

    [TestMethod]
    public void HistoricalReviewStableId_IsDeterministicAcrossRepeatedComputations()
    {
        var timestamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var id1 = Schema13HistoricalReviewStableIdPolicy.Compute("s-001", CardDirection.TermToMeaning, timestamp, ReviewRating.Easy, 0);
        var id2 = Schema13HistoricalReviewStableIdPolicy.Compute("s-001", CardDirection.TermToMeaning, timestamp, ReviewRating.Easy, 0);

        Assert.AreEqual(id1, id2);
        Assert.AreEqual(64, id1.Length);
        Assert.AreEqual(id1.ToLowerInvariant(), id1);
    }

    [TestMethod]
    public void HistoricalReviewStableId_DistinguishesDifferentSemanticInputs()
    {
        var t1 = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 8, 29, 12, 1, 0, DateTimeKind.Utc);

        var baseId = Schema13HistoricalReviewStableIdPolicy.Compute("s-001", CardDirection.TermToMeaning, t1, ReviewRating.Good, 0);
        var diffSense = Schema13HistoricalReviewStableIdPolicy.Compute("s-002", CardDirection.TermToMeaning, t1, ReviewRating.Good, 0);
        var diffDir = Schema13HistoricalReviewStableIdPolicy.Compute("s-001", CardDirection.MeaningToTerm, t1, ReviewRating.Good, 0);
        var diffTime = Schema13HistoricalReviewStableIdPolicy.Compute("s-001", CardDirection.TermToMeaning, t2, ReviewRating.Good, 0);
        var diffRating = Schema13HistoricalReviewStableIdPolicy.Compute("s-001", CardDirection.TermToMeaning, t1, ReviewRating.Hard, 0);

        Assert.AreNotEqual(baseId, diffSense);
        Assert.AreNotEqual(baseId, diffDir);
        Assert.AreNotEqual(baseId, diffTime);
        Assert.AreNotEqual(baseId, diffRating);
    }

    [TestMethod]
    public void HistoricalReviewStableId_FailsClosedOnInvalidInputs()
    {
        var t = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        Assert.ThrowsExactly<ArgumentException>(() =>
            Schema13HistoricalReviewStableIdPolicy.Compute("", CardDirection.TermToMeaning, t, ReviewRating.Good, 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Schema13HistoricalReviewStableIdPolicy.Compute("   ", CardDirection.TermToMeaning, t, ReviewRating.Good, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Schema13HistoricalReviewStableIdPolicy.Compute("s-1", (CardDirection)99, t, ReviewRating.Good, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Schema13HistoricalReviewStableIdPolicy.Compute("s-1", CardDirection.TermToMeaning, t, (ReviewRating)99, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Schema13HistoricalReviewStableIdPolicy.Compute("s-1", CardDirection.TermToMeaning, t, ReviewRating.Good, -1));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Schema13HistoricalReviewStableIdPolicy.Compute("s-1", CardDirection.TermToMeaning, new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Local), ReviewRating.Good, 0));
    }

    #endregion

    #region B. Review Mapping & C. Replay Bootstrap

    [TestMethod]
    public async Task LearningBootstrap_ReplaysFactualReviews_MatchingFsrs6Replayer()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        int wordId = 0;
        int senseId = 0;
        int cardId = 0;
        var t1 = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 8, 29, 10, 15, 0, DateTimeKind.Utc);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            (wordId, senseId, cardId) = SeedGraph(conn, "test");
            var sessionId = SeedSession(conn, t1);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 0, ?)", sessionId, cardId, t1);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)", sessionId, cardId, t2);
        });

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            plan = Schema13LearningBootstrap.BuildPlan(conn);
        });

        Assert.AreEqual(1, plan.CardStates.Count);
        Assert.AreEqual(2, plan.ReviewHistory.Count);

        var expectedReplayer = new Fsrs6Replayer();
        var expectedCard = expectedReplayer.Replay(Fsrs6Card.New(),
        [
            new Fsrs6ReviewEvent(new DateTimeOffset(t1, TimeSpan.Zero), ReviewRating.Again),
            new Fsrs6ReviewEvent(new DateTimeOffset(t2, TimeSpan.Zero), ReviewRating.Good)
        ]);

        var actualCard = plan.CardStates[0].Card;
        Assert.AreEqual(expectedCard.State, actualCard.State);
        Assert.AreEqual(expectedCard.Stability!.Value, actualCard.Stability!.Value, 1e-9);
        Assert.AreEqual(expectedCard.Difficulty!.Value, actualCard.Difficulty!.Value, 1e-9);
        Assert.AreEqual(expectedCard.LastReviewedAtUtc, actualCard.LastReviewedAtUtc);
        Assert.AreEqual(expectedCard.StepIndex, actualCard.StepIndex);
        Assert.AreEqual(expectedCard.DueAtUtc, actualCard.DueAtUtc);

        // Sequence numbers 1..N
        Assert.AreEqual(1, plan.ReviewHistory[0].SequenceNumber);
        Assert.AreEqual(2, plan.ReviewHistory[1].SequenceNumber);
        Assert.AreEqual(0, plan.ReviewHistory[0].Rating);
        Assert.AreEqual(2, plan.ReviewHistory[1].Rating);
    }

    [TestMethod]
    public async Task LearningBootstrap_EqualTimestampEvents_ReplayDeterministicallyViaIdTieBreak()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        int cardId = 0;
        var sameTime = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            (_, _, cardId) = SeedGraph(conn, "same-time");
            var sessionId = SeedSession(conn, sameTime);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 0, ?)", sessionId, cardId, sameTime);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)", sessionId, cardId, sameTime);
        });

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            plan = Schema13LearningBootstrap.BuildPlan(conn);
        });

        Assert.AreEqual(2, plan.ReviewHistory.Count);
        Assert.AreEqual(1, plan.ReviewHistory[0].SequenceNumber);
        Assert.AreEqual(2, plan.ReviewHistory[1].SequenceNumber);
        Assert.AreEqual(0, plan.ReviewHistory[0].Rating); // Again was inserted first (lower Id)
        Assert.AreEqual(2, plan.ReviewHistory[1].Rating); // Good was inserted second (higher Id)
        Assert.AreNotEqual(plan.ReviewHistory[0].StableId, plan.ReviewHistory[1].StableId);
    }

    [TestMethod]
    public async Task LearningBootstrap_LegacySchedulerFields_DoNotInfluenceFsrsReplay()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        int cardId = 0;
        var t1 = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var fakeLegacyDue = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            (_, _, cardId) = SeedGraph(conn, "legacy-fields");
            var sessionId = SeedSession(conn, t1);
            conn.Execute(
                "UPDATE LearningCards SET IntervalDays = 999, EaseFactor = 9.9, DueAtUtc = ? WHERE Id = ?",
                fakeLegacyDue, cardId);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)", sessionId, cardId, t1);
        });

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            plan = Schema13LearningBootstrap.BuildPlan(conn);
        });

        var actualCard = plan.CardStates[0].Card;
        var expectedCard = new Fsrs6Replayer().Replay(Fsrs6Card.New(),
        [
            new Fsrs6ReviewEvent(new DateTimeOffset(t1, TimeSpan.Zero), ReviewRating.Good)
        ]);

        Assert.AreEqual(expectedCard.DueAtUtc, actualCard.DueAtUtc);
        Assert.AreNotEqual(new DateTimeOffset(fakeLegacyDue, TimeSpan.Zero), actualCard.DueAtUtc);
        Assert.AreEqual(expectedCard.Stability!.Value, actualCard.Stability!.Value, 1e-9);
    }

    [TestMethod]
    public async Task LearningBootstrap_FailsClosedOnCorruptRating()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        int cardId = 0;
        var t = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            (_, _, cardId) = SeedGraph(conn, "corrupt-rating");
            var sessionId = SeedSession(conn, t);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 99, ?)", sessionId, cardId, t);
        });

        var ex = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(async () =>
        {
            await fixture.Connection.RunInTransactionAsync(conn =>
            {
                Schema13LearningBootstrap.BuildPlan(conn);
            });
        });

        Assert.AreEqual("schema13-migration-corrupt-review-rating", ex.ErrorCode);
    }

    #endregion

    #region D. Genuine Zero-Review Card & E. Progressed/Historyless Card

    [TestMethod]
    public async Task LearningBootstrap_GenuinelyZeroReviewCard_MapsToFsrs6CardNew()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        int cardId = 0;

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            (_, _, cardId) = SeedGraph(conn, "zero-review");
        });

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            plan = Schema13LearningBootstrap.BuildPlan(conn);
        });

        Assert.AreEqual(1, plan.CardStates.Count);
        Assert.AreEqual(0, plan.ReviewHistory.Count);

        var card = plan.CardStates[0].Card;
        Assert.AreEqual(Fsrs6CardState.New, card.State);
        Assert.IsNull(card.Stability);
        Assert.IsNull(card.Difficulty);
        Assert.IsNull(card.LastReviewedAtUtc);
        Assert.IsNull(card.StepIndex);
        Assert.IsNull(card.DueAtUtc);
    }

    [TestMethod]
    public async Task LearningBootstrap_ProgressedCardWithoutReviewHistory_FailsClosed()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();

        // 1. Non-New State with 0 reviews
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn, "progress-state");
            conn.Execute("UPDATE LearningCards SET State = 2 WHERE Id = ?", cardId);
        });

        var exState = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(async () =>
        {
            await fixture.Connection.RunInTransactionAsync(conn => Schema13LearningBootstrap.BuildPlan(conn));
        });
        Assert.AreEqual("schema13-migration-missing-review-history", exState.ErrorCode);

        // Clean slate for next check
        await using var fixture2 = await CreateValidSchema12DatabaseAsync();
        await fixture2.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn, "progress-reviewed-at");
            conn.Execute("UPDATE LearningCards SET LastReviewedAtUtc = '2026-08-29T09:00:00Z' WHERE Id = ?", cardId);
        });
        var exRev = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(async () =>
        {
            await fixture2.Connection.RunInTransactionAsync(conn => Schema13LearningBootstrap.BuildPlan(conn));
        });
        Assert.AreEqual("schema13-migration-missing-review-history", exRev.ErrorCode);

        // Counter check
        await using var fixture3 = await CreateValidSchema12DatabaseAsync();
        await fixture3.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn, "progress-counter");
            conn.Execute("UPDATE LearningCards SET SuccessfulReviewCount = 3 WHERE Id = ?", cardId);
        });
        var exCount = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(async () =>
        {
            await fixture3.Connection.RunInTransactionAsync(conn => Schema13LearningBootstrap.BuildPlan(conn));
        });
        Assert.AreEqual("schema13-migration-missing-review-history", exCount.ErrorCode);
    }

    #endregion

    #region F. AlreadyKnown & G. Sense StopLearning

    [TestMethod]
    public async Task LearningBootstrap_ExtractsAlreadyKnownWords_WithUpdatedAtTimestamp()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        var knownTime = new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc);
        int knownWordId = 0;
        int unreviewedWordId = 0;

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            (knownWordId, _, _) = SeedGraph(conn, "known-word", WordStatus.Known, knownTime);
            (unreviewedWordId, _, _) = SeedGraph(conn, "unreviewed-word", WordStatus.Unreviewed);
        });

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            plan = Schema13LearningBootstrap.BuildPlan(conn);
        });

        Assert.AreEqual(1, plan.WordControls.Count);
        Assert.AreEqual(knownWordId, plan.WordControls[0].WordId);
        Assert.AreEqual(Schema13TimestampCodec.FormatUtc(knownTime), plan.WordControls[0].DecidedAtUtc);
    }

    [TestMethod]
    public async Task LearningBootstrap_SenseStopLearning_ProducesZeroControls()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wId, sId, _) = SeedGraph(conn, "sense-status-check");
            // Set sense status to Mastered (2) or Suspended (3)
            conn.Execute("UPDATE Senses SET Status = 2 WHERE Id = ?", sId);
        });

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            plan = Schema13LearningBootstrap.BuildPlan(conn);
        });

        // Plan contains zero sense controls
        Assert.AreEqual(0, plan.WordControls.Count); // WordStatus was Unreviewed
    }

    #endregion

    #region H. Integrity Validator

    [TestMethod]
    public async Task MigrationIntegrityValidator_AcceptsValidPopulatedState()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        var t1 = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var knownTime = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        Schema13BootstrapPlan plan = null!;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (w1, _, c1) = SeedGraph(conn, "word-with-review", WordStatus.Known, knownTime);
            var sessionId = SeedSession(conn, t1);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)", sessionId, c1, t1);

            var (w2, _, c2) = SeedGraph(conn, "word-new", WordStatus.Unreviewed);

            ApplySchema13TargetDdl(conn);
            plan = Schema13LearningBootstrap.BuildPlan(conn);
            MaterializeBootstrapPlan(conn, plan);
        });

        bool isValid = false;
        string? failure = null;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema13MigrationIntegrityValidator.Validate(conn, out failure);
        });

        Assert.IsTrue(isValid, $"Integrity validation failed: {failure}");
        Assert.IsNull(failure);
    }

    [TestMethod]
    public async Task MigrationIntegrityValidator_RejectsMissingCardState()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            SeedGraph(conn, "missing-state");
            ApplySchema13TargetDdl(conn);
            // Intentionally do NOT insert FsrsCardStates
        });

        bool isValid = false;
        string? failure = null;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema13MigrationIntegrityValidator.Validate(conn, out failure);
        });

        Assert.IsFalse(isValid);
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure, "LearningCards count");
    }

    [TestMethod]
    public async Task MigrationIntegrityValidator_RejectsReplayStateMismatch()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        var t1 = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn, "mismatch");
            var sessionId = SeedSession(conn, t1);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)", sessionId, cardId, t1);
            ApplySchema13TargetDdl(conn);
            var plan = Schema13LearningBootstrap.BuildPlan(conn);
            MaterializeBootstrapPlan(conn, plan);

            // Corrupt stability in FsrsCardStates
            conn.Execute("UPDATE FsrsCardStates SET Stability = 999.0 WHERE CardId = ?", cardId);
        });

        bool isValid = false;
        string? failure = null;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema13MigrationIntegrityValidator.Validate(conn, out failure);
        });

        Assert.IsFalse(isValid);
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure, "Stability mismatch");
    }

    [TestMethod]
    public async Task MigrationIntegrityValidator_RejectsSequenceCorruption()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        var t1 = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 8, 29, 10, 10, 0, DateTimeKind.Utc);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn, "seq-gap");
            var sessionId = SeedSession(conn, t1);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 0, ?)", sessionId, cardId, t1);
            conn.Execute("INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)", sessionId, cardId, t2);
            ApplySchema13TargetDdl(conn);
            var plan = Schema13LearningBootstrap.BuildPlan(conn);
            MaterializeBootstrapPlan(conn, plan);

            // Create gap in sequence numbers
            conn.Execute("UPDATE FsrsReviewHistoryEntries SET SequenceNumber = 3 WHERE CardId = ? AND SequenceNumber = 2", cardId);
        });

        bool isValid = false;
        string? failure = null;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema13MigrationIntegrityValidator.Validate(conn, out failure);
        });

        Assert.IsFalse(isValid);
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure, "history sequence broken");
    }

    [TestMethod]
    public async Task MigrationIntegrityValidator_RejectsUnexpectedSenseControl()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, senseId, _) = SeedGraph(conn, "unexpected-sense-ctrl");
            ApplySchema13TargetDdl(conn);
            var plan = Schema13LearningBootstrap.BuildPlan(conn);
            MaterializeBootstrapPlan(conn, plan);

            // Inject unexpected sense control row
            conn.Execute(
                "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, '2026-08-29T10:00:00.0000000Z')",
                senseId);
        });

        bool isValid = false;
        string? failure = null;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema13MigrationIntegrityValidator.Validate(conn, out failure);
        });

        Assert.IsFalse(isValid);
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure, "SenseLearningControls must be empty");
    }

    #endregion
}
