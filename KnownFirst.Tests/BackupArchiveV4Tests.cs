using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema14;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class BackupArchiveV4Tests
{
    private sealed class TestAppSettings(LearningMode mode) : IAppSettingsService
    {
        public int PreparationLimit => PreparationLimitPolicy.DefaultLimit;
        public IReadOnlyList<int> SupportedPreparationLimits => [PreparationLimitPolicy.DefaultLimit];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => mode;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => false;
        public LearningTimezoneMode LearningTimezoneMode => LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId => null;
        public int LearningDayCutoffMinutes => LearningDayConfiguration.DefaultCutoffMinutes;
        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode m) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool enabled) => throw new NotSupportedException();
        public void SetLearningTimezoneMode(LearningTimezoneMode m) => throw new NotSupportedException();
        public void SetExplicitLearningTimezoneId(string? timezoneId) => throw new NotSupportedException();
        public void SetLearningDayCutoffMinutes(int minutes) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

    private sealed class ObservableLegacyScheduler : ISpacedRepetitionScheduler
    {
        public int InvocationCount { get; private set; }

        public CardSchedule Schedule(CardSchedule current, ReviewRating rating, DateTime reviewedAtUtc)
        {
            InvocationCount++;
            return new CardSchedule(
                current.State,
                reviewedAtUtc.AddDays(1),
                1,
                2.5,
                current.SuccessfulReviewCount + 1,
                current.LapseCount,
                reviewedAtUtc,
                rating);
        }
    }

    private sealed class TemporaryDatabaseAdapter(string rootDirectory, string path, SQLiteAsyncConnection connection, bool enableForeignKeys = false) : IKnownFirstDatabase, IAsyncDisposable
    {
        public string DatabasePath => path;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation) =>
            operation(connection);

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            T? result = default;
            await connection.RunInTransactionAsync(conn =>
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

        public async ValueTask DisposeAsync()
        {
            await connection.CloseAsync();
            if (Directory.Exists(rootDirectory))
            {
                try
                {
                    Directory.Delete(rootDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    private sealed class ThrowAtCheckpoint(string checkpoint) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName)
        {
            if (string.Equals(checkpointName, checkpoint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Injected V4 restore failure at {checkpoint}.");
            }
        }
    }

    private sealed class FailedSafetyCopyService : IMergeSafetyCopyService
    {
        public Task<MergeSafetyCopyResult> CreateSafetyCopyAsync(
            string? sourceDescription,
            CancellationToken cancellationToken) =>
            Task.FromResult(MergeSafetyCopyResult.Failed);
    }

    private static async Task<TemporaryDatabaseAdapter> CreateValidSchema14DatabaseAsync(bool enableForeignKeys = false)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "kf-schema14-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, "knownfirst.db3");
        var connection = new SQLiteAsyncConnection(path);
        await DatabaseSchema.InitializeAsync(connection);
        if (enableForeignKeys)
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        }

        return new TemporaryDatabaseAdapter(rootDirectory, path, connection, enableForeignKeys);
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

    private static int InsertSense(SQLiteConnection conn, int wordId, string timestamp, string stableId = "st-sense-1", string srcLang = "en", string expLang = "de")
    {
        conn.Execute(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, 1, ?, ?)
            """,
            stableId, wordId, srcLang, expLang, timestamp, timestamp);
        return conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    public static BackupPayloadV4 CreateValidPayloadV4()
    {
        var text = "network";
        var textSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var now = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);

        return new BackupPayloadV4(
            SourceMaterials:
            [
                new BackupSourceMaterial(
                    "doc-1",
                    "Document 1",
                    "en",
                    "de",
                    BackupLexicalLookupMode.Definition,
                    null,
                    text,
                    textSha,
                    now,
                    1,
                    [new BackupSentenceRange("sent-1", 0, 0, 7)],
                    [new BackupOccurrence("vocab-1", "sent-1", 0, 7, "network", 0, BackupTechnicalTokenFamily.None, null, null, null)])
            ],
            Vocabulary:
            [
                new BackupVocabularyItem(
                    "vocab-1",
                    "en",
                    "network",
                    "network",
                    BackupTokenKind.Word,
                    BackupKnowledgeState.Unreviewed,
                    BackupPreparationState.Prepared,
                    1,
                    1,
                    now,
                    now,
                    [new BackupEncounteredForm("network", 1)],
                    new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false),
                    [])
            ],
            Senses:
            [
                new BackupSense(
                    "sense-1",
                    "st-sense-1",
                    "vocab-1",
                    "en",
                    "de",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "prep-1",
                    BackupSenseStatus.Learning,
                    now,
                    now)
            ],
            PreparedLearning:
            [
                new BackupPreparedItemV2(
                    "prep-1",
                    "sense-1",
                    "st-prep-1",
                    "vocab-1",
                    "en",
                    "de",
                    "network",
                    "network",
                    null,
                    BackupTokenKind.Word,
                    null,
                    null,
                    "Netzwerk",
                    null,
                    null,
                    null,
                    null,
                    [],
                    true,
                    new BackupSourceReference("manual", "", "", null, ""),
                    now,
                    now,
                    now,
                    [new BackupContextSnapshotV2("doc-1", "Document 1", "network", 0, 7, "fp-1", now, "sense-1")])
            ],
            Workflows: new BackupWorkflowDataV2([], [], []),
            DerivedTermEvidence: [],
            WordLearningControls:
            [
                new BackupWordLearningControl("vocab-1", now)
            ],
            SenseLearningControls:
            [
                new BackupSenseLearningControl("sense-1", now)
            ],
            LearningTargets:
            [
                new BackupLearningTarget(
                    "lt-1",
                    "st-lt-1",
                    "sense-1",
                    BackupLearningTargetKind.Definition,
                    "en",
                    "de",
                    false,
                    now,
                    now)
            ],
            TargetAnswerVariants:
            [
                new BackupTargetAnswerVariant(
                    "tav-1",
                    "st-tav-1",
                    "lt-1",
                    "de",
                    "Netzwerk",
                    "netzwerk",
                    BackupAnswerVariantRequirement.Required,
                    true,
                    now,
                    "prep-1",
                    now,
                    now)
            ],
            TargetFsrsStates:
            [
                new BackupTargetFsrsState("lt-1", BackupFsrsCardStateKind.New, null, null, null, null, null)
            ],
            TargetFsrsReviewHistoryEntries: [],
            TargetReviews: [],
            Extensions: new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));
    }

    public static MemoryStream BuildArchiveV4(
        Func<BackupPayloadV4, BackupPayloadV4>? payloadMutator = null,
        Func<string, string>? dataMutator = null,
        Func<string, string>? manifestMutator = null)
    {
        var payload = CreateValidPayloadV4();
        if (payloadMutator is not null)
        {
            payload = payloadMutator(payload);
        }

        var counts = BackupModelContractV4.CountRecords(payload);
        var dataBytes = BackupJsonCodecV4.SerializeData(payload);

        if (dataMutator is not null)
        {
            var json = Encoding.UTF8.GetString(dataBytes);
            json = dataMutator(json);
            dataBytes = Encoding.UTF8.GetBytes(json);
        }

        var hash = SHA256.HashData(dataBytes);
        var hashString = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

        var manifest = new BackupManifestV4(
            FormatVersion: 4,
            SourceAppVersion: "1.0.0-test",
            SourceDatabaseSchemaVersion: 14,
            CreatedAtUtc: new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc),
            SourcePlatform: BackupSourcePlatform.Windows,
            RecordCounts: counts,
            OptionalFeatures: Array.Empty<string>(),
            RequiredFeatures: new[] { ArchiveLearningReviewCausalOrderPolicy.RequiredFeature },
            DataChecksum: hashString);

        var manifestBytes = BackupJsonCodecV4.SerializeManifest(manifest);
        if (manifestMutator is not null)
        {
            var mJson = Encoding.UTF8.GetString(manifestBytes);
            mJson = manifestMutator(mJson);
            manifestBytes = Encoding.UTF8.GetBytes(mJson);
        }

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var mStream = manifestEntry.Open())
            {
                mStream.Write(manifestBytes);
            }

            var dataEntry = zip.CreateEntry("data.json", CompressionLevel.Optimal);
            using (var dStream = dataEntry.Open())
            {
                dStream.Write(dataBytes);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [TestMethod]
    public void V4SourceGeneratedJson_CoversReachableTypes()
    {
        Type[] roots = [typeof(BackupManifestV4), typeof(BackupPayloadV4)];
        foreach (var root in roots)
        {
            Assert.IsNotNull(BackupJsonCodecV4.GetGeneratedTypeInfo(root), $"Missing generated TypeInfo for {root.Name}");
        }
    }

    [TestMethod]
    public void V4SourceGeneratedJson_RoundTripsAllV4Types()
    {
        var payload = CreateValidPayloadV4();
        var dataBytes = BackupJsonCodecV4.SerializeData(payload);
        var deserializedPayload = BackupJsonCodecV4.DeserializeData(dataBytes);

        Assert.AreEqual(1, deserializedPayload.LearningTargets.Count);
        Assert.AreEqual("lt-1", deserializedPayload.LearningTargets[0].Id);
        Assert.AreEqual(BackupLearningTargetKind.Definition, deserializedPayload.LearningTargets[0].TargetKind);
        Assert.AreEqual(1, deserializedPayload.TargetAnswerVariants.Count);
        Assert.AreEqual("tav-1", deserializedPayload.TargetAnswerVariants[0].Id);
        Assert.IsTrue(deserializedPayload.TargetAnswerVariants[0].IsPreferred);
        Assert.AreEqual(BackupAnswerVariantRequirement.Required, deserializedPayload.TargetAnswerVariants[0].Requirement);
        Assert.AreEqual(1, deserializedPayload.TargetFsrsStates.Count);
        Assert.AreEqual(BackupFsrsCardStateKind.New, deserializedPayload.TargetFsrsStates[0].State);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_ValidFormat4Archive_IsAccepted()
    {
        using var stream = BuildArchiveV4();

        var envelope = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);

        Assert.AreEqual(4, envelope.FormatVersion);
        Assert.IsNull(envelope.V1);
        Assert.IsNull(envelope.V2);
        Assert.IsNull(envelope.V3);
        Assert.IsNotNull(envelope.V4);
        Assert.AreEqual(4, envelope.V4.Manifest.FormatVersion);
        Assert.AreEqual(14, envelope.V4.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(1, envelope.V4.Payload.LearningTargets.Count);
        Assert.AreEqual(1, envelope.V4.Payload.TargetAnswerVariants.Count);
        Assert.AreEqual(1, envelope.V4.Payload.TargetFsrsStates.Count);
        Assert.AreEqual(0, envelope.V4.Payload.TargetFsrsReviewHistoryEntries.Count);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_DuplicateTargetStableId_FailsValidation()
    {
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var targets = new List<BackupLearningTarget>(payload.LearningTargets)
            {
                new("lt-2", "st-lt-1", "sense-1", BackupLearningTargetKind.Translation, "en", "de", false, DateTime.UtcNow, DateTime.UtcNow)
            };
            var variants = new List<BackupTargetAnswerVariant>(payload.TargetAnswerVariants)
            {
                new("tav-2", "st-tav-2", "lt-2", "de", "Netz", "netz", BackupAnswerVariantRequirement.Required, true, DateTime.UtcNow, null, DateTime.UtcNow, DateTime.UtcNow)
            };
            var states = new List<BackupTargetFsrsState>(payload.TargetFsrsStates)
            {
                new("lt-2", BackupFsrsCardStateKind.New, null, null, null, null, null)
            };
            return payload with { LearningTargets = targets, TargetAnswerVariants = variants, TargetFsrsStates = states };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_DuplicateVariantStableId_FailsValidation()
    {
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var variants = new List<BackupTargetAnswerVariant>(payload.TargetAnswerVariants)
            {
                new("tav-2", "st-tav-1", "lt-1", "de", "Netz", "netz", BackupAnswerVariantRequirement.AcceptedOnly, false, null, null, DateTime.UtcNow, DateTime.UtcNow)
            };
            return payload with { TargetAnswerVariants = variants };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_FsrsSequenceNumberGap_FailsValidation()
    {
        var now = DateTime.UtcNow;
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-1", "st-trh-1", "lt-1", 1, BackupReviewRating.Good, now.AddMinutes(-10)),
                new("trh-2", "st-trh-2", "lt-1", 3, BackupReviewRating.Good, now) // Sequence 3 skips 2
            };
            var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
                new(new DateTimeOffset(now.AddMinutes(-10), TimeSpan.Zero), ReviewRating.Good),
                new(new DateTimeOffset(now, TimeSpan.Zero), ReviewRating.Good)
            ]);
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed.Stability, replayed.Difficulty, replayed.LastReviewedAtUtc?.UtcDateTime, replayed.StepIndex, replayed.DueAtUtc?.UtcDateTime)
            };
            return payload with { TargetFsrsReviewHistoryEntries = history, TargetFsrsStates = states };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_StateHistoryInconsistency_FailsValidation()
    {
        var now = DateTime.UtcNow;
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-1", "st-trh-1", "lt-1", 1, BackupReviewRating.Good, now)
            };
            // Has history but state is New
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.New, null, null, null, null, null)
            };
            return payload with { TargetFsrsReviewHistoryEntries = history, TargetFsrsStates = states };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_DuplicateProperty_FailsClosed()
    {
        using var stream = BuildArchiveV4(dataMutator: json =>
            json.Replace("\"learningTargets\":[", "\"learningTargets\":[],\"learningTargets\":[", StringComparison.Ordinal));

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.DataJsonInvalid, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_InvalidChecksum_FailsClosed()
    {
        using var stream = BuildArchiveV4(manifestMutator: json =>
        {
            var idx = json.IndexOf("sha256:", StringComparison.Ordinal);
            if (idx >= 0 && idx + 8 < json.Length)
            {
                var flipped = json[idx + 7] == '0' ? '1' : '0';
                return json.Substring(0, idx + 7) + flipped + json.Substring(idx + 8);
            }
            return json;
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.ChecksumMismatch, ex.Code);
    }

    [TestMethod]
    public async Task CurrentBackup_Schema14_ExportsV4_AndRestoresIntoEmptyDatabase_FullIntegrity()
    {
        await using var sourceDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);

        int wordId = 0, senseId = 0, targetId = 0, variantId = 0, sessionId = 0;
        await sourceDb.RunInTransactionAsync(conn =>
        {
            wordId = InsertWord(conn, "Haus", timeStr);
            senseId = InsertSense(conn, wordId, timeStr);

            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-roundtrip-1', ?, 0, 'de', 'en', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-roundtrip-1', ?, 'en', 'House', 'house', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);
            variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
                new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Good)
            ]);

            conn.Execute("""
                INSERT INTO TargetFsrsStates
                    (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed.State, replayed.Stability, replayed.Difficulty, timeStr, replayed.StepIndex, Schema13TimestampCodec.FormatUtc(replayed.DueAtUtc!.Value.UtcDateTime));

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries
                    (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-roundtrip-1', ?, 1, 2, ?);
            """, targetId, timeStr);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, timeStr, timeStr, timeStr);
            sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-roundtrip-1', ?, ?, 2, 1, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, timeStr, Schema13TimestampCodec.FormatUtc(replayed.DueAtUtc!.Value.UtcDateTime));

            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?);", wordId, timeStr);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?);", senseId, timeStr);
            Assert.IsTrue(Schema13ShapeValidator.IsValidDatabase(conn, out var s13Fail), $"Schema13ShapeValidator failed: {s13Fail}");
            Assert.IsTrue(TargetPersistenceShapeValidator.Validate(conn, out var targetFail), $"TargetPersistenceShapeValidator failed: {targetFail}");
            return true;
        });

        // 1. Export V4 archive
        var service = new BackupService(sourceDb, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await service.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        archiveStream.Position = 0;
        var summary = await service.ValidatePortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(4, summary.FormatVersion);
        Assert.AreEqual(14, summary.SourceDatabaseSchemaVersion);

        // 2. Restore into empty Schema 14 target database
        await using var targetDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var targetService = new BackupService(targetDb, new FakePlatformInfo());

        archiveStream.Position = 0;
        var restoreResult = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, restoreResult.Summary!.Disposition);

        // 3. Verify target database has exact records
        await targetDb.RunInTransactionAsync(conn =>
        {
            var targets = conn.Table<LearningTargetEntity>().ToList();
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual("lt-roundtrip-1", targets[0].StableId);
            Assert.AreEqual(LearningTargetKind.Definition, targets[0].TargetKind);

            var variants = conn.Table<TargetAnswerVariantEntity>().ToList();
            Assert.AreEqual(1, variants.Count);
            Assert.AreEqual("tav-roundtrip-1", variants[0].StableId);
            Assert.AreEqual(targets[0].Id, variants[0].TargetId);
            Assert.AreEqual("House", variants[0].DisplayText);
            Assert.IsTrue(variants[0].IsPreferred);
            Assert.AreEqual(AnswerVariantRequirement.Required, variants[0].Requirement);

            var states = conn.Table<TargetFsrsStateEntity>().ToList();
            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(targets[0].Id, states[0].TargetId);
            Assert.IsNotNull(states[0].Stability);

            var history = conn.Table<TargetFsrsReviewHistoryEntryEntity>().ToList();
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("trh-roundtrip-1", history[0].StableId);
            Assert.AreEqual(ReviewRating.Good, history[0].Rating);

            var reviews = conn.Table<TargetReviewEntity>().ToList();
            Assert.AreEqual(1, reviews.Count);
            Assert.AreEqual("trv-roundtrip-1", reviews[0].StableId);
            Assert.AreEqual(targets[0].Id, reviews[0].TargetId);
            Assert.AreEqual(variants[0].Id, reviews[0].TargetAnswerVariantId);
            Assert.AreEqual(variants[0].Id, reviews[0].MatchedAnswerVariantId);
            Assert.AreEqual(ReviewRating.Good, reviews[0].Rating);
            Assert.IsTrue(reviews[0].WasTypedAnswer);
            Assert.IsTrue(reviews[0].WasCorrect);

            var wordControls = conn.Table<WordLearningControlEntity>().ToList();
            Assert.AreEqual(1, wordControls.Count);

            var senseControls = conn.Table<SenseLearningControlEntity>().ToList();
            Assert.AreEqual(1, senseControls.Count);
            return true;
        });
    }

    [TestMethod]
    public async Task Schema14_EmptyTargetRestore_FailureInjection_RollsBackCleanly()
    {
        await using var sourceDb = await CreateValidSchema14DatabaseAsync();
        var timeStr = Schema13TimestampCodec.FormatUtc(DateTime.UtcNow);
        await sourceDb.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "Haus", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-roll-1', ?, 0, 'de', 'en', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-roll-1', ?, 'en', 'House', 'house', 1, 1, NULL, ?, ?);
            """, targetId, timeStr, timeStr);
            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 0, NULL, NULL, NULL, NULL, NULL);
            """, targetId);
            return true;
        });

        using var archiveStream = new MemoryStream();
        await new BackupService(sourceDb, new FakePlatformInfo()).CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        archiveStream.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(archiveStream, CancellationToken.None);

        await using var targetDb = await CreateValidSchema14DatabaseAsync();
        var failureInjector = new ThrowAtCheckpoint(Schema14BackupImportRepository.Checkpoints.DuringTargetAnswerVariantInsertion);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            targetDb.RunInTransactionAsync(conn =>
            {
                Schema14BackupImportRepository.ImportNativeV4IntoEmptyDatabase(
                    conn,
                    new ValidatedSchema14Capability(hasTargetData: false),
                    validated.V4!.Payload,
                    CancellationToken.None,
                    failureInjector);
                return true;
            }));

        // Verify database rolled back and has 0 targets
        await targetDb.RunInTransactionAsync(conn =>
        {
            var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets");
            Assert.AreEqual(0, count, "Target table must be empty after rollback.");
            return true;
        });
    }

    [TestMethod]
    public async Task Schema14_BackupSnapshotRepository_ReplayMismatch_Throws()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time = DateTime.UtcNow;
        var timeStr = Schema13TimestampCodec.FormatUtc(time);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "Haus", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-diverge-1', ?, 0, 'de', 'en', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-diverge-1', ?, 'en', 'House', 'house', 1, 1, NULL, ?, ?);
            """, targetId, timeStr, timeStr);

            // Insert 1 history entry
            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-diverge-1', ?, 1, 3, ?);
            """, targetId, timeStr);

            // Tamper with state stability to mismatch replay
            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 2, 999.9, 1.0, ?, NULL, ?);
            """, targetId, timeStr, timeStr);
            return true;
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            db.ExecuteSnapshotAsync(Schema14BackupSnapshotRepository.CapturePortableSnapshot));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task Schema14MergePreflight_NewTargetsAndVariants_ClassifiedAsInsert()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var timeStr = Schema13TimestampCodec.FormatUtc(DateTime.UtcNow);
        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            InsertSense(conn, wordId, timeStr);
            return true;
        });

        using var archiveStream = BuildArchiveV4();
        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsTrue(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddLearningTarget));
        Assert.IsTrue(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddTargetAnswerVariant));
    }

    [TestMethod]
    public async Task Schema14MergePreflight_CausalHistoryPrefixExtension_ClassifiedAsPreserve()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);
            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 0, NULL, NULL, NULL, NULL, NULL);
            """, targetId);
            return true;
        });

        // Remote archive adds 1 review history event (extending prefix)
        var reviewTime = time.AddMinutes(10);
        var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(reviewTime, TimeSpan.Zero), ReviewRating.Good)
        ]);

        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-ext-1", "st-trh-ext-1", "lt-1", 1, BackupReviewRating.Good, reviewTime)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed.Stability, replayed.Difficulty, reviewTime, replayed.StepIndex, replayed.DueAtUtc?.UtcDateTime)
            };
            return payload with { TargetFsrsReviewHistoryEntries = history, TargetFsrsStates = states };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsTrue(plan.IsExecutable);
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsTrue(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AppendTargetFsrsReviewHistory));
        Assert.IsTrue(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.UpdateTargetFsrsState));
    }

    [TestMethod]
    public async Task Schema14MergePreflight_NonPrefixHistoryDivergence_ClassifiedAsConflict()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);

            var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
                new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Good)
            ]);
            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed.State, replayed.Stability, replayed.Difficulty, timeStr, replayed.StepIndex, timeStr);

            // Local rating is Good
            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?);
            """, targetId, timeStr);
            return true;
        });

        // Remote archive has diverging rating: Again (rating 1)
        var remoteReplayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Again)
        ]);

        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "st-trh-remote-1", "lt-1", 1, BackupReviewRating.Again, time)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Learning, remoteReplayed.Stability, remoteReplayed.Difficulty, time, remoteReplayed.StepIndex, remoteReplayed.DueAtUtc?.UtcDateTime)
            };
            return payload with { TargetFsrsReviewHistoryEntries = history, TargetFsrsStates = states };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.IsFalse(plan.IsExecutable, "Diverging review history must not be executable.");
        Assert.AreEqual(MergePreflightStatus.NonExecutableConflict, plan.Status);
        Assert.AreEqual(Schema14MergePreflightErrorCodes.CausalHistoryConflict, plan.ErrorCode);
    }

    [TestMethod]
    public async Task Schema14Merge_ExecutesAndPreservesSafetyCopy()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var timeStr = Schema13TimestampCodec.FormatUtc(DateTime.UtcNow);
        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            InsertSense(conn, wordId, timeStr);
            return true;
        });

        using var archiveStream = BuildArchiveV4();
        var service = new BackupService(db, new FakePlatformInfo());

        var preview = await service.PreviewPortableImportAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition);

        archiveStream.Position = 0;
        var result = await service.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"ErrorCode: {result.ErrorCode}");
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary!.Disposition);
        Assert.IsTrue(result.Summary!.SafetyCopyCreated);

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        Assert.IsTrue(Directory.Exists(safetyCopyDirectory));
        var safetyCopyFiles = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive");
        Assert.AreEqual(1, safetyCopyFiles.Length);

        // Verify safety copy is a valid V4 archive
        using (var safetyStream = File.OpenRead(safetyCopyFiles[0]))
        {
            var envelope = await BackupArchiveReader.ValidateVersionedAsync(safetyStream, CancellationToken.None);
            Assert.AreEqual(4, envelope.FormatVersion);
        }

        // Clean up safety copy file
        try
        {
            File.Delete(safetyCopyFiles[0]);
        }
        catch
        {
            // Ignore temporary test cleanup error
        }

        // Verify target database has the merged target and variant
        await db.RunInTransactionAsync(conn =>
        {
            var targets = conn.Table<LearningTargetEntity>().ToList();
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual("st-lt-1", targets[0].StableId);

            var variants = conn.Table<TargetAnswerVariantEntity>().ToList();
            Assert.AreEqual(1, variants.Count);
            Assert.AreEqual("st-tav-1", variants[0].StableId);
            return true;
        });
    }

    [TestMethod]
    public async Task Schema14MergePreflight_ScheduledVsSessionRepeatDivergentTargetReviews_ClassifiedAsConflict()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);
        var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime = replayed.DueAtUtc!.Value.UtcDateTime;
        var dueTimeStr = Schema13TimestampCodec.FormatUtc(dueTime);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed.State, replayed.Stability, replayed.Difficulty, timeStr, replayed.StepIndex, dueTimeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?);
            """, targetId, timeStr);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, timeStr, timeStr, timeStr);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Destination TargetReview: Good + scheduled (IsSessionRepeat = 0)
            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, timeStr, dueTimeStr);

            return true;
        });

        // Incoming archive: Good + session-repeat (IsSessionRepeat = true), different StableId ('trv-src-1')
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "st-trh-remote-1", "lt-1", 1, BackupReviewRating.Good, time)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed.Stability, replayed.Difficulty, time, replayed.StepIndex, dueTime)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-remote-1", "trv-src-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: true,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time, DueAtUtc: dueTime)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, time, time, time, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = workflows
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.IsFalse(plan.IsExecutable, "Scheduled vs session-repeat divergence must not be executable.");
        Assert.AreEqual(MergePreflightStatus.NonExecutableConflict, plan.Status);
        Assert.AreEqual(Schema14MergePreflightErrorCodes.CausalHistoryConflict, plan.ErrorCode);
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsFalse(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddTargetReview),
            "No AddTargetReview action should be planned for divergent stream.");
    }

    [TestMethod]
    public async Task Schema14MergePreflight_IdenticalSemanticTargetReview_DifferentStableIds_ClassifiedAsNoChange()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);
        var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime = replayed.DueAtUtc!.Value.UtcDateTime;
        var dueTimeStr = Schema13TimestampCodec.FormatUtc(dueTime);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed.State, replayed.Stability, replayed.Difficulty, timeStr, replayed.StepIndex, dueTimeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?);
            """, targetId, timeStr);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, timeStr, timeStr, timeStr);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, timeStr, dueTimeStr);

            return true;
        });

        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "st-trh-remote-1", "lt-1", 1, BackupReviewRating.Good, time)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed.Stability, replayed.Difficulty, time, replayed.StepIndex, dueTime)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-remote-1", "trv-src-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time, DueAtUtc: dueTime)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, time, time, time, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                WordLearningControls = [],
                SenseLearningControls = [],
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = workflows
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.IsTrue(plan.IsExecutable, "Semantically identical review stream with different StableId must be executable.");
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsFalse(plan.Schema14Plan.RequiresMutation, "Identical stream requires no schema 14 mutations.");
        Assert.IsFalse(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddTargetReview),
            "No AddTargetReview action should be planned for semantically identical review.");
        var noChangeReviewAction = plan.Schema14Plan.Actions.FirstOrDefault(a =>
            a.Classification == Schema14MergeActionClassification.NoChange && a.ReasonCode == "target-review-identical");
        Assert.IsNotNull(noChangeReviewAction, "Should have a NoChange action for the identical review.");
    }

    [TestMethod]
    public async Task Schema14MergePreflight_TargetReviewDestinationPrefix_AppendsMissingSuffixOnly()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time1 = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var time1Str = Schema13TimestampCodec.FormatUtc(time1);
        var replayed1 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var time2 = replayed1.DueAtUtc!.Value.UtcDateTime;
        var time2Str = Schema13TimestampCodec.FormatUtc(time2);
        var replayed2 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime2 = replayed2.DueAtUtc!.Value.UtcDateTime;

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", time1Str);
            var senseId = InsertSense(conn, wordId, time1Str);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, time1Str, time1Str);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, time1Str, time1Str, time1Str);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed1.State, replayed1.Stability, replayed1.Difficulty, time1Str, replayed1.StepIndex, time2Str);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?);
            """, targetId, time1Str);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, time1Str, time1Str, time1Str);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, time1Str, time2Str);

            return true;
        });

        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "st-trh-remote-1", "lt-1", 1, BackupReviewRating.Good, time1),
                new("trh-remote-2", "st-trh-remote-2", "lt-1", 2, BackupReviewRating.Good, time2)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed2.Stability, replayed2.Difficulty, time2, replayed2.StepIndex, dueTime2)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-remote-1", "trv-src-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1, DueAtUtc: time2),
                new("trv-remote-2", "trv-src-2", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time2, DueAtUtc: dueTime2)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 2, 2, 0, 0, 2, 0, time1, time2, time2, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = workflows
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.IsTrue(plan.IsExecutable);
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsNotNull(plan.Schema14Plan);

        var addReviewActions = plan.Schema14Plan.Actions.Where(a => a.Classification == Schema14MergeActionClassification.AddTargetReview).ToList();
        Assert.AreEqual(1, addReviewActions.Count, "Exactly the suffix review should be added.");
        Assert.AreEqual("trv-src-2", addReviewActions[0].ReviewFact!.StableId);

        var noChangeReviewActions = plan.Schema14Plan.Actions.Where(a =>
            a.Classification == Schema14MergeActionClassification.NoChange && a.ReasonCode == "target-review-identical").ToList();
        Assert.AreEqual(1, noChangeReviewActions.Count, "The common prefix review should be marked NoChange.");
    }

    [TestMethod]
    public async Task Schema14MergePreflight_TargetReviewSourceOlderPrefix_ClassifiedAsNoChange()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time1 = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var time1Str = Schema13TimestampCodec.FormatUtc(time1);
        var replayed1 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var time2 = replayed1.DueAtUtc!.Value.UtcDateTime;
        var time2Str = Schema13TimestampCodec.FormatUtc(time2);
        var replayed2 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime2 = replayed2.DueAtUtc!.Value.UtcDateTime;
        var dueTime2Str = Schema13TimestampCodec.FormatUtc(dueTime2);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", time1Str);
            var senseId = InsertSense(conn, wordId, time1Str);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, time1Str, time1Str);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, time1Str, time1Str, time1Str);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed2.State, replayed2.Stability, replayed2.Difficulty, time2Str, replayed2.StepIndex, dueTime2Str);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?), ('trh-local-2', ?, 2, 2, ?);
            """, targetId, time1Str, targetId, time2Str);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 2, 2, 0, 0, 2, 0, ?, ?, ?);
            """, time1Str, time2Str, time2Str);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?),
                       ('trv-dest-2', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, time1Str, time2Str,
                 targetId, sessionId, variantId, variantId, time2Str, dueTime2Str);

            return true;
        });

        // Source archive has only older prefix (1 review)
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "st-trh-remote-1", "lt-1", 1, BackupReviewRating.Good, time1)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed1.Stability, replayed1.Difficulty, time1, replayed1.StepIndex, time2)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-remote-1", "trv-src-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1, DueAtUtc: time2)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, time1, time1, time1, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                WordLearningControls = [],
                SenseLearningControls = [],
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = workflows
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.IsTrue(plan.IsExecutable);
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsFalse(plan.Schema14Plan.RequiresMutation, "Source older prefix requires no schema 14 mutations.");
        Assert.IsFalse(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddTargetReview));
        var aheadAction = plan.Schema14Plan.Actions.FirstOrDefault(a =>
            a.Classification == Schema14MergeActionClassification.NoChange && a.ReasonCode == "target-review-ahead-preserved");
        Assert.IsNotNull(aheadAction, "Should preserve ahead destination review stream.");
    }

    [TestMethod]
    public async Task Schema14MergePreflight_NonPrefixFactualTargetReviewDivergence_ClassifiedAsConflict()
    {
        await using var db = await CreateValidSchema14DatabaseAsync();
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);
        var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime = replayed.DueAtUtc!.Value.UtcDateTime;
        var dueTimeStr = Schema13TimestampCodec.FormatUtc(dueTime);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed.State, replayed.Stability, replayed.Difficulty, timeStr, replayed.StepIndex, dueTimeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?);
            """, targetId, timeStr);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, timeStr, timeStr, timeStr);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Destination: Rating = Good (2)
            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, timeStr, dueTimeStr);

            return true;
        });

        // Source archive: Divergent rating at same timestamp: Rating = Hard (1)
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "st-trh-remote-1", "lt-1", 1, BackupReviewRating.Good, time)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed.Stability, replayed.Difficulty, time, replayed.StepIndex, dueTime)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-remote-1", "trv-src-1", "lt-1", "ls-000001", BackupReviewRating.Hard,
                    WasTypedAnswer: false, WasCorrect: false, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time, DueAtUtc: dueTime)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 1, 1, 0, 1, 0, 0, time, time, time, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = workflows
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        Assert.IsFalse(plan.IsExecutable, "Factual divergence in review rating must not be executable.");
        Assert.AreEqual(MergePreflightStatus.NonExecutableConflict, plan.Status);
        Assert.AreEqual(Schema14MergePreflightErrorCodes.CausalHistoryConflict, plan.ErrorCode);
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsFalse(plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddTargetReview));
    }

    [TestMethod]
    public async Task Schema14Merge_CompatibleLongerTargetReviewStream_PostMergeAutomaticReplayMatchesLongerStream()
    {
        await using var db = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time1 = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var time1Repeat = new DateTime(2026, 9, 1, 10, 5, 0, DateTimeKind.Utc);
        var time1Str = Schema13TimestampCodec.FormatUtc(time1);
        var time1RepeatStr = Schema13TimestampCodec.FormatUtc(time1Repeat);

        var replayed1 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime1 = replayed1.DueAtUtc!.Value.UtcDateTime;
        var dueTime1Str = Schema13TimestampCodec.FormatUtc(dueTime1);

        var time2 = dueTime1;
        var time2Str = Schema13TimestampCodec.FormatUtc(time2);
        var replayed2 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime2 = replayed2.DueAtUtc!.Value.UtcDateTime;
        var dueTime2Str = Schema13TimestampCodec.FormatUtc(dueTime2);

        var time3 = dueTime2;
        var time3Str = Schema13TimestampCodec.FormatUtc(time3);
        var replayed3 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time3, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime3 = replayed3.DueAtUtc!.Value.UtcDateTime;
        var dueTime3Str = Schema13TimestampCodec.FormatUtc(dueTime3);

        var time4 = dueTime3;
        var time4Str = Schema13TimestampCodec.FormatUtc(time4);
        var replayedFull = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time3, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time4, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTimeFull = replayedFull.DueAtUtc!.Value.UtcDateTime;

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", time1Str);
            var senseId = InsertSense(conn, wordId, time1Str);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, time1Str, time1Str);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, time1Str, time1Str, time1Str);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed1.State, replayed1.Stability, replayed1.Difficulty, time1Str, replayed1.StepIndex, dueTime1Str);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-dest-1', ?, 1, 2, ?);
            """, targetId, time1Str);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 2, 2, 0, 0, 2, 0, ?, ?, ?);
            """, time1Str, time1RepeatStr, time1RepeatStr);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?),
                       ('trv-dest-2', ?, ?, 2, 0, 1, 1, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, time1Str, dueTime1Str,
                 targetId, sessionId, variantId, variantId, time1RepeatStr, dueTime1Str);

            return true;
        });

        // Source archive has 5 reviews spanning recall -> typing qualification -> qualified reading maintenance,
        // including a session repeat.
        List<BackupTargetReview>? sourceArchiveReviews = null;
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "trh-dest-1", "lt-1", 1, BackupReviewRating.Good, time1),
                new("trh-remote-2", "trh-src-2", "lt-1", 2, BackupReviewRating.Good, time2),
                new("trh-remote-3", "trh-src-3", "lt-1", 3, BackupReviewRating.Good, time3),
                new("trh-remote-4", "trh-src-4", "lt-1", 4, BackupReviewRating.Good, time4)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayedFull.Stability, replayedFull.Difficulty, time4, replayedFull.StepIndex, dueTimeFull)
            };
            sourceArchiveReviews = new List<BackupTargetReview>
            {
                // Review 1: Scheduled recall (success 1)
                new("trv-remote-1", "trv-dest-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1, DueAtUtc: dueTime1),
                // Review 2: Session repeat (does not advance progression)
                new("trv-remote-2", "trv-dest-2", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: true,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1Repeat, DueAtUtc: dueTime1),
                // Review 3: Scheduled recall (success 2 -> transitions to Typing)
                new("trv-remote-3", "trv-src-3", "lt-1", "ls-000002", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time2, DueAtUtc: dueTime2),
                // Review 4: Scheduled typing (typing success 1)
                new("trv-remote-4", "trv-src-4", "lt-1", "ls-000002", BackupReviewRating.Good,
                    WasTypedAnswer: true, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time3, DueAtUtc: dueTime3),
                // Review 5: Scheduled typing (typing success 2 -> qualifies -> Reading maintenance)
                new("trv-remote-5", "trv-src-5", "lt-1", "ls-000002", BackupReviewRating.Good,
                    WasTypedAnswer: true, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time4, DueAtUtc: dueTimeFull)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 2, 2, 0, 0, 2, 0, time1, time1Repeat, time1Repeat, [], "11112222333344445555666677778888"),
                new("ls-000002", BackupLearningSessionStatus.Completed, 3, 3, 0, 0, 3, 0, time2, time4, time4, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = sourceArchiveReviews,
                Workflows = workflows
            };
        });

        var service = new BackupService(db, new FakePlatformInfo());
        var result = await service.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"Merge failed with code: {result.ErrorCode}");

        await db.RunInTransactionAsync(conn =>
        {
            var reviews = conn.Table<TargetReviewEntity>().OrderBy(r => r.ReviewedAtUtc).ToList();
            Assert.AreEqual(5, reviews.Count);
            Assert.AreEqual("trv-dest-1", reviews[0].StableId);
            Assert.AreEqual("trv-dest-2", reviews[1].StableId);
            Assert.AreEqual("trv-src-3", reviews[2].StableId);
            Assert.AreEqual("trv-src-4", reviews[3].StableId);
            Assert.AreEqual("trv-src-5", reviews[4].StableId);

            var history = conn.Table<TargetFsrsReviewHistoryEntryEntity>().OrderBy(h => h.SequenceNumber).ToList();
            Assert.AreEqual(4, history.Count);
            Assert.AreEqual(1, history[0].SequenceNumber);
            Assert.AreEqual(2, history[1].SequenceNumber);
            Assert.AreEqual(3, history[2].SequenceNumber);
            Assert.AreEqual(4, history[3].SequenceNumber);

            var state = conn.Table<TargetFsrsStateEntity>().Single();
            Assert.AreEqual(replayedFull.State, state.State);
            Assert.AreEqual(replayedFull.Stability!.Value, state.Stability!.Value, 0.0001);
            Assert.AreEqual(replayedFull.Difficulty!.Value, state.Difficulty!.Value, 0.0001);

            // Verify TargetAutomaticProgressionPolicy replay on merged destination reviews matches source replay
            var destEvents = reviews.Select(r => new TargetInteractionEvent(
                Schema13TimestampCodec.ParseUtcDateTimeOffset(r.ReviewedAtUtc),
                (ReviewRating)r.Rating,
                r.WasTypedAnswer,
                r.WasCorrect,
                r.IsSessionRepeat)).ToList();

            var sourceEvents = sourceArchiveReviews!.Select(r => new TargetInteractionEvent(
                new DateTimeOffset(DateTime.SpecifyKind(r.ReviewedAtUtc, DateTimeKind.Utc), TimeSpan.Zero),
                (ReviewRating)(int)r.Rating,
                r.WasTypedAnswer,
                r.WasCorrect,
                r.IsSessionRepeat)).ToList();

            var destProgression = TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, destEvents);
            var sourceProgression = TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, sourceEvents);

            Assert.AreEqual(sourceProgression.ConsecutiveRecallSuccesses, destProgression.ConsecutiveRecallSuccesses);
            Assert.AreEqual(sourceProgression.ConsecutiveTypingSuccesses, destProgression.ConsecutiveTypingSuccesses);
            Assert.AreEqual(sourceProgression.IsTypingQualified, destProgression.IsTypingQualified);
            Assert.AreEqual(sourceProgression.IsRecheckArmed, destProgression.IsRecheckArmed);
            Assert.AreEqual(sourceProgression.InteractionMode, destProgression.InteractionMode);
            Assert.AreEqual(sourceProgression.TypingOptOut, destProgression.TypingOptOut);

            Assert.IsTrue(destProgression.IsTypingQualified);
            Assert.AreEqual(2, destProgression.ConsecutiveRecallSuccesses);
            Assert.AreEqual(2, destProgression.ConsecutiveTypingSuccesses);
            Assert.AreEqual(LearningInteractionMode.Reading, destProgression.InteractionMode);
            Assert.IsFalse(destProgression.IsRecheckArmed);

            return true;
        });
    }

    [TestMethod]
    public async Task CurrentBackup_Schema14_TargetReviews_PreBackupReplayMatchesPostRestoreReplay_IncludingSessionRepeat()
    {
        await using var sourceDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time1 = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var time1Repeat = new DateTime(2026, 9, 1, 10, 5, 0, DateTimeKind.Utc);
        var time1Str = Schema13TimestampCodec.FormatUtc(time1);
        var time1RepeatStr = Schema13TimestampCodec.FormatUtc(time1Repeat);

        var replayed1 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime1 = replayed1.DueAtUtc!.Value.UtcDateTime;
        var dueTime1Str = Schema13TimestampCodec.FormatUtc(dueTime1);

        var time2 = dueTime1;
        var time2Str = Schema13TimestampCodec.FormatUtc(time2);
        var replayed2 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime2 = replayed2.DueAtUtc!.Value.UtcDateTime;
        var dueTime2Str = Schema13TimestampCodec.FormatUtc(dueTime2);

        var time3 = dueTime2;
        var time3Str = Schema13TimestampCodec.FormatUtc(time3);
        var replayed3 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time3, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime3 = replayed3.DueAtUtc!.Value.UtcDateTime;
        var dueTime3Str = Schema13TimestampCodec.FormatUtc(dueTime3);

        var time4 = dueTime3;
        var time4Str = Schema13TimestampCodec.FormatUtc(time4);
        var replayedFull = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time3, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time4, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTimeFull = replayedFull.DueAtUtc!.Value.UtcDateTime;
        var dueTimeFullStr = Schema13TimestampCodec.FormatUtc(dueTimeFull);

        await sourceDb.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", time1Str);
            var senseId = InsertSense(conn, wordId, time1Str);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, time1Str, time1Str);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, time1Str, time1Str, time1Str);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayedFull.State, replayedFull.Stability, replayedFull.Difficulty, time4Str, replayedFull.StepIndex, dueTimeFullStr);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-1', ?, 1, 2, ?),
                       ('trh-2', ?, 2, 2, ?),
                       ('trh-3', ?, 3, 2, ?),
                       ('trh-4', ?, 4, 2, ?);
            """, targetId, time1Str, targetId, time2Str, targetId, time3Str, targetId, time4Str);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 2, 2, 0, 0, 2, 0, ?, ?, ?),
                       ('22223333444455556666777788889999', 1, 3, 3, 0, 0, 3, 0, ?, ?, ?);
            """, time1Str, time1RepeatStr, time1RepeatStr, time2Str, time4Str, time4Str);
            var session1Id = conn.ExecuteScalar<int>("SELECT Id FROM LearningSessions WHERE StableId = '11112222333344445555666677778888'");
            var session2Id = conn.ExecuteScalar<int>("SELECT Id FROM LearningSessions WHERE StableId = '22223333444455556666777788889999'");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?),
                       ('trv-2', ?, ?, 2, 0, 1, 1, ?, ?, ?, ?),
                       ('trv-3', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?),
                       ('trv-4', ?, ?, 2, 1, 1, 0, ?, ?, ?, ?),
                       ('trv-5', ?, ?, 2, 1, 1, 0, ?, ?, ?, ?);
            """, targetId, session1Id, variantId, variantId, time1Str, dueTime1Str,
                 targetId, session1Id, variantId, variantId, time1RepeatStr, dueTime1Str,
                 targetId, session2Id, variantId, variantId, time2Str, dueTime2Str,
                 targetId, session2Id, variantId, variantId, time3Str, dueTime3Str,
                 targetId, session2Id, variantId, variantId, time4Str, dueTimeFullStr);

            return true;
        });

        // 1. Replay pre-backup
        TargetAutomaticProgressionState preBackupReplay = null!;
        await sourceDb.RunInTransactionAsync(conn =>
        {
            var preReviews = conn.Table<TargetReviewEntity>().OrderBy(r => r.ReviewedAtUtc).ToList();
            var events = preReviews.Select(r => new TargetInteractionEvent(
                Schema13TimestampCodec.ParseUtcDateTimeOffset(r.ReviewedAtUtc),
                (ReviewRating)r.Rating,
                r.WasTypedAnswer,
                r.WasCorrect,
                r.IsSessionRepeat)).ToList();
            preBackupReplay = TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, events);
            return true;
        });

        Assert.IsTrue(preBackupReplay.IsTypingQualified);
        Assert.AreEqual(2, preBackupReplay.ConsecutiveRecallSuccesses);
        Assert.AreEqual(2, preBackupReplay.ConsecutiveTypingSuccesses);
        Assert.AreEqual(LearningInteractionMode.Reading, preBackupReplay.InteractionMode);

        // 2. Export V4 archive
        var sourceService = new BackupService(sourceDb, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        // 3. Clean restore into fresh empty Schema 14 database
        archiveStream.Position = 0;
        await using var targetDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var targetService = new BackupService(targetDb, new FakePlatformInfo());
        var restoreResult = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, restoreResult.Status);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, restoreResult.Summary!.Disposition);

        // 4. Replay post-restore and assert 100% field equality
        await targetDb.RunInTransactionAsync(conn =>
        {
            var postReviews = conn.Table<TargetReviewEntity>().OrderBy(r => r.ReviewedAtUtc).ToList();
            Assert.AreEqual(5, postReviews.Count);
            var events = postReviews.Select(r => new TargetInteractionEvent(
                Schema13TimestampCodec.ParseUtcDateTimeOffset(r.ReviewedAtUtc),
                (ReviewRating)r.Rating,
                r.WasTypedAnswer,
                r.WasCorrect,
                r.IsSessionRepeat)).ToList();
            var postRestoreReplay = TargetAutomaticProgressionPolicy.Replay(TargetAutomaticProgressionState.Initial, events);

            Assert.AreEqual(preBackupReplay.ConsecutiveRecallSuccesses, postRestoreReplay.ConsecutiveRecallSuccesses);
            Assert.AreEqual(preBackupReplay.ConsecutiveTypingSuccesses, postRestoreReplay.ConsecutiveTypingSuccesses);
            Assert.AreEqual(preBackupReplay.IsTypingQualified, postRestoreReplay.IsTypingQualified);
            Assert.AreEqual(preBackupReplay.IsRecheckArmed, postRestoreReplay.IsRecheckArmed);
            Assert.AreEqual(preBackupReplay.InteractionMode, postRestoreReplay.InteractionMode);
            Assert.AreEqual(preBackupReplay.TypingOptOut, postRestoreReplay.TypingOptOut);

            Assert.IsTrue(postRestoreReplay.IsTypingQualified);
            Assert.AreEqual(2, postRestoreReplay.ConsecutiveRecallSuccesses);
            Assert.AreEqual(2, postRestoreReplay.ConsecutiveTypingSuccesses);
            Assert.AreEqual(LearningInteractionMode.Reading, postRestoreReplay.InteractionMode);
            Assert.IsFalse(postRestoreReplay.IsRecheckArmed);

            return true;
        });
    }

    [TestMethod]
    public async Task CurrentBackup_Schema14_PostRestore_WorkflowStateService_ReportsCorrectTargetCountsAndNextDue()
    {
        await using var sourceDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var baseTime = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var baseTimeStr = Schema13TimestampCodec.FormatUtc(baseTime);
        var reviewTime = baseTime.AddDays(-10);
        var reviewTimeStr = Schema13TimestampCodec.FormatUtc(reviewTime);

        var replayedDue = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(reviewTime, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var targetDueAtUtc = replayedDue.DueAtUtc!.Value.UtcDateTime;
        var targetDueAtUtcStr = Schema13TimestampCodec.FormatUtc(targetDueAtUtc);

        await sourceDb.RunInTransactionAsync(conn =>
        {
            // Target 1: Due review target
            var w1 = InsertWord(conn, "dueWord", baseTimeStr);
            var s1 = InsertSense(conn, w1, baseTimeStr, "sense-due-1");
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-due-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, s1, baseTimeStr, baseTimeStr);
            var t1 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-due-1', ?, 'de', 'dueAnswer', 'dueanswer', 0, 1, ?, ?, ?);
            """, t1, baseTimeStr, baseTimeStr, baseTimeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, t1, (int)replayedDue.State, replayedDue.Stability, replayedDue.Difficulty, reviewTimeStr, replayedDue.StepIndex, targetDueAtUtcStr);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-due-1', ?, 1, 2, ?);
            """, t1, reviewTimeStr);

            // Target 2: New target (state 0 / New)
            var w2 = InsertWord(conn, "newWord", baseTimeStr);
            var s2 = InsertSense(conn, w2, baseTimeStr, "sense-new-2");
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-new-2', ?, 0, 'en', 'de', 0, ?, ?);
            """, s2, baseTimeStr, baseTimeStr);
            var t2 = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-new-2', ?, 'de', 'newAnswer', 'newanswer', 0, 1, ?, ?, ?);
            """, t2, baseTimeStr, baseTimeStr, baseTimeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 0, NULL, NULL, NULL, NULL, NULL);
            """, t2);

            return true;
        });

        // Export V4 archive
        var sourceService = new BackupService(sourceDb, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        // Restore into fresh empty Schema 14 target database
        archiveStream.Position = 0;
        await using var targetDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var targetService = new BackupService(targetDb, new FakePlatformInfo());
        var restoreResult = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, restoreResult.Status);

        // Verify that restored LearningCards table is empty
        await targetDb.RunInTransactionAsync(conn =>
        {
            var legacyCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards");
            Assert.AreEqual(0, legacyCount, "Restored database must have empty LearningCards table.");
            return true;
        });

        // Run WorkflowStateService on restored database
        var workflowService = new WorkflowStateService(targetDb, new FakeClock(baseTime));
        var snapshot = await workflowService.GetSnapshotAsync();

        Assert.AreEqual(1, snapshot.DueCardCount, "DueCardCount must reflect exactly the 1 due LearningTarget.");
        Assert.AreEqual(1, snapshot.PreparedNewItemCount, "PreparedNewItemCount must reflect exactly the 1 new LearningTarget word.");
        Assert.AreEqual(targetDueAtUtc, snapshot.NextDueAtUtc, "NextDueAtUtc must come from the TargetFsrsState DueAtUtc.");
    }

    [TestMethod]
    public async Task CurrentBackup_Schema14_PostRestore_LearningService_CompletesTargetReviewInteractionWithoutLegacyScheduler()
    {
        await using var sourceDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var baseTime = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var baseTimeStr = Schema13TimestampCodec.FormatUtc(baseTime);
        var reviewTime = baseTime.AddDays(-10);
        var reviewTimeStr = Schema13TimestampCodec.FormatUtc(reviewTime);

        var replayedDue = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(reviewTime, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var targetDueAtUtc = replayedDue.DueAtUtc!.Value.UtcDateTime;
        var targetDueAtUtcStr = Schema13TimestampCodec.FormatUtc(targetDueAtUtc);

        await sourceDb.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "learningWord", baseTimeStr);
            conn.Execute("UPDATE Words SET Status = 0, PreparationState = 2 WHERE Id = ?", wordId);

            var senseId = InsertSense(conn, wordId, baseTimeStr, "sense-learn-1");
            conn.Execute("""
                INSERT INTO Meanings
                    (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                     GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                     AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                     Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
                VALUES (?, ?, 'de', 'en', 'learningWord', 'learningWord', '', 0, 'Antwort', 'Definition', '', '',
                        '[]', 'Antwort', 'manual', '', '', '', 1, ?, ?, ?, 'meaning-learn-1');
            """, wordId, senseId, baseTimeStr, baseTimeStr, baseTimeStr);

            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-learn-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, baseTimeStr, baseTimeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-learn-1', ?, 'de', 'Antwort', 'antwort', 0, 1, ?, ?, ?);
            """, targetId, baseTimeStr, baseTimeStr, baseTimeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayedDue.State, replayedDue.Stability, replayedDue.Difficulty, reviewTimeStr, replayedDue.StepIndex, targetDueAtUtcStr);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-learn-1', ?, 1, 2, ?);
            """, targetId, reviewTimeStr);

            return true;
        });

        // Export V4 archive
        var sourceService = new BackupService(sourceDb, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        // Restore into fresh empty Schema 14 target database
        archiveStream.Position = 0;
        await using var targetDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var targetService = new BackupService(targetDb, new FakePlatformInfo());
        var restoreResult = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, restoreResult.Status);

        // Set up LearningService with spy ObservableLegacyScheduler on the restored database
        var clock = new FakeClock(baseTime);
        var observableScheduler = new ObservableLegacyScheduler();
        var fsrs = new Fsrs6SchedulingService(clock);
        var learningService = new LearningService(
            targetDb,
            observableScheduler,
            new SpellingAnswerComparer(),
            clock,
            new TestAppSettings(LearningMode.Reading),
            fsrs6SchedulingService: fsrs);

        // Retrieve restored target work
        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card, "Restored due target must be served by LearningService.");
        Assert.AreEqual("learningWord", loadResult.Card.Term);
        Assert.AreEqual(0, observableScheduler.InvocationCount, "Legacy scheduler must not be called during GetOrStart.");

        // Execute reveal and rating
        await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
        var rateResult = await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        Assert.AreEqual(0, observableScheduler.InvocationCount, "Legacy scheduler must not be called during target rating.");

        // Verify target tables received the review and legacy tables remained untouched
        await targetDb.RunInTransactionAsync(conn =>
        {
            var reviews = conn.Table<TargetReviewEntity>().ToList();
            Assert.AreEqual(1, reviews.Count, "Exactly 1 target review must be persisted in TargetReviews.");
            Assert.AreEqual(ReviewRating.Good, reviews[0].Rating);
            Assert.AreEqual(baseTimeStr, reviews[0].ReviewedAtUtc);

            var history = conn.Table<TargetFsrsReviewHistoryEntryEntity>().OrderBy(h => h.SequenceNumber).ToList();
            Assert.AreEqual(2, history.Count, "TargetFsrsReviewHistoryEntries must now have 2 entries.");
            Assert.AreEqual(2, history[1].SequenceNumber);
            Assert.AreEqual(ReviewRating.Good, history[1].Rating);

            var fsrsState = conn.Table<TargetFsrsStateEntity>().Single();
            Assert.AreEqual(baseTimeStr, fsrsState.LastReviewedAtUtc);
            Assert.IsTrue(fsrsState.Stability > replayedDue.Stability, "Target FSRS stability must increase after Good rating.");

            var legacyReviewCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews");
            var legacyCardCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards");
            Assert.AreEqual(0, legacyReviewCount, "Legacy LearningReviews table must remain untouched (0 rows).");
            Assert.AreEqual(0, legacyCardCount, "Legacy LearningCards table must remain untouched (0 rows).");

            return true;
        });
    }

    [TestMethod]
    public async Task CurrentBackup_Schema14_MultiTargetAndLanguages_ExportsV4_AndRestoresIntoEmptyDatabase_FullIntegrity()
    {
        await using var sourceDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeRepeat = time.AddMinutes(5);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);
        var timeRepeatStr = Schema13TimestampCodec.FormatUtc(timeRepeat);

        var replayed1 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var dueTime1 = replayed1.DueAtUtc!.Value.UtcDateTime;
        var dueTime1Str = Schema13TimestampCodec.FormatUtc(dueTime1);

        var replayed2 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Again)
        ]);
        var dueTime2 = replayed2.DueAtUtc!.Value.UtcDateTime;
        var dueTime2Str = Schema13TimestampCodec.FormatUtc(dueTime2);

        var replayed3 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Hard)
        ]);
        var dueTime3 = replayed3.DueAtUtc!.Value.UtcDateTime;
        var dueTime3Str = Schema13TimestampCodec.FormatUtc(dueTime3);

        int wordId = 0, sense1Id = 0, sense2Id = 0, meaning1Id = 0;
        int target1Id = 0, target2Id = 0, target3Id = 0;
        int variant1Id = 0, variant2aId = 0, variant2bId = 0, variant3Id = 0;
        int sessionId = 0;

        await sourceDb.RunInTransactionAsync(conn =>
        {
            wordId = InsertWord(conn, "Netzwerk", timeStr);
            conn.Execute("UPDATE Words SET Language = 'de' WHERE Id = ?", wordId);

            sense1Id = InsertSense(conn, wordId, timeStr, "st-sense-1", "de", "de");
            sense2Id = InsertSense(conn, wordId, timeStr, "st-sense-2", "de", "fr");

            conn.Execute("""
                INSERT INTO Meanings
                    (WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                     GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                     AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                     Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
                VALUES (?, ?, 'de', 'de', 'Netzwerk', 'Netzwerk', '', 0, '', 'Zusammenschluss von Systemen', '', '',
                        '[]', 'Zusammenschluss', 'manual', '', '', '', 1, ?, ?, ?, 'meaning-st-1');
            """, wordId, sense1Id, timeStr, timeStr, timeStr);
            meaning1Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Target 1: Definition (de -> de), TypingOptOut: false
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-multi-1', ?, 0, 'de', 'de', 0, ?, ?);
            """, sense1Id, timeStr, timeStr);
            target1Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Variant 1: Preferred required definition with SourceMeaningId
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-multi-1', ?, 'de', 'Zusammenschluss', 'zusammenschluss', 0, 1, ?, ?, ?, ?);
            """, target1Id, timeStr, meaning1Id, timeStr, timeStr);
            variant1Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates
                    (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, target1Id, (int)replayed1.State, replayed1.Stability, replayed1.Difficulty, timeStr, replayed1.StepIndex, dueTime1Str);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries
                    (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-multi-1', ?, 1, 2, ?);
            """, target1Id, timeStr);

            // Target 2: Translation (de -> en), TypingOptOut: false, multiple variants (preferred + alternative)
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-multi-2', ?, 1, 'de', 'en', 0, ?, ?);
            """, sense1Id, timeStr, timeStr);
            target2Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-multi-2a', ?, 'en', 'network', 'network', 0, 1, ?, NULL, ?, ?),
                       ('tav-multi-2b', ?, 'en', 'mesh', 'mesh', 1, 0, NULL, NULL, ?, ?);
            """, target2Id, timeStr, timeStr, timeStr,
                 target2Id, timeStr, timeStr);
            variant2aId = conn.ExecuteScalar<int>("SELECT Id FROM TargetAnswerVariants WHERE StableId = 'tav-multi-2a'");
            variant2bId = conn.ExecuteScalar<int>("SELECT Id FROM TargetAnswerVariants WHERE StableId = 'tav-multi-2b'");

            conn.Execute("""
                INSERT INTO TargetFsrsStates
                    (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, target2Id, (int)replayed2.State, replayed2.Stability, replayed2.Difficulty, timeStr, replayed2.StepIndex, dueTime2Str);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries
                    (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-multi-2', ?, 1, 0, ?);
            """, target2Id, timeStr);

            // Target 3: Translation (de -> fr), TypingOptOut: true
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-multi-3', ?, 1, 'de', 'fr', 1, ?, ?);
            """, sense2Id, timeStr, timeStr);
            target3Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-multi-3', ?, 'fr', 'réseau', 'reseau', 0, 1, ?, NULL, ?, ?);
            """, target3Id, timeStr, timeStr, timeStr);
            variant3Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates
                    (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, target3Id, (int)replayed3.State, replayed3.Stability, replayed3.Difficulty, timeStr, replayed3.StepIndex, dueTime3Str);

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries
                    (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-multi-3', ?, 1, 1, ?);
            """, target3Id, timeStr);

            // Session with 4 reviews
            conn.Execute("""
                INSERT INTO LearningSessions
                    (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 4, 4, 1, 1, 2, 0, ?, ?, ?);
            """, timeStr, timeRepeatStr, timeRepeatStr);
            sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Reviews with nullable and non-null variant references, and session repeat
            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-multi-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?),
                       ('trv-multi-2', ?, ?, 2, 0, 1, 1, ?, ?, ?, ?),
                       ('trv-multi-3', ?, ?, 0, 1, 0, 0, ?, NULL, ?, ?),
                       ('trv-multi-4', ?, ?, 1, 0, 1, 0, ?, ?, ?, ?);
            """, target1Id, sessionId, variant1Id, variant1Id, timeStr, dueTime1Str,
                 target1Id, sessionId, variant1Id, variant1Id, timeRepeatStr, dueTime1Str,
                 target2Id, sessionId, variant2aId, timeStr, dueTime2Str,
                 target3Id, sessionId, variant3Id, variant3Id, timeStr, dueTime3Str);

            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?);", wordId, timeStr);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?), (?, ?);", sense1Id, timeStr, sense2Id, timeStr);

            Assert.IsTrue(Schema13ShapeValidator.IsValidDatabase(conn, out var s13Fail), $"Schema13ShapeValidator: {s13Fail}");
            Assert.IsTrue(TargetPersistenceShapeValidator.Validate(conn, out var targetFail), $"TargetPersistenceShapeValidator: {targetFail}");
            return true;
        });

        // 1. Export V4 archive
        var service = new BackupService(sourceDb, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await service.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        archiveStream.Position = 0;
        var summary = await service.ValidatePortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(4, summary.FormatVersion);
        Assert.AreEqual(14, summary.SourceDatabaseSchemaVersion);

        // 2. Restore into empty Schema 14 target database
        await using var targetDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var targetService = new BackupService(targetDb, new FakePlatformInfo());

        archiveStream.Position = 0;
        var restoreResult = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, restoreResult.Summary!.Disposition);

        // 3. Verify target database has exact distinct records
        await targetDb.RunInTransactionAsync(conn =>
        {
            Assert.IsTrue(Schema13ShapeValidator.IsValidDatabase(conn, out var s13Fail), $"Restored Schema13ShapeValidator: {s13Fail}");
            Assert.IsTrue(TargetPersistenceShapeValidator.Validate(conn, out var targetFail), $"Restored TargetPersistenceShapeValidator: {targetFail}");

            var targets = conn.Table<LearningTargetEntity>().OrderBy(t => t.StableId).ToList();
            Assert.AreEqual(3, targets.Count, "All 3 learning targets must remain distinct.");

            Assert.AreEqual("lt-multi-1", targets[0].StableId);
            Assert.AreEqual(LearningTargetKind.Definition, targets[0].TargetKind);
            Assert.AreEqual("de", targets[0].SourceLanguage);
            Assert.AreEqual("de", targets[0].TargetLanguage);
            Assert.IsFalse(targets[0].TypingOptOut);

            Assert.AreEqual("lt-multi-2", targets[1].StableId);
            Assert.AreEqual(LearningTargetKind.Translation, targets[1].TargetKind);
            Assert.AreEqual("de", targets[1].SourceLanguage);
            Assert.AreEqual("en", targets[1].TargetLanguage);
            Assert.IsFalse(targets[1].TypingOptOut);

            Assert.AreEqual("lt-multi-3", targets[2].StableId);
            Assert.AreEqual(LearningTargetKind.Translation, targets[2].TargetKind);
            Assert.AreEqual("de", targets[2].SourceLanguage);
            Assert.AreEqual("fr", targets[2].TargetLanguage);
            Assert.IsTrue(targets[2].TypingOptOut);

            var variants = conn.Table<TargetAnswerVariantEntity>().OrderBy(v => v.StableId).ToList();
            Assert.AreEqual(4, variants.Count, "All 4 answer variants must be restored.");

            Assert.AreEqual("tav-multi-1", variants[0].StableId);
            Assert.AreEqual(targets[0].Id, variants[0].TargetId);
            Assert.AreEqual("Zusammenschluss", variants[0].DisplayText);
            Assert.AreEqual("zusammenschluss", variants[0].NormalizedText);
            Assert.IsTrue(variants[0].IsPreferred);
            Assert.AreEqual(AnswerVariantRequirement.Required, variants[0].Requirement);
            Assert.IsNotNull(variants[0].SourceMeaningId, "Target 1 variant must have non-null SourceMeaningId.");

            Assert.AreEqual("tav-multi-2a", variants[1].StableId);
            Assert.AreEqual(targets[1].Id, variants[1].TargetId);
            Assert.AreEqual("network", variants[1].DisplayText);
            Assert.IsTrue(variants[1].IsPreferred);
            Assert.AreEqual(AnswerVariantRequirement.Required, variants[1].Requirement);
            Assert.IsNull(variants[1].SourceMeaningId);

            Assert.AreEqual("tav-multi-2b", variants[2].StableId);
            Assert.AreEqual(targets[1].Id, variants[2].TargetId);
            Assert.AreEqual("mesh", variants[2].DisplayText);
            Assert.IsFalse(variants[2].IsPreferred);
            Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, variants[2].Requirement);
            Assert.IsNull(variants[2].SourceMeaningId);

            Assert.AreEqual("tav-multi-3", variants[3].StableId);
            Assert.AreEqual(targets[2].Id, variants[3].TargetId);
            Assert.AreEqual("réseau", variants[3].DisplayText);
            Assert.IsTrue(variants[3].IsPreferred);
            Assert.AreEqual(AnswerVariantRequirement.Required, variants[3].Requirement);
            Assert.IsNull(variants[3].SourceMeaningId);

            var states = conn.Table<TargetFsrsStateEntity>().OrderBy(s => s.TargetId).ToList();
            Assert.AreEqual(3, states.Count);
            Assert.AreEqual(replayed1.State, states[0].State);
            Assert.AreEqual(replayed1.Stability!.Value, states[0].Stability!.Value, 0.0001);
            Assert.AreEqual(replayed2.State, states[1].State);
            Assert.AreEqual(replayed2.Stability!.Value, states[1].Stability!.Value, 0.0001);
            Assert.AreEqual(replayed3.State, states[2].State);
            Assert.AreEqual(replayed3.Stability!.Value, states[2].Stability!.Value, 0.0001);

            var reviews = conn.Table<TargetReviewEntity>().OrderBy(r => r.StableId).ToList();
            Assert.AreEqual(4, reviews.Count);

            Assert.AreEqual("trv-multi-1", reviews[0].StableId);
            Assert.AreEqual(ReviewRating.Good, reviews[0].Rating);
            Assert.IsFalse(reviews[0].WasTypedAnswer);
            Assert.IsTrue(reviews[0].WasCorrect);
            Assert.IsFalse(reviews[0].IsSessionRepeat);
            Assert.AreEqual(variants[0].Id, reviews[0].TargetAnswerVariantId);
            Assert.AreEqual(variants[0].Id, reviews[0].MatchedAnswerVariantId);

            Assert.AreEqual("trv-multi-2", reviews[1].StableId);
            Assert.AreEqual(ReviewRating.Good, reviews[1].Rating);
            Assert.IsFalse(reviews[1].WasTypedAnswer);
            Assert.IsTrue(reviews[1].WasCorrect);
            Assert.IsTrue(reviews[1].IsSessionRepeat);
            Assert.AreEqual(variants[0].Id, reviews[1].TargetAnswerVariantId);
            Assert.AreEqual(variants[0].Id, reviews[1].MatchedAnswerVariantId);

            Assert.AreEqual("trv-multi-3", reviews[2].StableId);
            Assert.AreEqual(ReviewRating.Again, reviews[2].Rating);
            Assert.IsTrue(reviews[2].WasTypedAnswer);
            Assert.IsFalse(reviews[2].WasCorrect);
            Assert.IsFalse(reviews[2].IsSessionRepeat);
            Assert.AreEqual(variants[1].Id, reviews[2].TargetAnswerVariantId);
            Assert.IsNull(reviews[2].MatchedAnswerVariantId, "MatchedAnswerVariantId must be null for incorrect typed answer.");

            Assert.AreEqual("trv-multi-4", reviews[3].StableId);
            Assert.AreEqual(ReviewRating.Hard, reviews[3].Rating);
            Assert.IsFalse(reviews[3].WasTypedAnswer);
            Assert.IsTrue(reviews[3].WasCorrect);
            Assert.IsFalse(reviews[3].IsSessionRepeat);
            Assert.AreEqual(variants[3].Id, reviews[3].TargetAnswerVariantId);
            Assert.AreEqual(variants[3].Id, reviews[3].MatchedAnswerVariantId);

            return true;
        });
    }

    [TestMethod]
    public async Task Schema14Merge_PopulatedDestination_RestoresSafetyCopyCleanly()
    {
        await using var destDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);

        await destDb.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-dest-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('tav-dest-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);
            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 0, NULL, NULL, NULL, NULL, NULL);
            """, targetId);
            conn.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?);", wordId, timeStr);
            conn.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?);", senseId, timeStr);
            Assert.IsTrue(TargetPersistenceShapeValidator.Validate(conn, out var targetFail), $"TargetPersistenceShapeValidator: {targetFail}");
            return true;
        });

        using var archiveStream = BuildArchiveV4();
        var service = new BackupService(destDb, new FakePlatformInfo());
        var result = await service.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"Merge failed: {result.ErrorCode}");
        Assert.IsTrue(result.Summary!.SafetyCopyCreated);

        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(destDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        var safetyCopyFiles = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive");
        Assert.AreEqual(1, safetyCopyFiles.Length);

        // Restore safety copy into a fresh empty database
        await using var restoredDb = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var restoredService = new BackupService(restoredDb, new FakePlatformInfo());
        using (var safetyStream = File.OpenRead(safetyCopyFiles[0]))
        {
            var restoreResult = await restoredService.ImportPortableArchiveAsync(safetyStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, restoreResult.Status);
            Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, restoreResult.Summary!.Disposition);
        }

        // Clean up safety copy
        try { File.Delete(safetyCopyFiles[0]); } catch { }

        // Verify restoredDb has destination state ('lt-dest-1') and not archive state ('st-lt-1')
        await restoredDb.RunInTransactionAsync(conn =>
        {
            var targets = conn.Table<LearningTargetEntity>().ToList();
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual("lt-dest-1", targets[0].StableId);
            return true;
        });
    }

    [TestMethod]
    public async Task Schema14Merge_SafetyCopyFailure_BlocksDestructiveMerge()
    {
        await using var db = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var timeStr = Schema13TimestampCodec.FormatUtc(DateTime.UtcNow);
        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", timeStr);
            var senseId = InsertSense(conn, wordId, timeStr);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('lt-dest-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            return true;
        });

        using var archiveStream = BuildArchiveV4();
        var service = new BackupService(
            db,
            new FakePlatformInfo(),
            mergeSafetyCopyService: new FailedSafetyCopyService());

        var result = await service.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Failed, result.Status);

        // Destination was not mutated
        await db.RunInTransactionAsync(conn =>
        {
            var targets = conn.Table<LearningTargetEntity>().ToList();
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual("lt-dest-1", targets[0].StableId);
            return true;
        });
    }

    [TestMethod]
    public async Task Schema14Merge_FailureInjectionDuringExecution_RollsBackCleanly()
    {
        await using var db = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time1 = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var time1Str = Schema13TimestampCodec.FormatUtc(time1);
        var time2 = time1.AddHours(2);
        var time2Str = Schema13TimestampCodec.FormatUtc(time2);

        var replayed1 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good)
        ]);
        var replayed2 = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
            new(new DateTimeOffset(time1, TimeSpan.Zero), ReviewRating.Good),
            new(new DateTimeOffset(time2, TimeSpan.Zero), ReviewRating.Good)
        ]);

        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", time1Str);
            var senseId = InsertSense(conn, wordId, time1Str);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, time1Str, time1Str);
            var targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");
            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, time1Str, time1Str, time1Str);
            var variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?);
            """, targetId, (int)replayed1.State, replayed1.Stability, replayed1.Difficulty, time1Str, replayed1.StepIndex, Schema13TimestampCodec.FormatUtc(replayed1.DueAtUtc!.Value.UtcDateTime));

            conn.Execute("""
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('trh-local-1', ?, 1, 2, ?);
            """, targetId, time1Str);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, time1Str, time1Str, time1Str);
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, time1Str, Schema13TimestampCodec.FormatUtc(replayed1.DueAtUtc!.Value.UtcDateTime));

            return true;
        });

        // Incoming archive has a 2nd review
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-remote-1", "trh-local-1", "lt-1", 1, BackupReviewRating.Good, time1),
                new("trh-remote-2", "trh-src-2", "lt-1", 2, BackupReviewRating.Good, time2)
            };
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed2.Stability, replayed2.Difficulty, time2, replayed2.StepIndex, replayed2.DueAtUtc!.Value.UtcDateTime)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-remote-1", "trv-dest-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1, DueAtUtc: replayed1.DueAtUtc!.Value.UtcDateTime),
                new("trv-remote-2", "trv-src-2", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time2, DueAtUtc: replayed2.DueAtUtc!.Value.UtcDateTime)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 2, 2, 0, 0, 2, 0, time1, time2, time2, [], "22223333444455556666777788889999")
            };
            var workflows = payload.Workflows with { LearningSessions = sessions };
            return payload with
            {
                TargetFsrsReviewHistoryEntries = history,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = workflows
            };
        });

        var failureInjector = new ThrowAtCheckpoint(Schema14MergeWriterExecutor.Checkpoints.DuringTargetReviews);
        var service = new BackupService(db, new FakePlatformInfo(), failureInjector: failureInjector);

        var result = await service.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Failed, result.Status);

        // Destination was rolled back cleanly — only 1 review remains
        await db.RunInTransactionAsync(conn =>
        {
            var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetReviews");
            Assert.AreEqual(1, count, "TargetReviews must be rolled back to 1 original review.");
            return true;
        });
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_CrossTargetVariantReference_FailsValidation()
    {
        var now = DateTime.UtcNow;
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var targets = new List<BackupLearningTarget>(payload.LearningTargets)
            {
                new("lt-2", "st-lt-2", "sense-1", BackupLearningTargetKind.Translation, "en", "de", false, now, now)
            };
            var variants = new List<BackupTargetAnswerVariant>(payload.TargetAnswerVariants)
            {
                new("tav-2", "st-tav-2", "lt-2", "de", "Netz", "netz", BackupAnswerVariantRequirement.Required, true, now, null, now, now)
            };
            var states = new List<BackupTargetFsrsState>(payload.TargetFsrsStates)
            {
                new("lt-2", BackupFsrsCardStateKind.New, null, null, null, null, null)
            };
            var reviews = new List<BackupTargetReview>
            {
                new("trv-1", "st-trv-1", "lt-1", "ls-1", BackupReviewRating.Good,
                    false, true, false, "tav-2", "tav-2", now, now.AddDays(1))
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-1", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, now, now, now, [], "11112222333344445555666677778888")
            };
            return payload with
            {
                LearningTargets = targets,
                TargetAnswerVariants = variants,
                TargetFsrsStates = states,
                TargetReviews = reviews,
                Workflows = payload.Workflows with { LearningSessions = sessions }
            };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_TargetReviewNonexistentTarget_FailsValidation()
    {
        var now = DateTime.UtcNow;
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var reviews = new List<BackupTargetReview>
            {
                new("trv-1", "st-trv-1", "lt-nonexistent", "ls-1", BackupReviewRating.Good,
                    false, true, false, null, null, now, now.AddDays(1))
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-1", BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, now, now, now, [], "11112222333344445555666677778888")
            };
            return payload with
            {
                TargetReviews = reviews,
                Workflows = payload.Workflows with { LearningSessions = sessions }
            };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.MissingReference, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_DuplicateTargetFsrsState_FailsValidation()
    {
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var states = new List<BackupTargetFsrsState>(payload.TargetFsrsStates)
            {
                new("lt-1", BackupFsrsCardStateKind.New, null, null, null, null, null)
            };
            return payload with { TargetFsrsStates = states };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_DuplicateHistorySequenceNumber_FailsValidation()
    {
        var now = DateTime.UtcNow;
        using var stream = BuildArchiveV4(payloadMutator: payload =>
        {
            var history = new List<BackupTargetFsrsReviewHistoryEntry>
            {
                new("trh-1", "st-trh-1", "lt-1", 1, BackupReviewRating.Good, now.AddMinutes(-10)),
                new("trh-2", "st-trh-2", "lt-1", 1, BackupReviewRating.Good, now)
            };
            var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [
                new(new DateTimeOffset(now, TimeSpan.Zero), ReviewRating.Good)
            ]);
            var states = new List<BackupTargetFsrsState>
            {
                new("lt-1", BackupFsrsCardStateKind.Review, replayed.Stability, replayed.Difficulty, now, replayed.StepIndex, replayed.DueAtUtc?.UtcDateTime)
            };
            return payload with { TargetFsrsReviewHistoryEntries = history, TargetFsrsStates = states };
        });

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, ex.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_UnknownFutureArchiveVersion_FailsValidation()
    {
        using var stream = BuildArchiveV4(manifestMutator: json =>
            json.Replace("\"formatVersion\":4", "\"formatVersion\":99", StringComparison.Ordinal));

        var ex = await Assert.ThrowsExactlyAsync<BackupFormatException>(() =>
            BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.UnsupportedFormat, ex.Code);
    }

    [TestMethod]
    public async Task Schema14Merge_CrossDatabaseEquivalentSenseWithDifferentStableIds_ReusesExistingTargetWithoutUniqueConstraintViolation()
    {
        await using var db = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var timeStr = Schema13TimestampCodec.FormatUtc(time);

        int wordId = 0, senseId = 0, targetId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            wordId = InsertWord(conn, "network", timeStr);
            senseId = InsertSense(conn, wordId, timeStr, stableId: "st-sense-dest-1");
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-dest-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, timeStr, timeStr);
            targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-dest-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, timeStr, timeStr, timeStr);

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 0, NULL, NULL, NULL, NULL, NULL);
            """, targetId);

            return true;
        });

        // Source archive has the semantically identical Sense but with different StableId "st-sense-source-1",
        // and identical target (Definition en -> de) with different StableId "st-lt-source-1".
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var senses = new List<BackupSense>
            {
                payload.Senses[0] with { StableId = "st-sense-source-1" }
            };
            var targets = new List<BackupLearningTarget>
            {
                payload.LearningTargets[0] with { StableId = "st-lt-source-1" }
            };
            return payload with
            {
                Senses = senses,
                LearningTargets = targets
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        // Preflight must recognize existing destination target and NOT plan AddLearningTarget
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsFalse(
            plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddLearningTarget),
            "Preflight must not plan AddLearningTarget when target semantically exists under equivalent Sense.");

        // Import must execute successfully without UNIQUE constraint failure
        archiveStream.Position = 0;
        var backupService = new BackupService(db, new FakePlatformInfo());
        var result = await backupService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"Merge failed with: {result.ErrorCode}");

        // Existing target is reused, no duplicate target inserted
        await db.ReadAsync(async conn =>
        {
            var targets = await conn.Table<LearningTargetEntity>().ToListAsync();
            Assert.AreEqual(1, targets.Count, "Existing destination target must be reused without duplicate insertion.");
            Assert.AreEqual("st-lt-dest-1", targets[0].StableId, "Destination target StableId must be preserved.");
            return true;
        });
    }

    [TestMethod]
    public async Task Schema14Merge_EqualTimestampTargetReviews_PreservesArchiveCausalOrder_CompatiblePrefixSucceeds()
    {
        await using var db = await CreateValidSchema14DatabaseAsync(enableForeignKeys: true);
        var time1 = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        var time1Str = Schema13TimestampCodec.FormatUtc(time1);
        var dueTime = time1.AddDays(1);
        var dueTimeStr = Schema13TimestampCodec.FormatUtc(dueTime);

        int targetId = 0, variantId = 0, sessionId = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(conn, "network", time1Str);
            var senseId = InsertSense(conn, wordId, time1Str);
            conn.Execute("""
                INSERT INTO LearningTargets
                    (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-lt-1', ?, 0, 'en', 'de', 0, ?, ?);
            """, senseId, time1Str, time1Str);
            targetId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetAnswerVariants
                    (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('st-tav-1', ?, 'de', 'Netzwerk', 'netzwerk', 0, 1, ?, ?, ?);
            """, targetId, time1Str, time1Str, time1Str);
            variantId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute("""
                INSERT INTO TargetFsrsStates (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, 0, NULL, NULL, NULL, NULL, NULL);
            """, targetId);

            conn.Execute("""
                INSERT INTO LearningSessions (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('11112222333344445555666677778888', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?);
            """, time1Str, time1Str, time1Str);
            sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            // Destination has Review 1 at time1 (Rating.Good, IsSessionRepeat: false)
            conn.Execute("""
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-dest-1', ?, ?, 2, 0, 1, 0, ?, ?, ?, ?);
            """, targetId, sessionId, variantId, variantId, time1Str, dueTimeStr);

            return true;
        });

        // Source archive has Review 1 (Good, IsSessionRepeat: false) followed causally by Review 2 (Again, IsSessionRepeat: true) at identical ReviewedAtUtc
        using var archiveStream = BuildArchiveV4(payloadMutator: payload =>
        {
            var reviews = new List<BackupTargetReview>
            {
                new("trv-src-1", "trv-dest-1", "lt-1", "ls-000001", BackupReviewRating.Good,
                    WasTypedAnswer: false, WasCorrect: true, IsSessionRepeat: false,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1, DueAtUtc: dueTime),
                new("trv-src-2", "trv-remote-2", "lt-1", "ls-000001", BackupReviewRating.Again,
                    WasTypedAnswer: false, WasCorrect: false, IsSessionRepeat: true,
                    TargetAnswerVariantId: "tav-1", MatchedAnswerVariantId: "tav-1",
                    ReviewedAtUtc: time1, DueAtUtc: dueTime)
            };
            var sessions = new List<BackupLearningWorkflowV2>
            {
                new("ls-000001", BackupLearningSessionStatus.Completed, 1, 1, 1, 0, 1, 0, time1, time1, time1, [], "22223333444455556666777788889999")
            };
            return payload with
            {
                TargetReviews = reviews,
                Workflows = payload.Workflows with { LearningSessions = sessions }
            };
        });

        var preflight = new MergePreflightService(db);
        var plan = await preflight.CreatePreflightPlanAsync(archiveStream, CancellationToken.None);

        // Destination is a strict causal prefix under V3 policy: plan must be executable and ready
        Assert.IsTrue(plan.IsExecutable, $"Compatible equal-timestamp causal review prefix must be executable. Error: {plan.ErrorCode}");
        Assert.AreEqual(MergePreflightStatus.Ready, plan.Status);
        Assert.IsNotNull(plan.Schema14Plan);
        Assert.IsTrue(
            plan.Schema14Plan.Actions.Any(a => a.Classification == Schema14MergeActionClassification.AddTargetReview),
            "AddTargetReview action must be planned for second equal-timestamp review.");

        archiveStream.Position = 0;
        var backupService = new BackupService(db, new FakePlatformInfo());
        var result = await backupService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"Merge failed with: {result.ErrorCode}");

        await db.ReadAsync(async conn =>
        {
            var count = await conn.Table<TargetReviewEntity>().CountAsync();
            Assert.AreEqual(2, count, "Both equal-timestamp review occurrences must be preserved.");
            return true;
        });
    }
}
