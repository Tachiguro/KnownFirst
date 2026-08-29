using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13ShapeValidatorTests
{
    private static async Task<Schema7Fixture> CreateValidSchema12DatabaseAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        return fixture;
    }

    private static void ApplySchema13Ddl(SQLiteConnection connection)
    {
        connection.Execute(Schema13Ddl.CreateFsrsCardStatesTable);
        connection.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
        connection.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
        connection.Execute(Schema13Ddl.CreateWordLearningControlsTable);
        connection.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
    }

    [TestMethod]
    public async Task Schema13_DormantShape_AppliesCleanly_OverSchema12()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();

        await fixture.Connection.RunInTransactionAsync(ApplySchema13Ddl);

        var version = await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(12, version, "Dormant Schema 13 physical tables must not bump PRAGMA user_version to 13.");

        var isValid = false;
        string? failure = null;
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            isValid = Schema13ShapeValidator.IsValidDatabase(conn, out failure);
        });

        Assert.IsTrue(isValid, $"Dormant Schema 13 shape should be valid: {failure}");
    }

    [TestMethod]
    public async Task ProductionDatabase_DoesNotCreateSchema13Tables()
    {
        Assert.AreEqual(12, DatabaseSchema.CurrentVersion, "Production CurrentVersion must remain exactly 12.");

        await using var fixture = await CreateValidSchema12DatabaseAsync();

        var version = await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(12, version);

        foreach (var table in new[] { Schema13Ddl.FsrsCardStatesTableName, Schema13Ddl.FsrsReviewHistoryEntriesTableName, Schema13Ddl.WordLearningControlsTableName, Schema13Ddl.SenseLearningControlsTableName })
        {
            var count = await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table);
            Assert.AreEqual(0, count, $"Production initialization must not create dormant table {table}.");
        }
    }

    [TestMethod]
    public async Task Schema13_TableAndColumnAffinity_Validation()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(ApplySchema13Ddl);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            Assert.IsTrue(Schema13ShapeValidator.IsValidDatabase(conn, out var failure), failure);
        });

        var cardStateCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", Schema13Ddl.FsrsCardStatesTableName);
        Assert.AreEqual(1, cardStateCount);

        var historyCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", Schema13Ddl.FsrsReviewHistoryEntriesTableName);
        Assert.AreEqual(1, historyCount);

        var wordControlCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", Schema13Ddl.WordLearningControlsTableName);
        Assert.AreEqual(1, wordControlCount);

        var senseControlCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", Schema13Ddl.SenseLearningControlsTableName);
        Assert.AreEqual(1, senseControlCount);
    }

    [TestMethod]
    public async Task Schema13_ForeignKeys_ExistWithCascadeDelete()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(ApplySchema13Ddl);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            AssertForeignKeys(conn, Schema13Ddl.FsrsCardStatesTableName, "LearningCards", "CardId", "Id");
            AssertForeignKeys(conn, Schema13Ddl.FsrsReviewHistoryEntriesTableName, "LearningCards", "CardId", "Id");
            AssertForeignKeys(conn, Schema13Ddl.WordLearningControlsTableName, "Words", "WordId", "Id");
            AssertForeignKeys(conn, Schema13Ddl.SenseLearningControlsTableName, "Senses", "SenseId", "Id");
        });
    }

    private static void AssertForeignKeys(SQLiteConnection conn, string table, string parentTable, string fromColumn, string toColumn)
    {
        var fks = conn.Query<ForeignKeyPragmaRow>($"PRAGMA foreign_key_list(\"{table}\")");
        var fk = fks.FirstOrDefault(f => string.Equals(f.Table, parentTable, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(f.From, fromColumn, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(f.To, toColumn, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(fk, $"Table {table} must have FK on ({fromColumn}) -> {parentTable}({toColumn}).");
        Assert.AreEqual("CASCADE", fk.On_delete?.ToUpperInvariant(), $"FK on {table}.{fromColumn} must declare ON DELETE CASCADE.");
    }

    [TestMethod]
    public async Task Schema13_FsrsCardStates_Constraints()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(ApplySchema13Ddl);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            // Seed a word and learning card in Schema 12 baseline
            conn.Execute("INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, CreatedAt, UpdatedAt) VALUES ('en', 'test', 'test', '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')");
            var wordId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('s-1', ?, 'en', 'en', 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId);
            var senseId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 0, 0, '2026-08-29T10:00:00Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId, senseId);
            var cardId1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 1, 0, '2026-08-29T10:00:00Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId, senseId);
            var cardId2 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // State 0: New (all active fields null) - valid
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 0, NULL, NULL, NULL, NULL, '2026-08-30T10:00:00Z')", cardId1);

            // State 0: New with stability - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 0, 1.0, NULL, NULL, NULL, NULL)", cardId2);
            });

            // State 1: Learning - valid
            conn.Execute("DELETE FROM FsrsCardStates WHERE CardId = ?", cardId1);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 2.3, 4.5, '2026-08-29T11:00:00Z', 0, NULL)", cardId1);

            // State 1: Learning with StepIndex != 0 - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 2.3, 4.5, '2026-08-29T11:00:00Z', 1, NULL)", cardId2);
            });

            // State 2: Review - valid
            conn.Execute("DELETE FROM FsrsCardStates WHERE CardId = ?", cardId1);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 2, 5.0, 3.2, '2026-08-29T11:00:00Z', NULL, '2026-09-05T11:00:00Z')", cardId1);

            // State 2: Review with StepIndex non-null - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 2, 5.0, 3.2, '2026-08-29T11:00:00Z', 0, NULL)", cardId2);
            });

            // State 3: Relearning - valid
            conn.Execute("DELETE FROM FsrsCardStates WHERE CardId = ?", cardId1);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 3, 1.5, 6.0, '2026-08-29T11:00:00Z', 0, NULL)", cardId1);

            // State 4: Undefined state - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 4, 1.5, 6.0, '2026-08-29T11:00:00Z', 0, NULL)", cardId2);
            });

            // Stability below minimum (0.001) - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 0.0001, 5.0, '2026-08-29T11:00:00Z', 0, NULL)", cardId2);
            });

            // Difficulty below 1.0 - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 2.0, 0.9, '2026-08-29T11:00:00Z', 0, NULL)", cardId2);
            });

            // Difficulty above 10.0 - invalid
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 1, 2.0, 10.1, '2026-08-29T11:00:00Z', 0, NULL)", cardId2);
            });
        });
    }

    [TestMethod]
    public async Task Schema13_FsrsReviewHistoryEntries_Constraints()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(ApplySchema13Ddl);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            conn.Execute("INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, CreatedAt, UpdatedAt) VALUES ('en', 'test', 'test', '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')");
            var wordId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('s-1', ?, 'en', 'en', 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId);
            var senseId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 0, 0, '2026-08-29T10:00:00Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId, senseId);
            var cardId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Rating 0 (Again), 1 (Hard), 2 (Good), 3 (Easy) accepted
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-1', ?, 1, 0, '2026-08-29T10:00:00Z')", cardId);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-2', ?, 2, 1, '2026-08-29T10:10:00Z')", cardId);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-3', ?, 3, 2, '2026-08-29T10:20:00Z')", cardId);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-4', ?, 4, 3, '2026-08-29T10:30:00Z')", cardId);

            // Equal timestamps allowed with distinct SequenceNumber and StableId
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-5', ?, 5, 2, '2026-08-29T10:30:00Z')", cardId);

            // Rating -1 rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-bad-1', ?, 6, -1, '2026-08-29T11:00:00Z')", cardId);
            });

            // Rating 4 rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-bad-2', ?, 6, 4, '2026-08-29T11:00:00Z')", cardId);
            });

            // Duplicate StableId rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-1', ?, 6, 2, '2026-08-29T11:00:00Z')", cardId);
            });

            // Duplicate (CardId, SequenceNumber) rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-6', ?, 1, 2, '2026-08-29T11:00:00Z')", cardId);
            });

            // SequenceNumber <= 0 rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('ev-7', ?, 0, 2, '2026-08-29T11:00:00Z')", cardId);
            });

            // Empty StableId rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('', ?, 7, 2, '2026-08-29T11:00:00Z')", cardId);
            });
        });
    }

    [TestMethod]
    public async Task Schema13_LearningControls_Constraints()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(ApplySchema13Ddl);

        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            conn.Execute("INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, CreatedAt, UpdatedAt) VALUES ('en', 'test', 'test', '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')");
            var wordId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('s-1', ?, 'en', 'en', 0, '2026-08-29T10:00:00Z', '2026-08-29T10:00:00Z')", wordId);
            var senseId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // WordLearningControls: 1 per word
            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, '2026-08-29T12:00:00Z')", wordId);

            // Duplicate WordId rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, '2026-08-29T13:00:00Z')", wordId);
            });

            // SenseLearningControls: 1 per sense
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, '2026-08-29T12:00:00Z')", senseId);

            // Duplicate SenseId rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, '2026-08-29T13:00:00Z')", senseId);
            });

            // Empty DecidedAtUtc rejected
            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (999, '')");
            });

            Assert.ThrowsExactly<SQLiteException>(() =>
            {
                conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (999, '')");
            });
        });
    }

    [TestMethod]
    public async Task Schema13_ShapeValidator_FailsClosed_OnMalformedShapes()
    {
        // 1. Missing table: FsrsCardStates missing
        await using (var fixture = await CreateValidSchema12DatabaseAsync())
        {
            await fixture.Connection.RunInTransactionAsync(conn =>
            {
                conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
                conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
                conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
                var valid = Schema13ShapeValidator.IsValidDatabase(conn, out var failure);
                Assert.IsFalse(valid);
                Assert.IsTrue(failure?.Contains("FsrsCardStates") == true);
            });
        }

        // 2. Missing required column in FsrsCardStates
        await using (var fixture = await CreateValidSchema12DatabaseAsync())
        {
            await fixture.Connection.RunInTransactionAsync(conn =>
            {
                ApplySchema13Ddl(conn);
                conn.Execute("DROP TABLE FsrsCardStates");
                conn.Execute("CREATE TABLE FsrsCardStates (CardId INTEGER PRIMARY KEY, State INTEGER NOT NULL)");
                var valid = Schema13ShapeValidator.IsValidDatabase(conn, out var failure);
                Assert.IsFalse(valid);
                Assert.IsTrue(failure?.Contains("Stability") == true);
            });
        }

        // 3. Missing index on FsrsReviewHistoryEntries
        await using (var fixture = await CreateValidSchema12DatabaseAsync())
        {
            await fixture.Connection.RunInTransactionAsync(conn =>
            {
                conn.Execute(Schema13Ddl.CreateFsrsCardStatesTable);
                conn.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);
                conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
                // Omit indexes
                conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
                conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);
                var valid = Schema13ShapeValidator.IsValidDatabase(conn, out var failure);
                Assert.IsFalse(valid);
                Assert.IsTrue(failure?.Contains("index") == true || failure?.Contains("Index") == true);
            });
        }
    }

    [TestMethod]
    public async Task Schema13_ShapeValidator_RejectsMateriallyWeakenedCheckConstraints()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
            conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
            conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);

            conn.Execute("""
                CREATE TABLE FsrsCardStates (
                    CardId INTEGER PRIMARY KEY,
                    State INTEGER NOT NULL,
                    Stability REAL,
                    Difficulty REAL,
                    LastReviewedAtUtc TEXT,
                    StepIndex INTEGER,
                    DueAtUtc TEXT,
                    FOREIGN KEY (CardId) REFERENCES LearningCards(Id) ON DELETE CASCADE,
                    CHECK (1 = 1)
                )
                """);
            conn.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);

            var isValid = Schema13ShapeValidator.IsValidDatabase(conn, out var failureDetail);
            Assert.IsFalse(isValid, "Validator must reject FsrsCardStates when required domain CHECK constraints are replaced with dummy CHECK (1 = 1).");
            Assert.IsNotNull(failureDetail);
        });
    }

    [TestMethod]
    public async Task Schema13_ShapeValidator_RejectsIncorrectForeignKey()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(conn =>
        {
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesTable);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesStableIdIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesCardSequenceIndex);
            conn.Execute(Schema13Ddl.CreateFsrsReviewHistoryEntriesReplayIndex);
            conn.Execute(Schema13Ddl.CreateWordLearningControlsTable);
            conn.Execute(Schema13Ddl.CreateSenseLearningControlsTable);

            // Create FsrsCardStates with FK missing ON DELETE CASCADE
            conn.Execute("""
                CREATE TABLE FsrsCardStates (
                    CardId INTEGER PRIMARY KEY,
                    State INTEGER NOT NULL,
                    Stability REAL,
                    Difficulty REAL,
                    LastReviewedAtUtc TEXT,
                    StepIndex INTEGER,
                    DueAtUtc TEXT,
                    FOREIGN KEY (CardId) REFERENCES LearningCards(Id),
                    CHECK (State IN (0, 1, 2, 3))
                )
                """);
            conn.Execute(Schema13Ddl.CreateFsrsCardStatesDueIndex);

            var isValid = Schema13ShapeValidator.IsValidDatabase(conn, out var failureDetail);
            Assert.IsFalse(isValid, "Validator must reject FsrsCardStates when foreign key is missing ON DELETE CASCADE.");
            Assert.IsTrue(failureDetail?.Contains("ON DELETE CASCADE") == true);
        });
    }

    private sealed class ForeignKeyPragmaRow
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string Table { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string? On_update { get; set; }
        public string? On_delete { get; set; }
        public string? Match { get; set; }
    }
}
