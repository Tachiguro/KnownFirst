using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13ReviewPersistenceTransactionTests
{
    private sealed class TemporaryDatabaseAdapter(Schema7Fixture fixture, bool enableForeignKeys = false) : IKnownFirstDatabase, IAsyncDisposable
    {
        public string DatabasePath => fixture.DatabasePath;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation) =>
            operation(fixture.Connection);

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            T? result = default;
            await fixture.Connection.RunInTransactionAsync(conn =>
            {
                if (enableForeignKeys)
                {
                    conn.Execute("PRAGMA foreign_keys = ON;");
                }
                result = operation(conn);
            });
            return result!;
        }

        public Task ResetAsync() => Task.CompletedTask;

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation) =>
            RunInTransactionAsync(operation);

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }

    private static async Task<TemporaryDatabaseAdapter> CreateValidSchema13DatabaseAsync(bool enableForeignKeys = false)
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        if (enableForeignKeys)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        }
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            if (enableForeignKeys)
            {
                conn.Execute("PRAGMA foreign_keys = ON;");
            }
            conn.Execute(Schema13Ddl.CreateFsrsCardStatesTable);
            conn.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
            conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
            conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
        });
        return new TemporaryDatabaseAdapter(fixture, enableForeignKeys);
    }

    private static (int wordId, int senseId, int cardId) SeedGraph(SQLiteConnection conn, string term = "apple")
    {
        conn.Execute(
            "INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, CreatedAt, UpdatedAt) VALUES ('en', ?, ?, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')",
            term, term);
        var wordId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        conn.Execute(
            "INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'en', 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')",
            $"s-{Guid.NewGuid():N}", wordId);
        var senseId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        conn.Execute(
            "INSERT INTO LearningCards (WordId, SenseId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 0, 0, '2026-08-29T10:00:00Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')",
            wordId, senseId);
        var cardId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

        return (wordId, senseId, cardId);
    }

    // ========================================================================
    // A. Successful atomic transaction
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_PersistsStateAndHistoryAtomically()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var reviewedAtUtc = new DateTimeOffset(2026, 8, 29, 14, 0, 0, TimeSpan.Zero);
        var reviewEvent = new Fsrs6ReviewEvent(reviewedAtUtc, ReviewRating.Good);
        var resultingCard = Fsrs6Card.Learning(2.4, 4.8, reviewedAtUtc, 0, reviewedAtUtc.AddDays(1));
        var stableId = "rev-event-1";

        var coordinator = new FsrsReviewPersistenceCoordinator(db);
        var persisted = await coordinator.PersistReviewAsync(cardId, stableId, reviewEvent, resultingCard);

        Assert.IsNotNull(persisted);
        Assert.AreEqual(stableId, persisted.StableId);
        Assert.AreEqual(cardId, persisted.CardId);
        Assert.AreEqual(1, persisted.SequenceNumber);
        Assert.AreEqual(reviewEvent, persisted.Event);

        await db.RunInTransactionAsync(conn =>
        {
            var state = FsrsCardStateRepository.Load(conn, cardId);
            Assert.IsNotNull(state);
            Assert.AreEqual(resultingCard.State, state.State);
            Assert.AreEqual(resultingCard.Stability!.Value, state.Stability!.Value, 0.0001);
            Assert.AreEqual(resultingCard.Difficulty!.Value, state.Difficulty!.Value, 0.0001);
            Assert.AreEqual(resultingCard.LastReviewedAtUtc, state.LastReviewedAtUtc);
            Assert.AreEqual(resultingCard.DueAtUtc, state.DueAtUtc);
            Assert.AreEqual(resultingCard.StepIndex, state.StepIndex);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(stableId, history[0].StableId);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(reviewedAtUtc, history[0].Event.ReviewedAtUtc);
            Assert.AreEqual(ReviewRating.Good, history[0].Event.Rating);
            return true;
        });
    }

    // ========================================================================
    // B. Existing-state replacement
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_ExistingStateReplacement_ReplacesCardStateAndAppendsEvent()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        var t1 = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var cardA = Fsrs6Card.Learning(2.0, 5.0, t1, 0, t1.AddDays(1));
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            FsrsCardStateRepository.Save(conn, cardId, cardA);
            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "stable-init", new Fsrs6ReviewEvent(t1, ReviewRating.Hard));
            return true;
        });

        var t2 = new DateTimeOffset(2026, 8, 29, 11, 0, 0, TimeSpan.Zero);
        var cardB = Fsrs6Card.Review(5.5, 4.2, t2, t2.AddDays(4));
        var ev2 = new Fsrs6ReviewEvent(t2, ReviewRating.Good);

        var persisted = await coordinator.PersistReviewAsync(cardId, "stable-later", ev2, cardB);

        Assert.AreEqual(2, persisted.SequenceNumber);
        Assert.AreEqual("stable-later", persisted.StableId);

        await db.RunInTransactionAsync(conn =>
        {
            var state = FsrsCardStateRepository.Load(conn, cardId)!;
            Assert.AreEqual(Fsrs6CardState.Review, state.State);
            Assert.AreEqual(5.5, state.Stability!.Value, 0.0001);
            Assert.AreEqual(4.2, state.Difficulty!.Value, 0.0001);
            Assert.AreEqual(t2, state.LastReviewedAtUtc);
            Assert.IsNull(state.StepIndex);
            Assert.AreEqual(t2.AddDays(4), state.DueAtUtc);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("stable-init", history[0].StableId);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual("stable-later", history[1].StableId);
            Assert.AreEqual(2, history[1].SequenceNumber);
            return true;
        });
    }

    // ========================================================================
    // C. Equal-timestamp second review
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_EqualTimestampSecondReview_SucceedsAndAdvancesSequence()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var sameTimestamp = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        var card1 = Fsrs6Card.Learning(2.0, 5.0, sameTimestamp, 0);
        var ev1 = new Fsrs6ReviewEvent(sameTimestamp, ReviewRating.Hard);
        var p1 = await coordinator.PersistReviewAsync(cardId, "stable-c1", ev1, card1);
        Assert.AreEqual(1, p1.SequenceNumber);

        var card2 = Fsrs6Card.Learning(2.5, 4.8, sameTimestamp, 0);
        var ev2 = new Fsrs6ReviewEvent(sameTimestamp, ReviewRating.Good);
        var p2 = await coordinator.PersistReviewAsync(cardId, "stable-c2", ev2, card2);
        Assert.AreEqual(2, p2.SequenceNumber);

        await db.RunInTransactionAsync(conn =>
        {
            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("stable-c1", history[0].StableId);
            Assert.AreEqual("stable-c2", history[1].StableId);
            Assert.AreEqual(sameTimestamp, history[0].Event.ReviewedAtUtc);
            Assert.AreEqual(sameTimestamp, history[1].Event.ReviewedAtUtc);

            var state = FsrsCardStateRepository.Load(conn, cardId)!;
            Assert.AreEqual(2.5, state.Stability!.Value, 0.0001);
            Assert.AreEqual(sameTimestamp, state.LastReviewedAtUtc);
            return true;
        });
    }

    // ========================================================================
    // D. Same-timestamp/same-rating multiplicity
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_SameTimestampSameRatingMultiplicity_PersistsDeterministically()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var t = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        var card1 = Fsrs6Card.Learning(2.0, 5.0, t, 0);
        var ev1 = new Fsrs6ReviewEvent(t, ReviewRating.Good);
        var p1 = await coordinator.PersistReviewAsync(cardId, "stable-d1", ev1, card1);

        var card2 = Fsrs6Card.Learning(2.6, 4.5, t, 0);
        var ev2 = new Fsrs6ReviewEvent(t, ReviewRating.Good);
        var p2 = await coordinator.PersistReviewAsync(cardId, "stable-d2", ev2, card2);

        Assert.AreEqual(1, p1.SequenceNumber);
        Assert.AreEqual(2, p2.SequenceNumber);

        await db.RunInTransactionAsync(conn =>
        {
            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("stable-d1", history[0].StableId);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(ReviewRating.Good, history[0].Event.Rating);

            Assert.AreEqual("stable-d2", history[1].StableId);
            Assert.AreEqual(2, history[1].SequenceNumber);
            Assert.AreEqual(ReviewRating.Good, history[1].Event.Rating);
            return true;
        });
    }

    // ========================================================================
    // E. Duplicate-StableId rollback (Critical)
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_DuplicateStableId_RollsBackCardStateAndAppendsNoHistory()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        var t1 = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var cardA = Fsrs6Card.Learning(2.0, 5.0, t1, 0, t1.AddDays(1));
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        // First successful transaction with StableId "stable-x"
        var ev1 = new Fsrs6ReviewEvent(t1, ReviewRating.Good);
        await coordinator.PersistReviewAsync(cardId, "stable-x", ev1, cardA);

        // Attempt second transaction with cardB, but reuse StableId "stable-x"
        var t2 = new DateTimeOffset(2026, 8, 29, 11, 0, 0, TimeSpan.Zero);
        var cardB = Fsrs6Card.Review(6.0, 3.5, t2, t2.AddDays(3));
        var ev2 = new Fsrs6ReviewEvent(t2, ReviewRating.Easy);

        await Assert.ThrowsExactlyAsync<SQLiteException>(async () =>
        {
            await coordinator.PersistReviewAsync(cardId, "stable-x", ev2, cardB);
        });

        // Verify that the whole transaction was rolled back: card state remains cardA, history remains 1 row
        await db.RunInTransactionAsync(conn =>
        {
            var state = FsrsCardStateRepository.Load(conn, cardId)!;
            Assert.AreEqual(Fsrs6CardState.Learning, state.State);
            Assert.AreEqual(2.0, state.Stability!.Value, 0.0001);
            Assert.AreEqual(5.0, state.Difficulty!.Value, 0.0001);
            Assert.AreEqual(t1, state.LastReviewedAtUtc);
            Assert.AreEqual(0, state.StepIndex);
            Assert.AreEqual(t1.AddDays(1), state.DueAtUtc);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("stable-x", history[0].StableId);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(ev1, history[0].Event);
            return true;
        });
    }

    // ========================================================================
    // F. Earlier-than-tail timestamp rollback
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_EarlierThanTailTimestamp_RollsBackCardStateAndPreservesTail()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var tLate = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var cardA = Fsrs6Card.Learning(2.0, 5.0, tLate, 0);
        var coordinator = new FsrsReviewPersistenceCoordinator(db);
        await coordinator.PersistReviewAsync(cardId, "ev-tail", new Fsrs6ReviewEvent(tLate, ReviewRating.Good), cardA);

        var tEarly = new DateTimeOffset(2026, 8, 29, 11, 0, 0, TimeSpan.Zero);
        var cardB = Fsrs6Card.Review(4.0, 4.0, tEarly, tEarly.AddDays(2));
        var evEarly = new Fsrs6ReviewEvent(tEarly, ReviewRating.Good);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
        {
            await coordinator.PersistReviewAsync(cardId, "ev-early", evEarly, cardB);
        });

        await db.RunInTransactionAsync(conn =>
        {
            var state = FsrsCardStateRepository.Load(conn, cardId)!;
            Assert.AreEqual(Fsrs6CardState.Learning, state.State);
            Assert.AreEqual(2.0, state.Stability!.Value, 0.0001);
            Assert.AreEqual(tLate, state.LastReviewedAtUtc);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("ev-tail", history[0].StableId);
            return true;
        });
    }

    // ========================================================================
    // G. Card-state failure leaves history unchanged
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_CardStateFailure_LeavesHistoryUnchanged()
    {
        await using var db = await CreateValidSchema13DatabaseAsync(enableForeignKeys: true);

        var reviewedAtUtc = new DateTimeOffset(2026, 8, 29, 14, 0, 0, TimeSpan.Zero);
        var reviewEvent = new Fsrs6ReviewEvent(reviewedAtUtc, ReviewRating.Good);
        var resultingCard = Fsrs6Card.Learning(2.0, 5.0, reviewedAtUtc, 0);
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        int nonexistentCardId = 99999;
        await Assert.ThrowsExactlyAsync<SQLiteException>(async () =>
        {
            await coordinator.PersistReviewAsync(nonexistentCardId, "stable-fk-fail", reviewEvent, resultingCard);
        });

        await db.RunInTransactionAsync(conn =>
        {
            var historyCount = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE CardId = ?", nonexistentCardId);
            Assert.AreEqual(0, historyCount);
            return true;
        });
    }

    // ========================================================================
    // H. Timestamp mismatch rejected before writes
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_TimestampMismatch_RejectedBeforeWrites()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var tEvent = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var tCard = new DateTimeOffset(2026, 8, 29, 10, 5, 0, TimeSpan.Zero);

        var reviewEvent = new Fsrs6ReviewEvent(tEvent, ReviewRating.Good);
        var resultingCard = Fsrs6Card.Learning(2.0, 5.0, tCard, 0);

        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await coordinator.PersistReviewAsync(cardId, "stable-mismatch", reviewEvent, resultingCard);
        });

        await db.RunInTransactionAsync(conn =>
        {
            var state = FsrsCardStateRepository.Load(conn, cardId);
            Assert.IsNull(state);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(0, history.Count);
            return true;
        });
    }

    // ========================================================================
    // I. New resulting state rejected before writes
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_NewResultingState_RejectedBeforeWrites()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var reviewEvent = new Fsrs6ReviewEvent(t, ReviewRating.Good);
        var newCard = Fsrs6Card.New();

        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await coordinator.PersistReviewAsync(cardId, "stable-new", reviewEvent, newCard);
        });

        await db.RunInTransactionAsync(conn =>
        {
            var state = FsrsCardStateRepository.Load(conn, cardId);
            Assert.IsNull(state);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(0, history.Count);
            return true;
        });
    }

    // ========================================================================
    // J. Invalid identity validation
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_InvalidIdentity_ThrowsWithoutWriting()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var ev = new Fsrs6ReviewEvent(t, ReviewRating.Good);
        var card = Fsrs6Card.Learning(2.0, 5.0, t, 0);
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        // cardId <= 0
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await coordinator.PersistReviewAsync(0, "st-1", ev, card));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await coordinator.PersistReviewAsync(-1, "st-1", ev, card));

        // stableId null, empty, or whitespace
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await coordinator.PersistReviewAsync(1, "", ev, card));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await coordinator.PersistReviewAsync(1, "   ", ev, card));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await coordinator.PersistReviewAsync(1, null!, ev, card));

        // resultingCard null
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await coordinator.PersistReviewAsync(1, "st-1", ev, null!));

        // Raw method static signature also validates directly
        await db.RunInTransactionAsync(conn =>
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                FsrsReviewPersistenceCoordinator.PersistReview(conn, 0, "st-1", ev, card));
            Assert.ThrowsExactly<ArgumentException>(() =>
                FsrsReviewPersistenceCoordinator.PersistReview(conn, 1, "", ev, card));
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                FsrsReviewPersistenceCoordinator.PersistReview(conn, 1, "st-1", ev, null!));
            return true;
        });
    }

    // ========================================================================
    // K. Dormant-table fail-closed
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_DormantTableFailClosed_WhenSchema13TablesAbsent()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        var db = new TemporaryDatabaseAdapter(fixture);

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;
            return true;
        });

        var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var reviewEvent = new Fsrs6ReviewEvent(t, ReviewRating.Good);
        var card = Fsrs6Card.Learning(2.0, 5.0, t, 0);

        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        await Assert.ThrowsExactlyAsync<SQLiteException>(async () =>
        {
            await coordinator.PersistReviewAsync(cardId, "st-fail", reviewEvent, card);
        });

        await db.RunInTransactionAsync(conn =>
        {
            var countState = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'FsrsCardStates'");
            Assert.AreEqual(0, countState, "FsrsCardStates table must not be created on fail-closed execution.");

            var countHistory = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'FsrsReviewHistoryEntries'");
            Assert.AreEqual(0, countHistory, "FsrsReviewHistoryEntries table must not be created on fail-closed execution.");
            return true;
        });
    }

    // ========================================================================
    // L. Legacy non-write integrity
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_LegacyNonWriteIntegrity_LeavesLegacyColumnsAndReviewsUntouched()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();

        int cardId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var (_, _, cId) = SeedGraph(conn);
            cardId = cId;

            // Seed distinct legacy scheduling columns
            conn.Execute("""
                UPDATE LearningCards SET
                    State = 1,
                    DueAtUtc = '2026-08-30T10:00:00Z',
                    IntervalDays = 5,
                    EaseFactor = 2.4,
                    SuccessfulReviewCount = 3,
                    LapseCount = 1,
                    LastReviewedAtUtc = '2026-08-25T10:00:00Z',
                    LastRating = 2
                WHERE Id = ?
                """, cardId);

            // Seed distinct legacy LearningReviews row
            conn.Execute("""
                INSERT INTO LearningReviews
                    (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor)
                VALUES (?, 1, 2, 0, 1, '2026-08-25T10:00:00Z', '2026-08-30T10:00:00Z', 5, 2.4)
                """, cardId);

            return true;
        });

        // Capture legacy row state before
        LearningCardEntity cardBefore = null!;
        List<LearningReviewEntity> reviewsBefore = null!;
        await db.RunInTransactionAsync(conn =>
        {
            cardBefore = conn.Query<LearningCardEntity>("SELECT * FROM LearningCards WHERE Id = ?", cardId).First();
            reviewsBefore = conn.Query<LearningReviewEntity>("SELECT * FROM LearningReviews WHERE CardId = ?", cardId);
            return true;
        });

        // Execute successful atomic Schema 13 review
        var t = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var ev = new Fsrs6ReviewEvent(t, ReviewRating.Easy);
        var card = Fsrs6Card.Review(7.0, 3.0, t, t.AddDays(7));
        var coordinator = new FsrsReviewPersistenceCoordinator(db);

        await coordinator.PersistReviewAsync(cardId, "stable-leg-check", ev, card);

        // Verify legacy columns and rows are completely untouched
        await db.RunInTransactionAsync(conn =>
        {
            var cardAfter = conn.Query<LearningCardEntity>("SELECT * FROM LearningCards WHERE Id = ?", cardId).First();
            var reviewsAfter = conn.Query<LearningReviewEntity>("SELECT * FROM LearningReviews WHERE CardId = ?", cardId);

            Assert.AreEqual(cardBefore.State, cardAfter.State);
            Assert.AreEqual(cardBefore.DueAtUtc, cardAfter.DueAtUtc);
            Assert.AreEqual(cardBefore.IntervalDays, cardAfter.IntervalDays);
            Assert.AreEqual(cardBefore.EaseFactor, cardAfter.EaseFactor);
            Assert.AreEqual(cardBefore.SuccessfulReviewCount, cardAfter.SuccessfulReviewCount);
            Assert.AreEqual(cardBefore.LapseCount, cardAfter.LapseCount);
            Assert.AreEqual(cardBefore.LastReviewedAtUtc, cardAfter.LastReviewedAtUtc);
            Assert.AreEqual(cardBefore.LastRating, cardAfter.LastRating);

            Assert.AreEqual(reviewsBefore.Count, reviewsAfter.Count);
            Assert.AreEqual(reviewsBefore[0].Id, reviewsAfter[0].Id);
            Assert.AreEqual(reviewsBefore[0].Rating, reviewsAfter[0].Rating);
            Assert.AreEqual(reviewsBefore[0].ReviewedAtUtc, reviewsAfter[0].ReviewedAtUtc);
            return true;
        });
    }

    // ========================================================================
    // M. CurrentVersion/non-activation
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewPersistenceCoordinator_CurrentVersionRemains12_AndDormantSchemaNotActive()
    {
        Assert.AreEqual(12, DatabaseSchema.CurrentVersion);

        await using var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var version = await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(12, version);

        foreach (var table in new[] { Schema13Ddl.FsrsCardStatesTableName, Schema13Ddl.FsrsReviewHistoryEntriesTableName, Schema13Ddl.WordLearningControlsTableName, Schema13Ddl.SenseLearningControlsTableName })
        {
            var exists = await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table);
            Assert.AreEqual(0, exists, $"Table {table} must not exist in a production Schema 12 database.");
        }
    }
}
