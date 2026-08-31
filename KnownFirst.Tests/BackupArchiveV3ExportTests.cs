using System.IO.Compression;
using System.Security.Cryptography;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class BackupArchiveV3ExportTests
{
    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

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
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
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
            conn.Execute("PRAGMA user_version = 13;");
        });

        return new TemporaryDatabaseAdapter(fixture, enableForeignKeys);
    }

    private static int InsertWord(SQLiteConnection conn, string term, string timestamp)
    {
        conn.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES ('en', ?, ?, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            term, term, timestamp, timestamp);
        return conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    [TestMethod]
    public async Task SchemaCapability_Schema13_IsRecognizedAndFutureVersionFailsClosed()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        var result = await db.ExecuteSnapshotAsync(conn => BackupSchemaCapability.Resolve(conn));

        Assert.IsNotNull(result);
        Assert.AreEqual("Schema13CapabilityResult", result.GetType().Name);

        // Verify future version 14 fails closed
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("PRAGMA user_version = 14;");
            return true;
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupSchemaCapabilityException>(() =>
            db.ExecuteSnapshotAsync(conn => BackupSchemaCapability.Resolve(conn)));
        Assert.AreEqual(14, ex.FoundVersion);
        Assert.IsFalse(ex.ShapeMismatch);
    }

    [TestMethod]
    public async Task CreatePortableArchiveAsync_FromEmptySchema13Database_ProducesValidArchiveV3()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        var service = new BackupService(db, new FakePlatformInfo());

        using var ms = new MemoryStream();
        await service.CreatePortableArchiveAsync(ms, CancellationToken.None);

        ms.Position = 0;
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(ms, CancellationToken.None);

        Assert.AreEqual(3, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V3);
        Assert.AreEqual(3, envelope.V3.Manifest.FormatVersion);
        Assert.AreEqual(13, envelope.V3.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(0, envelope.V3.Manifest.RecordCounts.WordLearningControls);
        Assert.AreEqual(0, envelope.V3.Manifest.RecordCounts.SenseLearningControls);
        Assert.AreEqual(0, envelope.V3.Manifest.RecordCounts.FsrsReviewHistoryEntries);
        Assert.AreEqual(0, envelope.V3.Manifest.RecordCounts.FsrsCardStates);
        Assert.AreEqual(0, envelope.V3.Payload.WordLearningControls.Count);
        Assert.AreEqual(0, envelope.V3.Payload.SenseLearningControls.Count);
        Assert.AreEqual(0, envelope.V3.Payload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual(0, envelope.V3.Payload.FsrsCardStates.Count);
    }

    [TestMethod]
    public async Task CreatePortableArchiveAsync_FromPopulatedSchema13Database_ProducesCompleteV3Archive()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        var fixedTime = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        await db.RunInTransactionAsync(conn =>
        {
            // Word 1: apple
            var word1Id = InsertWord(conn, "apple", "2026-08-29T10:00:00.0000000Z");

            // Word 2: banana
            var word2Id = InsertWord(conn, "banana", "2026-08-29T10:00:00.0000000Z");

            // Sibling senses for apple
            conn.Execute(
                "INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('sense-apple-1', ?, 'en', 'en', 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')",
                word1Id);
            var sense1Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute(
                "INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('sense-apple-2', ?, 'en', 'en', 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')",
                word1Id);
            var sense2Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Meaning for sense 1
            conn.Execute(
                "INSERT INTO Meanings (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId) VALUES (?, ?, 'en', 'en', 'apple', 'apple', '', 0, 'Apfel', 'a fruit', 'eat an apple', '', '[]', 'Apfel', 'test', 'test', 'test', 'test', 1, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', 'meaning-apple-1')",
                word1Id, sense1Id);
            var meaning1Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning1Id, sense1Id);

            // Meaning for sense 2
            conn.Execute(
                "INSERT INTO Meanings (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId) VALUES (?, ?, 'en', 'en', 'apple tree', 'apple tree', '', 0, 'Apfelbaum', 'the tree', 'under the apple tree', '', '[]', 'Apfelbaum', 'test', 'test', 'test', 'test', 1, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', 'meaning-apple-2')",
                word1Id, sense2Id);
            var meaning2Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning2Id, sense2Id);

            // LearningCard 1 (Forward on Sense 1)
            conn.Execute(
                "INSERT INTO LearningCards (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 0, 0, '2026-08-29T10:00:00.0000000Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')",
                word1Id, sense1Id, meaning1Id);
            var card1Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // LearningCard 2 (Reverse on Sense 1)
            conn.Execute(
                "INSERT INTO LearningCards (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 1, 0, '2026-08-29T10:00:00.0000000Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')",
                word1Id, sense1Id, meaning1Id);
            var card2Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Word-level AlreadyKnown on banana
            conn.Execute(
                "INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, '2026-08-29T10:00:00.0000000Z')",
                word2Id);

            // Sense-level StopLearning on Sense 2
            conn.Execute(
                "INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, '2026-08-29T10:00:00.0000000Z')",
                sense2Id);

            // FSRS Review History for Card 1 with two events at the exact same timestamp
            var timeIso = "2026-08-29T10:00:00.0000000Z";
            conn.Execute(
                "INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('hist-apple-1', ?, 1, 1, ?)",
                card1Id, timeIso); // Rating.Hard = 1
            conn.Execute(
                "INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('hist-apple-2', ?, 2, 2, ?)",
                card1Id, timeIso); // Rating.Good = 2

            // Replay events to get valid Fsrs6Card state for Card 1
            var events = new List<Fsrs6ReviewEvent>
            {
                new(new DateTimeOffset(fixedTime, TimeSpan.Zero), ReviewRating.Hard),
                new(new DateTimeOffset(fixedTime, TimeSpan.Zero), ReviewRating.Good)
            };
            var card1FSRS = new Fsrs6Replayer().Replay(Fsrs6Card.New(), events);

            conn.Execute(
                "INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                card1Id, (int)card1FSRS.State, card1FSRS.Stability, card1FSRS.Difficulty, timeIso, card1FSRS.StepIndex,
                card1FSRS.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(card1FSRS.DueAtUtc.Value) : null);

            // FSRS Card State for Card 2 (New)
            conn.Execute(
                "INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 0, NULL, NULL, NULL, NULL, NULL)",
                card2Id);

            // Completed LearningSession and legacy LearningReview for Card 1
            var sessionStableId = Guid.NewGuid().ToString("N");
            conn.Execute(
                "INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) VALUES (?, 1, 1, 1, 0, 0, 1, 0, '2026-08-29T09:00:00.0000000Z', '2026-08-29T09:10:00.0000000Z', '2026-08-29T09:10:00.0000000Z')",
                sessionStableId);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute(
                "INSERT INTO LearningReviews (SessionId, CardId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId) VALUES (?, ?, 2, 0, 1, '2026-08-29T09:05:00.0000000Z', '2026-08-30T09:05:00.0000000Z', 1, 2.5, NULL, NULL)",
                sessionId, card1Id);

            return true;
        });

        var service = new BackupService(db, new FakePlatformInfo());
        using var ms = new MemoryStream();
        await service.CreatePortableArchiveAsync(ms, CancellationToken.None);

        ms.Position = 0;
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(ms, CancellationToken.None);

        Assert.AreEqual(3, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V3);
        var v3 = envelope.V3;

        // Verify WordLearningControls
        Assert.AreEqual(1, v3.Payload.WordLearningControls.Count);
        var wordControl = v3.Payload.WordLearningControls[0];
        Assert.AreEqual(fixedTime, wordControl.DecidedAtUtc);
        // Owner should be banana (alphabetically second word -> v-000002)
        Assert.AreEqual("v-000002", wordControl.VocabularyId);

        // Verify SenseLearningControls
        Assert.AreEqual(1, v3.Payload.SenseLearningControls.Count);
        var senseControl = v3.Payload.SenseLearningControls[0];
        Assert.AreEqual(fixedTime, senseControl.DecidedAtUtc);

        // Verify FsrsReviewHistoryEntries
        Assert.AreEqual(2, v3.Payload.FsrsReviewHistoryEntries.Count);
        var h1 = v3.Payload.FsrsReviewHistoryEntries[0];
        var h2 = v3.Payload.FsrsReviewHistoryEntries[1];
        Assert.AreEqual("hist-apple-1", h1.StableId);
        Assert.AreEqual(1, h1.SequenceNumber);
        Assert.AreEqual(BackupReviewRating.Hard, h1.Rating);
        Assert.AreEqual(fixedTime, h1.ReviewedAtUtc);
        Assert.AreEqual("hist-apple-2", h2.StableId);
        Assert.AreEqual(2, h2.SequenceNumber);
        Assert.AreEqual(BackupReviewRating.Good, h2.Rating);
        Assert.AreEqual(fixedTime, h2.ReviewedAtUtc);
        Assert.AreEqual(h1.CardId, h2.CardId);

        // Verify FsrsCardStates
        Assert.AreEqual(2, v3.Payload.FsrsCardStates.Count);
        var stateForCard1 = v3.Payload.FsrsCardStates.First(s => s.CardId == h1.CardId);
        Assert.AreNotEqual(BackupFsrsCardStateKind.New, stateForCard1.State);
        Assert.IsNotNull(stateForCard1.Stability);
        Assert.IsNotNull(stateForCard1.Difficulty);
        Assert.AreEqual(fixedTime, stateForCard1.LastReviewedAtUtc);

        // Verify legacy LearningReviews remain separate from FSRS history
        Assert.AreEqual(1, v3.Payload.Learning.ReviewEvents.Count);
        Assert.AreEqual(2, v3.Payload.FsrsReviewHistoryEntries.Count);
    }

    [TestMethod]
    public async Task CreatePortableArchiveAsync_FromSemanticallyEquivalentDatabasesWithDifferentRowIds_IsDeterministic()
    {
        await using var db1 = await CreateValidSchema13DatabaseAsync();
        await using var db2 = await CreateValidSchema13DatabaseAsync();
        var fixedTime = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var timeIso = "2026-08-29T10:00:00.0000000Z";

        // Seed DB1: "apple" first, then "banana"
        await db1.RunInTransactionAsync(conn =>
        {
            var w1 = InsertWord(conn, "apple", "2026-08-29T10:00:00.0000000Z");
            var w2 = InsertWord(conn, "banana", "2026-08-29T10:00:00.0000000Z");

            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('se-apple', ?, 'en', 'en', 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1);
            var s1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("INSERT INTO Meanings (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId) VALUES (?, ?, 'en', 'en', 'apple', 'apple', '', 0, 'Apfel', 'fruit', '', '', '[]', 'Apfel', 't', 't', 't', 't', 1, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', 'm-apple')", w1, s1);
            var m1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", m1, s1);

            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 0, 0, '2026-08-29T10:00:00.0000000Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1, s1, m1);
            var c1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)", w2, timeIso);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)", s1, timeIso);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('hist-1', ?, 1, 2, ?)", c1, timeIso);

            var cardState = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [new Fsrs6ReviewEvent(new DateTimeOffset(fixedTime, TimeSpan.Zero), ReviewRating.Good)]);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                c1, (int)cardState.State, cardState.Stability, cardState.Difficulty, timeIso, cardState.StepIndex,
                cardState.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(cardState.DueAtUtc.Value) : null);
            return true;
        });

        // Seed DB2: "banana" first, then dummy word, then "apple", giving completely different row IDs
        await db2.RunInTransactionAsync(conn =>
        {
            var w2 = InsertWord(conn, "banana", "2026-08-29T10:00:00.0000000Z");
            var w1 = InsertWord(conn, "apple", "2026-08-29T10:00:00.0000000Z");

            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('se-apple', ?, 'en', 'en', 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1);
            var s1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("INSERT INTO Meanings (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId) VALUES (?, ?, 'en', 'en', 'apple', 'apple', '', 0, 'Apfel', 'fruit', '', '', '[]', 'Apfel', 't', 't', 't', 't', 1, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', 'm-apple')", w1, s1);
            var m1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", m1, s1);

            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 0, 0, '2026-08-29T10:00:00.0000000Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1, s1, m1);
            var c1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)", w2, timeIso);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)", s1, timeIso);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('hist-1', ?, 1, 2, ?)", c1, timeIso);

            var cardState = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [new Fsrs6ReviewEvent(new DateTimeOffset(fixedTime, TimeSpan.Zero), ReviewRating.Good)]);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                c1, (int)cardState.State, cardState.Stability, cardState.Difficulty, timeIso, cardState.StepIndex,
                cardState.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(cardState.DueAtUtc.Value) : null);
            return true;
        });

        var platform = new FakePlatformInfo();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        await new BackupService(db1, platform).CreatePortableArchiveAsync(ms1, CancellationToken.None);
        await new BackupService(db2, platform).CreatePortableArchiveAsync(ms2, CancellationToken.None);

        ms1.Position = 0;
        ms2.Position = 0;

        using var zip1 = new ZipArchive(ms1, ZipArchiveMode.Read);
        using var zip2 = new ZipArchive(ms2, ZipArchiveMode.Read);

        var dataEntry1 = zip1.GetEntry("data.json")!;
        var dataEntry2 = zip2.GetEntry("data.json")!;

        using var s1Stream = dataEntry1.Open();
        using var s2Stream = dataEntry2.Open();
        using var dataMs1 = new MemoryStream();
        using var dataMs2 = new MemoryStream();
        await s1Stream.CopyToAsync(dataMs1);
        await s2Stream.CopyToAsync(dataMs2);

        var data1Bytes = dataMs1.ToArray();
        var data2Bytes = dataMs2.ToArray();

        CollectionAssert.AreEqual(data1Bytes, data2Bytes);
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_FromSchema13Database_ProducesValidatedV3SafetyCopy()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        var fixedTime = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var timeIso = "2026-08-29T10:00:00.0000000Z";

        await db.RunInTransactionAsync(conn =>
        {
            var w1 = InsertWord(conn, "apple", "2026-08-29T10:00:00.0000000Z");
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('se-apple', ?, 'en', 'en', 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1);
            var s1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("INSERT INTO Meanings (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId) VALUES (?, ?, 'en', 'en', 'apple', 'apple', '', 0, 'Apfel', 'fruit', '', '', '[]', 'Apfel', 't', 't', 't', 't', 1, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', 'm-apple')", w1, s1);
            var m1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", m1, s1);
            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 0, 0, '2026-08-29T10:00:00.0000000Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1, s1, m1);
            var c1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)", w1, timeIso);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)", s1, timeIso);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('hist-1', ?, 1, 2, ?)", c1, timeIso);

            var cardState = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [new Fsrs6ReviewEvent(new DateTimeOffset(fixedTime, TimeSpan.Zero), ReviewRating.Good)]);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                c1, (int)cardState.State, cardState.Stability, cardState.Difficulty, timeIso, cardState.StepIndex,
                cardState.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(cardState.DueAtUtc.Value) : null);
            return true;
        });

        var safetyCopyService = new MergeSafetyCopyService(db, new FakePlatformInfo());
        var result = await safetyCopyService.CreateSafetyCopyAsync("pre-merge test", CancellationToken.None);

        Assert.AreEqual(MergeSafetyCopyStatus.Success, result.Status);
        Assert.IsNotNull(result.ArchivePath);
        Assert.IsTrue(File.Exists(result.ArchivePath));

        await using var archiveStream = File.OpenRead(result.ArchivePath);
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(3, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V3);
        Assert.AreEqual(13, envelope.V3.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(1, envelope.V3.Payload.WordLearningControls.Count);
        Assert.AreEqual(1, envelope.V3.Payload.SenseLearningControls.Count);
        Assert.AreEqual(1, envelope.V3.Payload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual(1, envelope.V3.Payload.FsrsCardStates.Count);

        // Verify metadata file
        Assert.IsNotNull(result.MetadataPath);
        Assert.IsTrue(File.Exists(result.MetadataPath));
    }

    [TestMethod]
    public async Task CreateBackupAsync_FromSchema13Database_ProducesValidV3Archive()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        var service = new BackupService(db, new FakePlatformInfo());

        using var ms = new MemoryStream();
        await service.CreateBackupAsync(ms, CancellationToken.None);

        ms.Position = 0;
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(ms, CancellationToken.None);

        Assert.AreEqual(3, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V3);
        Assert.AreEqual(13, envelope.V3.Manifest.SourceDatabaseSchemaVersion);
    }

    [TestMethod]
    public async Task CreatePortableArchiveAsync_DoesNotMutateSourceDatabase()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        await db.RunInTransactionAsync(conn =>
        {
            var w1 = InsertWord(conn, "apple", "2026-08-29T10:00:00.0000000Z");
            conn.Execute("INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES ('se-apple', ?, 'en', 'en', 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1);
            var s1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("INSERT INTO Meanings (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId) VALUES (?, ?, 'en', 'en', 'apple', 'apple', '', 0, 'Apfel', 'fruit', '', '', '[]', 'Apfel', 't', 't', 't', 't', 1, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z', 'm-apple')", w1, s1);
            var m1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", m1, s1);
            conn.Execute("INSERT INTO LearningCards (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 0, 0, '2026-08-29T10:00:00.0000000Z', 0, 2.5, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')", w1, s1, m1);
            var c1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, '2026-08-29T10:00:00.0000000Z')", w1);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, '2026-08-29T10:00:00.0000000Z')", s1);
            conn.Execute("INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('hist-1', ?, 1, 2, '2026-08-29T10:00:00.0000000Z')", c1);

            var cardState = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [new Fsrs6ReviewEvent(new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero), ReviewRating.Good)]);
            conn.Execute("INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, ?, ?, ?, '2026-08-29T10:00:00.0000000Z', ?, ?)",
                c1, (int)cardState.State, cardState.Stability, cardState.Difficulty, cardState.StepIndex,
                cardState.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(cardState.DueAtUtc.Value) : null);
            return true;
        });

        int wordsBefore = await db.ExecuteSnapshotAsync(c => c.Table<WordEntity>().Count());
        int sensesBefore = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses"));
        int cardsBefore = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards"));
        int wlcBefore = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
        int slcBefore = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
        int histBefore = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        int statesBefore = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));

        var service = new BackupService(db, new FakePlatformInfo());
        using var ms = new MemoryStream();
        await service.CreatePortableArchiveAsync(ms, CancellationToken.None);

        int wordsAfter = await db.ExecuteSnapshotAsync(c => c.Table<WordEntity>().Count());
        int sensesAfter = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses"));
        int cardsAfter = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards"));
        int wlcAfter = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
        int slcAfter = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
        int histAfter = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
        int statesAfter = await db.ExecuteSnapshotAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));

        Assert.AreEqual(wordsBefore, wordsAfter);
        Assert.AreEqual(sensesBefore, sensesAfter);
        Assert.AreEqual(cardsBefore, cardsAfter);
        Assert.AreEqual(wlcBefore, wlcAfter);
        Assert.AreEqual(slcBefore, slcAfter);
        Assert.AreEqual(histBefore, histAfter);
        Assert.AreEqual(statesBefore, statesAfter);
    }

    [TestMethod]
    public async Task SchemaCapability_Schema12_RemainsValidForV2Export()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        await using var db = new TemporaryDatabaseAdapter(fixture);

        var cap = await db.ExecuteSnapshotAsync(conn => BackupSchemaCapability.Resolve(conn));
        Assert.IsInstanceOfType<Schema12CapabilityResult>(cap);

        var service = new BackupService(db, new FakePlatformInfo());
        using var ms = new MemoryStream();
        await service.CreatePortableArchiveAsync(ms, CancellationToken.None);

        ms.Position = 0;
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(ms, CancellationToken.None);
        Assert.AreEqual(2, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V2);
    }

    [TestMethod]
    public async Task SchemaCapability_InvalidShape_FailsClosed()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DROP TABLE FsrsCardStates;");
            return true;
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupSchemaCapabilityException>(() =>
            db.ExecuteSnapshotAsync(conn => BackupSchemaCapability.Resolve(conn)));
        Assert.AreEqual(13, ex.FoundVersion);
        Assert.IsTrue(ex.ShapeMismatch);
    }

    [TestMethod]
    public async Task CreateSafetyCopyAsync_WithActiveWorkflow_IsBlocked()
    {
        await using var db = await CreateValidSchema13DatabaseAsync();
        await db.RunInTransactionAsync(conn =>
        {
            var sessionStableId = Guid.NewGuid().ToString("N");
            conn.Execute(
                "INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc) VALUES (?, 0, 0, 0, 0, 0, 0, 0, '2026-08-29T10:00:00.0000000Z', '2026-08-29T10:00:00.0000000Z')",
                sessionStableId);
            return true;
        });

        var service = new MergeSafetyCopyService(db, new FakePlatformInfo());
        var result = await service.CreateSafetyCopyAsync("blocked test", CancellationToken.None);

        Assert.AreEqual(MergeSafetyCopyStatus.BlockedByActiveWorkflow, result.Status);
        Assert.IsNull(result.ArchivePath);
    }
}
