using System.IO.Compression;
using System.Text.Json;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnownFirst.Tests;

[TestClass]
public class BackupCreationTests
{
    private class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.2.3";
    }

    [TestMethod]
    public async Task CreateBackup_WithSyntheticDatabase_ProducesTwoEntryZipWithCorrectChecksum()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity
                {
                    Title = "Test Doc",
                    TextLanguage = "en",
                    ExplanationLanguage = "de",
                    Content = "Test content.",
                    ContentFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Test content."))).ToLowerInvariant(),
                    LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();
            await service.CreateBackupAsync(ms, CancellationToken.None);

            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            Assert.HasCount(2, zip.Entries);

            var manifestEntry = zip.GetEntry("manifest.json");
            Assert.IsNotNull(manifestEntry);

            var dataEntry = zip.GetEntry("data.json");
            Assert.IsNotNull(dataEntry);

            using var dataStream = dataEntry.Open();
            using var dataMs = new MemoryStream();
            await dataStream.CopyToAsync(dataMs);
            var dataBytes = dataMs.ToArray();

            var dataHash = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dataBytes)).ToLowerInvariant();

            using var manifestStream = manifestEntry.Open();
            var manifestBytes = new byte[manifestEntry.Length];
            int read = await manifestStream.ReadAsync(manifestBytes);

            var manifest = BackupJsonCodec.DeserializeManifest(manifestBytes.AsSpan(0, read));

            Assert.IsNotNull(manifest);
            Assert.AreEqual(dataHash, manifest.DataChecksum);
            Assert.AreEqual(1, manifest.RecordCounts.SourceMaterials);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_GuaranteesConsistentSnapshotAcrossTables()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity
                {
                    Title = "Doc 1",
                    TextLanguage = "en",
                    ExplanationLanguage = "de",
                    Content = "c1",
                    ContentFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("c1"))).ToLowerInvariant(),
                    LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                });
                conn.Insert(new WordEntity
                {
                    CanonicalTerm = "word1",
                    NormalizedTerm = "word1",
                    Language = "en"
                });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());

            await database.ExecuteSnapshotAsync(conn =>
            {
                var docs = conn.Table<DocumentEntity>().ToList();

                try
                {
                    using var db2 = new SQLite.SQLiteConnection(database.DatabasePath);
                    db2.Insert(new DocumentEntity
                    {
                        Title = "Doc 2",
                        TextLanguage = "en",
                        ExplanationLanguage = "de",
                        Content = "c2",
                        ContentFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("c2"))).ToLowerInvariant(),
                        LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                    });
                    db2.Insert(new WordEntity
                    {
                        CanonicalTerm = "word2",
                        NormalizedTerm = "word2",
                        Language = "en"
                    });
                }
                catch (SQLite.SQLiteException ex) when (ex.Message.Contains("locked") || ex.Message.Contains("busy"))
                {
                    // Database lock during transaction proves snapshot transaction boundary
                }

                var words = conn.Table<WordEntity>().ToList();

                Assert.HasCount(1, docs);
                Assert.HasCount(1, words);
                return true;
            });
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithFutureSchema_RefusesAndDoesNotMaterialize()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                conn.Execute("PRAGMA user_version = 99");
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();

            DatabaseSchemaCompatibilityException? exception = null;
            try
            {
                await service.CreateBackupAsync(ms, CancellationToken.None);
            }
            catch (DatabaseSchemaCompatibilityException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(99, exception.FoundVersion);
            Assert.AreEqual(0, ms.Length);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    /// <summary>
    /// Regression guard for the temporary-fixture reset path. sqlite-net keeps closed connections in a
    /// shared pool keyed by connection string, so deleting the database file without draining that pool can
    /// hand a stale native handle to the next open of the same path — which faulted the whole test host
    /// inside <c>sqlite3_changes</c> during <c>CreateTable</c>. Several cycles are run because the defect
    /// only appears once a pooled handle from a previous cycle survives.
    /// </summary>
    [TestMethod]
    public async Task RepeatedResetAndRecreate_LeavesNoStaleSchemaStateOrPooledHandles()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-reset-cycle");
        await database.InitializeAsync();

        for (var cycle = 0; cycle < 5; cycle++)
        {
            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(BuildCycleDocument(cycle));
                return true;
            });
            Assert.AreEqual(
                1,
                await database.ReadAsync(conn => conn.Table<DocumentEntity>().CountAsync()),
                $"Cycle {cycle} should observe exactly the document it just seeded.");

            await database.ResetAsync();

            Assert.AreEqual(
                0,
                await database.ReadAsync(conn => conn.Table<DocumentEntity>().CountAsync()),
                $"Cycle {cycle} retained stale rows across the reset.");
            Assert.AreEqual(
                7,
                await database.ReadAsync(conn => conn.ExecuteScalarAsync<int>("PRAGMA user_version")),
                $"Cycle {cycle} did not rebuild the expected fixture schema version.");
        }
    }

    /// <summary>
    /// The disposal counterpart: a released fixture must leave no handle holding its file open. On Windows a
    /// surviving pooled handle makes the delete fail with a sharing violation, so the assertion below is a
    /// direct probe for a leaked connection rather than an indirect one.
    /// </summary>
    [TestMethod]
    public async Task RepeatedCreateDisposeRecreate_ReleasesEveryDatabaseFile()
    {
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var database = new TemporaryKnownFirstDatabase("knownfirst-dispose-cycle");
            await using (database)
            {
                await database.InitializeAsync();
                await database.RunInTransactionAsync(conn =>
                {
                    conn.Insert(BuildCycleDocument(cycle));
                    return true;
                });
                Assert.AreEqual(1, await database.ReadAsync(conn => conn.Table<DocumentEntity>().CountAsync()));
            }

            Assert.IsFalse(
                File.Exists(database.DatabasePath),
                $"Cycle {cycle} left the database file behind, so a handle still referenced it.");
            Assert.IsFalse(
                File.Exists($"{database.DatabasePath}-wal"),
                $"Cycle {cycle} left a write-ahead log behind.");
            Assert.IsFalse(
                File.Exists($"{database.DatabasePath}-shm"),
                $"Cycle {cycle} left a shared-memory sidecar behind.");
        }
    }

    /// <summary>
    /// The exact shape of the crash observed in the complete suite: an operation faults while a sqlite-net
    /// transaction is open, the fixture is reset in the caller's <c>finally</c>, and the same path is opened
    /// again. Without draining the connection pool before deleting the file, the reopened database receives a
    /// stale native handle and <c>CreateTable</c> faults the test host.
    /// </summary>
    [TestMethod]
    public async Task ResetAfterFaultedTransaction_RemainsUsableAcrossCycles()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-faulted-reset");
        await database.InitializeAsync();

        for (var cycle = 0; cycle < 5; cycle++)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                database.RunInTransactionAsync<bool>(conn =>
                {
                    conn.Insert(BuildCycleDocument(cycle));
                    throw new InvalidOperationException($"injected-cycle-{cycle}");
                }));

            await database.ResetAsync();

            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(BuildCycleDocument(cycle));
                return true;
            });
            Assert.AreEqual(
                1,
                await database.ReadAsync(conn => conn.Table<DocumentEntity>().CountAsync()),
                $"Cycle {cycle} did not recover a usable database after the faulted transaction.");

            await database.ResetAsync();
        }
    }

    /// <summary>
    /// Concurrent temporary databases on distinct paths must be able to create tables, write, close, and
    /// delete independently. This is the shape a process-wide <c>SQLiteAsyncConnection.ResetPool()</c>
    /// breaks: one worker's drain closes the native handles the other workers are still using, which faults
    /// the host inside <c>sqlite3_changes</c> instead of failing cleanly. Kept deliberately small — a handful
    /// of workers doing real work concurrently is enough to expose a global drain.
    /// </summary>
    [TestMethod]
    public async Task ConcurrentTemporaryDatabases_OnDistinctPaths_DoNotDisturbEachOther()
    {
        var workers = Enumerable.Range(0, 8).Select(async worker =>
        {
            var database = new TemporaryKnownFirstDatabase($"knownfirst-parallel-{worker}");
            await using (database)
            {
                await database.InitializeAsync();

                for (var round = 0; round < 3; round++)
                {
                    await database.RunInTransactionAsync(conn =>
                    {
                        conn.Insert(BuildCycleDocument(worker * 10 + round));
                        return true;
                    });
                }

                Assert.AreEqual(
                    3,
                    await database.ReadAsync(conn => conn.Table<DocumentEntity>().CountAsync()),
                    $"Worker {worker} lost writes to a concurrently running database.");
                Assert.AreEqual(
                    $"Cycle {worker * 10}",
                    await database.ReadAsync(conn =>
                        conn.ExecuteScalarAsync<string>("SELECT Title FROM Documents ORDER BY Id LIMIT 1")),
                    $"Worker {worker} observed another worker's rows.");
            }

            Assert.IsFalse(
                File.Exists(database.DatabasePath),
                $"Worker {worker} left its database file behind.");
        });

        await Task.WhenAll(workers);
    }

    private static DocumentEntity BuildCycleDocument(int cycle)
    {
        var content = $"Cycle {cycle} content.";
        return new DocumentEntity
        {
            Title = $"Cycle {cycle}",
            TextLanguage = "en",
            ExplanationLanguage = "de",
            Content = content,
            ContentFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
            LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
        };
    }

    [TestMethod]
    public async Task CreateBackup_WithExcessiveRecordCount_RefusesBeforeFullMaterialization()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                var docs = Enumerable.Range(1, 10001).Select(i => new DocumentEntity
                {
                    Title = $"Doc {i}",
                    TextLanguage = "en",
                    ExplanationLanguage = "de",
                    Content = "c",
                    ContentFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("c"))).ToLowerInvariant(),
                    LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                });
                conn.InsertAll(docs);
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();

            BackupFormatException? exception = null;
            try
            {
                await service.CreateBackupAsync(ms, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(BackupErrorCodes.LimitExceeded, exception.Code);
            Assert.AreEqual(0, ms.Length);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithMalformedInternalJson_TranslatesToSafeBackupFormatException()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new PreparationSessionEntity
                {
                    Status = KnownFirst.Models.PreparationSessionStatus.Active,
                    Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline,
                    TotalItems = 1
                });
                conn.Insert(new PreparationCandidateEntity
                {
                    SessionId = 1,
                    WordId = 1,
                    Order = 0,
                    ResultJson = "{ INVALID_JSON_DATA }"
                });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();

            BackupFormatException? exception = null;
            try
            {
                await service.CreateBackupAsync(ms, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(BackupErrorCodes.DataJsonInvalid, exception.Code);
            Assert.DoesNotContain(exception.Message, "INVALID_JSON_DATA");
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    private class GateCheckStream(IKnownFirstDatabase database) : MemoryStream
    {
        public bool DatabaseAccessSucceeded { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                var docCount = await database.ReadAsync(conn => conn.Table<DocumentEntity>().CountAsync());
                DatabaseAccessSucceeded = true;
            }
            catch
            {
                DatabaseAccessSucceeded = false;
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    [TestMethod]
    public async Task CreateBackup_ReleasesDatabaseGateBeforeFileIO()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            var service = new BackupService(database, new FakePlatformInfo());
            using var gateStream = new GateCheckStream(database);

            await service.CreateBackupAsync(gateStream, CancellationToken.None);

            Assert.IsTrue(gateStream.DatabaseAccessSucceeded);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithCompleteSchema7Dataset_PreservesAllEntitiesAndUtf8WithoutBom()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            const string contentText = "The houses stand here.";
            var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentText))).ToLowerInvariant();

            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity { Id = 1, Title = "Doc 1", TextLanguage = "en", ExplanationLanguage = "de", Content = contentText, ContentFingerprint = contentHash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, TargetLanguage = null, ImportedAt = DateTime.UtcNow, WordCount = 1 });
                conn.Insert(new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = contentText.Length });
                conn.Insert(new WordEntity { Id = 1, Language = "en", CanonicalTerm = "houses", NormalizedTerm = "house", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, TotalOccurrenceCount = 1, DocumentCount = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, AutomaticInteractionMode = KnownFirst.Core.Learning.LearningInteractionMode.Reading, ConsecutiveRecallSuccessCount = 1, ConsecutiveTypingSuccessCount = 1, ConsecutiveTypingFailureCount = 0, MasteryReviewExtensionScheduled = false });
                conn.Insert(new WordFormEntity { Id = 1, WordId = 1, SurfaceForm = "houses", OccurrenceCount = 1 });
                conn.Insert(new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = 4, Length = 6, SurfaceForm = "houses", Order = 0, TechnicalFamily = KnownFirst.Core.Text.TechnicalTokenFamily.None, TechnicalInstanceYear = null, TechnicalInstanceIdentifier = null, TechnicalVariant = null });
                conn.Insert(new MeaningEntity { Id = 1, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "house", EncounteredSurfaceForm = "houses", GrammaticalRelationship = null, TokenKind = KnownFirst.Core.Text.TokenKind.Word, SelectedMeaningId = null, AcronymExpansion = null, Translation = "Haus", Definition = "A building for human habitation.", DictionaryExample = null, AdditionalNote = null, TranslationOrDefinition = "Haus", AcceptedAliasesJson = "[\"Häuschen\"]", ConfirmedByUser = true, Source = "dict", SourceProject = string.Empty, SourcePageTitle = string.Empty, SourceRevisionId = null, Attribution = string.Empty, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, PreparedAt = DateTime.UtcNow });
                conn.Insert(new ContextSnapshotEntity { Id = 1, MeaningId = 1, SourceDocumentId = 1, SourceDocumentTitle = "Doc 1", Text = contentText, TargetStart = 4, TargetLength = 6, NormalizedFingerprint = contentHash, CreatedAtUtc = DateTime.UtcNow });
                conn.Insert(new ReviewStateEntity { Id = 1, WordId = 1, ReviewCount = 1, ForgotCount = 0, PartialCount = 0, KnownCount = 1, LastReviewedAt = DateTime.UtcNow });
                conn.Insert(new ReviewSessionEntity { Id = 1, DocumentId = 1, Status = KnownFirst.Models.ReviewSessionStatus.Completed, TotalCandidates = 1, ReviewedCount = 1, KnownCount = 1, UnknownCount = 0, IgnoredCount = 0, DecisionSequence = 1, StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
                conn.Insert(new ReviewCandidateEntity { Id = 1, SessionId = 1, WordId = 1, Order = 0, Status = KnownFirst.Models.WordStatus.Known, PreviousWordStatus = KnownFirst.Models.WordStatus.Unreviewed, PreviousTotalOccurrenceCount = 1, PreviousDocumentCount = 1, PreviousUpdatedAt = DateTime.UtcNow, DecisionSequence = 1, WasWordCreatedForSession = false, DecidedAt = DateTime.UtcNow });
                conn.Insert(new PreparationSessionEntity { Id = 1, Status = KnownFirst.Models.PreparationSessionStatus.Completed, Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline, TotalItems = 1, CompletedItems = 1, StartedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow });
                conn.Insert(new PreparationCandidateEntity { Id = 1, SessionId = 1, WordId = 1, Order = 0, Status = KnownFirst.Models.PreparationCandidateStatus.Prepared, SelectedMeaningIndex = 0, LastErrorCode = string.Empty, LookupAttemptCount = 1, UpdatedAtUtc = DateTime.UtcNow, ResultJson = string.Empty });
                conn.Insert(new LearningCardEntity { Id = 1, WordId = 1, MeaningId = 1, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.Review, DueAtUtc = DateTime.UtcNow, IntervalDays = 1, EaseFactor = 2.5, SuccessfulReviewCount = 1, LapseCount = 0, LastReviewedAtUtc = DateTime.UtcNow, LastRating = KnownFirst.Core.Learning.ReviewRating.Good, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
                conn.Insert(new LearningReviewEntity { Id = 1, CardId = 1, SessionId = 1, Rating = KnownFirst.Core.Learning.ReviewRating.Good, WasTypedAnswer = false, WasCorrect = true, ReviewedAtUtc = DateTime.UtcNow, DueAtUtc = DateTime.UtcNow, IntervalDays = 1, EaseFactor = 2.5 });
                conn.Insert(new LearningSessionEntity { Id = 1, Status = KnownFirst.Models.LearningSessionStatus.Completed, TotalCards = 1, CompletedCards = 1, AgainCount = 0, HardCount = 0, GoodCount = 1, EasyCount = 0, StartedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow });
                conn.Insert(new LearningSessionCardEntity { Id = 1, SessionId = 1, CardId = 1, QueueOrder = 0, IsDueCard = true, IsAgainRepeat = false, AnswerRevealed = true, SpellingChecked = false, SpellingCorrect = false, IsCompleted = true, Rating = KnownFirst.Core.Learning.ReviewRating.Good, CompletedAtUtc = DateTime.UtcNow });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();
            await service.CreateBackupAsync(ms, CancellationToken.None);

            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            Assert.HasCount(2, zip.Entries);

            var manifestEntry = zip.GetEntry("manifest.json")!;
            var dataEntry = zip.GetEntry("data.json")!;

            using (var manifestStream = manifestEntry.Open())
            using (var dataStream = dataEntry.Open())
            {
                var manifestBytes = new byte[manifestEntry.Length];
                int manifestRead = 0;
                while (manifestRead < manifestBytes.Length)
                {
                    int n = await manifestStream.ReadAsync(manifestBytes.AsMemory(manifestRead, manifestBytes.Length - manifestRead));
                    if (n == 0) break;
                    manifestRead += n;
                }

                var dataBytes = new byte[dataEntry.Length];
                int dataRead = 0;
                while (dataRead < dataBytes.Length)
                {
                    int n = await dataStream.ReadAsync(dataBytes.AsMemory(dataRead, dataBytes.Length - dataRead));
                    if (n == 0) break;
                    dataRead += n;
                }

                // Verify UTF-8 without BOM
                Assert.IsFalse(manifestBytes.Length >= 3 && manifestBytes[0] == 0xEF && manifestBytes[1] == 0xBB && manifestBytes[2] == 0xBF);
                Assert.IsFalse(dataBytes.Length >= 3 && dataBytes[0] == 0xEF && dataBytes[1] == 0xBB && dataBytes[2] == 0xBF);

                var manifest = BackupJsonCodec.DeserializeManifest(manifestBytes);
                Assert.IsNotNull(manifest);
                Assert.AreEqual(7, manifest.SourceDatabaseSchemaVersion);
                Assert.AreEqual(1, manifest.RecordCounts.SourceMaterials);
                Assert.AreEqual(1, manifest.RecordCounts.VocabularyItems);
                Assert.AreEqual(1, manifest.RecordCounts.PreparedItems);
                Assert.AreEqual(1, manifest.RecordCounts.LearningCards);

                var payload = BackupJsonCodec.DeserializeData(dataBytes);
                Assert.IsNotNull(payload);
                Assert.HasCount(1, payload.SourceMaterials);
                Assert.HasCount(1, payload.Vocabulary);
                Assert.HasCount(1, payload.PreparedLearning);
                Assert.HasCount(1, payload.Learning.Cards);
                Assert.HasCount(1, payload.Learning.ReviewEvents);
                Assert.HasCount(1, payload.Workflows.VocabularyReviews);
                Assert.HasCount(1, payload.Workflows.PreparationBatches);
                Assert.HasCount(1, payload.Workflows.LearningSessions);
            }
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithDuplicateSemanticVocabularyIdentity_RefusesArchiveCreation()
    {
        var snapshot = new BackupSnapshot(
            Array.Empty<DocumentEntity>(),
            new[]
            {
                new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Test", NormalizedTerm = "test", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new WordEntity { Id = 2, Language = "en", CanonicalTerm = "test", NormalizedTerm = "test", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            },
            Array.Empty<WordFormEntity>(),
            Array.Empty<SentenceSpanEntity>(),
            Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(),
            Array.Empty<ReviewStateEntity>(),
            Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(),
            Array.Empty<PreparationSessionEntity>(),
            Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(),
            Array.Empty<LearningCardEntity>(),
            Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(),
            Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snapshot);
        using var ms = new MemoryStream();

        BackupFormatException? exception = null;
        try
        {
            await BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), DateTime.UtcNow, ms, CancellationToken.None);
        }
        catch (BackupFormatException ex)
        {
            exception = ex;
        }

        Assert.IsNotNull(exception);
        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedLearningCardReference_RefusesArchiveCreation()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Word 1", NormalizedTerm = "word1" });
                // Learning card references MeaningId 999 which does NOT exist!
                conn.Insert(new LearningCardEntity { Id = 1, WordId = 1, MeaningId = 999, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.New, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();

            BackupFormatException? exception = null;
            try
            {
                await service.CreateBackupAsync(ms, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(BackupErrorCodes.MissingReference, exception.Code);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithOccurrenceSurfaceFormMismatch_RefusesArchiveCreation()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            const string contentText = "The houses stand here.";
            var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentText))).ToLowerInvariant();

            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity { Id = 1, Title = "Doc 1", TextLanguage = "en", ExplanationLanguage = "de", Content = contentText, ContentFingerprint = contentHash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition });
                conn.Insert(new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = contentText.Length });
                conn.Insert(new WordEntity { Id = 1, Language = "en", CanonicalTerm = "houses", NormalizedTerm = "house" });
                // Surface form in occurrence is "WRONG" instead of "houses" at index 4, length 6 ("houses")
                conn.Insert(new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = 4, Length = 6, SurfaceForm = "WRONG", Order = 0 });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();

            BackupFormatException? exception = null;
            try
            {
                await service.CreateBackupAsync(ms, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithUndefinedPersistedEnum_RefusesArchiveCreation()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            await database.RunInTransactionAsync(conn =>
            {
                // WordEntity with undefined Status enum value 999
                conn.Insert(new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Word 1", NormalizedTerm = "word1", Status = (KnownFirst.Models.WordStatus)999 });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();

            BackupFormatException? exception = null;
            try
            {
                await service.CreateBackupAsync(ms, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
            Assert.AreEqual(BackupErrorCodes.UnknownEnum, exception.Code);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_ExcludesLexicalCacheAndInternalData()
    {
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            const string sentinelCacheData = "SECRET_LEXICAL_CACHE_SENTINEL_9999";
            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity { Id = 1, Title = "Doc 1", TextLanguage = "en", ExplanationLanguage = "de", Content = "Test text", ContentFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Test text"))).ToLowerInvariant(), LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition });
                conn.Insert(new LexicalCacheEntity { CacheKey = "v2|en|test", ResultJson = sentinelCacheData, FetchedAtUtc = DateTime.UtcNow });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();
            await service.CreateBackupAsync(ms, CancellationToken.None);

            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var dataEntry = zip.GetEntry("data.json")!;
            using var dataStream = dataEntry.Open();
            using var sr = new StreamReader(dataStream, System.Text.Encoding.UTF8);
            var dataJsonText = await sr.ReadToEndAsync();

            Assert.DoesNotContain(dataJsonText, sentinelCacheData);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    [TestMethod]
    public async Task CreateBackup_WithProductionStyleUppercaseContentFingerprint_Succeeds()
    {
        // TextReviewService.CreateContentFingerprint hashes with Convert.ToHexString, which produces
        // UPPERCASE hex. BackupModelContract.ValidateChecksum only accepts lowercase hex, so any document
        // fingerprint generated the way production actually generates it used to fail export outright.
        var database = new TemporaryKnownFirstDatabase();
        await database.InitializeAsync();
        try
        {
            const string contentText = "The houses stand here.";
            var productionStyleFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentText)));
            Assert.IsTrue(productionStyleFingerprint.Any(char.IsUpper));

            await database.RunInTransactionAsync(conn =>
            {
                conn.Insert(new DocumentEntity
                {
                    Title = "Doc 1",
                    TextLanguage = "en",
                    ExplanationLanguage = "de",
                    Content = contentText,
                    ContentFingerprint = productionStyleFingerprint,
                    LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition
                });
                return true;
            });

            var service = new BackupService(database, new FakePlatformInfo());
            using var ms = new MemoryStream();
            await service.CreateBackupAsync(ms, CancellationToken.None);

            Assert.IsGreaterThan(0, ms.Length);
        }
        finally
        {
            await database.ResetAsync();
        }
    }

    // --- Package B.1 Opaque & Deterministic ID Tests ---

    [TestMethod]
    public async Task CreateBackup_ConcealsSparseDatabaseIdsInGeneratedArchiveIds()
    {
        const int sparseDocId = 987654321;
        const int sparseWordId = 888888888;
        const int sparseMeaningId = 777777777;

        var snapshot = new BackupSnapshot(
            new[] { new DocumentEntity { Id = sparseDocId, Title = "Sparse Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = "Sparse text", ContentFingerprint = "hash1", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = DateTime.UtcNow } },
            new[] { new WordEntity { Id = sparseWordId, Language = "en", CanonicalTerm = "Sparse", NormalizedTerm = "sparse", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow } },
            Array.Empty<WordFormEntity>(),
            Array.Empty<SentenceSpanEntity>(),
            Array.Empty<WordOccurrenceEntity>(),
            new[] { new MeaningEntity { Id = sparseMeaningId, WordId = sparseWordId, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "sparse", Translation = "spärlich", Definition = "Thinly scattered", TokenKind = KnownFirst.Core.Text.TokenKind.Word, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, PreparedAt = DateTime.UtcNow } },
            Array.Empty<ReviewStateEntity>(),
            Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(),
            Array.Empty<PreparationSessionEntity>(),
            Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(),
            Array.Empty<LearningCardEntity>(),
            Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(),
            Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snapshot);

        Assert.AreEqual("sm-000001", payload.SourceMaterials[0].Id);
        Assert.AreEqual("v-000001", payload.Vocabulary[0].Id);
        Assert.AreEqual("m-000001", payload.PreparedLearning[0].Id);
        Assert.AreEqual("v-000001", payload.PreparedLearning[0].VocabularyId);

        var dataJson = JsonSerializer.Serialize(payload, BackupJsonSerializerContext.Default.BackupPayload);
        Assert.DoesNotContain(dataJson, "987654321");
        Assert.DoesNotContain(dataJson, "888888888");
        Assert.DoesNotContain(dataJson, "777777777");
    }

    [TestMethod]
    public async Task CreateBackup_LogicallyEquivalentSnapshotsWithDifferentDatabaseIds_ProduceIdenticalArchiveIds()
    {
        var now = DateTime.UtcNow;
        var snap1 = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = "Text", ContentFingerprint = "h1", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 10, Language = "en", CanonicalTerm = "Word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var snap2 = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 999, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = "Text", ContentFingerprint = "h1", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 888, Language = "en", CanonicalTerm = "Word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload1 = BackupModelMapper.MapToExternal(snap1);
        var payload2 = BackupModelMapper.MapToExternal(snap2);

        Assert.AreEqual(payload1.SourceMaterials[0].Id, payload2.SourceMaterials[0].Id);
        Assert.AreEqual(payload1.Vocabulary[0].Id, payload2.Vocabulary[0].Id);
    }

    [TestMethod]
    public async Task CreateBackup_InputOrderingIndependence_ProducesDeterministicArchiveIds()
    {
        var now = DateTime.UtcNow;
        var w1 = new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Apple", NormalizedTerm = "apple", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now };
        var w2 = new WordEntity { Id = 2, Language = "en", CanonicalTerm = "Banana", NormalizedTerm = "banana", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now };

        var snapOrder1 = new BackupSnapshot(Array.Empty<DocumentEntity>(), new[] { w1, w2 }, Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());
        var snapOrder2 = new BackupSnapshot(Array.Empty<DocumentEntity>(), new[] { w2, w1 }, Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload1 = BackupModelMapper.MapToExternal(snapOrder1);
        var payload2 = BackupModelMapper.MapToExternal(snapOrder2);

        Assert.AreEqual(payload1.Vocabulary[0].CanonicalTerm, payload2.Vocabulary[0].CanonicalTerm);
        Assert.AreEqual(payload1.Vocabulary[0].Id, payload2.Vocabulary[0].Id);
        Assert.AreEqual(payload1.Vocabulary[1].CanonicalTerm, payload2.Vocabulary[1].CanonicalTerm);
        Assert.AreEqual(payload1.Vocabulary[1].Id, payload2.Vocabulary[1].Id);
    }

    [TestMethod]
    public async Task CreateBackup_RepeatedMappingOfSameSnapshot_IsDeterministic()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 10, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = "Text", ContentFingerprint = "h1", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 20, Language = "en", CanonicalTerm = "Word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var json1 = JsonSerializer.Serialize(BackupModelMapper.MapToExternal(snap), BackupJsonSerializerContext.Default.BackupPayload);
        var json2 = JsonSerializer.Serialize(BackupModelMapper.MapToExternal(snap), BackupJsonSerializerContext.Default.BackupPayload);

        Assert.AreEqual(json1, json2);
    }

    [TestMethod]
    public async Task CreateBackup_GeneratedIdsAreUniquePerObjectKind()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "D1", TextLanguage = "en", ExplanationLanguage = "de", Content = "T1", ContentFingerprint = "h1", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now }, new DocumentEntity { Id = 2, Title = "D2", TextLanguage = "en", ExplanationLanguage = "de", Content = "T2", ContentFingerprint = "h2", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 10, Language = "en", CanonicalTerm = "W1", NormalizedTerm = "w1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now }, new WordEntity { Id = 20, Language = "en", CanonicalTerm = "W2", NormalizedTerm = "w2", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        var docIds = payload.SourceMaterials.Select(s => s.Id).ToHashSet();
        var vocabIds = payload.Vocabulary.Select(v => v.Id).ToHashSet();

        Assert.HasCount(2, docIds);
        Assert.HasCount(2, vocabIds);
    }

    [TestMethod]
    public async Task CreateBackup_ResolvesAllReferencesToGeneratedArchiveIds()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 50, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = "Text", ContentFingerprint = "h1", LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 60, Language = "en", CanonicalTerm = "Word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 70, DocumentId = 50, Order = 0, StartPosition = 0, Length = 4 } },
            new[] { new WordOccurrenceEntity { Id = 80, DocumentId = 50, SentenceSpanId = 70, WordId = 60, StartPosition = 0, Length = 4, SurfaceForm = "Text", Order = 0 } },
            new[] { new MeaningEntity { Id = 90, WordId = 60, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "word", Translation = "Wort", Definition = "unit of language", TokenKind = KnownFirst.Core.Text.TokenKind.Word, CreatedAt = now, UpdatedAt = now, PreparedAt = now } },
            Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(),
            Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(),
            new[] { new LearningCardEntity { Id = 100, WordId = 60, MeaningId = 90, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.Review, CreatedAtUtc = now, UpdatedAtUtc = now } },
            Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);

        var docId = payload.SourceMaterials[0].Id;
        var sentenceId = payload.SourceMaterials[0].Sentences[0].Id;
        var occurrence = payload.SourceMaterials[0].Occurrences[0];
        var vocabId = payload.Vocabulary[0].Id;
        var meaningId = payload.PreparedLearning[0].Id;
        var card = payload.Learning.Cards[0];

        Assert.AreEqual(sentenceId, occurrence.SentenceId);
        Assert.AreEqual(vocabId, occurrence.VocabularyId);
        Assert.AreEqual(vocabId, card.VocabularyId);
        Assert.AreEqual(meaningId, card.PreparedItemId);
    }

    // --- Package B.1 Complete Orphan-Reference Matrix Tests ---

    [TestMethod]
    public async Task CreateBackup_WithOrphanedOccurrenceSentenceReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Text", NormalizedTerm = "text", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(),
            // SentenceSpanId 999 does NOT exist
            new[] { new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 999, WordId = 1, StartPosition = 0, Length = 4, SurfaceForm = "Text", Order = 0 } },
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
        Assert.DoesNotContain(ex.Message, "999");
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedOccurrenceVocabularyReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = 4 } },
            // WordId 999 does NOT exist
            new[] { new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 999, StartPosition = 0, Length = 4, SurfaceForm = "Text", Order = 0 } },
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(),
            Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedPreparedItemVocabularyReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(), Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(),
            Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            // Meaning references WordId 999 which does NOT exist
            new[] { new MeaningEntity { Id = 1, WordId = 999, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "test", Translation = "Test", Definition = "test", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now } },
            Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(),
            Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedContextSnapshotSourceMaterialReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(),
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Test", NormalizedTerm = "test", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            new[] { new MeaningEntity { Id = 1, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "test", Translation = "Test", Definition = "test", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now } },
            Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(),
            Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            // ContextSnapshot references SourceDocumentId 999 which does NOT exist
            new[] { new ContextSnapshotEntity { Id = 1, MeaningId = 1, SourceDocumentId = 999, SourceDocumentTitle = "Missing Doc", Text = "Test context text", TargetStart = 0, TargetLength = 4, NormalizedFingerprint = "fp", CreatedAtUtc = now } },
            Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedVocabularyReviewItemVocabularyReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(),
            new[] { new ReviewSessionEntity { Id = 1, DocumentId = 1, Status = KnownFirst.Models.ReviewSessionStatus.Active, TotalCandidates = 1, StartedAt = now } },
            // ReviewCandidate references WordId 999 which does NOT exist
            new[] { new ReviewCandidateEntity { Id = 1, SessionId = 1, WordId = 999, Order = 0, Status = KnownFirst.Models.WordStatus.Known, PreviousWordStatus = KnownFirst.Models.WordStatus.Unreviewed, PreviousUpdatedAt = now, DecidedAt = now } },
            Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(),
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedPreparationItemVocabularyReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(), Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(),
            new[] { new PreparationSessionEntity { Id = 1, Status = KnownFirst.Models.PreparationSessionStatus.Active, Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline, TotalItems = 1, StartedAtUtc = now, UpdatedAtUtc = now } },
            // PreparationCandidate references WordId 999 which does NOT exist
            new[] { new PreparationCandidateEntity { Id = 1, SessionId = 1, WordId = 999, Order = 0, Status = KnownFirst.Models.PreparationCandidateStatus.Pending, UpdatedAtUtc = now } },
            Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedLearningCardVocabularyReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(),
            // WordId 1 exists for Meaning, but LearningCard references WordId 999
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "W1", NormalizedTerm = "w1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            new[] { new MeaningEntity { Id = 1, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "w1", Translation = "W1", Definition = "d1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now } },
            Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(),
            Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(),
            new[] { new LearningCardEntity { Id = 1, WordId = 999, MeaningId = 1, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.Review, CreatedAtUtc = now, UpdatedAtUtc = now } },
            Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedLearningReviewCardReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(), Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(),
            Array.Empty<LearningCardEntity>(),
            // LearningReview references CardId 999 which does NOT exist
            new[] { new LearningReviewEntity { Id = 1, CardId = 999, SessionId = 1, Rating = KnownFirst.Core.Learning.ReviewRating.Good, ReviewedAtUtc = now, DueAtUtc = now } },
            new[] { new LearningSessionEntity { Id = 1, Status = KnownFirst.Models.LearningSessionStatus.Completed, TotalCards = 1, StartedAtUtc = now, UpdatedAtUtc = now } },
            Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedLearningReviewSessionReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(),
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "W1", NormalizedTerm = "w1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(),
            new[] { new MeaningEntity { Id = 1, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "w1", Translation = "W1", Definition = "d1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now } },
            Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(),
            new[] { new LearningCardEntity { Id = 1, WordId = 1, MeaningId = 1, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.Review, CreatedAtUtc = now, UpdatedAtUtc = now } },
            // LearningReview references SessionId 999 which does NOT exist
            new[] { new LearningReviewEntity { Id = 1, CardId = 1, SessionId = 999, Rating = KnownFirst.Core.Learning.ReviewRating.Good, ReviewedAtUtc = now, DueAtUtc = now } },
            Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOrphanedLearningQueueItemCardReference_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        var snap = new BackupSnapshot(
            Array.Empty<DocumentEntity>(), Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(),
            Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(),
            new[] { new LearningSessionEntity { Id = 1, Status = KnownFirst.Models.LearningSessionStatus.Active, TotalCards = 1, StartedAtUtc = now, UpdatedAtUtc = now } },
            // LearningSessionCard references CardId 999 which does NOT exist
            new[] { new LearningSessionCardEntity { Id = 1, SessionId = 1, CardId = 999, QueueOrder = 0, IsDueCard = true } });

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    // --- Package B.1 Coordinate and Ordering Matrix Tests ---

    [TestMethod]
    public async Task CreateBackup_WithNegativeSentenceStart_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = -1, Length = 4 } },
            Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithSentenceExtendingPastOriginalText_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = 10 } },
            Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithNegativeOccurrenceStart_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Text", NormalizedTerm = "text", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = 4 } },
            new[] { new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = -1, Length = 4, SurfaceForm = "Text", Order = 0 } },
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOccurrenceExtendingPastOriginalText_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Text";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Text", NormalizedTerm = "text", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = 4 } },
            new[] { new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = 0, Length = 10, SurfaceForm = "Text", Order = 0 } },
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithOccurrenceOutsideSentence_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Sentence 1. Sentence 2.";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Sentence", NormalizedTerm = "sentence", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = 10 }, new SentenceSpanEntity { Id = 2, DocumentId = 1, Order = 1, StartPosition = 12, Length = 11 } },
            // Occurrence claims SentenceSpanId 1 (start 0, len 10) but starts at position 12
            new[] { new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = 12, Length = 8, SurfaceForm = "Sentence", Order = 0 } },
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithDuplicateSentenceOrder_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Sentence 1. Sentence 2.";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(),
            // Both sentence spans have Order = 0
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = 10 }, new SentenceSpanEntity { Id = 2, DocumentId = 1, Order = 0, StartPosition = 12, Length = 11 } },
            Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task CreateBackup_WithDuplicateOccurrenceOrder_RefusesArchiveCreation()
    {
        var now = DateTime.UtcNow;
        const string text = "Word Word";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var snap = new BackupSnapshot(
            new[] { new DocumentEntity { Id = 1, Title = "Doc", TextLanguage = "en", ExplanationLanguage = "de", Content = text, ContentFingerprint = hash, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, ImportedAt = now } },
            new[] { new WordEntity { Id = 1, Language = "en", CanonicalTerm = "Word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now } },
            Array.Empty<WordFormEntity>(),
            new[] { new SentenceSpanEntity { Id = 1, DocumentId = 1, Order = 0, StartPosition = 0, Length = text.Length } },
            // Both occurrences have Order = 0
            new[] { new WordOccurrenceEntity { Id = 1, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = 0, Length = 4, SurfaceForm = "Word", Order = 0 }, new WordOccurrenceEntity { Id = 2, DocumentId = 1, SentenceSpanId = 1, WordId = 1, StartPosition = 5, Length = 4, SurfaceForm = "Word", Order = 0 } },
            Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload = BackupModelMapper.MapToExternal(snap);
        using var ms = new MemoryStream();
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), new ValidatedSchema7Capability(), now, ms, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    // --- Package B.2 Canonical Logical Ordering & Collision Tests ---

    [TestMethod]
    public async Task CreateBackup_CollidingSourceMaterialSortKeys_ProducesCanonicalOutput()
    {
        var now = DateTime.UtcNow;
        const string contentA = "Content Alpha";
        const string contentB = "Content Beta";
        var hashA = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentA))).ToLowerInvariant();
        var hashB = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentB))).ToLowerInvariant();

        // Both documents share Title, TextLanguage, ExplanationLanguage, ContentFingerprint, Content, but differ in LookupMode/TargetLanguage
        var docA1 = new DocumentEntity { Id = 1, Title = "Same Title", TextLanguage = "en", ExplanationLanguage = "de", Content = contentA, ContentFingerprint = hashA, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, TargetLanguage = null, ImportedAt = now };
        var docB1 = new DocumentEntity { Id = 2, Title = "Same Title", TextLanguage = "en", ExplanationLanguage = "de", Content = contentA, ContentFingerprint = hashA, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.DefinitionAndTranslation, TargetLanguage = "de", ImportedAt = now };

        // snap2 has swapped DB IDs: docA2 has Id=2, docB2 has Id=1
        var docA2 = new DocumentEntity { Id = 2, Title = "Same Title", TextLanguage = "en", ExplanationLanguage = "de", Content = contentA, ContentFingerprint = hashA, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition, TargetLanguage = null, ImportedAt = now };
        var docB2 = new DocumentEntity { Id = 1, Title = "Same Title", TextLanguage = "en", ExplanationLanguage = "de", Content = contentA, ContentFingerprint = hashA, LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.DefinitionAndTranslation, TargetLanguage = "de", ImportedAt = now };

        var snap1 = new BackupSnapshot(new[] { docA1, docB1 }, Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());
        var snap2 = new BackupSnapshot(new[] { docB2, docA2 }, Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload1 = BackupModelMapper.MapToExternal(snap1);
        var payload2 = BackupModelMapper.MapToExternal(snap2);

        var bytes1 = BackupJsonCodec.SerializeData(payload1);
        var bytes2 = BackupJsonCodec.SerializeData(payload2);

        Assert.IsTrue(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    [TestMethod]
    public async Task CreateBackup_CollidingPreparedItemSortKeys_ProducesCanonicalOutput()
    {
        var now = DateTime.UtcNow;
        var word1 = new WordEntity { Id = 1, Language = "en", CanonicalTerm = "house", NormalizedTerm = "house", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now };
        var word2 = new WordEntity { Id = 2, Language = "en", CanonicalTerm = "house", NormalizedTerm = "house", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now };

        // Meanings share WordId, SourceLanguage, ExplanationLanguage, DisplayTerm, but differ in Translation
        var m1_1 = new MeaningEntity { Id = 10, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "house", Translation = "Haus", Definition = "Building", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };
        var m2_1 = new MeaningEntity { Id = 20, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "house", Translation = "Gebäude", Definition = "Structure", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };

        var m1_2 = new MeaningEntity { Id = 20, WordId = 2, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "house", Translation = "Haus", Definition = "Building", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };
        var m2_2 = new MeaningEntity { Id = 10, WordId = 2, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "house", Translation = "Gebäude", Definition = "Structure", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };

        var snap1 = new BackupSnapshot(Array.Empty<DocumentEntity>(), new[] { word1 }, Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), new[] { m1_1, m2_1 }, Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());
        var snap2 = new BackupSnapshot(Array.Empty<DocumentEntity>(), new[] { word2 }, Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), new[] { m2_2, m1_2 }, Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload1 = BackupModelMapper.MapToExternal(snap1);
        var payload2 = BackupModelMapper.MapToExternal(snap2);

        var bytes1 = BackupJsonCodec.SerializeData(payload1);
        var bytes2 = BackupJsonCodec.SerializeData(payload2);

        Assert.IsTrue(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    [TestMethod]
    public async Task CreateBackup_CollidingWorkflowSortKeys_ProducesCanonicalOutput()
    {
        var now = DateTime.UtcNow;
        // Preparation sessions share StartedAtUtc, Method, but differ in TotalItems
        var ps1_1 = new PreparationSessionEntity { Id = 100, Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline, StartedAtUtc = now, TotalItems = 5, Status = KnownFirst.Models.PreparationSessionStatus.Completed, UpdatedAtUtc = now };
        var ps2_1 = new PreparationSessionEntity { Id = 200, Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline, StartedAtUtc = now, TotalItems = 10, Status = KnownFirst.Models.PreparationSessionStatus.Completed, UpdatedAtUtc = now };

        var ps1_2 = new PreparationSessionEntity { Id = 200, Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline, StartedAtUtc = now, TotalItems = 5, Status = KnownFirst.Models.PreparationSessionStatus.Completed, UpdatedAtUtc = now };
        var ps2_2 = new PreparationSessionEntity { Id = 100, Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline, StartedAtUtc = now, TotalItems = 10, Status = KnownFirst.Models.PreparationSessionStatus.Completed, UpdatedAtUtc = now };

        var snap1 = new BackupSnapshot(Array.Empty<DocumentEntity>(), Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), new[] { ps1_1, ps2_1 }, Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());
        var snap2 = new BackupSnapshot(Array.Empty<DocumentEntity>(), Array.Empty<WordEntity>(), Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), Array.Empty<MeaningEntity>(), Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), new[] { ps2_2, ps1_2 }, Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), Array.Empty<LearningCardEntity>(), Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload1 = BackupModelMapper.MapToExternal(snap1);
        var payload2 = BackupModelMapper.MapToExternal(snap2);

        var bytes1 = BackupJsonCodec.SerializeData(payload1);
        var bytes2 = BackupJsonCodec.SerializeData(payload2);

        Assert.IsTrue(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    [TestMethod]
    public async Task CreateBackup_CollidingLearningCardSortKeys_ProducesCanonicalOutput()
    {
        var now = DateTime.UtcNow;
        var w1 = new WordEntity { Id = 1, Language = "en", CanonicalTerm = "word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now };
        var w2 = new WordEntity { Id = 2, Language = "en", CanonicalTerm = "word", NormalizedTerm = "word", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Status = KnownFirst.Models.WordStatus.Known, PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared, CreatedAt = now, UpdatedAt = now };

        var m1_1 = new MeaningEntity { Id = 10, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "word", Translation = "Wort1", Definition = "d1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };
        var m2_1 = new MeaningEntity { Id = 20, WordId = 1, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "word", Translation = "Wort2", Definition = "d2", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };

        var m1_2 = new MeaningEntity { Id = 10, WordId = 2, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "word", Translation = "Wort1", Definition = "d1", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };
        var m2_2 = new MeaningEntity { Id = 20, WordId = 2, SourceLanguage = "en", ExplanationLanguage = "de", DisplayTerm = "word", Translation = "Wort2", Definition = "d2", TokenKind = KnownFirst.Core.Text.TokenKind.Word, Source = "dict", CreatedAt = now, UpdatedAt = now, PreparedAt = now };

        // Cards share WordId and Direction (TermToMeaning), but differ in MeaningId / State
        var c1_1 = new LearningCardEntity { Id = 100, WordId = 1, MeaningId = 10, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.Review, CreatedAtUtc = now, UpdatedAtUtc = now };
        var c2_1 = new LearningCardEntity { Id = 200, WordId = 1, MeaningId = 20, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.New, CreatedAtUtc = now, UpdatedAtUtc = now };

        var c1_2 = new LearningCardEntity { Id = 200, WordId = 2, MeaningId = 10, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.Review, CreatedAtUtc = now, UpdatedAtUtc = now };
        var c2_2 = new LearningCardEntity { Id = 100, WordId = 2, MeaningId = 20, Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning, State = KnownFirst.Core.Learning.CardState.New, CreatedAtUtc = now, UpdatedAtUtc = now };

        var snap1 = new BackupSnapshot(Array.Empty<DocumentEntity>(), new[] { w1 }, Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), new[] { m1_1, m2_1 }, Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), new[] { c1_1, c2_1 }, Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());
        var snap2 = new BackupSnapshot(Array.Empty<DocumentEntity>(), new[] { w2 }, Array.Empty<WordFormEntity>(), Array.Empty<SentenceSpanEntity>(), Array.Empty<WordOccurrenceEntity>(), new[] { m1_2, m2_2 }, Array.Empty<ReviewStateEntity>(), Array.Empty<ReviewSessionEntity>(), Array.Empty<ReviewCandidateEntity>(), Array.Empty<PreparationSessionEntity>(), Array.Empty<PreparationCandidateEntity>(), Array.Empty<ContextSnapshotEntity>(), new[] { c2_2, c1_2 }, Array.Empty<LearningReviewEntity>(), Array.Empty<LearningSessionEntity>(), Array.Empty<LearningSessionCardEntity>());

        var payload1 = BackupModelMapper.MapToExternal(snap1);
        var payload2 = BackupModelMapper.MapToExternal(snap2);

        var bytes1 = BackupJsonCodec.SerializeData(payload1);
        var bytes2 = BackupJsonCodec.SerializeData(payload2);

        Assert.IsTrue(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    // ---- Package A characterization: portable export can never emit two review items sharing one
    // vocabulary id inside one workflow.
    //
    // Part 1 exercises the real import + decide creation path. It also records the verified product fact
    // that <c>TextReviewService.CompleteSession</c> deletes every ReviewCandidate row when a session
    // completes, so an ordinarily-completed session exports with zero items.
    //
    // Part 2 covers the only shape that can carry items into an export at all — a completed session whose
    // candidate rows were written by restore/merge rather than by the live review flow. Without it the
    // uniqueness assertion would be vacuous. ----
    [TestMethod]
    public async Task PortableExport_NeverEmitsTwoReviewItemsSharingOneVocabularyIdInOneWorkflow()
    {
        await using var database = new TemporarySchema8Database("knownfirst-review-export-invariant");
        await database.InitializeAsync();
        // This test characterizes the portable-export duplicate-vocabulary-id invariant, not literal-version
        // behavior. The fixture upgrades immediately after construction so TextReviewService's
        // review-selection/completion methods, which now require the current schema, keep working; the
        // later Schema8BackupSnapshotRepository capture call is schema-version-agnostic at the raw-table
        // level (Schema 9-11 share Schema 8's meaning-centric data model exactly).
        await database.UpgradeToCurrentSchemaAsync();
        var service = new KnownFirst.Services.TextReviewService(
            database,
            new KnownFirst.Core.Text.TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var importResult = await service.ImportAsync(new KnownFirst.Models.ImportTextRequest(
            "Repeated identity document",
            "Bank matters. The bank is open. BANK again, and banks differ.",
            "en",
            "de"));
        Assert.AreEqual(KnownFirst.Models.ImportAnalysisOutcome.Accepted, importResult.Outcome);

        var orderedCandidates = await database.ReadAsync(connection => connection.Table<ReviewCandidateEntity>()
            .Where(item => item.SessionId == importResult.SessionId)
            .OrderBy(item => item.Order)
            .ToListAsync());
        Assert.IsGreaterThan(0, orderedCandidates.Count);
        CollectionAssert.AllItemsAreUnique(
            orderedCandidates.Select(candidate => candidate.WordId).ToList(),
            "One review session must never hold two candidates for the same word.");

        // The first decision is UnknownBacklog so the completed session (and its document) survives
        // completion; a session whose UnknownCount is zero is pruned entirely by design.
        for (var index = 0; index < orderedCandidates.Count; index++)
        {
            await service.DecideAsync(
                orderedCandidates[index].WordId,
                index == 0 ? KnownFirst.Models.WordStatus.UnknownBacklog : KnownFirst.Models.WordStatus.Known);
        }

        var snapshot = await database.ExecuteSnapshotAsync(
            KnownFirst.Data.Schema8.Schema8BackupSnapshotRepository.CapturePortableSnapshot);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        Assert.HasCount(1, payload.Workflows.VocabularyReviews);
        Assert.IsEmpty(
            payload.Workflows.VocabularyReviews[0].Items,
            "Verified product behaviour: completing a review session deletes its candidate rows.");
        AssertReviewItemVocabularyIdsAreUnique(payload);

        // Part 2 — a completed session that still carries candidate rows (restore/merge-written shape).
        await using var fixture = await Schema7Fixture.CreateAsync();
        var documentId = await fixture.InsertDocumentAsync(title: "Restored", content: "bank and river");
        var bankWordId = await fixture.InsertWordAsync(
            "bank", status: KnownFirst.Models.WordStatus.Known, preparationState: KnownFirst.Core.Preparation.PreparationState.Unprepared);
        var riverWordId = await fixture.InsertWordAsync(
            "river", status: KnownFirst.Models.WordStatus.UnknownBacklog, preparationState: KnownFirst.Core.Preparation.PreparationState.Unprepared);
        var startedAtUtc = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = new DateTime(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc);
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, 1, 2, 2, 1, 1, 0, 2, ?, ?)
            """,
            documentId, startedAtUtc, completedAtUtc);
        var restoredSessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        foreach (var (wordId, order) in new[] { (bankWordId, 0), (riverWordId, 1) })
        {
            await fixture.Connection.ExecuteAsync(
                """
                INSERT INTO ReviewCandidates
                    (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount,
                     PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
                VALUES (?, ?, ?, 1, 0, 0, 0, ?, ?, 0, ?)
                """,
                restoredSessionId, wordId, order, startedAtUtc, order + 1, completedAtUtc);
        }

        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        KnownFirst.Data.Schema8.Schema8BackupSnapshot? restoredSnapshot = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            restoredSnapshot = KnownFirst.Data.Schema8.Schema8BackupSnapshotRepository.CapturePortableSnapshot(connection));
        var restoredPayload = BackupModelMapperV2.MapToExternal(restoredSnapshot!);

        Assert.HasCount(1, restoredPayload.Workflows.VocabularyReviews);
        Assert.HasCount(2, restoredPayload.Workflows.VocabularyReviews[0].Items);
        AssertReviewItemVocabularyIdsAreUnique(restoredPayload);
    }

    private static void AssertReviewItemVocabularyIdsAreUnique(BackupPayloadV2 payload)
    {
        foreach (var workflow in payload.Workflows.VocabularyReviews)
        {
            CollectionAssert.AllItemsAreUnique(
                workflow.Items.Select(item => item.VocabularyId).ToList(),
                "One exported review workflow must never contain two items for the same vocabulary id.");
        }
    }

    // ---- Package B: canonical v2 output for two completed review sessions over one document.
    //
    // Schema 9 replaced the legacy unique ReviewSessions(DocumentId) index with a non-unique index plus a
    // partial unique index restricted to Active sessions, so two independently completed review histories
    // for one document are a representable state for the first time. The v2 mapper must therefore order
    // review sessions by a total key; the two sessions below are deliberately equal on every field the
    // ordering considered before this package (Status, TotalCandidates, ReviewedCount, DecisionSequence,
    // StartedAt) and differ only in the retained outcome counters and CompletedAt — exactly the fields the
    // Schema-9 full-history session identity uses to tell two completed histories apart. ----
    [TestMethod]
    public void CreateBackup_CollidingCompletedReviewSessionSortKeys_ProducesCanonicalOutput()
    {
        const string content = "The bank is open.";
        var document = new DocumentEntity
        {
            Id = 1,
            Title = "Review history document",
            TextLanguage = "en",
            ExplanationLanguage = "de",
            LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition,
            Content = content,
            ContentFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
            ImportedAt = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc),
            WordCount = 4
        };

        var bank = NewCanonicalOrderingWord(1, "bank", KnownFirst.Models.WordStatus.Known);
        var river = NewCanonicalOrderingWord(2, "river", KnownFirst.Models.WordStatus.UnknownBacklog);

        var startedAtUtc = new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc);

        // Equal on every pre-existing v2 ordering input; genuinely distinct histories.
        var sessionA = NewCompletedReviewSession(
            id: 10, documentId: document.Id, startedAtUtc: startedAtUtc,
            completedAtUtc: startedAtUtc.AddMinutes(30), knownCount: 2, unknownCount: 0, ignoredCount: 0);
        var sessionB = NewCompletedReviewSession(
            id: 20, documentId: document.Id, startedAtUtc: startedAtUtc,
            completedAtUtc: startedAtUtc.AddMinutes(45), knownCount: 0, unknownCount: 2, ignoredCount: 0);

        var candidatesA = NewReviewCandidates(sessionA.Id, baseId: 100, bank.Id, river.Id, startedAtUtc);
        var candidatesB = NewReviewCandidates(sessionB.Id, baseId: 200, bank.Id, river.Id, startedAtUtc);

        var snapshot1 = NewCanonicalOrderingSnapshot(
            document, [bank, river],
            [sessionA, sessionB],
            [.. candidatesA, .. candidatesB]);
        var snapshot2 = NewCanonicalOrderingSnapshot(
            document, [river, bank],
            [sessionB, sessionA],
            [.. candidatesB, .. candidatesA]);

        var payload1 = BackupModelMapperV2.MapToExternal(snapshot1);
        var payload2 = BackupModelMapperV2.MapToExternal(snapshot2);

        // Localizes the defect to the review-session ordering itself: every other collection in this
        // snapshot already has a total ordering key, so only these two sessions can swap positions.
        CollectionAssert.AreEqual(
            payload1.Workflows.VocabularyReviews
                .Select(workflow => (workflow.Id, workflow.KnownCount, workflow.UnknownCount, workflow.CompletedAtUtc)).ToList(),
            payload2.Workflows.VocabularyReviews
                .Select(workflow => (workflow.Id, workflow.KnownCount, workflow.UnknownCount, workflow.CompletedAtUtc)).ToList(),
            "The v2 review-session ordering must assign the same archive-local id to the same completed history "
            + "regardless of raw row enumeration order.");

        var bytes1 = BackupJsonCodecV2.SerializeData(payload1);
        var bytes2 = BackupJsonCodecV2.SerializeData(payload2);

        Assert.IsTrue(
            bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()),
            "Two completed review sessions for one document that collide on the v2 sort key must still map to "
            + "byte-identical canonical output regardless of raw row enumeration order.");
    }

    // ---- Package C: cross-installation canonical output for two completed review histories that tie on
    // every session-level mapper sort field and differ only through candidate-decision content.
    //
    // Package B proved that the v2 review-session ordering is independent of raw row enumeration order for
    // two sessions whose session-level fields differ, and deliberately left one boundary open: when two
    // completed histories tie on every session-level field the mapper orders by, the key falls through to the
    // installation-local ReviewSession.Id. Two independently created installations can assign the opposite
    // local ids to the same two histories, so the same synthetic vr-*/rc-* ids bind to different completed
    // histories and the canonical payload stops being a pure function of exported content.
    //
    // The fixture deliberately contains no Sense/Meaning/AnswerVariant/Assignment rows. Those carry
    // Guid.NewGuid()-generated StableId values that are installation-random by design, so serialized byte
    // equality is a legitimate oracle only for a subgraph that excludes them. ----
    [TestMethod]
    public void CreateBackupV2_TwoInstallationsWithOppositeRowIds_CollidingCompletedReviewSessionFields_ProduceIdenticalCanonicalOutput()
    {
        var installationA = NewCollidingReviewHistoryInstallation(
            documentId: 1, bankWordId: 1, riverWordId: 2,
            bankKnownSessionId: 10, riverKnownSessionId: 20,
            bankKnownCandidateBaseId: 100, riverKnownCandidateBaseId: 200);

        // Same logical content, independently created: every local row id differs, and the two completed
        // histories carry the opposite ReviewSession ids — and therefore the opposite raw enumeration order.
        var installationB = NewCollidingReviewHistoryInstallation(
            documentId: 7, bankWordId: 42, riverWordId: 41,
            bankKnownSessionId: 20, riverKnownSessionId: 10,
            bankKnownCandidateBaseId: 500, riverKnownCandidateBaseId: 400);

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.AreEqual(
            string.Join(Environment.NewLine, CanonicalReviewSubgraphProjection(payloadA)),
            string.Join(Environment.NewLine, CanonicalReviewSubgraphProjection(payloadB)),
            "Two installations holding the same two completed review histories must bind the same archive-local "
            + "vr-*/rc-* ids to the same history, regardless of which local row id each installation assigned.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Two installations whose completed review histories tie on every session-level field and differ "
            + "only through candidate content must still produce byte-identical canonical v2 output.");
    }

    // ---- Package C hardening: the v2 review-session ordering still ends with a local row-id comparison as a
    // syntactic total-order guarantee. This proves that comparison is output-neutral: two sessions that are
    // indistinguishable in every emitted field — same full Schema-9 identity AND the same absolute candidate
    // orders and decision content, the only residual the identity itself does not encode — produce identical
    // canonical output whichever local row id each one carries. (Two wholly identical full-history identities
    // are rejected downstream by the merge planner and MergeWriterTargetIndex; the mapper's own job is only to
    // stay total and deterministic, which is what this test pins.) ----
    [TestMethod]
    public void CreateBackupV2_TwoIndistinguishableCompletedReviewSessions_RemainByteIdenticalUnderAnyRowIdAssignment()
    {
        var installationA = NewIndistinguishableReviewHistoryInstallation(
            firstSessionId: 10, secondSessionId: 20, firstCandidateBaseId: 100, secondCandidateBaseId: 200);
        var installationB = NewIndistinguishableReviewHistoryInstallation(
            firstSessionId: 20, secondSessionId: 10, firstCandidateBaseId: 200, secondCandidateBaseId: 100);

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.HasCount(2, payloadA.Workflows.VocabularyReviews);

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Reaching the final local row-id comparison must not be observable: sessions that tie on the full "
            + "Schema-9 identity and on their emitted candidate rows are byte-identical either way round.");
    }

    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewIndistinguishableReviewHistoryInstallation(
        int firstSessionId, int secondSessionId, int firstCandidateBaseId, int secondCandidateBaseId)
    {
        var document = NewCollidingHistoryDocument(1);
        var bank = NewCanonicalOrderingWord(1, "bank", KnownFirst.Models.WordStatus.Known);
        var river = NewCanonicalOrderingWord(2, "river", KnownFirst.Models.WordStatus.UnknownBacklog);

        var first = NewCompletedReviewSession(
            id: firstSessionId, documentId: document.Id,
            startedAtUtc: CollidingHistoryStartedAtUtc, completedAtUtc: CollidingHistoryCompletedAtUtc,
            knownCount: 1, unknownCount: 1, ignoredCount: 0);
        var second = NewCompletedReviewSession(
            id: secondSessionId, documentId: document.Id,
            startedAtUtc: CollidingHistoryStartedAtUtc, completedAtUtc: CollidingHistoryCompletedAtUtc,
            knownCount: 1, unknownCount: 1, ignoredCount: 0);

        // Identical candidate content, identical absolute Order values: nothing but the row id is left.
        ReviewCandidateEntity[] candidates =
        [
            NewDecidedReviewCandidate(firstCandidateBaseId, first.Id, bank.Id, 0, KnownFirst.Models.WordStatus.Known),
            NewDecidedReviewCandidate(firstCandidateBaseId + 1, first.Id, river.Id, 1, KnownFirst.Models.WordStatus.UnknownBacklog),
            NewDecidedReviewCandidate(secondCandidateBaseId, second.Id, bank.Id, 0, KnownFirst.Models.WordStatus.Known),
            NewDecidedReviewCandidate(secondCandidateBaseId + 1, second.Id, river.Id, 1, KnownFirst.Models.WordStatus.UnknownBacklog)
        ];

        return NewCanonicalOrderingSnapshot(
            document,
            [bank, river],
            new[] { first, second }.OrderBy(session => session.Id).ToList(),
            candidates.OrderBy(candidate => candidate.Id).ToList());
    }

    // ---- Package C: cross-installation canonical output for distinct source materials that collide on the
    // v2 mapper's (ContentFingerprint, Title) key.
    //
    // Title is deliberately excluded from SourceMaterialIdentityPolicy (free text typed at import time) and
    // the archive writer enforces no semantic uniqueness for source materials, so byte-identical content
    // imported under a different TextLanguage — or, after a merge, under a different LookupMode/TargetLanguage
    // — is a valid distinct document that ties on the whole current v2 key. The shipped v1 mapper already
    // orders documents by every retained field before its own id fallback; the v2 mapper does not, so the
    // positional sm-* ids (and therefore every ss-*/vr-*/rc-* id derived from them) depend on local row
    // order. ----
    [TestMethod]
    public void CreateBackupV2_TwoInstallationsWithOppositeRowIds_CollidingSourceMaterialSortKeys_ProduceIdenticalCanonicalOutput()
    {
        var installationA = NewCollidingSourceMaterialInstallation(
            englishDocumentId: 1, germanDocumentId: 2, translationDocumentId: 3);

        // Same three logical documents, independently created, with the opposite local row ids and therefore
        // the opposite raw enumeration order.
        var installationB = NewCollidingSourceMaterialInstallation(
            englishDocumentId: 30, germanDocumentId: 20, translationDocumentId: 10);

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.AreEqual(
            string.Join(Environment.NewLine, CanonicalSourceMaterialProjection(payloadA)),
            string.Join(Environment.NewLine, CanonicalSourceMaterialProjection(payloadB)),
            "Two installations holding the same distinct source materials must bind the same archive-local "
            + "sm-* id to the same document, regardless of which local row id each installation assigned.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Distinct source materials that collide on (ContentFingerprint, Title) must still produce "
            + "byte-identical canonical v2 output across installations.");
    }

    /// <summary>
    /// Three genuinely distinct documents sharing byte-identical content and the same user-typed title, so
    /// all three tie on the whole current v2 SourceMaterial ordering key while differing in retained logical
    /// fields: TextLanguage (locally reachable — the live duplicate check is (ContentFingerprint,
    /// TextLanguage)) and LookupMode/TargetLanguage (merge-reachable — design §4.1's PC/phone case).
    /// </summary>
    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewCollidingSourceMaterialInstallation(
        int englishDocumentId, int germanDocumentId, int translationDocumentId)
    {
        var english = NewCollidingSourceMaterialDocument(
            englishDocumentId, "en", "de",
            KnownFirst.Core.Preparation.LexicalLookupMode.Definition, string.Empty);
        var german = NewCollidingSourceMaterialDocument(
            germanDocumentId, "de", "en",
            KnownFirst.Core.Preparation.LexicalLookupMode.Definition, string.Empty);
        var translation = NewCollidingSourceMaterialDocument(
            translationDocumentId, "en", "de",
            KnownFirst.Core.Preparation.LexicalLookupMode.DefinitionAndTranslation, "ru");

        return NewSourceMaterialOrderingSnapshot(
            new[] { english, german, translation }.OrderBy(document => document.Id).ToList());
    }

    private static DocumentEntity NewCollidingSourceMaterialDocument(
        int id, string textLanguage, string explanationLanguage,
        KnownFirst.Core.Preparation.LexicalLookupMode lookupMode, string targetLanguage) => new()
        {
            Id = id,
            Title = "Same title",
            TextLanguage = textLanguage,
            ExplanationLanguage = explanationLanguage,
            LookupMode = lookupMode,
            TargetLanguage = targetLanguage,
            Content = CollidingHistoryContent,
            ContentFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(CollidingHistoryContent))).ToLowerInvariant(),
            ImportedAt = CollidingHistoryImportedAtUtc,
            WordCount = 7
        };

    // ---- Package C MINOR-1: two source materials can be equal on every scalar the v2 ordering compares and
    // still emit different child subgraphs. MapSourceMaterial selects Sentences and Occurrences through the
    // local DocumentId foreign key, and neither collection's content participates in the document ordering
    // key, so the positional sm-* ids — and every ss-* id derived from them — fell back to raw snapshot
    // enumeration order for exactly this shape. The fixture below carries no Sense/Meaning/AnswerVariant/
    // Assignment rows, so serialized byte equality remains a legitimate oracle. ----
    [TestMethod]
    public void CreateBackupV2_TwoInstallationsWithOppositeRowIds_ScalarIdenticalSourceMaterialsWithDifferentChildGraphs_ProduceIdenticalCanonicalOutput()
    {
        var installationA = NewScalarIdenticalChildGraphInstallation(
            bankDocumentId: 1, riverDocumentId: 2, bankWordId: 1, riverWordId: 2);

        // Same two logical documents, independently created: the opposite local Document row ids, therefore
        // the opposite raw enumeration order, and different dependent child row ids.
        var installationB = NewScalarIdenticalChildGraphInstallation(
            bankDocumentId: 20, riverDocumentId: 10, bankWordId: 42, riverWordId: 41);

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.HasCount(2, payloadA.SourceMaterials);

        Assert.AreEqual(
            string.Join(Environment.NewLine, CanonicalSourceMaterialChildProjection(payloadA)),
            string.Join(Environment.NewLine, CanonicalSourceMaterialChildProjection(payloadB)),
            "Two source materials that tie on every scalar ordering component must still bind the same "
            + "archive-local sm-* id — and the same dependent ss-* references — to the same child subgraph, "
            + "regardless of which local row id each installation assigned.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Scalar-identical source materials whose child subgraphs differ must still produce byte-identical "
            + "canonical v2 output across installations.");
    }

    /// <summary>
    /// Two documents identical on every scalar the v2 SourceMaterial ordering compares (ContentFingerprint,
    /// Title, TextLanguage, ExplanationLanguage, LookupMode, canonical TargetLanguage, Content, WordCount and
    /// UTC-normalized ImportedAt — all supplied by the shared <see cref="NewCollidingHistoryDocument"/>), and
    /// differing only in their exported child subgraph: one carries a single sentence spanning the whole text
    /// with a "bank" occurrence, the other splits the same text into two sentences and carries a "river"
    /// occurrence in the second. Every span is a valid, in-bounds range whose surface form is the exact
    /// original substring, and each document's sentence and occurrence <c>Order</c> values are unique, so the
    /// emitted child ordering is itself total.
    /// </summary>
    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewScalarIdenticalChildGraphInstallation(
        int bankDocumentId, int riverDocumentId, int bankWordId, int riverWordId)
    {
        var bankDocument = NewCollidingHistoryDocument(bankDocumentId);
        var riverDocument = NewCollidingHistoryDocument(riverDocumentId);
        var bank = NewCanonicalOrderingWord(bankWordId, "bank", KnownFirst.Models.WordStatus.Known);
        var river = NewCanonicalOrderingWord(riverWordId, "river", KnownFirst.Models.WordStatus.UnknownBacklog);

        var wholeTextLength = CollidingHistoryContent.Length;
        var halfTextLength = wholeTextLength / 2;

        var bankSentence = NewChildGraphSentence(
            id: bankDocumentId * 100 + 1, documentId: bankDocument.Id, order: 0, start: 0, length: wholeTextLength);
        var bankOccurrence = NewChildGraphOccurrence(
            id: bankDocumentId * 100 + 11, documentId: bankDocument.Id, sentenceSpanId: bankSentence.Id,
            wordId: bank.Id, order: 0, start: BankSurfaceFormStart, length: BankSurfaceForm.Length,
            surfaceForm: BankSurfaceForm);

        var riverFirstSentence = NewChildGraphSentence(
            id: riverDocumentId * 100 + 1, documentId: riverDocument.Id, order: 0, start: 0, length: halfTextLength);
        var riverSecondSentence = NewChildGraphSentence(
            id: riverDocumentId * 100 + 2, documentId: riverDocument.Id, order: 1,
            start: halfTextLength, length: wholeTextLength - halfTextLength);
        var riverOccurrence = NewChildGraphOccurrence(
            id: riverDocumentId * 100 + 11, documentId: riverDocument.Id, sentenceSpanId: riverSecondSentence.Id,
            wordId: river.Id, order: 0, start: RiverSurfaceFormStart, length: RiverSurfaceForm.Length,
            surfaceForm: RiverSurfaceForm);

        // A raw Schema8BackupSnapshot capture reads unordered SELECTs, so row enumeration order follows the
        // local rowids; ordering the fixture rows by id reproduces exactly that.
        return NewChildGraphSnapshot(
            new[] { bankDocument, riverDocument }.OrderBy(document => document.Id).ToList(),
            new[] { bank, river }.OrderBy(word => word.Id).ToList(),
            new[] { bankSentence, riverFirstSentence, riverSecondSentence }.OrderBy(sentence => sentence.Id).ToList(),
            new[] { bankOccurrence, riverOccurrence }.OrderBy(occurrence => occurrence.Id).ToList());
    }

    private const string BankSurfaceForm = "bank";
    private const int BankSurfaceFormStart = 4;
    private const string RiverSurfaceForm = "river";
    private const int RiverSurfaceFormStart = 26;

    private static SentenceSpanEntity NewChildGraphSentence(
        int id, int documentId, int order, int start, int length) => new()
        {
            Id = id,
            DocumentId = documentId,
            Order = order,
            StartPosition = start,
            Length = length
        };

    private static WordOccurrenceEntity NewChildGraphOccurrence(
        int id, int documentId, int sentenceSpanId, int wordId, int order, int start, int length, string surfaceForm) => new()
        {
            Id = id,
            DocumentId = documentId,
            SentenceSpanId = sentenceSpanId,
            WordId = wordId,
            Order = order,
            StartPosition = start,
            Length = length,
            SurfaceForm = surfaceForm,
            TechnicalFamily = KnownFirst.Core.Text.TechnicalTokenFamily.None,
            TechnicalInstanceYear = null,
            TechnicalInstanceIdentifier = string.Empty,
            TechnicalVariant = string.Empty
        };

    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewChildGraphSnapshot(
        IReadOnlyList<DocumentEntity> documents,
        IReadOnlyList<WordEntity> words,
        IReadOnlyList<SentenceSpanEntity> sentences,
        IReadOnlyList<WordOccurrenceEntity> occurrences) => new(
            documents,
            words,
            [],
            sentences,
            occurrences,
            [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);

    /// <summary>
    /// The installation-independent part of a v2 payload's source-material subgraph, including the child
    /// content the scalar ordering components cannot see: which archive-local sm-* id binds to which sentence
    /// and occurrence rows, and which ss-* id each occurrence references.
    /// </summary>
    private static List<string> CanonicalSourceMaterialChildProjection(BackupPayloadV2 payload) =>
        payload.SourceMaterials
            .Select(material => string.Join(
                " | ",
                material.Id,
                string.Join(
                    ",",
                    material.Sentences.Select(sentence => string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}/{1}/{2}/{3}",
                        sentence.Id, sentence.Order, sentence.Start, sentence.Length))),
                string.Join(
                    ",",
                    material.Occurrences.Select(occurrence => string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}/{1}/{2}/{3}/{4}/{5}",
                        occurrence.VocabularyId, occurrence.SentenceId, occurrence.Order,
                        occurrence.Start, occurrence.Length, occurrence.SurfaceForm)))))
            .ToList();

    private static List<string> CanonicalSourceMaterialProjection(BackupPayloadV2 payload) =>
        payload.SourceMaterials
            .Select(material => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4}",
                material.Id, material.TextLanguage, material.ExplanationLanguage,
                material.LookupMode, material.TargetLanguage ?? "<none>"))
            .ToList();

    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewSourceMaterialOrderingSnapshot(
        IReadOnlyList<DocumentEntity> documents) => new(
            documents,
            [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);

    private const string CollidingHistoryContent = "The bank is open near the river.";
    private static readonly DateTime CollidingHistoryImportedAtUtc = new(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CollidingHistoryStartedAtUtc = new(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CollidingHistoryCompletedAtUtc = new(2026, 4, 1, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// One installation holding exactly two completed review histories over one document. Both sessions are
    /// equal on every session-level field the v2 mapper orders by (document, Status, TotalCandidates,
    /// ReviewedCount, KnownCount, UnknownCount, IgnoredCount, DecisionSequence, StartedAt, CompletedAt); they
    /// differ only in which word each history decided Known and which UnknownBacklog, so the two Schema-9
    /// full-history identities are genuinely distinct and neither session repeats a vocabulary identity.
    /// </summary>
    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewCollidingReviewHistoryInstallation(
        int documentId, int bankWordId, int riverWordId,
        int bankKnownSessionId, int riverKnownSessionId,
        int bankKnownCandidateBaseId, int riverKnownCandidateBaseId)
    {
        var document = NewCollidingHistoryDocument(documentId);
        var bank = NewCanonicalOrderingWord(bankWordId, "bank", KnownFirst.Models.WordStatus.Known);
        var river = NewCanonicalOrderingWord(riverWordId, "river", KnownFirst.Models.WordStatus.UnknownBacklog);

        var bankKnownSession = NewCompletedReviewSession(
            id: bankKnownSessionId, documentId: document.Id,
            startedAtUtc: CollidingHistoryStartedAtUtc, completedAtUtc: CollidingHistoryCompletedAtUtc,
            knownCount: 1, unknownCount: 1, ignoredCount: 0);
        var riverKnownSession = NewCompletedReviewSession(
            id: riverKnownSessionId, documentId: document.Id,
            startedAtUtc: CollidingHistoryStartedAtUtc, completedAtUtc: CollidingHistoryCompletedAtUtc,
            knownCount: 1, unknownCount: 1, ignoredCount: 0);

        ReviewCandidateEntity[] candidates =
        [
            NewDecidedReviewCandidate(
                bankKnownCandidateBaseId, bankKnownSession.Id, bank.Id, 0, KnownFirst.Models.WordStatus.Known),
            NewDecidedReviewCandidate(
                bankKnownCandidateBaseId + 1, bankKnownSession.Id, river.Id, 1, KnownFirst.Models.WordStatus.UnknownBacklog),
            NewDecidedReviewCandidate(
                riverKnownCandidateBaseId, riverKnownSession.Id, bank.Id, 0, KnownFirst.Models.WordStatus.UnknownBacklog),
            NewDecidedReviewCandidate(
                riverKnownCandidateBaseId + 1, riverKnownSession.Id, river.Id, 1, KnownFirst.Models.WordStatus.Known)
        ];

        // A raw Schema8BackupSnapshot capture reads unordered SELECTs, so row enumeration order follows the
        // local rowids; ordering the fixture rows by id reproduces exactly that.
        return NewCanonicalOrderingSnapshot(
            document,
            new[] { bank, river }.OrderBy(word => word.Id).ToList(),
            new[] { bankKnownSession, riverKnownSession }.OrderBy(session => session.Id).ToList(),
            candidates.OrderBy(candidate => candidate.Id).ToList());
    }

    private static DocumentEntity NewCollidingHistoryDocument(int id) => new()
    {
        Id = id,
        Title = "Colliding review history document",
        TextLanguage = "en",
        ExplanationLanguage = "de",
        LookupMode = KnownFirst.Core.Preparation.LexicalLookupMode.Definition,
        Content = CollidingHistoryContent,
        ContentFingerprint = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(CollidingHistoryContent))).ToLowerInvariant(),
        ImportedAt = CollidingHistoryImportedAtUtc,
        WordCount = 7
    };

    private static ReviewCandidateEntity NewDecidedReviewCandidate(
        int id, int sessionId, int wordId, int order, KnownFirst.Models.WordStatus status) => new()
        {
            Id = id,
            SessionId = sessionId,
            WordId = wordId,
            Order = order,
            Status = status,
            PreviousWordStatus = KnownFirst.Models.WordStatus.Unreviewed,
            PreviousTotalOccurrenceCount = 0,
            PreviousDocumentCount = 0,
            PreviousUpdatedAt = CollidingHistoryStartedAtUtc,
            DecisionSequence = order + 1,
            WasWordCreatedForSession = false,
            DecidedAt = CollidingHistoryStartedAtUtc.AddMinutes(order + 1)
        };

    /// <summary>
    /// The installation-independent part of a v2 payload's completed-review subgraph: which archive-local
    /// workflow/item id binds to which document, decision content, and ordinal position.
    /// </summary>
    private static List<string> CanonicalReviewSubgraphProjection(BackupPayloadV2 payload) =>
        payload.Workflows.VocabularyReviews
            .Select(workflow => string.Join(
                " | ",
                new[]
                {
                    workflow.Id,
                    workflow.SourceMaterialId,
                    workflow.Status.ToString(),
                    workflow.KnownCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    workflow.UnknownCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }.Concat(workflow.Items.Select(item =>
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}/{1}/{2}/{3}",
                        item.Id, item.VocabularyId, item.Order, item.Status)))))
            .ToList();

    private static WordEntity NewCanonicalOrderingWord(int id, string term, KnownFirst.Models.WordStatus status) => new()
    {
        Id = id,
        Language = "en",
        CanonicalTerm = term,
        NormalizedTerm = term,
        Status = status,
        TokenKind = KnownFirst.Core.Text.TokenKind.Word,
        PreparationState = KnownFirst.Core.Preparation.PreparationState.Unprepared,
        CreatedAt = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc)
    };

    private static ReviewSessionEntity NewCompletedReviewSession(
        int id, int documentId, DateTime startedAtUtc, DateTime completedAtUtc,
        int knownCount, int unknownCount, int ignoredCount) => new()
        {
            Id = id,
            DocumentId = documentId,
            Status = KnownFirst.Models.ReviewSessionStatus.Completed,
            TotalCandidates = 2,
            ReviewedCount = 2,
            KnownCount = knownCount,
            UnknownCount = unknownCount,
            IgnoredCount = ignoredCount,
            DecisionSequence = 2,
            StartedAt = startedAtUtc,
            CompletedAt = completedAtUtc
        };

    private static ReviewCandidateEntity[] NewReviewCandidates(
        int sessionId, int baseId, int firstWordId, int secondWordId, DateTime startedAtUtc) =>
    [
        NewReviewCandidate(baseId, sessionId, firstWordId, order: 0, startedAtUtc),
        NewReviewCandidate(baseId + 1, sessionId, secondWordId, order: 1, startedAtUtc)
    ];

    private static ReviewCandidateEntity NewReviewCandidate(
        int id, int sessionId, int wordId, int order, DateTime startedAtUtc) => new()
        {
            Id = id,
            SessionId = sessionId,
            WordId = wordId,
            Order = order,
            Status = KnownFirst.Models.WordStatus.Known,
            PreviousWordStatus = KnownFirst.Models.WordStatus.Unreviewed,
            PreviousTotalOccurrenceCount = 0,
            PreviousDocumentCount = 0,
            PreviousUpdatedAt = startedAtUtc,
            DecisionSequence = order + 1,
            WasWordCreatedForSession = false,
            DecidedAt = startedAtUtc.AddMinutes(order + 1)
        };

    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewCanonicalOrderingSnapshot(
        DocumentEntity document,
        IReadOnlyList<WordEntity> words,
        IReadOnlyList<ReviewSessionEntity> reviewSessions,
        IReadOnlyList<ReviewCandidateEntity> reviewCandidates) => new(
            [document],
            words,
            [],
            [],
            [],
            [],
            [],
            reviewSessions,
            reviewCandidates,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    [TestMethod]
    public void CreateBackupV2_LegacyReviewSummariesWithNullAndMinimumTimestamps_ProduceIdenticalCanonicalOutputAcrossInstallations()
    {
        var wordTimestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var minimumUtc = new DateTime(DateTime.MinValue.Ticks, DateTimeKind.Utc);

        static WordEntity NewWord(int id, DateTime timestamp) => new()
        {
            Id = id,
            Language = "en",
            CanonicalTerm = "legacy",
            NormalizedTerm = "legacy",
            TokenKind = KnownFirst.Core.Text.TokenKind.Word,
            Status = KnownFirst.Models.WordStatus.Unreviewed,
            PreparationState = KnownFirst.Core.Preparation.PreparationState.Unprepared,
            AutomaticInteractionMode = KnownFirst.Core.Learning.LearningInteractionMode.Reading,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        static ReviewStateEntity NewSummary(int id, int wordId, DateTime? lastReviewedAt) => new()
        {
            Id = id,
            WordId = wordId,
            ReviewCount = 4,
            ForgotCount = 1,
            PartialCount = 1,
            KnownCount = 2,
            LastReviewedAt = lastReviewedAt
        };

        static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewInstallation(
            WordEntity word,
            IReadOnlyList<ReviewStateEntity> summaries) => new(
                [],
                [word],
                [],
                [],
                [],
                [],
                summaries,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);

        var installationA = NewInstallation(
            NewWord(id: 1, wordTimestamp),
            [
                NewSummary(id: 10, wordId: 1, lastReviewedAt: null),
                NewSummary(id: 20, wordId: 1, lastReviewedAt: minimumUtc)
            ]);
        var installationB = NewInstallation(
            NewWord(id: 42, wordTimestamp),
            [
                NewSummary(id: 10, wordId: 42, lastReviewedAt: minimumUtc),
                NewSummary(id: 20, wordId: 42, lastReviewedAt: null)
            ]);

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);
        var summariesA = payloadA.Vocabulary.Single().LegacyReviewSummaries;
        var summariesB = payloadB.Vocabulary.Single().LegacyReviewSummaries;

        Assert.HasCount(2, summariesA, "Canonical ordering must preserve every legacy review summary row.");
        Assert.HasCount(2, summariesB, "Canonical ordering must preserve every legacy review summary row.");

        static string Projection(IReadOnlyList<BackupLegacyReviewSummary> summaries) => string.Join(
            Environment.NewLine,
            summaries.Select(summary =>
                $"{summary.ReviewCount}|{summary.ForgotCount}|{summary.PartialCount}|{summary.KnownCount}|"
                + (summary.LastReviewedAtUtc?.ToString("O") ?? "null")));

        Assert.AreEqual(
            Projection(summariesA),
            Projection(summariesB),
            "The same null and present UTC DateTime.MinValue summaries must have one canonical order, "
            + "regardless of local row IDs or controlled snapshot enumeration order.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Logically equivalent installations must produce byte-identical canonical V2 data.json output.");
    }

    [TestMethod]
    public void CreateBackupV2_TwoInstallationsWithOppositeSenseStableIdOrder_ProduceIdenticalCanonicalLearningCardOrder()
    {
        var createdAtUtc = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);

        static WordEntity NewWord(int id, string term, DateTime createdAtUtc) => new()
        {
            Id = id,
            Language = "en",
            CanonicalTerm = term,
            NormalizedTerm = term,
            TokenKind = KnownFirst.Core.Text.TokenKind.Word,
            Status = KnownFirst.Models.WordStatus.Known,
            PreparationState = KnownFirst.Core.Preparation.PreparationState.Prepared,
            CreatedAt = createdAtUtc,
            UpdatedAt = createdAtUtc
        };

        static KnownFirst.Data.Migrations.Schema8.SenseRow NewSense(
            int id, int wordId, int defaultMeaningId, string stableId, string providerSenseId, DateTime createdAtUtc) => new()
            {
                Id = id,
                StableId = stableId,
                WordId = wordId,
                SourceLanguage = "en",
                ExplanationLanguage = "de",
                ProviderSenseId = providerSenseId,
                PartOfSpeech = "noun",
                GrammaticalRelationship = "noun",
                DefaultMeaningId = defaultMeaningId,
                Status = KnownFirst.Data.Migrations.Schema8.SenseStatus.Learning,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc
            };

        static KnownFirst.Data.Schema8.Schema8MeaningRow NewMeaning(
            int id, int wordId, int senseId, string stableId, string term, string providerSenseId,
            string translation, string definition, DateTime createdAtUtc) => new()
            {
                Id = id,
                WordId = wordId,
                SenseId = senseId,
                StableId = stableId,
                SourceLanguage = "en",
                ExplanationLanguage = "de",
                DisplayTerm = term,
                GrammaticalRelationship = "noun",
                TokenKind = KnownFirst.Core.Text.TokenKind.Word,
                SelectedMeaningId = providerSenseId,
                Translation = translation,
                Definition = definition,
                AcceptedAliasesJson = "[]",
                Source = "test-dictionary",
                SourceProject = "canonical-ordering",
                SourcePageTitle = term,
                ConfirmedByUser = true,
                CreatedAt = createdAtUtc,
                UpdatedAt = createdAtUtc,
                PreparedAt = createdAtUtc
            };

        static KnownFirst.Data.Migrations.Schema8.AnswerVariantRow NewVariant(
            int id, int senseId, int meaningId, string stableId, string text, DateTime createdAtUtc) => new()
            {
                Id = id,
                StableId = stableId,
                SenseId = senseId,
                AnswerLanguage = "de",
                DisplayText = text,
                NormalizedText = text.ToLowerInvariant(),
                SourceMeaningId = meaningId,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc
            };

        static KnownFirst.Data.Migrations.Schema8.SenseAnswerVariantAssignmentRow NewAssignment(
            int id, int senseId, int variantId, string stableId, DateTime createdAtUtc) => new()
            {
                Id = id,
                StableId = stableId,
                SenseId = senseId,
                CardDirection = KnownFirst.Core.Learning.CardDirection.TermToMeaning,
                AnswerVariantId = variantId,
                Requirement = KnownFirst.Data.Migrations.Schema8.AnswerVariantRequirement.Required,
                IsPreferred = true,
                RequiredSinceUtc = createdAtUtc,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc
            };

        static KnownFirst.Data.Schema8.Schema8CardRow NewCard(
            int id, int wordId, int senseId, int meaningId, bool bankCard, DateTime createdAtUtc) => new()
            {
                Id = id,
                WordId = wordId,
                SenseId = senseId,
                PreferredMeaningId = meaningId,
                Direction = KnownFirst.Core.Learning.CardDirection.TermToMeaning,
                State = bankCard ? KnownFirst.Core.Learning.CardState.Review : KnownFirst.Core.Learning.CardState.Learning,
                DueAtUtc = bankCard ? createdAtUtc.AddDays(2) : createdAtUtc.AddDays(1),
                IntervalDays = bankCard ? 5 : 2,
                EaseFactor = bankCard ? 2.3 : 2.1,
                SuccessfulReviewCount = bankCard ? 4 : 1,
                LapseCount = bankCard ? 1 : 0,
                LastReviewedAtUtc = bankCard ? createdAtUtc.AddHours(1) : null,
                LastRating = bankCard ? KnownFirst.Core.Learning.ReviewRating.Good : null,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = bankCard ? createdAtUtc.AddHours(2) : createdAtUtc.AddHours(3)
            };

        static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewInstallation(
            int bankWordId, int riverWordId, int bankSenseId, int riverSenseId,
            int bankMeaningId, int riverMeaningId, int bankVariantId, int riverVariantId,
            int bankAssignmentId, int riverAssignmentId, int bankCardId, int riverCardId,
            string bankSenseStableId, string riverSenseStableId,
            string bankMeaningStableId, string riverMeaningStableId,
            string bankVariantStableId, string riverVariantStableId,
            string bankAssignmentStableId, string riverAssignmentStableId,
            bool reverseEnumeration, DateTime createdAtUtc)
        {
            var bankWord = NewWord(bankWordId, "bank", createdAtUtc);
            var riverWord = NewWord(riverWordId, "river", createdAtUtc);
            var bankSense = NewSense(
                bankSenseId, bankWordId, bankMeaningId, bankSenseStableId, "bank-financial", createdAtUtc);
            var riverSense = NewSense(
                riverSenseId, riverWordId, riverMeaningId, riverSenseStableId, "river-edge", createdAtUtc);
            var bankMeaning = NewMeaning(
                bankMeaningId, bankWordId, bankSenseId, bankMeaningStableId, "bank", "bank-financial",
                "Bank", "A financial institution.", createdAtUtc);
            var riverMeaning = NewMeaning(
                riverMeaningId, riverWordId, riverSenseId, riverMeaningStableId, "river", "river-edge",
                "Flussufer", "The edge of a river.", createdAtUtc);
            var bankVariant = NewVariant(
                bankVariantId, bankSenseId, bankMeaningId, bankVariantStableId, "Bank", createdAtUtc);
            var riverVariant = NewVariant(
                riverVariantId, riverSenseId, riverMeaningId, riverVariantStableId, "Flussufer", createdAtUtc);
            var bankAssignment = NewAssignment(
                bankAssignmentId, bankSenseId, bankVariantId, bankAssignmentStableId, createdAtUtc);
            var riverAssignment = NewAssignment(
                riverAssignmentId, riverSenseId, riverVariantId, riverAssignmentStableId, createdAtUtc);
            var bankCard = NewCard(bankCardId, bankWordId, bankSenseId, bankMeaningId, bankCard: true, createdAtUtc);
            var riverCard = NewCard(riverCardId, riverWordId, riverSenseId, riverMeaningId, bankCard: false, createdAtUtc);

            return new KnownFirst.Data.Schema8.Schema8BackupSnapshot(
                [],
                reverseEnumeration ? [riverWord, bankWord] : [bankWord, riverWord],
                [], [], [],
                reverseEnumeration ? [riverMeaning, bankMeaning] : [bankMeaning, riverMeaning],
                [], [], [], [], [], [],
                reverseEnumeration ? [riverSense, bankSense] : [bankSense, riverSense],
                reverseEnumeration ? [riverVariant, bankVariant] : [bankVariant, riverVariant],
                reverseEnumeration ? [riverAssignment, bankAssignment] : [bankAssignment, riverAssignment],
                [],
                reverseEnumeration ? [riverCard, bankCard] : [bankCard, riverCard],
                [], [], []);
        }

        static string SenseStableIdForTerm(
            KnownFirst.Data.Schema8.Schema8BackupSnapshot snapshot, string term)
        {
            var wordId = snapshot.Words.Single(word => word.NormalizedTerm == term).Id;
            return snapshot.Senses.Single(sense => sense.WordId == wordId).StableId;
        }

        static List<string> SemanticCardProjection(BackupPayloadV2 payload)
        {
            var vocabularyById = payload.Vocabulary.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var vocabularyIdentityById = vocabularyById.ToDictionary(
                pair => pair.Key,
                pair => KnownFirst.Services.DataSafety.Merge.VocabularyMergeIdentityPolicy.Compute(pair.Value),
                StringComparer.Ordinal);
            var senseById = payload.Senses.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var meaningById = payload.PreparedLearning.ToDictionary(item => item.Id, StringComparer.Ordinal);

            return payload.Learning.Cards.Select(card =>
            {
                var vocabulary = vocabularyById[card.VocabularyId];
                var vocabularyIdentity = vocabularyIdentityById[card.VocabularyId];
                var sense = senseById[card.SenseId];
                var semanticSenseIdentity = KnownFirst.Services.DataSafety.Merge.SemanticMeaningIdentityPolicy.Compute(
                    sense, vocabularyIdentity);
                var preferredMeaning = meaningById[card.PreferredMeaningId];
                var preferredMeaningSense = senseById[preferredMeaning.SenseId];
                var preferredMeaningSenseIdentity = KnownFirst.Services.DataSafety.Merge.SemanticMeaningIdentityPolicy.Compute(
                    preferredMeaningSense, vocabularyIdentityById[preferredMeaning.VocabularyId]);
                var exactMeaningIdentity = KnownFirst.Services.DataSafety.Merge.ExactMeaningVariantIdentityPolicy.Compute(
                    preferredMeaning, preferredMeaningSenseIdentity);

                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}|{1}:{2}|{3}|{4}|{5}|{6}|{7}|{8:R}|{9}|{10}|{11}:{12}|{13}:{14}|{15}|{16}",
                    card.Id,
                    vocabulary.Language,
                    vocabulary.IdentityKey,
                    vocabularyIdentity.Value,
                    semanticSenseIdentity.Value,
                    exactMeaningIdentity.Value,
                    card.Direction,
                    card.State,
                    card.EaseFactor,
                    card.IntervalDays,
                    card.SuccessfulReviewCount,
                    card.LastReviewedAtUtc.HasValue,
                    card.LastReviewedAtUtc?.Ticks ?? 0L,
                    card.LastRating.HasValue,
                    card.LastRating.HasValue ? (int)card.LastRating.Value : 0,
                    card.CreatedAtUtc.Ticks,
                    card.UpdatedAtUtc.Ticks)
                    + string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "|{0}|{1}",
                        card.DueAtUtc.Ticks,
                        card.LapseCount);
            }).ToList();
        }

        var installationA = NewInstallation(
            bankWordId: 1, riverWordId: 2,
            bankSenseId: 11, riverSenseId: 22,
            bankMeaningId: 111, riverMeaningId: 222,
            bankVariantId: 1111, riverVariantId: 2222,
            bankAssignmentId: 11111, riverAssignmentId: 22222,
            bankCardId: 111111, riverCardId: 222222,
            bankSenseStableId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1",
            riverSenseStableId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb2",
            bankMeaningStableId: "ccccccccccccccccccccccccccccccc3",
            riverMeaningStableId: "ddddddddddddddddddddddddddddddd4",
            bankVariantStableId: "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeee5",
            riverVariantStableId: "fffffffffffffffffffffffffffffff6",
            bankAssignmentStableId: "assignment-bank-a",
            riverAssignmentStableId: "assignment-river-a",
            reverseEnumeration: false,
            createdAtUtc);
        var installationB = NewInstallation(
            bankWordId: 42, riverWordId: 41,
            bankSenseId: 420, riverSenseId: 410,
            bankMeaningId: 4200, riverMeaningId: 4100,
            bankVariantId: 42000, riverVariantId: 41000,
            bankAssignmentId: 420000, riverAssignmentId: 410000,
            bankCardId: 4200000, riverCardId: 4100000,
            bankSenseStableId: "ddddddddddddddddddddddddddddddd4",
            riverSenseStableId: "ccccccccccccccccccccccccccccccc3",
            bankMeaningStableId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb2",
            riverMeaningStableId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1",
            bankVariantStableId: "fffffffffffffffffffffffffffffff6",
            riverVariantStableId: "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeee5",
            bankAssignmentStableId: "assignment-bank-b",
            riverAssignmentStableId: "assignment-river-b",
            reverseEnumeration: true,
            createdAtUtc);

        Assert.IsTrue(
            string.CompareOrdinal(
                SenseStableIdForTerm(installationA, "bank"),
                SenseStableIdForTerm(installationA, "river")) < 0,
            "Installation A must bind the ordinally earlier Sense StableId to the bank card.");
        Assert.IsTrue(
            string.CompareOrdinal(
                SenseStableIdForTerm(installationB, "bank"),
                SenseStableIdForTerm(installationB, "river")) > 0,
            "Installation B must reverse the semantic association of the Sense StableId ordering.");

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);
        BackupModelContractV2.ValidatePayload(payloadA);
        BackupArchiveWriterV2.ValidatePayloadGraphV2(payloadA);
        BackupModelContractV2.ValidatePayload(payloadB);
        BackupArchiveWriterV2.ValidatePayloadGraphV2(payloadB);

        Assert.AreEqual(
            string.Join(Environment.NewLine, SemanticCardProjection(payloadA)),
            string.Join(Environment.NewLine, SemanticCardProjection(payloadB)),
            "The same c-* id must bind to the same semantic bank/river card and emitted card state across installations, "
            + "even when installation-random Sense StableIds have the opposite semantic association.");
    }

    // ---- Package D (KF-BACKUP-003): cross-installation canonical output for completed preparation
    // workflows, completed learning workflows, and learning-review events.
    //
    // Package B/C brought SourceMaterials and completed ReviewSessions to a total ordering over emitted
    // content. The v2 mapper's three remaining workflow/history collections were left behind:
    //   * PreparationSessions order by (Method, Status, TotalItems, CompletedItems, StartedAtUtc,
    //     UpdatedAtUtc) and omit the emitted CompletedAtUtc and every emitted PreparationItem;
    //   * LearningSessions order by (Status, the four rating counters, TotalCards, CompletedCards,
    //     StartedAtUtc, UpdatedAtUtc) and omit the emitted CompletedAtUtc and every emitted queue item;
    //   * LearningReviews omit the emitted LearningSessionId, TargetAnswerVariantId and
    //     MatchedAnswerVariantId.
    // None of the three ends in any tie-break at all — not even the local row-id fallback ReviewSessions
    // and the shipped v1 BackupModelMapper both retain — so two rows that tie on the key fall through to
    // raw snapshot enumeration order, i.e. local SQLite row order. Two installations holding the same
    // content under different local row ids then bind different archive-local ids to the same history.
    //
    // The key's U+0001 separator is not the defect: no string-typed field reaches any of these three
    // ContentKey call sites, so no delimiter ambiguity is representable. The defect is the missing
    // ordering material. ----

    private static readonly DateTime TiedWorkflowStartedAtUtc = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TiedWorkflowUpdatedAtUtc = new(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime TiedWorkflowFirstCompletedAtUtc = new(2026, 5, 1, 9, 20, 0, DateTimeKind.Utc);
    private static readonly DateTime TiedWorkflowSecondCompletedAtUtc = new(2026, 5, 1, 9, 25, 0, DateTimeKind.Utc);
    private static readonly DateTime TiedWorkflowReviewedAtUtc = new(2026, 5, 1, 9, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime TiedWorkflowReviewDueAtUtc = new(2026, 5, 2, 9, 15, 0, DateTimeKind.Utc);

    [TestMethod]
    public void CreateBackupV2_TwoInstallationsWithOppositeRowIds_TiedPreparationSessionSortKeys_ProduceIdenticalCanonicalOutput()
    {
        var installationA = NewTiedPreparationWorkflowInstallation(
            bankPreparedSessionId: 10, riverPreparedSessionId: 20,
            bankPreparedCandidateBaseId: 100, riverPreparedCandidateBaseId: 200,
            bankWordId: 1, riverWordId: 2);

        // Same two logical preparation histories, independently created: the opposite local session row
        // ids and therefore the opposite raw enumeration order.
        var installationB = NewTiedPreparationWorkflowInstallation(
            bankPreparedSessionId: 20, riverPreparedSessionId: 10,
            bankPreparedCandidateBaseId: 500, riverPreparedCandidateBaseId: 400,
            bankWordId: 42, riverWordId: 41);

        AssertPreparationSessionsTieOnCurrentSortKey(installationA);
        AssertPreparationSessionsTieOnCurrentSortKey(installationB);
        AssertOppositeRowOrder(
            installationA.PreparationSessions.Select(session => session.CompletedAtUtc).ToList(),
            installationB.PreparationSessions.Select(session => session.CompletedAtUtc).ToList(),
            "preparation sessions");

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.HasCount(2, payloadA.Workflows.PreparationBatches);
        AssertPreparationWorkflowsDifferInEmittedContent(payloadA);
        AssertPreparationWorkflowsDifferInEmittedContent(payloadB);

        Assert.AreEqual(
            string.Join(Environment.NewLine, CanonicalPreparationWorkflowProjection(payloadA)),
            string.Join(Environment.NewLine, CanonicalPreparationWorkflowProjection(payloadB)),
            "Two installations holding the same two completed preparation histories must bind the same "
            + "archive-local pb-*/pi-* ids to the same history, regardless of which local row id each "
            + "installation assigned.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Two completed preparation histories that tie on every currently-ordered session field and "
            + "differ only in CompletedAtUtc and candidate content must still produce byte-identical "
            + "canonical v2 output across installations.");
    }

    [TestMethod]
    public void CreateBackupV2_TwoInstallationsWithOppositeRowIds_TiedLearningSessionSortKeys_ProduceIdenticalCanonicalOutput()
    {
        var installationA = NewTiedLearningWorkflowInstallation(
            bankSessionId: 10, riverSessionId: 20,
            bankQueueItemId: 100, riverQueueItemId: 200,
            bankWordId: 1, riverWordId: 2, bankCardId: 300, riverCardId: 400);

        // Same two logical learning histories, independently created, with the opposite local session row
        // ids and therefore the opposite raw enumeration order.
        var installationB = NewTiedLearningWorkflowInstallation(
            bankSessionId: 20, riverSessionId: 10,
            bankQueueItemId: 500, riverQueueItemId: 400,
            bankWordId: 42, riverWordId: 41, bankCardId: 700, riverCardId: 600);

        AssertLearningSessionsTieOnCurrentSortKey(installationA);
        AssertLearningSessionsTieOnCurrentSortKey(installationB);
        AssertOppositeRowOrder(
            installationA.LearningSessions.Select(session => session.CompletedAtUtc).ToList(),
            installationB.LearningSessions.Select(session => session.CompletedAtUtc).ToList(),
            "learning sessions");

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.HasCount(2, payloadA.Workflows.LearningSessions);
        AssertLearningWorkflowsDifferInEmittedContent(payloadA);
        AssertLearningWorkflowsDifferInEmittedContent(payloadB);

        Assert.AreEqual(
            string.Join(Environment.NewLine, CanonicalLearningWorkflowProjection(payloadA)),
            string.Join(Environment.NewLine, CanonicalLearningWorkflowProjection(payloadB)),
            "Two installations holding the same two completed learning histories must bind the same "
            + "archive-local ls-*/lq-* ids to the same history, regardless of which local row id each "
            + "installation assigned.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Two completed learning histories that tie on every currently-ordered session field and differ "
            + "only in CompletedAtUtc and queue-item content must still produce byte-identical canonical v2 "
            + "output across installations.");
    }

    // ---- Package D: the LearningReviews ordering key stops at EaseFactor, so two review rows that tie on
    // every field it considers fall through to raw enumeration order even though the emitted rows differ in
    // LearningSessionId, TargetAnswerVariantId and MatchedAnswerVariantId.
    //
    // This pins the mapper's totality contract, exactly as
    // CreateBackupV2_TwoIndistinguishableCompletedReviewSessions_RemainByteIdenticalUnderAnyRowIdAssignment
    // does for completed review sessions. It is NOT a claim that two reviews of one card sharing an
    // identical ReviewedAtUtc is normal user-reachable runtime behaviour: LearningService records one review
    // per submitted rating with a wall-clock timestamp. ----
    [TestMethod]
    public void CreateBackupV2_LearningReviewsTiedOnSortKeyButDifferingInSessionOrAnswerVariant_ProduceIdenticalCanonicalOrder()
    {
        var installationA = NewTiedLearningReviewInstallation(
            shortSessionId: 10, longSessionId: 20,
            firstReviewId: 100, secondReviewId: 200,
            alphaVariantId: 300, betaVariantId: 400,
            wordId: 1, cardId: 500);

        // Same logical content, independently created: the opposite local LearningReview row ids and
        // therefore the opposite raw enumeration order.
        var installationB = NewTiedLearningReviewInstallation(
            shortSessionId: 20, longSessionId: 10,
            firstReviewId: 200, secondReviewId: 100,
            alphaVariantId: 400, betaVariantId: 300,
            wordId: 42, cardId: 900);

        AssertLearningReviewsTieOnCurrentSortKey(installationA);
        AssertLearningReviewsTieOnCurrentSortKey(installationB);
        // Marked by a content property, never by a local row id: this fixture deliberately assigns the
        // opposite local ids to the answer variants and sessions too, so only content is comparable.
        AssertOppositeRowOrder(
            installationA.LearningReviews.Select(review => review.MatchedAnswerVariantId.HasValue).ToList(),
            installationB.LearningReviews.Select(review => review.MatchedAnswerVariantId.HasValue).ToList(),
            "learning reviews");

        var payloadA = BackupModelMapperV2.MapToExternal(installationA);
        var payloadB = BackupModelMapperV2.MapToExternal(installationB);

        Assert.HasCount(2, payloadA.Learning.ReviewEvents);
        AssertLearningReviewsDifferInEmittedContent(payloadA);
        AssertLearningReviewsDifferInEmittedContent(payloadB);

        Assert.AreEqual(
            string.Join(Environment.NewLine, CanonicalLearningReviewProjection(payloadA)),
            string.Join(Environment.NewLine, CanonicalLearningReviewProjection(payloadB)),
            "Two review events that tie on every currently-ordered field must still be emitted in the same "
            + "order across installations, because the emitted rows differ in session and answer-variant "
            + "references the ordering key never consults.");

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        Assert.IsTrue(
            bytesA.AsSpan().SequenceEqual(bytesB.AsSpan()),
            "Review events distinguishable only by their emitted session/answer-variant references must "
            + "still produce byte-identical canonical v2 output across installations.");
    }

    // ---- Package D fixtures and projections ----

    private static void AssertOppositeRowOrder<T>(
        IReadOnlyList<T> firstInstallationOrder, IReadOnlyList<T> secondInstallationOrder, string what)
    {
        Assert.HasCount(2, firstInstallationOrder);
        Assert.HasCount(2, secondInstallationOrder);
        Assert.AreEqual(
            firstInstallationOrder[0], secondInstallationOrder[1],
            $"The two installations must enumerate their {what} in the opposite order; otherwise this test "
            + "would not exercise the ordering boundary at all.");
        Assert.AreEqual(firstInstallationOrder[1], secondInstallationOrder[0]);
    }

    private static void AssertPreparationSessionsTieOnCurrentSortKey(
        KnownFirst.Data.Schema8.Schema8BackupSnapshot snapshot)
    {
        Assert.HasCount(2, snapshot.PreparationSessions);
        var first = snapshot.PreparationSessions[0];
        var second = snapshot.PreparationSessions[1];

        Assert.AreEqual(first.Method, second.Method, "Method participates in the current sort key.");
        Assert.AreEqual(first.Status, second.Status, "Status participates in the current sort key.");
        Assert.AreEqual(first.TotalItems, second.TotalItems, "TotalItems participates in the current sort key.");
        Assert.AreEqual(first.CompletedItems, second.CompletedItems, "CompletedItems participates in the current sort key.");
        Assert.AreEqual(first.StartedAtUtc, second.StartedAtUtc, "StartedAtUtc participates in the current sort key.");
        Assert.AreEqual(first.UpdatedAtUtc, second.UpdatedAtUtc, "UpdatedAtUtc participates in the current sort key.");
    }

    private static void AssertPreparationWorkflowsDifferInEmittedContent(BackupPayloadV2 payload)
    {
        var first = payload.Workflows.PreparationBatches[0];
        var second = payload.Workflows.PreparationBatches[1];

        Assert.AreNotEqual(
            first.CompletedAtUtc, second.CompletedAtUtc,
            "The two emitted preparation workflows must differ in CompletedAtUtc, which the current sort key omits.");
        Assert.AreNotEqual(
            EmittedPreparationItems(first), EmittedPreparationItems(second),
            "The two emitted preparation workflows must differ in candidate content, which the current sort key omits.");
    }

    private static string EmittedPreparationItems(BackupPreparationWorkflow workflow) =>
        string.Join(
            ",",
            workflow.Items.Select(item => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}/{1}/{2}/{3}", item.VocabularyId, item.Order, item.Status, item.LastErrorCode ?? "<none>")));

    private static List<string> CanonicalPreparationWorkflowProjection(BackupPayloadV2 payload) =>
        payload.Workflows.PreparationBatches
            .Select(workflow => string.Join(
                " | ",
                new[]
                {
                    workflow.Id,
                    workflow.Status.ToString(),
                    workflow.Method.ToString(),
                    workflow.TotalItems.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    workflow.CompletedItems.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    workflow.StartedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    workflow.UpdatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    workflow.CompletedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "<none>"
                }.Concat(workflow.Items.Select(item => string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}/{1}/{2}/{3}/{4}/{5}/{6}",
                    item.Id, item.VocabularyId, item.Order, item.Status, item.SelectedMeaningIndex,
                    item.LastErrorCode ?? "<none>", item.LookupAttemptCount)))))
            .ToList();

    /// <summary>
    /// One installation holding exactly two completed preparation histories. Both are equal on every field
    /// the current v2 ordering compares (Method, Status, TotalItems, CompletedItems, StartedAtUtc,
    /// UpdatedAtUtc); they differ in the emitted CompletedAtUtc and in which word each batch prepared
    /// successfully — both of which the current key ignores entirely.
    /// </summary>
    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewTiedPreparationWorkflowInstallation(
        int bankPreparedSessionId, int riverPreparedSessionId,
        int bankPreparedCandidateBaseId, int riverPreparedCandidateBaseId,
        int bankWordId, int riverWordId)
    {
        // Prepared/Learning/Mastered are rejected at the Word level by BackupModelContractV2 (Schema-8 moved
        // that concern to Sense progression), so the fixture uses review-level statuses.
        var bank = NewCanonicalOrderingWord(bankWordId, "bank", KnownFirst.Models.WordStatus.Known);
        var river = NewCanonicalOrderingWord(riverWordId, "river", KnownFirst.Models.WordStatus.UnknownBacklog);

        var bankPreparedSession = NewCompletedPreparationSession(
            bankPreparedSessionId, TiedWorkflowFirstCompletedAtUtc);
        var riverPreparedSession = NewCompletedPreparationSession(
            riverPreparedSessionId, TiedWorkflowSecondCompletedAtUtc);

        PreparationCandidateEntity[] candidates =
        [
            NewPreparationCandidate(
                bankPreparedCandidateBaseId, bankPreparedSession.Id, bank.Id, 0,
                KnownFirst.Models.PreparationCandidateStatus.Prepared),
            NewPreparationCandidate(
                bankPreparedCandidateBaseId + 1, bankPreparedSession.Id, river.Id, 1,
                KnownFirst.Models.PreparationCandidateStatus.Failed),
            NewPreparationCandidate(
                riverPreparedCandidateBaseId, riverPreparedSession.Id, bank.Id, 0,
                KnownFirst.Models.PreparationCandidateStatus.Failed),
            NewPreparationCandidate(
                riverPreparedCandidateBaseId + 1, riverPreparedSession.Id, river.Id, 1,
                KnownFirst.Models.PreparationCandidateStatus.Prepared)
        ];

        // A raw Schema8BackupSnapshot capture reads unordered SELECTs, so row enumeration order follows the
        // local rowids; ordering the fixture rows by id reproduces exactly that.
        return NewPreparationOrderingSnapshot(
            new[] { bank, river }.OrderBy(word => word.Id).ToList(),
            new[] { bankPreparedSession, riverPreparedSession }.OrderBy(session => session.Id).ToList(),
            candidates.OrderBy(candidate => candidate.Id).ToList());
    }

    private static PreparationSessionEntity NewCompletedPreparationSession(int id, DateTime completedAtUtc) => new()
    {
        Id = id,
        Status = KnownFirst.Models.PreparationSessionStatus.Completed,
        Method = KnownFirst.Core.Preparation.PreparationMethod.AutomaticOnline,
        TotalItems = 2,
        CompletedItems = 2,
        StartedAtUtc = TiedWorkflowStartedAtUtc,
        UpdatedAtUtc = TiedWorkflowUpdatedAtUtc,
        CompletedAtUtc = completedAtUtc
    };

    private static PreparationCandidateEntity NewPreparationCandidate(
        int id, int sessionId, int wordId, int order,
        KnownFirst.Models.PreparationCandidateStatus status) => new()
        {
            Id = id,
            SessionId = sessionId,
            WordId = wordId,
            Order = order,
            Status = status,
            ResultJson = string.Empty,
            SelectedMeaningIndex = 0,
            LastErrorCode = status == KnownFirst.Models.PreparationCandidateStatus.Failed
                ? "lookup-failed"
                : string.Empty,
            LookupAttemptCount = 1,
            UpdatedAtUtc = TiedWorkflowUpdatedAtUtc
        };

    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewPreparationOrderingSnapshot(
        IReadOnlyList<WordEntity> words,
        IReadOnlyList<PreparationSessionEntity> preparationSessions,
        IReadOnlyList<PreparationCandidateEntity> preparationCandidates) => new(
            [],
            words,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            preparationSessions,
            preparationCandidates,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private static void AssertLearningSessionsTieOnCurrentSortKey(
        KnownFirst.Data.Schema8.Schema8BackupSnapshot snapshot)
    {
        Assert.HasCount(2, snapshot.LearningSessions);
        var first = snapshot.LearningSessions[0];
        var second = snapshot.LearningSessions[1];

        Assert.AreEqual(first.Status, second.Status, "Status participates in the current sort key.");
        Assert.AreEqual(first.TotalCards, second.TotalCards, "TotalCards participates in the current sort key.");
        Assert.AreEqual(first.CompletedCards, second.CompletedCards, "CompletedCards participates in the current sort key.");
        Assert.AreEqual(first.AgainCount, second.AgainCount, "AgainCount participates in the current sort key.");
        Assert.AreEqual(first.HardCount, second.HardCount, "HardCount participates in the current sort key.");
        Assert.AreEqual(first.GoodCount, second.GoodCount, "GoodCount participates in the current sort key.");
        Assert.AreEqual(first.EasyCount, second.EasyCount, "EasyCount participates in the current sort key.");
        Assert.AreEqual(first.StartedAtUtc, second.StartedAtUtc, "StartedAtUtc participates in the current sort key.");
        Assert.AreEqual(first.UpdatedAtUtc, second.UpdatedAtUtc, "UpdatedAtUtc participates in the current sort key.");
    }

    private static void AssertLearningWorkflowsDifferInEmittedContent(BackupPayloadV2 payload)
    {
        var first = payload.Workflows.LearningSessions[0];
        var second = payload.Workflows.LearningSessions[1];

        Assert.AreNotEqual(
            first.CompletedAtUtc, second.CompletedAtUtc,
            "The two emitted learning workflows must differ in CompletedAtUtc, which the current sort key omits.");
        Assert.AreNotEqual(
            EmittedQueueItems(first), EmittedQueueItems(second),
            "The two emitted learning workflows must differ in queue-item content, which the current sort key omits.");
    }

    private static string EmittedQueueItems(BackupLearningWorkflowV2 workflow) =>
        string.Join(
            ",",
            workflow.QueueItems.Select(item => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}/{1}/{2}", item.CardId, item.QueueOrder, item.Rating)));

    private static List<string> CanonicalLearningWorkflowProjection(BackupPayloadV2 payload) =>
        payload.Workflows.LearningSessions
            .Select(workflow => string.Join(
                " | ",
                new[]
                {
                    workflow.Id,
                    workflow.Status.ToString(),
                    workflow.TotalCards.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    workflow.CompletedCards.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    workflow.StartedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    workflow.UpdatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    workflow.CompletedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "<none>"
                }.Concat(workflow.QueueItems.Select(item => string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}/{1}/{2}/{3}/{4}/{5}",
                    item.Id, item.CardId, item.QueueOrder, item.IsCompleted, item.Rating,
                    item.TargetAnswerVariantId ?? "<none>")))))
            .ToList();

    /// <summary>
    /// One installation holding exactly two completed learning histories. Both are equal on every field the
    /// current v2 ordering compares (Status, TotalCards, CompletedCards, the four rating counters,
    /// StartedAtUtc, UpdatedAtUtc); they differ in the emitted CompletedAtUtc and in which card each session
    /// queued. The two cards carry no Sense reference and use different directions, so the card ordering is
    /// itself total and the fixture needs no Guid-generated StableId values — keeping serialized byte
    /// equality a legitimate oracle.
    /// </summary>
    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewTiedLearningWorkflowInstallation(
        int bankSessionId, int riverSessionId, int bankQueueItemId, int riverQueueItemId,
        int bankWordId, int riverWordId, int bankCardId, int riverCardId)
    {
        var bank = NewCanonicalOrderingWord(bankWordId, "bank", KnownFirst.Models.WordStatus.Known);
        var river = NewCanonicalOrderingWord(riverWordId, "river", KnownFirst.Models.WordStatus.UnknownBacklog);

        var bankCard = NewOrderingCard(bankCardId, bank.Id, KnownFirst.Core.Learning.CardDirection.TermToMeaning);
        var riverCard = NewOrderingCard(riverCardId, river.Id, KnownFirst.Core.Learning.CardDirection.MeaningToTerm);

        var bankSession = NewCompletedLearningSession(bankSessionId, TiedWorkflowFirstCompletedAtUtc);
        var riverSession = NewCompletedLearningSession(riverSessionId, TiedWorkflowSecondCompletedAtUtc);

        KnownFirst.Data.Schema8.Schema8QueueRow[] queueItems =
        [
            NewOrderingQueueItem(bankQueueItemId, bankSession.Id, bankCard.Id),
            NewOrderingQueueItem(riverQueueItemId, riverSession.Id, riverCard.Id)
        ];

        return NewLearningWorkflowOrderingSnapshot(
            new[] { bank, river }.OrderBy(word => word.Id).ToList(),
            [],
            new[] { bankCard, riverCard }.OrderBy(card => card.Id).ToList(),
            [],
            new[] { bankSession, riverSession }.OrderBy(session => session.Id).ToList(),
            queueItems.OrderBy(item => item.Id).ToList());
    }

    private static LearningSessionEntity NewCompletedLearningSession(int id, DateTime completedAtUtc) => new()
    {
        Id = id,
        Status = KnownFirst.Models.LearningSessionStatus.Completed,
        TotalCards = 1,
        CompletedCards = 1,
        AgainCount = 0,
        HardCount = 0,
        GoodCount = 1,
        EasyCount = 0,
        StartedAtUtc = TiedWorkflowStartedAtUtc,
        UpdatedAtUtc = TiedWorkflowUpdatedAtUtc,
        CompletedAtUtc = completedAtUtc
    };

    private static KnownFirst.Data.Schema8.Schema8CardRow NewOrderingCard(
        int id, int wordId, KnownFirst.Core.Learning.CardDirection direction) => new()
        {
            Id = id,
            WordId = wordId,
            SenseId = null,
            PreferredMeaningId = 0,
            Direction = direction,
            State = KnownFirst.Core.Learning.CardState.Review,
            DueAtUtc = TiedWorkflowReviewDueAtUtc,
            IntervalDays = 1,
            EaseFactor = 2.5,
            SuccessfulReviewCount = 1,
            LapseCount = 0,
            LastReviewedAtUtc = TiedWorkflowReviewedAtUtc,
            LastRating = KnownFirst.Core.Learning.ReviewRating.Good,
            CreatedAtUtc = TiedWorkflowStartedAtUtc,
            UpdatedAtUtc = TiedWorkflowUpdatedAtUtc
        };

    private static KnownFirst.Data.Schema8.Schema8QueueRow NewOrderingQueueItem(int id, int sessionId, int cardId) => new()
    {
        Id = id,
        SessionId = sessionId,
        CardId = cardId,
        QueueOrder = 0,
        IsDueCard = true,
        IsAgainRepeat = false,
        AnswerRevealed = true,
        SpellingChecked = false,
        SpellingCorrect = false,
        IsCompleted = true,
        Rating = KnownFirst.Core.Learning.ReviewRating.Good,
        CompletedAtUtc = TiedWorkflowReviewedAtUtc,
        TargetAnswerVariantId = null
    };

    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewLearningWorkflowOrderingSnapshot(
        IReadOnlyList<WordEntity> words,
        IReadOnlyList<KnownFirst.Data.Migrations.Schema8.AnswerVariantRow> answerVariants,
        IReadOnlyList<KnownFirst.Data.Schema8.Schema8CardRow> cards,
        IReadOnlyList<KnownFirst.Data.Schema8.Schema8ReviewRow> reviews,
        IReadOnlyList<LearningSessionEntity> learningSessions,
        IReadOnlyList<KnownFirst.Data.Schema8.Schema8QueueRow> queueItems) => new(
            [],
            words,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            answerVariants,
            [],
            [],
            cards,
            reviews,
            learningSessions,
            queueItems);

    private static void AssertLearningReviewsTieOnCurrentSortKey(
        KnownFirst.Data.Schema8.Schema8BackupSnapshot snapshot)
    {
        Assert.HasCount(2, snapshot.LearningReviews);
        var first = snapshot.LearningReviews[0];
        var second = snapshot.LearningReviews[1];

        Assert.AreEqual(first.CardId, second.CardId, "The emitted card reference participates in the current sort key.");
        Assert.AreEqual(first.ReviewedAtUtc, second.ReviewedAtUtc, "ReviewedAtUtc participates in the current sort key.");
        Assert.AreEqual(first.Rating, second.Rating, "Rating participates in the current sort key.");
        Assert.AreEqual(first.WasTypedAnswer, second.WasTypedAnswer, "WasTypedAnswer participates in the current sort key.");
        Assert.AreEqual(first.WasCorrect, second.WasCorrect, "WasCorrect participates in the current sort key.");
        Assert.AreEqual(first.DueAtUtc, second.DueAtUtc, "DueAtUtc participates in the current sort key.");
        Assert.AreEqual(first.IntervalDays, second.IntervalDays, "IntervalDays participates in the current sort key.");
        Assert.AreEqual(first.EaseFactor, second.EaseFactor, "EaseFactor participates in the current sort key.");
    }

    private static void AssertLearningReviewsDifferInEmittedContent(BackupPayloadV2 payload)
    {
        var first = payload.Learning.ReviewEvents[0];
        var second = payload.Learning.ReviewEvents[1];

        Assert.AreNotEqual(
            first.LearningSessionId, second.LearningSessionId,
            "The two emitted reviews must differ in LearningSessionId, which the current sort key omits.");
        Assert.AreNotEqual(
            first.TargetAnswerVariantId, second.TargetAnswerVariantId,
            "The two emitted reviews must differ in TargetAnswerVariantId, which the current sort key omits.");
        Assert.AreNotEqual(
            first.MatchedAnswerVariantId, second.MatchedAnswerVariantId,
            "The two emitted reviews must differ in MatchedAnswerVariantId, which the current sort key omits.");
    }

    private static List<string> CanonicalLearningReviewProjection(BackupPayloadV2 payload) =>
        payload.Learning.ReviewEvents
            .Select(review => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10}",
                review.CardId,
                review.LearningSessionId,
                review.Rating,
                review.WasTypedAnswer,
                review.WasCorrect,
                review.ReviewedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                review.DueAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                review.IntervalDays,
                review.EaseFactor.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
                review.TargetAnswerVariantId ?? "<none>",
                review.MatchedAnswerVariantId ?? "<none>"))
            .ToList();

    /// <summary>
    /// One installation holding one card and two review events that are equal on every field the current v2
    /// review ordering compares, and differ only in the emitted LearningSessionId, TargetAnswerVariantId and
    /// MatchedAnswerVariantId. The two parent learning sessions differ in TotalCards and the two answer
    /// variants carry fixed StableId values, so both of those collections are already totally ordered by
    /// content — the only ordering instability this fixture can expose is the review ordering itself.
    /// </summary>
    private static KnownFirst.Data.Schema8.Schema8BackupSnapshot NewTiedLearningReviewInstallation(
        int shortSessionId, int longSessionId, int firstReviewId, int secondReviewId,
        int alphaVariantId, int betaVariantId, int wordId, int cardId)
    {
        var word = NewCanonicalOrderingWord(wordId, "bank", KnownFirst.Models.WordStatus.Known);
        var card = NewOrderingCard(cardId, word.Id, KnownFirst.Core.Learning.CardDirection.TermToMeaning);

        var shortSession = NewCompletedLearningSession(shortSessionId, TiedWorkflowFirstCompletedAtUtc);
        var longSession = NewCompletedLearningSession(longSessionId, TiedWorkflowSecondCompletedAtUtc);
        longSession.TotalCards = 2;
        longSession.CompletedCards = 2;

        var alpha = NewOrderingAnswerVariant(alphaVariantId, "av-stable-alpha", "alpha");
        var beta = NewOrderingAnswerVariant(betaVariantId, "av-stable-beta", "beta");

        KnownFirst.Data.Schema8.Schema8ReviewRow[] reviews =
        [
            NewTiedReview(firstReviewId, card.Id, shortSession.Id, alpha.Id, alpha.Id),
            NewTiedReview(secondReviewId, card.Id, longSession.Id, beta.Id, null)
        ];

        return NewLearningWorkflowOrderingSnapshot(
            [word],
            new[] { alpha, beta }.OrderBy(variant => variant.Id).ToList(),
            [card],
            reviews.OrderBy(review => review.Id).ToList(),
            new[] { shortSession, longSession }.OrderBy(session => session.Id).ToList(),
            []);
    }

    private static KnownFirst.Data.Migrations.Schema8.AnswerVariantRow NewOrderingAnswerVariant(
        int id, string stableId, string text) => new()
        {
            Id = id,
            StableId = stableId,
            SenseId = 0,
            AnswerLanguage = "de",
            DisplayText = text,
            NormalizedText = text,
            SourceMeaningId = null,
            CreatedAtUtc = TiedWorkflowStartedAtUtc,
            UpdatedAtUtc = TiedWorkflowUpdatedAtUtc
        };

    private static KnownFirst.Data.Schema8.Schema8ReviewRow NewTiedReview(
        int id, int cardId, int sessionId, int? targetAnswerVariantId, int? matchedAnswerVariantId) => new()
        {
            Id = id,
            CardId = cardId,
            SessionId = sessionId,
            Rating = KnownFirst.Core.Learning.ReviewRating.Good,
            WasTypedAnswer = true,
            WasCorrect = true,
            ReviewedAtUtc = TiedWorkflowReviewedAtUtc,
            DueAtUtc = TiedWorkflowReviewDueAtUtc,
            IntervalDays = 1,
            EaseFactor = 2.5,
            TargetAnswerVariantId = targetAnswerVariantId,
            MatchedAnswerVariantId = matchedAnswerVariantId
        };
}
