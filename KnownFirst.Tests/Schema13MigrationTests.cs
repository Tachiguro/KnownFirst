using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13MigrationTests
{
    private static readonly string[] TargetTableNames =
    [
        Schema13Ddl.FsrsCardStatesTableName,
        Schema13Ddl.FsrsReviewHistoryEntriesTableName,
        Schema13Ddl.WordLearningControlsTableName,
        Schema13Ddl.SenseLearningControlsTableName
    ];

    private static readonly string[] TargetIndexNames =
    [
        Schema13Ddl.FsrsCardStatesDueIndexName,
        Schema13Ddl.FsrsReviewHistoryEntriesStableIdIndexName,
        Schema13Ddl.FsrsReviewHistoryEntriesCardSequenceIndexName,
        Schema13Ddl.FsrsReviewHistoryEntriesReplayIndexName
    ];

    private static async Task<Schema7Fixture> CreateValidSchema12DatabaseAsync(bool enableForeignKeys = false)
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        if (enableForeignKeys)
        {
            await fixture.Connection.ExecuteAsync("PRAGMA foreign_keys = ON");
        }

        return fixture;
    }

    private static (int WordId, int SenseId, int MeaningId, int CardId) SeedLearningGraph(
        SQLiteConnection connection,
        string term,
        WordStatus status,
        DateTime updatedAtUtc,
        string senseStableId,
        string meaningStableId)
    {
        connection.Execute(
            """
            INSERT INTO Words (
                Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
            VALUES ('en', ?, ?, ?, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            term,
            term,
            (int)status,
            updatedAtUtc,
            updatedAtUtc);
        var wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO Senses (
                StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, 'en', 'en', 0, ?, ?)
            """,
            senseStableId,
            wordId,
            updatedAtUtc,
            updatedAtUtc);
        var senseId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO Meanings (
                WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
            VALUES (?, ?, 'en', 'en', ?, ?, '', 0, 'meaning', 'definition', 'example', '', '[]',
                    'meaning', 'test', 'test', 'title', 'attribution', 1, ?, ?, ?, ?)
            """,
            wordId,
            senseId,
            term,
            term,
            updatedAtUtc,
            updatedAtUtc,
            updatedAtUtc,
            meaningStableId);
        var meaningId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
        connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

        connection.Execute(
            """
            INSERT INTO LearningCards (
                WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, 0, 0, ?, 0, 2.5, 0, 0, ?, ?)
            """,
            wordId,
            senseId,
            meaningId,
            updatedAtUtc,
            updatedAtUtc,
            updatedAtUtc);
        var cardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        return (wordId, senseId, meaningId, cardId);
    }

    private static void SeedPopulatedSchema12(SQLiteConnection connection)
    {
        var knownAt = new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc);
        var reviewedAt = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var known = SeedLearningGraph(
            connection,
            "known",
            WordStatus.Known,
            knownAt,
            new string('a', 32),
            new string('b', 32));
        var reviewed = SeedLearningGraph(
            connection,
            "reviewed",
            WordStatus.Unreviewed,
            reviewedAt,
            new string('c', 32),
            new string('d', 32));

        connection.Execute(
            """
            INSERT INTO LearningSessions (
                StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, 1, 2, 2, 0, 0, 2, 0, ?, ?, ?)
            """,
            new string('e', 32),
            reviewedAt,
            reviewedAt,
            reviewedAt);
        var sessionId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO LearningSessionCards (
                StableId, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed,
                SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc)
            VALUES (?, ?, ?, 0, 1, 0, 1, 0, 0, 1, 2, ?)
            """,
            new string('f', 32),
            sessionId,
            known.CardId,
            reviewedAt);
        connection.Execute(
            """
            INSERT INTO LearningSessionCards (
                StableId, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed,
                SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc)
            VALUES (?, ?, ?, 1, 1, 0, 1, 0, 0, 1, 2, ?)
            """,
            new string('1', 32),
            sessionId,
            reviewed.CardId,
            reviewedAt);

        connection.Execute(
            "INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)",
            sessionId,
            reviewed.CardId,
            reviewedAt);
        connection.Execute(
            "INSERT INTO LearningReviews (SessionId, CardId, Rating, ReviewedAtUtc) VALUES (?, ?, 2, ?)",
            sessionId,
            reviewed.CardId,
            reviewedAt);

        connection.Execute(
            """
            INSERT INTO LearningDayState (
                Id, Phase, DayOrdinal, ActiveDayStartUtc, ActiveDayEndUtc, FrozenTimeZoneId,
                FrozenCutoffMinutes, UpdatedAtUtc)
            VALUES (1, 1, 7, ?, ?, 'UTC', 0, ?)
            """,
            reviewedAt.Date,
            reviewedAt.Date.AddDays(1),
            reviewedAt);
        connection.Execute(
            "INSERT INTO LearningDayGrants (DayOrdinal, WordId, SlotOrdinal, GrantedAtUtc) VALUES (7, ?, 0, ?)",
            reviewed.WordId,
            reviewedAt);
    }

    private static string[] CaptureSourceState(SQLiteConnection connection) =>
        CaptureTables(connection, excludeSchema13Targets: true);

    private static string[] CaptureTargetState(SQLiteConnection connection) =>
        CaptureTables(connection, excludeSchema13Targets: false, includeOnlySchema13Targets: true);

    private static string[] CaptureTables(
        SQLiteConnection connection,
        bool excludeSchema13Targets,
        bool includeOnlySchema13Targets = false)
    {
        var targetTables = TargetTableNames.ToHashSet(StringComparer.Ordinal);
        var tables = connection.Query<MasterRow>(
                "SELECT name AS Name, COALESCE(sql, '') AS Sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
            .Where(table => includeOnlySchema13Targets
                ? targetTables.Contains(table.Name)
                : !excludeSchema13Targets || !targetTables.Contains(table.Name))
            .ToArray();

        var snapshot = new List<string>();
        foreach (var table in tables)
        {
            snapshot.Add($"table|{table.Name}|{table.Sql}");
            var escapedTable = EscapeIdentifier(table.Name);
            var columns = connection.Query<ColumnRow>($"PRAGMA table_info(\"{escapedTable}\")")
                .OrderBy(column => column.Cid)
                .ToArray();
            snapshot.AddRange(columns.Select(column =>
                $"column|{table.Name}|{column.Cid}|{column.Name}|{column.Type}|{column.NotNull}|{column.DefaultValue}|{column.Pk}"));

            if (columns.Length > 0)
            {
                var rowExpression = string.Join(
                    " || char(31) || ",
                    columns.Select(column => $"COALESCE(quote(\"{EscapeIdentifier(column.Name)}\"), 'NULL')"));
                var rows = connection.Query<ValueRow>(
                    $"SELECT {rowExpression} AS Value FROM \"{escapedTable}\" ORDER BY rowid");
                snapshot.AddRange(rows.Select((row, ordinal) => $"row|{table.Name}|{ordinal}|{row.Value}"));
            }
        }

        if (includeOnlySchema13Targets)
        {
            foreach (var indexName in TargetIndexNames)
            {
                var sql = connection.ExecuteScalar<string?>(
                    "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = ?",
                    indexName);
                snapshot.Add($"index|{indexName}|{sql ?? "<missing>"}");
            }
        }

        return [.. snapshot];
    }

    private static int CountTargetArtifacts(SQLiteConnection connection) =>
        TargetTableNames.Sum(name => connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", name))
        + TargetIndexNames.Sum(name => connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", name));

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    [TestMethod]
    public async Task Schema13DormantMigration_ApplyAsync_MigratesPopulatedSchema12Database()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        string[] sourceBefore = null!;
        await fixture.Connection.RunInTransactionAsync(connection => sourceBefore = CaptureSourceState(connection));

        var result = await Schema13DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema13MigrationOutcome.Migrated, result.Outcome);
        Assert.AreEqual(12, result.SourceVersion);
        Assert.AreEqual(13, result.TargetVersion);
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.IsTrue(Schema13ShapeValidator.IsValidDatabase(connection, out var shapeFailure), shapeFailure);
            Assert.IsTrue(Schema13MigrationIntegrityValidator.Validate(connection, out var integrityFailure), integrityFailure);
            Assert.AreEqual(8, CountTargetArtifacts(connection));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
            Assert.AreEqual(
                Schema13TimestampCodec.FormatUtc(new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc)),
                connection.ExecuteScalar<string>(
                    """
                    SELECT controls.DecidedAtUtc
                    FROM WordLearningControls controls
                    JOIN Words words ON words.Id = controls.WordId
                    WHERE words.CanonicalTerm = 'known'
                    """));
            Assert.AreEqual(
                1,
                connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(*)
                    FROM FsrsCardStates states
                    JOIN LearningCards cards ON cards.Id = states.CardId
                    JOIN Words words ON words.Id = cards.WordId
                    WHERE words.CanonicalTerm = 'known'
                      AND states.State = 0
                      AND states.Stability IS NULL
                      AND states.Difficulty IS NULL
                      AND states.LastReviewedAtUtc IS NULL
                      AND states.StepIndex IS NULL
                      AND states.DueAtUtc IS NULL
                    """));

            var history = connection.Query<HistoryRow>(
                "SELECT StableId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries ORDER BY SequenceNumber");
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(2, history[1].SequenceNumber);
            Assert.AreEqual(2, history[0].Rating);
            Assert.AreEqual(2, history[1].Rating);
            Assert.AreNotEqual(history[0].StableId, history[1].StableId);
            Assert.AreEqual(history[0].ReviewedAtUtc, history[1].ReviewedAtUtc);
            var reviewedAt = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(
                Schema13HistoricalReviewStableIdPolicy.Compute(
                    new string('c', 32), CardDirection.TermToMeaning, reviewedAt, ReviewRating.Good, 0),
                history[0].StableId);
            Assert.AreEqual(
                Schema13HistoricalReviewStableIdPolicy.Compute(
                    new string('c', 32), CardDirection.TermToMeaning, reviewedAt, ReviewRating.Good, 1),
                history[1].StableId);

            CollectionAssert.AreEqual(sourceBefore, CaptureSourceState(connection));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessions"));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessionCards"));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningDayState"));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningDayGrants"));
        });
    }

    [TestMethod]
    public async Task Schema13DormantMigration_ApplyAsync_MigratesEmptySchema12Database()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();

        var result = await Schema13DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema13MigrationOutcome.Migrated, result.Outcome);
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(8, CountTargetArtifacts(connection));
            foreach (var table in TargetTableNames)
            {
                Assert.AreEqual(0, connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{EscapeIdentifier(table)}\""));
            }

            Assert.IsTrue(Schema13ShapeValidator.IsValidDatabase(connection, out var shapeFailure), shapeFailure);
            Assert.IsTrue(Schema13MigrationIntegrityValidator.Validate(connection, out var integrityFailure), integrityFailure);
        });
    }

    [TestMethod]
    public async Task Schema13DormantMigration_ApplyAsync_IsIdempotent()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        string[] before = null!;
        await fixture.Connection.RunInTransactionAsync(connection => before = CaptureTargetState(connection));

        var result = await Schema13DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema13MigrationOutcome.AlreadyApplied, result.Outcome);
        Assert.AreEqual(13, result.SourceVersion);
        Assert.AreEqual(13, result.TargetVersion);
        Assert.AreEqual(13, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection =>
            CollectionAssert.AreEqual(before, CaptureTargetState(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_AlreadyAppliedInvalidShape_FailsWithoutRepair()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync($"DROP INDEX {Schema13Ddl.FsrsCardStatesDueIndexName}");
        string[] before = null!;
        await fixture.Connection.RunInTransactionAsync(connection => before = CaptureTargetState(connection));

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-already-applied-shape-invalid", exception.ErrorCode);
        await fixture.Connection.RunInTransactionAsync(connection =>
            CollectionAssert.AreEqual(before, CaptureTargetState(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_AlreadyAppliedInvalidRuntimeState_FailsWithoutRepair()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync(
            "UPDATE FsrsCardStates SET DueAtUtc = '2000-01-01T00:00:00.0000000Z'");
        string[] before = null!;
        await fixture.Connection.RunInTransactionAsync(connection => before = CaptureTargetState(connection));

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-already-applied-shape-invalid", exception.ErrorCode);
        await fixture.Connection.RunInTransactionAsync(connection =>
            CollectionAssert.AreEqual(before, CaptureTargetState(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_FutureVersion_FailsBeforeMutation()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 14");

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-future-version", exception.ErrorCode);
        Assert.AreEqual(14, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection => Assert.AreEqual(0, CountTargetArtifacts(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_UnsupportedSourceVersion_FailsBeforeMutation()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 11");

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-unsupported-source-version", exception.ErrorCode);
        Assert.AreEqual(11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection => Assert.AreEqual(0, CountTargetArtifacts(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_Version12PartialTargetState_FailsClosed()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.ExecuteAsync(Schema13Ddl.CreateSenseLearningControlsTable);

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-invariant-violation", exception.ErrorCode);
        Assert.AreEqual(12, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(1, CountTargetArtifacts(connection));
            Assert.AreEqual(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?",
                Schema13Ddl.SenseLearningControlsTableName));
        });
    }

    [TestMethod]
    public async Task Schema13DormantMigration_InvalidSchema12SourceShape_FailsBeforeTargetMutation()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.ExecuteAsync("DROP INDEX IX_LearningDayGrants_DayOrdinal");

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-invariant-violation", exception.ErrorCode);
        Assert.AreEqual(12, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection => Assert.AreEqual(0, CountTargetArtifacts(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_ProgressedHistorylessCard_RollsBackTargetDdl_AndRetriesDeterministically()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync();
        await fixture.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        await fixture.Connection.ExecuteAsync(
            """
            UPDATE LearningCards
            SET SuccessfulReviewCount = 1
            WHERE WordId = (SELECT Id FROM Words WHERE CanonicalTerm = 'known')
            """);
        string[] corruptSourceBefore = null!;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.IsTrue(KnownFirst.Data.Migrations.Schema12.Schema12ShapeValidator.IsValidDatabase(
                connection, out var sourceFailure), sourceFailure);
            corruptSourceBefore = CaptureSourceState(connection);
        });

        var exception = await Assert.ThrowsExactlyAsync<Schema13MigrationException>(
            () => Schema13DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema13-migration-missing-review-history", exception.ErrorCode);
        Assert.AreEqual(12, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(0, CountTargetArtifacts(connection),
                "The failure occurs after canonical target DDL execution begins, so zero surviving artifacts proves transactional DDL rollback.");
            CollectionAssert.AreEqual(corruptSourceBefore, CaptureSourceState(connection));
        });

        await fixture.Connection.ExecuteAsync(
            """
            UPDATE LearningCards
            SET SuccessfulReviewCount = 0
            WHERE WordId = (SELECT Id FROM Words WHERE CanonicalTerm = 'known')
            """);
        var retry = await Schema13DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema13MigrationOutcome.Migrated, retry.Outcome);
        string[] retryTarget = null!;
        await fixture.Connection.RunInTransactionAsync(connection => retryTarget = CaptureTargetState(connection));

        await using var fresh = await CreateValidSchema12DatabaseAsync();
        await fresh.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        await Schema13DormantMigration.ApplyAsync(fresh.Connection);
        await fresh.Connection.RunInTransactionAsync(connection =>
            CollectionAssert.AreEqual(retryTarget, CaptureTargetState(connection)));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_WithForeignKeysEnabled_ProducesNoViolations()
    {
        await using var fixture = await CreateValidSchema12DatabaseAsync(enableForeignKeys: true);
        await fixture.Connection.RunInTransactionAsync(SeedPopulatedSchema12);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys"));

        await Schema13DormantMigration.ApplyAsync(fixture.Connection);

        var violations = await fixture.Connection.QueryAsync<ForeignKeyViolationRow>("PRAGMA foreign_key_check");
        Assert.AreEqual(0, violations.Count);
    }

    [TestMethod]
    public async Task Schema13DormantMigration_ProductionInitializationCreatesSchema13WithoutDormantUpgrade()
    {
        Assert.AreEqual(13, DatabaseSchema.CurrentVersion);
        await using var database = new DatabaseSchema13ProductionCutoverTests.ProductionInitializedDatabase();
        await database.InitializeAsync();

        Assert.AreEqual(13, await database.ReadAsync(connection =>
            connection.ExecuteScalarAsync<int>("PRAGMA user_version")));
        Assert.AreEqual(8, await database.ExecuteSnapshotAsync(CountTargetArtifacts));
    }

    [TestMethod]
    public async Task Schema13DormantMigration_NullConnection_ThrowsArgumentNullException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => Schema13DormantMigration.ApplyAsync(null!));
    }

    private sealed class HistoryRow
    {
        public string StableId { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }

    private sealed class MasterRow
    {
        public string Name { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }

    private sealed class ColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        [Column("notnull")]
        public int NotNull { get; set; }
        [Column("dflt_value")]
        public string? DefaultValue { get; set; }
        public int Pk { get; set; }
    }

    private sealed class ValueRow
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyViolationRow
    {
        [Column("table")]
        public string Table { get; set; } = string.Empty;
        [Column("rowid")]
        public long RowId { get; set; }
        [Column("parent")]
        public string Parent { get; set; } = string.Empty;
        [Column("fkid")]
        public int ForeignKeyId { get; set; }
    }
}
