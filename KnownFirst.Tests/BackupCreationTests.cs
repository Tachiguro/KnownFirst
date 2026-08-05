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
        var service = new KnownFirst.Services.TextReviewService(database, new KnownFirst.Core.Text.TextAnalyzer());

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
}
