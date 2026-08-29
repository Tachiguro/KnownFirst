using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13PersistenceRepositoryTests
{
    private static async Task<Schema7Fixture> CreateValidSchema13DatabaseAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            conn.Execute(Schema13Ddl.CreateFsrsCardStatesTable);
            conn.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
            conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
            conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
        });
        return fixture;
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
    // A. WordLearningControl Repository
    // ========================================================================

    [TestMethod]
    public async Task WordLearningControlRepository_Load_AbsentRow_ReturnsDefault()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, _, _) = SeedGraph(conn);

            var control = WordLearningControlRepository.Load(conn, wordId);

            Assert.AreEqual(WordLearningControl.Default, control);
            Assert.IsFalse(control.IsAlreadyKnown);
        });
    }

    [TestMethod]
    public async Task WordLearningControlRepository_SaveAndLoad_ActiveDecision_RoundtripsExactUtc()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, _, _) = SeedGraph(conn);
            var timestamp = new DateTime(2026, 8, 29, 12, 34, 56, DateTimeKind.Utc);
            var control = WordLearningControl.Default.MarkAlreadyKnown(timestamp);

            WordLearningControlRepository.Save(conn, wordId, control);
            var loaded = WordLearningControlRepository.Load(conn, wordId);

            Assert.IsTrue(loaded.IsAlreadyKnown);
            Assert.AreEqual(DateTimeKind.Utc, loaded.AlreadyKnown!.DecidedAtUtc.Kind);
            Assert.AreEqual(timestamp, loaded.AlreadyKnown.DecidedAtUtc);
        });
    }

    [TestMethod]
    public async Task WordLearningControlRepository_SavingDefault_RemovesRow()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, _, _) = SeedGraph(conn);
            var control = WordLearningControl.Default.MarkAlreadyKnown(DateTime.UtcNow);

            WordLearningControlRepository.Save(conn, wordId, control);
            var rowCountBefore = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls WHERE WordId = ?", wordId);
            Assert.AreEqual(1, rowCountBefore);

            WordLearningControlRepository.Save(conn, wordId, WordLearningControl.Default);
            var rowCountAfter = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls WHERE WordId = ?", wordId);
            Assert.AreEqual(0, rowCountAfter);

            var loaded = WordLearningControlRepository.Load(conn, wordId);
            Assert.AreEqual(WordLearningControl.Default, loaded);
        });
    }

    [TestMethod]
    public async Task WordLearningControlRepository_ClearingControl_ReturnsDefault_AndIsReversible()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, _, _) = SeedGraph(conn);
            var t1 = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
            var t2 = new DateTime(2026, 8, 29, 11, 0, 0, DateTimeKind.Utc);

            WordLearningControlRepository.Save(conn, wordId, WordLearningControl.Default.MarkAlreadyKnown(t1));
            Assert.IsTrue(WordLearningControlRepository.Load(conn, wordId).IsAlreadyKnown);

            WordLearningControlRepository.Save(conn, wordId, WordLearningControl.Default);
            Assert.IsFalse(WordLearningControlRepository.Load(conn, wordId).IsAlreadyKnown);

            WordLearningControlRepository.Save(conn, wordId, WordLearningControl.Default.MarkAlreadyKnown(t2));
            var reMarked = WordLearningControlRepository.Load(conn, wordId);
            Assert.IsTrue(reMarked.IsAlreadyKnown);
            Assert.AreEqual(t2, reMarked.AlreadyKnown!.DecidedAtUtc);
        });
    }

    [TestMethod]
    public async Task WordLearningControlRepository_DoesNotModifyWordsStatusOrDeleteGraph()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, senseId, cardId) = SeedGraph(conn);
            var wordStatusBefore = conn.ExecuteScalar<int>("SELECT Status FROM Words WHERE Id = ?", wordId);

            WordLearningControlRepository.Save(conn, wordId, WordLearningControl.Default.MarkAlreadyKnown(DateTime.UtcNow));

            var wordStatusAfter = conn.ExecuteScalar<int>("SELECT Status FROM Words WHERE Id = ?", wordId);
            Assert.AreEqual(wordStatusBefore, wordStatusAfter, "WordLearningControlRepository must never alter Words.Status.");

            var senseCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses WHERE Id = ?", senseId);
            var cardCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards WHERE Id = ?", cardId);
            Assert.AreEqual(1, senseCount, "WordLearningControlRepository must not delete Senses.");
            Assert.AreEqual(1, cardCount, "WordLearningControlRepository must not delete LearningCards.");
        });
    }

    [TestMethod]
    public async Task WordLearningControlRepository_InvalidWordId_ThrowsArgumentOutOfRangeException()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WordLearningControlRepository.Load(conn, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WordLearningControlRepository.Load(conn, -1));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WordLearningControlRepository.Save(conn, 0, WordLearningControl.Default));
        });
    }

    [TestMethod]
    public async Task WordLearningControlRepository_MalformedOrNonUtcPersistedTimestamp_FailsClosed()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, _, _) = SeedGraph(conn);

            // Malformed
            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, 'not-a-timestamp')", wordId);
            Assert.ThrowsExactly<FormatException>(() => WordLearningControlRepository.Load(conn, wordId));

            // Non-UTC (offset +02:00)
            conn.Execute("DELETE FROM WordLearningControls WHERE WordId = ?", wordId);
            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, '2026-08-29T12:00:00+02:00')", wordId);
            Assert.ThrowsExactly<FormatException>(() => WordLearningControlRepository.Load(conn, wordId));
        });
    }

    // ========================================================================
    // B. SenseLearningControl Repository
    // ========================================================================

    [TestMethod]
    public async Task SenseLearningControlRepository_Load_AbsentRow_ReturnsDefault()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, senseId, _) = SeedGraph(conn);

            var control = SenseLearningControlRepository.Load(conn, senseId);

            Assert.AreEqual(SenseLearningControl.Default, control);
            Assert.IsFalse(control.IsStopped);
        });
    }

    [TestMethod]
    public async Task SenseLearningControlRepository_SaveAndLoad_StopLearning_RoundtripsExactUtc()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, senseId, _) = SeedGraph(conn);
            var timestamp = new DateTime(2026, 8, 29, 14, 0, 0, DateTimeKind.Utc);
            var control = SenseLearningControl.Default.Stop(timestamp);

            SenseLearningControlRepository.Save(conn, senseId, control);
            var loaded = SenseLearningControlRepository.Load(conn, senseId);

            Assert.IsTrue(loaded.IsStopped);
            Assert.AreEqual(DateTimeKind.Utc, loaded.StopLearning!.DecidedAtUtc.Kind);
            Assert.AreEqual(timestamp, loaded.StopLearning.DecidedAtUtc);
        });
    }

    [TestMethod]
    public async Task SenseLearningControlRepository_SavingDefault_RemovesRow()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, senseId, _) = SeedGraph(conn);
            var control = SenseLearningControl.Default.Stop(DateTime.UtcNow);

            SenseLearningControlRepository.Save(conn, senseId, control);
            Assert.AreEqual(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls WHERE SenseId = ?", senseId));

            SenseLearningControlRepository.Save(conn, senseId, SenseLearningControl.Default);
            Assert.AreEqual(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls WHERE SenseId = ?", senseId));

            var loaded = SenseLearningControlRepository.Load(conn, senseId);
            Assert.AreEqual(SenseLearningControl.Default, loaded);
        });
    }

    [TestMethod]
    public async Task SenseLearningControlRepository_StopOneSense_DoesNotAffectSiblingSense()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (wordId, sense1, _) = SeedGraph(conn);
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('s-2', ?, 'en', 'en', 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId);
            var sense2 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            SenseLearningControlRepository.Save(conn, sense1, SenseLearningControl.Default.Stop(DateTime.UtcNow));

            Assert.IsTrue(SenseLearningControlRepository.Load(conn, sense1).IsStopped);
            Assert.IsFalse(SenseLearningControlRepository.Load(conn, sense2).IsStopped);
            Assert.AreEqual(SenseLearningControl.Default, SenseLearningControlRepository.Load(conn, sense2));
        });
    }

    [TestMethod]
    public async Task SenseLearningControlRepository_InvalidSenseId_ThrowsArgumentOutOfRangeException()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SenseLearningControlRepository.Load(conn, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SenseLearningControlRepository.Save(conn, -1, SenseLearningControl.Default));
        });
    }

    [TestMethod]
    public async Task SenseLearningControlRepository_MalformedOrNonUtcPersistedTimestamp_FailsClosed()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, senseId, _) = SeedGraph(conn);

            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, 'bad-iso')", senseId);
            Assert.ThrowsExactly<FormatException>(() => SenseLearningControlRepository.Load(conn, senseId));

            conn.Execute("DELETE FROM SenseLearningControls WHERE SenseId = ?", senseId);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, '2026-08-29 14:00:00')", senseId);
            Assert.ThrowsExactly<FormatException>(() => SenseLearningControlRepository.Load(conn, senseId));
        });
    }

    // ========================================================================
    // C. FsrsCardState Repository
    // ========================================================================

    [TestMethod]
    public async Task FsrsCardStateRepository_Load_AbsentRow_ReturnsNull()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            var state = FsrsCardStateRepository.Load(conn, cardId);

            Assert.IsNull(state);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_SaveAndLoad_NewCard_RoundtripsSuccessfully()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var newCard = Fsrs6Card.New();

            FsrsCardStateRepository.Save(conn, cardId, newCard);
            var loaded = FsrsCardStateRepository.Load(conn, cardId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(Fsrs6CardState.New, loaded.State);
            Assert.IsNull(loaded.Stability);
            Assert.IsNull(loaded.Difficulty);
            Assert.IsNull(loaded.LastReviewedAtUtc);
            Assert.IsNull(loaded.StepIndex);
            Assert.IsNull(loaded.DueAtUtc);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_SaveAndLoad_LearningCard_RoundtripsSuccessfully()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var reviewTime = new DateTimeOffset(2026, 8, 29, 10, 30, 0, TimeSpan.Zero);
            var card = Fsrs6Card.Learning(2.45, 5.5, reviewTime, 0);

            FsrsCardStateRepository.Save(conn, cardId, card);
            var loaded = FsrsCardStateRepository.Load(conn, cardId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(Fsrs6CardState.Learning, loaded.State);
            Assert.AreEqual(2.45, loaded.Stability!.Value, 0.0001);
            Assert.AreEqual(5.5, loaded.Difficulty!.Value, 0.0001);
            Assert.AreEqual(reviewTime, loaded.LastReviewedAtUtc!.Value);
            Assert.AreEqual(0, loaded.StepIndex);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_SaveAndLoad_ReviewCard_RoundtripsSuccessfully()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var reviewTime = new DateTimeOffset(2026, 8, 29, 11, 0, 0, TimeSpan.Zero);
            var dueTime = new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero);
            var card = Fsrs6Card.Review(7.8, 3.2, reviewTime, dueTime);

            FsrsCardStateRepository.Save(conn, cardId, card);
            var loaded = FsrsCardStateRepository.Load(conn, cardId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(Fsrs6CardState.Review, loaded.State);
            Assert.AreEqual(7.8, loaded.Stability!.Value, 0.0001);
            Assert.AreEqual(3.2, loaded.Difficulty!.Value, 0.0001);
            Assert.AreEqual(reviewTime, loaded.LastReviewedAtUtc!.Value);
            Assert.IsNull(loaded.StepIndex);
            Assert.AreEqual(dueTime, loaded.DueAtUtc!.Value);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_SaveAndLoad_RelearningCard_RoundtripsSuccessfully()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var reviewTime = new DateTimeOffset(2026, 8, 29, 11, 30, 0, TimeSpan.Zero);
            var card = Fsrs6Card.Relearning(1.5, 6.0, reviewTime, 0);

            FsrsCardStateRepository.Save(conn, cardId, card);
            var loaded = FsrsCardStateRepository.Load(conn, cardId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(Fsrs6CardState.Relearning, loaded.State);
            Assert.AreEqual(1.5, loaded.Stability!.Value, 0.0001);
            Assert.AreEqual(6.0, loaded.Difficulty!.Value, 0.0001);
            Assert.AreEqual(reviewTime, loaded.LastReviewedAtUtc!.Value);
            Assert.AreEqual(0, loaded.StepIndex);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_SaveAndLoad_NullableDueAtUtc_RoundtripsSuccessfully()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            // Null due
            var cardNoDue = Fsrs6Card.New(null);
            FsrsCardStateRepository.Save(conn, cardId, cardNoDue);
            Assert.IsNull(FsrsCardStateRepository.Load(conn, cardId)!.DueAtUtc);

            // Non-null due
            var dueUtc = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
            var cardWithDue = Fsrs6Card.New(dueUtc);
            FsrsCardStateRepository.Save(conn, cardId, cardWithDue);
            var loaded = FsrsCardStateRepository.Load(conn, cardId)!;
            Assert.AreEqual(dueUtc, loaded.DueAtUtc!.Value);
            Assert.AreEqual(TimeSpan.Zero, loaded.DueAtUtc.Value.Offset);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_SaveExistingState_UpdatesSameRow()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            FsrsCardStateRepository.Save(conn, cardId, Fsrs6Card.New());
            var count1 = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates WHERE CardId = ?", cardId);
            Assert.AreEqual(1, count1);

            var t = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
            FsrsCardStateRepository.Save(conn, cardId, Fsrs6Card.Learning(2.0, 5.0, t, 0));
            var count2 = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates WHERE CardId = ?", cardId);
            Assert.AreEqual(1, count2);

            var loaded = FsrsCardStateRepository.Load(conn, cardId)!;
            Assert.AreEqual(Fsrs6CardState.Learning, loaded.State);
            Assert.AreEqual(2.0, loaded.Stability!.Value, 0.0001);
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_InvalidCardId_ThrowsArgumentOutOfRangeException()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FsrsCardStateRepository.Load(conn, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FsrsCardStateRepository.Load(conn, -5));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FsrsCardStateRepository.Save(conn, 0, Fsrs6Card.New()));
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_CorruptedPersistedState_FailsClosed()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            // Recreate FsrsCardStates in this isolated test connection without CHECK constraints to simulate persisted corruption
            conn.Execute("DROP TABLE FsrsCardStates");
            conn.Execute("""
                CREATE TABLE FsrsCardStates (
                    CardId INTEGER PRIMARY KEY,
                    State INTEGER NOT NULL,
                    Stability REAL,
                    Difficulty REAL,
                    LastReviewedAtUtc TEXT,
                    StepIndex INTEGER,
                    DueAtUtc TEXT
                )
                """);

            // Undefined state value 99
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 99, NULL, NULL, NULL, NULL, NULL)", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsCardStateRepository.Load(conn, cardId));

            // State 1 (Learning) but StepIndex is null (violates domain invariant)
            conn.Execute("DELETE FROM FsrsCardStates WHERE CardId = ?", cardId);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 2.0, 5.0, '2026-08-29T10:00:00Z', NULL, NULL)", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsCardStateRepository.Load(conn, cardId));

            // Non-finite stability
            conn.Execute("DELETE FROM FsrsCardStates WHERE CardId = ?", cardId);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 'NaN', 5.0, '2026-08-29T10:00:00Z', 0, NULL)", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsCardStateRepository.Load(conn, cardId));
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_MalformedOrNonUtcTimestamp_FailsClosed()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            // Malformed LastReviewedAtUtc
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 2.0, 5.0, 'bad-timestamp', 0, NULL)", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsCardStateRepository.Load(conn, cardId));

            // Non-UTC DueAtUtc
            conn.Execute("DELETE FROM FsrsCardStates WHERE CardId = ?", cardId);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 0, NULL, NULL, NULL, NULL, '2026-08-29T10:00:00+05:00')", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsCardStateRepository.Load(conn, cardId));
        });
    }

    [TestMethod]
    public async Task FsrsCardStateRepository_DoesNotModifyLegacyLearningCardsColumns()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var cardRowBefore = conn.Query<LearningCardEntity>("SELECT * FROM LearningCards WHERE Id = ?", cardId).First();

            var t = new DateTimeOffset(2026, 8, 29, 15, 0, 0, TimeSpan.Zero);
            FsrsCardStateRepository.Save(conn, cardId, Fsrs6Card.Review(5.0, 4.0, t, t.AddDays(3)));

            var cardRowAfter = conn.Query<LearningCardEntity>("SELECT * FROM LearningCards WHERE Id = ?", cardId).First();
            Assert.AreEqual(cardRowBefore.State, cardRowAfter.State);
            Assert.AreEqual(cardRowBefore.EaseFactor, cardRowAfter.EaseFactor);
            Assert.AreEqual(cardRowBefore.IntervalDays, cardRowAfter.IntervalDays);
            Assert.AreEqual(cardRowBefore.DueAtUtc, cardRowAfter.DueAtUtc);
            Assert.AreEqual(cardRowBefore.LapseCount, cardRowAfter.LapseCount);
            Assert.AreEqual(cardRowBefore.SuccessfulReviewCount, cardRowAfter.SuccessfulReviewCount);
        });
    }

    // ========================================================================
    // D. FSRS Factual Review History Repository
    // ========================================================================

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_AppendEvent_AssignsSequentialNumbers()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var t1 = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
            var t2 = new DateTimeOffset(2026, 8, 29, 10, 10, 0, TimeSpan.Zero);

            var e1 = FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "stable-1", new Fsrs6ReviewEvent(t1, ReviewRating.Again));
            Assert.AreEqual(1, e1.SequenceNumber);
            Assert.AreEqual("stable-1", e1.StableId);
            Assert.AreEqual(ReviewRating.Again, e1.Event.Rating);

            var e2 = FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "stable-2", new Fsrs6ReviewEvent(t2, ReviewRating.Good));
            Assert.AreEqual(2, e2.SequenceNumber);
            Assert.AreEqual("stable-2", e2.StableId);
            Assert.AreEqual(ReviewRating.Good, e2.Event.Rating);
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_EqualTimestampEvents_AcceptedAndPreserved()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var sameTime = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

            var e1 = FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "st-1", new Fsrs6ReviewEvent(sameTime, ReviewRating.Hard));
            var e2 = FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "st-2", new Fsrs6ReviewEvent(sameTime, ReviewRating.Easy));

            Assert.AreEqual(1, e1.SequenceNumber);
            Assert.AreEqual(2, e2.SequenceNumber);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("st-1", history[0].StableId);
            Assert.AreEqual("st-2", history[1].StableId);
            Assert.AreEqual(sameTime, history[0].Event.ReviewedAtUtc);
            Assert.AreEqual(sameTime, history[1].Event.ReviewedAtUtc);
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_EqualTimestampAndRating_PreservedWhenStableIdsDiffer()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var sameTime = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

            var e1 = FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "dup-1", new Fsrs6ReviewEvent(sameTime, ReviewRating.Good));
            var e2 = FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "dup-2", new Fsrs6ReviewEvent(sameTime, ReviewRating.Good));

            Assert.AreEqual(1, e1.SequenceNumber);
            Assert.AreEqual(2, e2.SequenceNumber);

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("dup-1", history[0].StableId);
            Assert.AreEqual("dup-2", history[1].StableId);
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_DuplicateStableId_ThrowsAndRollsBack()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "stable-unique", new Fsrs6ReviewEvent(t, ReviewRating.Good));

            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "stable-unique", new Fsrs6ReviewEvent(t.AddMinutes(5), ReviewRating.Easy));
            });

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(1, history.Count);
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_EarlierTimestampThanTail_RejectedAndNotPersisted()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var tLate = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
            var tEarly = new DateTimeOffset(2026, 8, 29, 11, 0, 0, TimeSpan.Zero);

            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "ev-late", new Fsrs6ReviewEvent(tLate, ReviewRating.Good));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            {
                FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "ev-early", new Fsrs6ReviewEvent(tEarly, ReviewRating.Good));
            });

            var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE CardId = ?", cardId);
            Assert.AreEqual(1, count);
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_LoadHistory_ReturnsInSequenceNumberOrder()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "ev-1", new Fsrs6ReviewEvent(t, ReviewRating.Again));
            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "ev-2", new Fsrs6ReviewEvent(t.AddMinutes(1), ReviewRating.Hard));
            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "ev-3", new Fsrs6ReviewEvent(t.AddMinutes(2), ReviewRating.Good));

            var history = FsrsReviewHistoryRepository.LoadHistory(conn, cardId);
            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(2, history[1].SequenceNumber);
            Assert.AreEqual(3, history[2].SequenceNumber);
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_CorruptOutOfOrderHistory_FailsClosed()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            // Directly insert out-of-order timestamps into database
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('e1', ?, 1, 0, '2026-08-29T12:00:00Z')", cardId);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('e2', ?, 2, 1, '2026-08-29T11:00:00Z')", cardId);

            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsReviewHistoryRepository.LoadHistory(conn, cardId));
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_InvalidCardIdOrEmptyStableId_Throws()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
            var ev = new Fsrs6ReviewEvent(t, ReviewRating.Good);

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FsrsReviewHistoryRepository.LoadHistory(conn, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FsrsReviewHistoryRepository.AppendEvent(conn, 0, "st", ev));
            Assert.ThrowsExactly<ArgumentException>(() => FsrsReviewHistoryRepository.AppendEvent(conn, 1, "", ev));
            Assert.ThrowsExactly<ArgumentException>(() => FsrsReviewHistoryRepository.AppendEvent(conn, 1, "   ", ev));
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_MalformedTimestampOrUndefinedRating_FailsClosed()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);

            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('bad-t', ?, 1, 1, 'not-a-timestamp')", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsReviewHistoryRepository.LoadHistory(conn, cardId));

            conn.Execute("DROP TABLE FsrsReviewHistoryEntries");
            conn.Execute("""
                CREATE TABLE FsrsReviewHistoryEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StableId TEXT NOT NULL,
                    CardId INTEGER NOT NULL,
                    SequenceNumber INTEGER NOT NULL,
                    Rating INTEGER NOT NULL,
                    ReviewedAtUtc TEXT NOT NULL
                )
                """);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('bad-r', ?, 1, 99, '2026-08-29T10:00:00Z')", cardId);
            Assert.ThrowsExactly<InvalidOperationException>(() => FsrsReviewHistoryRepository.LoadHistory(conn, cardId));
        });
    }

    [TestMethod]
    public async Task FsrsReviewHistoryRepository_DoesNotTouchLegacyLearningReviews()
    {
        await using var fixture = await CreateValidSchema13DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            var (_, _, cardId) = SeedGraph(conn);
            var reviewCountBefore = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews");

            var t = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
            FsrsReviewHistoryRepository.AppendEvent(conn, cardId, "rev-1", new Fsrs6ReviewEvent(t, ReviewRating.Easy));

            var reviewCountAfter = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews");
            Assert.AreEqual(reviewCountBefore, reviewCountAfter, "FSRS history repository must never write to LearningReviews.");
        });
    }

    // ========================================================================
    // E. Dormant Boundary
    // ========================================================================

    [TestMethod]
    public async Task Schema13_Repositories_FailClosed_WhenInvokedAgainstActiveSchema12WithoutDormantTables()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);

        // Verify Schema 12 without dormant Schema 13 DDL
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            Assert.ThrowsExactly<SQLiteException>(() => WordLearningControlRepository.Load(conn, 1));
            Assert.ThrowsExactly<SQLiteException>(() => SenseLearningControlRepository.Load(conn, 1));
            Assert.ThrowsExactly<SQLiteException>(() => FsrsCardStateRepository.Load(conn, 1));
            Assert.ThrowsExactly<SQLiteException>(() => FsrsReviewHistoryRepository.LoadHistory(conn, 1));
        });
    }

    [TestMethod]
    public async Task DatabaseSchema_CurrentVersion_Remains12_AndProductionInitializeUnchanged()
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
