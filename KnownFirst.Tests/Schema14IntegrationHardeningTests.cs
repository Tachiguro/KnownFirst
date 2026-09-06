using System.Collections.Concurrent;
using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Review;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema14IntegrationHardeningTests
{
    private static readonly DateTime TestStartTime = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task FreshDatabase_InitializesDirectlyToCanonicalSchema14TargetRuntime()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            var userVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
            Assert.AreEqual(14, userVersion);

            var learningCapability = LearningSchemaCapability.Resolve(connection);
            Assert.IsInstanceOfType<LearningSchema14CapabilityResult>(learningCapability);

            var prepCapability = PreparationSchemaCapability.Resolve(connection);
            Assert.IsInstanceOfType<PreparationSchema14CapabilityResult>(prepCapability);

            var backupCapability = BackupSchemaCapability.Resolve(connection);
            Assert.IsInstanceOfType<Schema14CapabilityResult>(backupCapability);

            Assert.IsTrue(Schema13RuntimeIntegrityValidator.Validate(connection, out var schema13Detail), schema13Detail);
            Assert.IsTrue(TargetPersistenceShapeValidator.Validate(connection, out var targetDetail), targetDetail);

            var foreignKeyViolations = connection.Query<ForeignKeyCheckRow>("PRAGMA foreign_key_check");
            Assert.IsEmpty(foreignKeyViolations);

            var integrity = connection.ExecuteScalar<string>("PRAGMA integrity_check");
            Assert.AreEqual("ok", integrity);

            var quickCheck = connection.ExecuteScalar<string>("PRAGMA quick_check");
            Assert.AreEqual("ok", quickCheck);

            return true;
        });
    }

    [TestMethod]
    public async Task FreshDatabase_EmptyDatabase_WorkflowStateAndLearningServiceRemainTargetAuthoritative()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        var workflow = new WorkflowStateService(database, clock);
        var snapshot = await workflow.GetSnapshotAsync();

        Assert.AreEqual(0, snapshot.DueCardCount);
        Assert.IsNull(snapshot.NextDueAtUtc);
        Assert.AreEqual(0, snapshot.PreparedNewItemCount);
        Assert.AreEqual(0, snapshot.UnpreparedUnknownCount);
        Assert.IsFalse(snapshot.HasActiveLearning);
        Assert.IsFalse(snapshot.HasActivePreparation);
        Assert.IsFalse(snapshot.HasActiveReview);

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);
        var overview = await preparation.GetOverviewAsync();

        Assert.AreEqual(0, overview.DueCardCount);
        Assert.AreEqual(0, overview.PreparedNewItemCount);

        var learningService = CreateLearningService(database, clock);
        var loadResult = await learningService.GetOrStartAsync();

        Assert.IsNull(loadResult.Card);
    }

    [TestMethod]
    public async Task FreshDatabase_EmptyDatabase_BackupServiceEmitsCurrentV4Archive()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var backupService = new BackupService(database, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await backupService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        archiveStream.Position = 0;
        var versioned = await BackupArchiveReader.ValidateVersionedAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(4, versioned.FormatVersion);
        Assert.IsNotNull(versioned.V4);
        Assert.AreEqual(14, versioned.V4!.Manifest.SourceDatabaseSchemaVersion);
        Assert.IsEmpty(versioned.V4.Payload.Vocabulary);
        Assert.IsEmpty(versioned.V4.Payload.Senses);
        Assert.IsEmpty(versioned.V4.Payload.LearningTargets);
        Assert.IsEmpty(versioned.V4.Payload.TargetAnswerVariants);
        Assert.IsEmpty(versioned.V4.Payload.TargetFsrsStates);
        Assert.IsEmpty(versioned.V4.Payload.TargetFsrsReviewHistoryEntries);

        await using var targetDb = new ProductionInitializedDatabase();
        await targetDb.InitializeAsync();
        var targetBackupService = new BackupService(targetDb, new FakePlatformInfo());

        archiveStream.Position = 0;
        var restoreResult = await targetBackupService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, restoreResult.Summary!.Disposition);
    }

    [TestMethod]
    public async Task FreshDatabase_PrepareAndReview_UpdatesTargetSchedulerAndWorkflowState()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(database, clock);

        int targetId = 0;
        await database.RunInTransactionAsync(conn =>
        {
            Assert.AreEqual(4, conn.Table<WordEntity>().Count());
            Assert.AreEqual(1, conn.Table<WordEntity>().Count(w => w.CanonicalTerm == "Haus"));
            Assert.AreEqual(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses"));
            Assert.AreEqual(1, TargetLearningRepository.CountTargets(conn));

            var targets = conn.Table<LearningTargetEntity>().ToList();
            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(LearningTargetKind.Translation, targets[0].TargetKind);
            targetId = targets[0].Id;

            var variants = conn.Table<TargetAnswerVariantEntity>().ToList();
            Assert.AreEqual(3, variants.Count);

            var card = TargetFsrsStateRepository.Load(conn, targetId);
            Assert.IsNotNull(card);
            Assert.AreEqual(Fsrs6CardState.New, card.State);
            Assert.IsNull(card.DueAtUtc);
            return true;
        });

        var workflow = new WorkflowStateService(database, clock);
        var beforeSnapshot = await workflow.GetSnapshotAsync();
        Assert.AreEqual(1, beforeSnapshot.PreparedNewItemCount);
        Assert.AreEqual(0, beforeSnapshot.DueCardCount);
        Assert.AreEqual(WorkflowPrimaryAction.StartLearning, beforeSnapshot.PrimaryAction);

        var learningService = CreateLearningService(database, clock);
        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card);
        Assert.AreEqual(LearningTargetKind.Translation, loadResult.Card.TargetKind);

        await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
        var rateResult = await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        await database.RunInTransactionAsync(conn =>
        {
            var card = TargetFsrsStateRepository.Load(conn, targetId);
            Assert.IsNotNull(card);
            Assert.AreNotEqual(Fsrs6CardState.New, card.State);
            Assert.IsNotNull(card.Stability);
            Assert.IsGreaterThan(0.0, card.Stability.Value);
            Assert.IsNotNull(card.DueAtUtc);
            Assert.IsGreaterThan(new DateTimeOffset(clock.UtcNow), card.DueAtUtc.Value);

            var history = TargetFsrsReviewHistoryRepository.LoadHistory(conn, targetId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(ReviewRating.Good, history[0].ReviewEvent.Rating);
            return true;
        });

        var afterSnapshot = await workflow.GetSnapshotAsync();
        Assert.AreEqual(0, afterSnapshot.DueCardCount);
        Assert.IsNotNull(afterSnapshot.NextDueAtUtc);
        Assert.IsGreaterThan(clock.UtcNow, afterSnapshot.NextDueAtUtc.Value);
    }

    [TestMethod]
    public async Task FreshDatabase_BackupAndRestore_PreservesTargetRuntimeAuthority()
    {
        await using var sourceDb = new ProductionInitializedDatabase();
        await sourceDb.InitializeAsync();

        var clock = new FakeClock(TestStartTime);
        await SeedPreparedWordWithTranslationAsync(sourceDb, clock);

        var learningService = CreateLearningService(sourceDb, clock);
        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card);
        await learningService.RevealAnswerAsync(loadResult.Card.QueueItemId);
        await learningService.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        var sourceBackup = new BackupService(sourceDb, new FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await sourceBackup.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        archiveStream.Position = 0;
        var versioned = await BackupArchiveReader.ValidateVersionedAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(4, versioned.FormatVersion);
        Assert.IsNotNull(versioned.V4);
        Assert.AreEqual(1, versioned.V4!.Payload.LearningTargets.Count);
        Assert.AreEqual(3, versioned.V4.Payload.TargetAnswerVariants.Count);
        Assert.AreEqual(1, versioned.V4.Payload.TargetFsrsStates.Count);
        Assert.AreEqual(1, versioned.V4.Payload.TargetFsrsReviewHistoryEntries.Count);

        await using var targetDb = new ProductionInitializedDatabase();
        await targetDb.InitializeAsync();
        var targetBackup = new BackupService(targetDb, new FakePlatformInfo());

        archiveStream.Position = 0;
        var restoreResult = await targetBackup.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, restoreResult.Status, restoreResult.ErrorCode);
        Assert.IsNotNull(restoreResult.Summary, restoreResult.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, restoreResult.Summary!.Disposition);

        await targetDb.RunInTransactionAsync(conn =>
        {
            Assert.AreEqual(1, TargetLearningRepository.CountTargets(conn));
            Assert.AreEqual(1, conn.Table<TargetFsrsStateEntity>().Count());
            Assert.AreEqual(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries"));
            Assert.IsTrue(Schema13RuntimeIntegrityValidator.Validate(conn, out var sErr), sErr);
            Assert.IsTrue(TargetPersistenceShapeValidator.Validate(conn, out var tErr), tErr);
            Assert.IsEmpty(conn.Query<ForeignKeyCheckRow>("PRAGMA foreign_key_check"));
            return true;
        });

        var targetWorkflow = new WorkflowStateService(targetDb, clock);
        var targetSnapshot = await targetWorkflow.GetSnapshotAsync();
        Assert.AreEqual(0, targetSnapshot.DueCardCount);
        Assert.IsNotNull(targetSnapshot.NextDueAtUtc);
        Assert.IsGreaterThan(clock.UtcNow, targetSnapshot.NextDueAtUtc.Value);
    }

    [TestMethod]
    public async Task FreshDatabase_ReopenIdempotence_PreservesSchemaAndCapability()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"schema14-idempotence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var dbPath = Path.Combine(tempDirectory, "test.db3");

        try
        {
            var conn1 = new SQLiteAsyncConnection(dbPath);
            await DatabaseSchema.InitializeAsync(conn1);

            var version1 = await conn1.ExecuteScalarAsync<int>("PRAGMA user_version");
            Assert.AreEqual(14, version1);

            await conn1.RunInTransactionAsync(conn =>
            {
                conn.Insert(new WordEntity
                {
                    Language = "de",
                    CanonicalTerm = "Testwort",
                    NormalizedTerm = "W:testwort",
                    Status = WordStatus.Unreviewed,
                    TokenKind = TokenKind.Word,
                    PreparationState = PreparationState.Unprepared,
                    CreatedAt = TestStartTime,
                    UpdatedAt = TestStartTime
                });
            });

            await conn1.CloseAsync();

            var conn2 = new SQLiteAsyncConnection(dbPath);
            await DatabaseSchema.InitializeAsync(conn2);

            var version2 = await conn2.ExecuteScalarAsync<int>("PRAGMA user_version");
            Assert.AreEqual(14, version2);

            await conn2.RunInTransactionAsync(conn =>
            {
                var count = conn.Table<WordEntity>().Count();
                Assert.AreEqual(1, count);

                var learningCapability = LearningSchemaCapability.Resolve(conn);
                Assert.IsInstanceOfType<LearningSchema14CapabilityResult>(learningCapability);

                var prepCapability = PreparationSchemaCapability.Resolve(conn);
                Assert.IsInstanceOfType<PreparationSchema14CapabilityResult>(prepCapability);

                var backupCapability = BackupSchemaCapability.Resolve(conn);
                Assert.IsInstanceOfType<Schema14CapabilityResult>(backupCapability);

                Assert.IsTrue(Schema13RuntimeIntegrityValidator.Validate(conn, out var sErr), sErr);
                Assert.IsTrue(TargetPersistenceShapeValidator.Validate(conn, out var tErr), tErr);
                Assert.IsEmpty(conn.Query<ForeignKeyCheckRow>("PRAGMA foreign_key_check"));
            });

            await conn2.CloseAsync();
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    [TestMethod]
    public async Task FreshDatabase_SchemaSnapshot_ValidatesAllSchema14TablesAndIntegrity()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(conn =>
        {
            var tableNames = conn.Query<TableNameRow>(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
                .Select(row => row.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string[] expectedTables =
            [
                "Documents",
                "Words",
                "WordForms",
                "SentenceSpans",
                "WordOccurrences",
                "Meanings",
                "ReviewStates",
                "ReviewSessions",
                "ReviewCandidates",
                "LexicalCache",
                "PreparationSessions",
                "PreparationCandidates",
                "ContextSnapshots",
                "LearningCards",
                "LearningReviews",
                "LearningSessions",
                "LearningSessionCards",
                "Senses",
                "AnswerVariants",
                "SenseAnswerVariantAssignments",
                "AnswerVariantProgress",
                "DerivedTermEvidenceEntries",
                "LearningDayState",
                "LearningDayGrants",
                "FsrsCardStates",
                "FsrsReviewHistoryEntries",
                "WordLearningControls",
                "SenseLearningControls",
                "LearningTargets",
                "TargetAnswerVariants",
                "TargetFsrsStates",
                "TargetFsrsReviewHistoryEntries",
                "TargetReviews"
            ];

            Assert.AreEqual(33, expectedTables.Length);
            Assert.AreEqual(33, tableNames.Count);

            foreach (var expectedTable in expectedTables)
            {
                Assert.IsTrue(tableNames.Contains(expectedTable), $"Expected table {expectedTable} was not found.");
            }

            var indexNames = conn.Query<TableNameRow>(
                "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%'")
                .Select(row => row.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(indexNames.Any(name => name.Contains("LearningTargets", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(indexNames.Any(name => name.Contains("TargetAnswerVariants", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(indexNames.Any(name => name.Contains("TargetFsrsStates", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(indexNames.Any(name => name.Contains("TargetFsrsReviewHistoryEntries", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(indexNames.Any(name => name.Contains("TargetReviews", StringComparison.OrdinalIgnoreCase)));

            Assert.IsEmpty(conn.Query<ForeignKeyCheckRow>("PRAGMA foreign_key_check"));
            Assert.AreEqual("ok", conn.ExecuteScalar<string>("PRAGMA integrity_check"));
            return true;
        });
    }

    private sealed class TableNameRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyCheckRow
    {
        public string? Table { get; set; }
        public long? RowId { get; set; }
        public string? Parent { get; set; }
        public int? Fkid { get; set; }
    }

    private static async Task SeedPreparedWordWithTranslationAsync(
        IKnownFirstDatabase database,
        FakeClock clock)
    {
        var provider = new MutableProvider(clock);
        var review = CreateTextReviewService(database);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus steht dort.", "Haus", "de", LexicalLookupMode.Translation, "en");

        var sessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        Assert.IsTrue(sessionId > 0);

        var item = await preparation.GetCurrentAsync();
        Assert.IsNotNull(item);

        var input = new PreparedMeaningInput(
            SelectedMeaningId: null,
            AcronymExpansion: null,
            Translation: "house",
            Definition: string.Empty,
            DictionaryExample: null,
            AdditionalNote: null,
            AcceptedAliases: ["building", "home"],
            ProviderName: "Wiktionary",
            SourceProject: "wiktionary",
            SourcePageTitle: "Haus",
            SourceRevisionId: 12345,
            Attribution: "CC BY-SA",
            ManualInputMode: LexicalLookupMode.Translation);

        await preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);
    }

    private static TextReviewService CreateTextReviewService(IKnownFirstDatabase database) =>
        new(
            database,
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

    private static PreparationService CreatePreparationService(
        IKnownFirstDatabase database,
        MutableProvider provider,
        FakeClock clock) =>
        new(
            database,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(database),
                new LexicalLookupProviderResolver([provider])),
            clock,
            diagnosticLog: null,
            faultInjector: null);

    private static LearningService CreateLearningService(
        IKnownFirstDatabase database,
        FakeClock clock) =>
        new(
            database,
            new SpellingAnswerComparer(),
            clock,
            new Fsrs6SchedulingService(clock),
            new TestAppSettings(LearningMode.Automatic));

    private static async Task<int> ImportWithOnlyThisWordUnknownAsync(
        TextReviewService review,
        string content,
        string unknownTerm,
        string sourceLanguage,
        LexicalLookupMode lookupMode,
        string? targetLanguage = null)
    {
        var request = new ImportTextRequest(
            $"Document {Guid.NewGuid():N}",
            content,
            sourceLanguage,
            lookupMode,
            targetLanguage);
        var result = await review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private sealed class MutableProvider(FakeClock clock) : IDictionaryLookupProvider
    {
        private readonly ConcurrentQueue<LexicalLookupRequest> _requests = new();

        public Func<LexicalLookupRequest, IReadOnlyList<LexicalMeaning>> MeaningsFactory { get; set; } =
            _ => [new LexicalMeaning("primary", "noun", "Ein Gebäude zum Wohnen", "house", null, ["building", "home"])];

        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public int RequestCount => _requests.Count;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
            var result = new LexicalResult(
                LexicalLookupStatus.Success,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                MeaningsFactory(request),
                ProviderName,
                "en.wiktionary.org",
                request.Term,
                1,
                "Wiktionary contributors",
                clock.UtcNow,
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage);
            return Task.FromResult(result);
        }
    }

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

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
}
