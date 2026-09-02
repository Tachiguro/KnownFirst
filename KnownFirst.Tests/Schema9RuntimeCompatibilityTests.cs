using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Schema-9 runtime-compatibility tests (backup-merge data-integrity program, Package 1): proves Backup,
/// Preparation, Learning, Dashboard, snapshot, and merge-writer routing stay operational against a valid
/// Schema-9 database. Every Schema-9 physical shape used here is built independently through raw SQLite
/// DDL (<see cref="Schema9RawFixture"/>) starting from a pinned Schema-8 fixture — never through any
/// future production Schema-9 migration code. Isolated temporary SQLite databases only; no real user
/// database or `.kfarchive` is ever used.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema9RuntimeCompatibilityTests
{
    [TestMethod]
    public async Task Resolve_ValidSchema9Shape_BackupPreparationAndLearningCapabilitiesAreAccepted()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();

        BackupSchemaCapabilityResult? backupCapability = null;
        PreparationSchemaCapabilityResult? preparationCapability = null;
        LearningSchemaCapabilityResult? learningCapability = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            backupCapability = BackupSchemaCapability.Resolve(connection);
            preparationCapability = PreparationSchemaCapability.Resolve(connection);
            learningCapability = LearningSchemaCapability.Resolve(connection);
        });

        Assert.IsNotNull(backupCapability);
        Assert.AreEqual("Schema9CapabilityResult", backupCapability.GetType().Name);
        Assert.IsNotNull(preparationCapability);
        Assert.AreEqual("PreparationSchema9CapabilityResult", preparationCapability.GetType().Name);
        Assert.IsNotNull(learningCapability);
        Assert.AreEqual("LearningSchema9CapabilityResult", learningCapability.GetType().Name);
    }

    /// <summary>
    /// SCHEMA9_MALFORMED_SHAPE_FAIL_CLOSED_TEST_HARDENING: a malformed Schema-9 database (reporting
    /// <c>user_version = 9</c> but missing the required partial unique Active-session index) must be
    /// rejected by all three schema-capability resolvers with shape-mismatch semantics — never accepted,
    /// never repaired. Each resolver is invoked and asserted independently so no resolver's result depends
    /// on another resolver's exception.
    /// </summary>
    [TestMethod]
    public async Task Resolve_MalformedSchema9Shape_BackupPreparationAndLearningCapabilitiesFailWithShapeMismatch()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        await fixture.Connection.ExecuteAsync($"DROP INDEX \"{Schema9RawFixture.ActiveIndexName}\"");

        var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);

        var backupException = await Assert.ThrowsExactlyAsync<BackupSchemaCapabilityException>(
            () => fixture.Connection.RunInTransactionAsync(connection => BackupSchemaCapability.Resolve(connection)));
        Assert.AreEqual(9, backupException.FoundVersion);
        Assert.IsTrue(backupException.ShapeMismatch);
        Assert.AreEqual("backup-schema-capability-shape-mismatch", backupException.ErrorCode);

        var preparationException = await Assert.ThrowsExactlyAsync<PreparationSchemaCapabilityException>(
            () => fixture.Connection.RunInTransactionAsync(connection => PreparationSchemaCapability.Resolve(connection)));
        Assert.AreEqual(9, preparationException.FoundVersion);
        Assert.IsTrue(preparationException.ShapeMismatch);
        Assert.AreEqual("preparation-schema-capability-shape-mismatch", preparationException.ErrorCode);

        var learningException = await Assert.ThrowsExactlyAsync<LearningSchemaCapabilityException>(
            () => fixture.Connection.RunInTransactionAsync(connection => LearningSchemaCapability.Resolve(connection)));
        Assert.AreEqual(9, learningException.FoundVersion);
        Assert.IsTrue(learningException.ShapeMismatch);
        Assert.AreEqual("learning-schema-capability-shape-mismatch", learningException.ErrorCode);

        Assert.AreEqual(9, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        var after = await PersistentDatabaseSnapshot.CaptureCompleteAsync(fixture.Connection);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task BackupSnapshotCapture_ValidSchema9Database_UsesMeaningCentricSnapshotRoute()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();

        CapturedBackupSnapshotEnvelope? captured = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            captured = BackupSnapshotCapture.CaptureForExport(connection));

        Assert.IsNotNull(captured);
        Assert.AreEqual("CapturedSchema9SnapshotEnvelope", captured.GetType().Name);
    }

    /// <summary>
    /// The Schema-9 <see cref="BackupArchiveWriterV2"/> overload cannot be referenced directly from test
    /// code before it exists (it accepts a not-yet-existing <c>ValidatedSchema9Capability</c>), so this
    /// proves the required manifest behavior — "a Schema-9 archive manifest reports
    /// <c>SourceDatabaseSchemaVersion = 9</c>" — through the already-existing public
    /// <see cref="BackupService.CreatePortableArchiveAsync"/> entry point, which exercises the exact same
    /// capture -&gt; write pipeline end to end.
    /// </summary>
    [TestMethod]
    public async Task BackupArchiveWriterV2_ValidSchema9Source_RecordsSourceSchemaVersionNine()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var archive = new MemoryStream();
        await service.CreatePortableArchiveAsync(archive, CancellationToken.None);
        archive.Position = 0;

        var validated = await BackupArchiveReader.ValidateVersionedAsync(archive, CancellationToken.None);

        Assert.AreEqual(2, validated.FormatVersion);
        Assert.IsNotNull(validated.V2);
        Assert.AreEqual(9, validated.V2!.Manifest.SourceDatabaseSchemaVersion);
    }

    [TestMethod]
    public async Task MergePreflightService_ValidSchema9Target_UsesV2Route()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var sourceService = new BackupService(sourceDatabase, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var archive = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archive, CancellationToken.None);
        archive.Position = 0;

        // An empty target (rather than another representative fixture) so every source entity classifies
        // New, proving the meaning-centric (Sense) planner output ran rather than merely matching itself.
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await Schema9RawFixture.ReplaceLegacyIndexAsync(targetFixture.Connection);
        await targetFixture.Connection.ExecuteAsync("PRAGMA user_version = 9");
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        var preflight = new MergePreflightService(targetDatabase);

        var plan = await preflight.CreatePreflightPlanAsync(archive, CancellationToken.None);

        Assert.IsTrue(
            plan.Status is MergePreflightStatus.Ready or MergePreflightStatus.NoChanges,
            $"Expected the V2 preflight route to accept a Schema-9 target, got status {plan.Status} (error {plan.ErrorCode}).");
        Assert.IsNotNull(plan.Manifest);
        Assert.IsTrue(
            plan.PerEntity.TryGetValue(MergeEntityKind.Sense, out var senseCounts) && senseCounts.NewCount > 0,
            "Expected the meaning-centric (Sense) planner output, proving the V2 route ran rather than the legacy V1 planner.");
    }

    [TestMethod]
    public async Task MergeWriterService_ValidSchema9Target_IsNotRejectedAsUnsupportedSchema()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var sourceService = new BackupService(sourceDatabase, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var archiveStream = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);
        archiveStream.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(archiveStream, CancellationToken.None);

        await using var targetFixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);

        var preflight = new MergePreflightService(targetDatabase);
        var plan = await preflight.CreatePreflightPlanAsync(validated, CancellationToken.None);
        Assert.IsTrue(plan.IsExecutable, $"Expected an executable plan, got {plan.Status} ({plan.ErrorCode}).");

        var writer = new MergeWriterService(targetDatabase);
        var result = await writer.ApplyAsync(validated.V2!.Payload, plan, CancellationToken.None);

        Assert.AreNotEqual(MergeWriterErrorCodes.TargetNotSchema8, result.ErrorCode);
        Assert.AreEqual(MergeWriteStatus.Success, result.Status);
        Assert.AreEqual(9, await targetFixture.ReadUserVersionAsync());
    }

    /// <summary>
    /// Independent-review correction (SCHEMA9_POPULATED_IMPORT_SAFETY_COPY_CORRECTION): proves a populated
    /// <em>current-schema</em> target routes through the real <see cref="BackupService.ImportPortableArchiveAsync"/>
    /// merge orchestration end to end — never the restore-into-empty path, never <see cref="PortableImportStatus.TargetNotEmpty"/> —
    /// and that the mandatory pre-merge safety copy is created, reopened, and strictly validated before the
    /// writer mutates anything. The target is built through real production initialization, so it tracks
    /// <see cref="DatabaseSchema.CurrentVersion"/> rather than being pinned to a raw Schema-9 shape. The
    /// archive carries one deterministic word-only Schema-8 source and no learning graph, producing
    /// <see cref="MergePreflightStatus.Ready"/> /
    /// <see cref="PortableImportDisposition.MergeApplied"/>, never a no-change import.
    /// </summary>
    [TestMethod]
    public async Task BackupService_ImportPortableArchiveAsync_PopulatedCurrentSchemaTarget_RoutesThroughMergeAndCreatesSafetyCopy()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await sourceFixture.Connection.InsertAsync(new WordEntity
        {
            Language = "en",
            CanonicalTerm = "bank",
            NormalizedTerm = "bank",
            Status = WordStatus.Unreviewed,
            TokenKind = TokenKind.Word,
            PreparationState = PreparationState.Unprepared,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        Schema8BackupSnapshot? sourceSnapshot = null;
        await sourceFixture.Connection.RunInTransactionAsync(
            connection => sourceSnapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(connection));
        BackupSchemaCapabilityResult? sourceCapabilityResult = null;
        await sourceFixture.Connection.RunInTransactionAsync(
            connection => sourceCapabilityResult = BackupSchemaCapability.Resolve(connection));
        var sourceCapability = ((Schema8CapabilityResult)sourceCapabilityResult!).Capability;
        var archivePayload = BackupModelMapperV2.MapToExternal(sourceSnapshot!);

        using var archiveStream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(
            archivePayload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), sourceCapability, DateTime.UtcNow,
            archiveStream, CancellationToken.None);
        var archiveBytes = archiveStream.ToArray();
        using (var validationStream = new MemoryStream(archiveBytes))
        {
            var validatedInput = await BackupArchiveReader.ValidateVersionedAsync(
                validationStream, CancellationToken.None);
            Assert.AreEqual(2, validatedInput.FormatVersion);
            Assert.IsNotNull(validatedInput.V2);
            Assert.AreEqual(8, validatedInput.V2!.Manifest.SourceDatabaseSchemaVersion);
            Assert.HasCount(1, validatedInput.V2.Payload.Vocabulary);
            Assert.AreEqual("bank", validatedInput.V2.Payload.Vocabulary[0].CanonicalTerm);
            Assert.IsEmpty(validatedInput.V2.Payload.Learning.Cards);
            Assert.IsEmpty(validatedInput.V2.Payload.Learning.ReviewEvents);
            Assert.IsEmpty(validatedInput.V2.Payload.Workflows.LearningSessions);
        }

        // Populated current-schema target with deterministic target-only durable data, built through real
        // production initialization: DatabaseSchema.InitializeAsync activates a fresh database directly to
        // DatabaseSchema.CurrentVersion.
        await using var targetDb = new IsolatedCurrentSchemaDatabase();
        await targetDb.InitializeAsync();
        await targetDb.RunInTransactionAsync(connection =>
        {
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = "targetonlyword",
                NormalizedTerm = "targetonlyword",
                Status = WordStatus.Unreviewed,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Unprepared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        });

        var wordCountBeforeImport = await targetDb.ExecuteSnapshotAsync(connection => connection.Table<WordEntity>().Count());
        Assert.AreEqual(1, wordCountBeforeImport, "Fixture setup expects exactly one target-only word before import.");

        var service = new BackupService(targetDb, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var importStream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);

        Assert.AreNotEqual(
            PortableImportStatus.TargetNotEmpty,
            result.Status,
            "A populated current-schema target must route through merge, never fall back to restore-into-empty.");
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.IsNotNull(result.Summary);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary.Disposition);
        Assert.IsTrue(result.Summary.SafetyCopyCreated, "The mandatory pre-merge safety copy must have been created.");
        Assert.AreEqual(1, result.Summary.InsertedCount);

        // Target-only data remains; archive-only data was inserted alongside it.
        Assert.AreEqual(1, await targetDb.ExecuteSnapshotAsync(connection =>
            connection.Table<WordEntity>().Count(w => w.CanonicalTerm == "targetonlyword")));
        Assert.AreEqual(1, await targetDb.ExecuteSnapshotAsync(connection =>
            connection.Table<WordEntity>().Count(w => w.CanonicalTerm == "bank")));

        // The mandatory safety copy exists, reopens, and strictly validates as an explicit current-schema
        // envelope capturing the target's state BEFORE merge — exactly the one target-only word, never the
        // archive's "bank" — proving it was captured before the writer mutated anything.
        var safetyCopyDirectory = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        var safetyCopyFiles = Directory.GetFiles(safetyCopyDirectory, "*.kfarchive");
        Assert.HasCount(1, safetyCopyFiles);

        await using (var safetyCopyStream = new FileStream(safetyCopyFiles[0], FileMode.Open, FileAccess.Read))
        {
            var validatedSafetyCopy = await BackupArchiveReader.ValidateVersionedAsync(safetyCopyStream, CancellationToken.None);
            Assert.AreEqual(3, validatedSafetyCopy.FormatVersion);
            Assert.IsNotNull(validatedSafetyCopy.V3);
            Assert.AreEqual(13, validatedSafetyCopy.V3!.Manifest.SourceDatabaseSchemaVersion);
            Assert.AreEqual(1, validatedSafetyCopy.V3.Payload.Vocabulary.Count);
            Assert.AreEqual("targetonlyword", validatedSafetyCopy.V3.Payload.Vocabulary[0].CanonicalTerm);
            Assert.IsEmpty(validatedSafetyCopy.V3.Payload.FsrsCardStates);
            Assert.IsEmpty(validatedSafetyCopy.V3.Payload.FsrsReviewHistoryEntries);
            Assert.IsEmpty(validatedSafetyCopy.V3.Payload.WordLearningControls);
            Assert.IsEmpty(validatedSafetyCopy.V3.Payload.SenseLearningControls);
        }

        // The final target database remains structurally sound after the merge writer's transaction commits.
        var integrityResult = await targetDb.ReadAsync(
            connection => connection.ExecuteScalarAsync<string>("PRAGMA integrity_check"));
        Assert.AreEqual("ok", integrityResult);
        var foreignKeyViolations = await targetDb.ReadAsync(
            connection => connection.QueryAsync<ForeignKeyViolationRow>("PRAGMA foreign_key_check"));
        Assert.IsEmpty(foreignKeyViolations);
    }

    /// <summary>
    /// Independent-review correction (SCHEMA9_POPULATED_IMPORT_SAFETY_COPY_CORRECTION): proves
    /// <see cref="MergeSafetyCopyService.CreateSafetyCopyAsync"/> succeeds directly against a populated,
    /// valid <em>current-schema</em> target — captured through an explicit versioned envelope, reporting
    /// manifest <c>SourceDatabaseSchemaVersion = </c><see cref="DatabaseSchema.CurrentVersion"/> — with
    /// record counts matching an independently captured pre-operation raw-SQL row count (never the same
    /// production mapper/validator used by the service under test) and zero target-row mutation.
    /// </summary>
    [TestMethod]
    public async Task MergeSafetyCopyService_ValidCurrentSchemaTarget_CreatesAndValidatesSafetyCopy()
    {
        await using var targetDb = new IsolatedCurrentSchemaDatabase();
        await targetDb.InitializeAsync();

        const string documentContent = "The house on the hill.";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(documentContent))).ToLowerInvariant();
        await targetDb.RunInTransactionAsync(connection =>
        {
            connection.Insert(new DocumentEntity
            {
                Title = "Doc",
                TextLanguage = "en",
                ExplanationLanguage = "de",
                LookupMode = LexicalLookupMode.Definition,
                Content = documentContent,
                ContentFingerprint = fingerprint,
                ImportedAt = DateTime.UtcNow,
                WordCount = 5
            });
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = "house",
                NormalizedTerm = "house",
                Status = WordStatus.Unreviewed,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Unprepared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        });

        // Independent oracle: raw row counts, queried directly by SQL — never through a production shape
        // validator or the production mapper that the service under test also uses.
        var documentCountBefore = await targetDb.ReadAsync(
            connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Documents"));
        var wordCountBefore = await targetDb.ReadAsync(
            connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(1, documentCountBefore, "Fixture setup expects exactly one target document.");
        Assert.AreEqual(1, wordCountBefore, "Fixture setup expects exactly one target word.");

        var service = new MergeSafetyCopyService(targetDb, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        var result = await service.CreateSafetyCopyAsync("schema9-runtime-compat-test", CancellationToken.None);

        Assert.AreEqual(MergeSafetyCopyStatus.Success, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.IsNotNull(result.ArchivePath);
        Assert.IsTrue(File.Exists(result.ArchivePath));

        var expectedRoot = Path.Combine(Path.GetDirectoryName(targetDb.DatabasePath)!, MergeSafetyCopyService.DirectoryName);
        StringAssert.StartsWith(result.ArchivePath!, expectedRoot);

        await using (var readStream = new FileStream(result.ArchivePath!, FileMode.Open, FileAccess.Read))
        {
            var validated = await BackupArchiveReader.ValidateVersionedAsync(readStream, CancellationToken.None);
            Assert.AreEqual(3, validated.FormatVersion);
            Assert.IsNotNull(validated.V3);
            Assert.AreEqual(13, validated.V3!.Manifest.SourceDatabaseSchemaVersion);
            Assert.AreEqual(1, validated.V3.Payload.SourceMaterials.Count);
            Assert.AreEqual(1, validated.V3.Payload.Vocabulary.Count);
            Assert.IsEmpty(validated.V3.Payload.FsrsCardStates);
            Assert.IsEmpty(validated.V3.Payload.FsrsReviewHistoryEntries);
            Assert.IsEmpty(validated.V3.Payload.WordLearningControls);
            Assert.IsEmpty(validated.V3.Payload.SenseLearningControls);
        }

        Assert.IsNotNull(result.ValidatedManifest);
        Assert.AreEqual(DatabaseSchema.CurrentVersion, result.ValidatedManifest!.SourceDatabaseSchemaVersion);
        Assert.IsNotNull(result.RecordCounts);
        Assert.AreEqual(documentCountBefore, result.RecordCounts!.SourceMaterials);
        Assert.AreEqual(wordCountBefore, result.RecordCounts.VocabularyItems);

        var documentCountAfter = await targetDb.ReadAsync(
            connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Documents"));
        var wordCountAfter = await targetDb.ReadAsync(
            connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(documentCountBefore, documentCountAfter, "No target row changes may occur during safety-copy creation.");
        Assert.AreEqual(wordCountBefore, wordCountAfter, "No target row changes may occur during safety-copy creation.");
    }

    [TestMethod]
    public async Task PreparationServices_ValidSchema9Database_UseMeaningCentricRoute()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        await fixture.InsertWordAsync(
            "unprepared-evidence", status: WordStatus.UnknownBacklog, preparationState: PreparationState.Unprepared);

        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = new PreparationService(database, new NoOpLexicalEnrichmentService(), new FakeClock(DateTime.UtcNow));

        var sessionId = await service.StartAsync(PreparationMethod.Manual, requestedLimit: 5);
        Assert.AreNotEqual(0, sessionId);

        var resultJson = await fixture.Connection.ExecuteScalarAsync<string>(
            "SELECT ResultJson FROM PreparationCandidates WHERE SessionId = ? ORDER BY \"Order\" LIMIT 1", sessionId);

        // Only the Schema-8/meaning-centric StartSchema8 selection path freezes a real EnvelopeV1 payload at
        // StartAsync time; the legacy Schema-7 path leaves ResultJson empty until a lookup result arrives.
        Assert.IsFalse(string.IsNullOrEmpty(resultJson));
        var read = PreparationCandidatePayloadCodec.Read(resultJson);
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, read.Kind);
    }

    [TestMethod]
    public async Task LearningServices_ValidSchema9Database_UseMeaningCentricRoute()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSlice4BoundarySchema8FixtureAsync();
        await Schema9RawFixture.ReplaceLegacyIndexAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM Senses WHERE StableId = ?", Schema8BackupFixtureBuilders.Slice4Boundary.SenseStableId);
        var acceptedOnlyVariantId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM AnswerVariants WHERE StableId = ?",
            Schema8BackupFixtureBuilders.Slice4Boundary.AcceptedOnlyVariantStableId);

        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var clock = new FakeClock(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        var service = new Schema8AnswerAssignmentService(database, clock);

        var result = await service.SetAssignmentAsync(
            senseId,
            CardDirection.TermToMeaning,
            acceptedOnlyVariantId,
            AnswerVariantRequirement.AcceptedOnly,
            isPreferred: false);

        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, result.Requirement);
        Assert.IsFalse(result.IsPreferred);
    }

    [TestMethod]
    public async Task DashboardService_ValidSchema9Database_UsesMeaningCentricRoute()
    {
        await using var fixture = await Schema9RawFixture.CreateValidSchema9FixtureAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var dashboard = new DashboardService(database);

        var statistics = await dashboard.GetStatisticsAsync();

        Assert.IsNotNull(statistics);
        Assert.IsTrue(statistics.PreparedAndLearningWordCount >= 1);
    }

    /// <summary>Never invoked by <see cref="PreparationService.StartAsync"/> — lookup enrichment only runs
    /// for a candidate once it is current, which this focused test never reaches.</summary>
    private sealed class NoOpLexicalEnrichmentService : ILexicalEnrichmentService
    {
        public Task<LexicalResult> EnrichAsync(
            LexicalLookupRequest request,
            string originalDocumentContent,
            string? representativeContext,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by this focused test.");
    }

    private sealed class ForeignKeyViolationRow
    {
        public string Table { get; set; } = string.Empty;
        public int RowId { get; set; }
        public string Parent { get; set; } = string.Empty;
        public int FKId { get; set; }
    }

    /// <summary>
    /// Isolated per-instance current-schema target database, built through real production initialization
    /// (<see cref="DatabaseSchema.InitializeAsync"/> activates a fresh database directly to
    /// <see cref="DatabaseSchema.CurrentVersion"/>). Deliberately not pinned to any one schema number: the
    /// two tests that use it assert current-schema behaviour, while every genuinely pinned raw Schema-9
    /// shape in this class continues to come from <see cref="Schema9RawFixture"/>. Rooted in its own unique
    /// temp directory tree so its sibling "merge-safety-copies" directory can never collide with another
    /// test's directory — the same isolation rationale documented on
    /// <c>BackupServiceImportRoutingTests.IsolatedSchema8Database</c> and
    /// <c>MergeSafetyCopyServiceTests.IsolatedDatabase</c>.
    /// </summary>
    private sealed class IsolatedCurrentSchemaDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _testRoot;
        private SQLiteAsyncConnection? _connection;
        private bool _initialized;

        public IsolatedCurrentSchemaDatabase()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "kf-current-schema-runtime-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            DatabasePath = Path.Combine(_testRoot, "knownfirst.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            await DatabaseSchema.InitializeAsync(_connection);
            _initialized = true;
        }

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                return await operation(_connection!);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ResetAsync() => throw new NotSupportedException("Not used by these tests.");

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
            }

            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }

            _gate.Dispose();
        }
    }
}
