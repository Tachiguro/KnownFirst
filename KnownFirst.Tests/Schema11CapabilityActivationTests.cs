using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Migrations.Schema10;
using KnownFirst.Data.Migrations.Schema11;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Study;
using KnownFirst.Services.Lexical;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Schema11CapabilityActivationTests
{
    private const int Schema11 = 11;

    private static async Task<Schema7Fixture> CreateSchema11EmptyFixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        await Schema9DormantMigration.ApplyAsync(fixture.Connection);
        await Schema10DormantMigration.ApplyAsync(fixture.Connection);
        await Schema11DormantMigration.ApplyAsync(fixture.Connection);
        return fixture;
    }

    private static async Task MigrateToSchema11Async(Schema7Fixture fixture)
    {
        await Schema10DormantMigration.ApplyAsync(fixture.Connection);
        await Schema11DormantMigration.ApplyAsync(fixture.Connection);
    }

    [TestMethod]
    public async Task Schema11Database_ResolvesAllThreeCapabilityFamilies()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var backup = BackupSchemaCapability.Resolve(connection);
            Assert.IsInstanceOfType<Schema11CapabilityResult>(backup, "The backup capability family must recognize Schema 11.");

            var learning = LearningSchemaCapability.Resolve(connection);
            Assert.IsInstanceOfType<LearningSchema11CapabilityResult>(learning, "The learning capability family must recognize Schema 11.");

            var preparation = PreparationSchemaCapability.Resolve(connection);
            Assert.IsInstanceOfType<PreparationSchema11CapabilityResult>(preparation, "The preparation capability family must recognize Schema 11.");
        });
    }

    [TestMethod]
    public async Task Schema11Database_SupportsPortableExportAndFullBackupCapture()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var exportEnvelope = BackupSnapshotCapture.CaptureForExport(connection);
            Assert.IsInstanceOfType<CapturedSchema11SnapshotEnvelope>(exportEnvelope, "Ordinary portable export must produce Schema-11 envelope on Schema 11.");

            var fullEnvelope = BackupSnapshotCapture.CaptureFullForBackup(connection);
            Assert.IsInstanceOfType<CapturedSchema11SnapshotEnvelope>(fullEnvelope, "Full/internal capture must produce Schema-11 envelope on Schema 11.");
        });
    }

    [TestMethod]
    public async Task Schema11Database_OrdinaryPortableExport_PreservesActiveLearningSession()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var captured = BackupSnapshotCapture.CaptureForExport(connection);
            Assert.IsInstanceOfType<CapturedSchema11SnapshotEnvelope>(captured);
            var envelope = (CapturedSchema11SnapshotEnvelope)captured;
            Assert.HasCount(1, envelope.Snapshot.LearningSessions, "Schema 11 ordinary portable export must preserve active learning session under KF-BACKUP-005B semantics.");
            Assert.AreEqual(LearningSessionStatus.Active, envelope.Snapshot.LearningSessions[0].Status);
        });
    }

    [TestMethod]
    public async Task Schema11Database_WithoutActiveWorkflow_CapturesAMergeSafetyCopy()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var captured = BackupMergeSafetyCopySnapshotCapture.CaptureForMergeSafetyCopy(connection);
            Assert.IsInstanceOfType<MergeSafetyCopySchema11Captured>(
                captured,
                "A Schema-11 target with no active workflow must produce a Schema-11 merge safety copy.");
        });
    }

    [TestMethod]
    public async Task Characterization_Schema11Database_WithActiveLearningWorkflow_StillBlocksMergeSafetyCopy()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        Assert.AreEqual(Schema11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var captured = BackupMergeSafetyCopySnapshotCapture.CaptureForMergeSafetyCopy(connection);
            Assert.IsInstanceOfType<MergeSafetyCopyCaptureBlocked>(
                captured,
                "Merge-safety-copy blocking on an active target workflow must remain enforced on Schema 11.");
        });
    }

    [TestMethod]
    public async Task Schema11Database_MalformedShape_FailsClosedAcrossAllThreeCapabilities()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        // Drop the Schema-11 table while user_version remains 11
        await fixture.Connection.ExecuteAsync("DROP TABLE DerivedTermEvidenceEntries");

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var backupEx = Assert.ThrowsExactly<BackupSchemaCapabilityException>(
                () => BackupSchemaCapability.Resolve(connection));
            Assert.IsTrue(backupEx.ShapeMismatch);

            var prepEx = Assert.ThrowsExactly<PreparationSchemaCapabilityException>(
                () => PreparationSchemaCapability.Resolve(connection));
            Assert.IsTrue(prepEx.ShapeMismatch);

            var learnEx = Assert.ThrowsExactly<LearningSchemaCapabilityException>(
                () => LearningSchemaCapability.Resolve(connection));
            Assert.IsTrue(learnEx.ShapeMismatch);
        });
    }

    [TestMethod]
    public async Task Schema11Database_PRAGMA13_ThrowsUnsupportedVersion()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 13");

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var backupEx = Assert.ThrowsExactly<BackupSchemaCapabilityException>(
                () => BackupSchemaCapability.Resolve(connection));
            Assert.IsFalse(backupEx.ShapeMismatch);
            Assert.AreEqual(13, backupEx.FoundVersion);

            var prepEx = Assert.ThrowsExactly<PreparationSchemaCapabilityException>(
                () => PreparationSchemaCapability.Resolve(connection));
            Assert.IsFalse(prepEx.ShapeMismatch);
            Assert.AreEqual(13, prepEx.FoundVersion);

            var learnEx = Assert.ThrowsExactly<LearningSchemaCapabilityException>(
                () => LearningSchemaCapability.Resolve(connection));
            Assert.IsFalse(learnEx.ShapeMismatch);
            Assert.AreEqual(13, learnEx.FoundVersion);
        });
    }

    [TestMethod]
    public async Task Schema11Database_PreviewPortableImport_EmptyTargetRoutesToRestore()
    {
        await using var fixture = await CreateSchema11EmptyFixtureAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var backupService = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var archiveStream = await CreateValidArchiveV2StreamAsync();
        var preview = await backupService.PreviewPortableImportAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(PortableImportPreviewDisposition.RestoreIntoEmpty, preview.Disposition);
    }

    [TestMethod]
    public async Task Schema11Database_PreviewPortableImport_PopulatedTargetRoutesToMergePreflight()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var backupService = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var archiveStream = await CreateValidArchiveV2StreamAsync();
        var preview = await backupService.PreviewPortableImportAsync(archiveStream, CancellationToken.None);

        Assert.AreNotEqual(PortableImportPreviewDisposition.Failed, preview.Disposition);
        Assert.AreNotEqual(PortableImportPreviewDisposition.ValidationFailed, preview.Disposition);
    }

    [TestMethod]
    public async Task Schema11Database_ImportPortableArchive_EmptyTargetRestoresSuccessfully()
    {
        await using var fixture = await CreateSchema11EmptyFixtureAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var backupService = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var archiveStream = await CreateValidArchiveV2StreamAsync();
        var result = await backupService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, result.Summary?.Disposition);
    }

    [TestMethod]
    public async Task Schema11Database_EmptyRestore_PreservesSchema11VersionAndEvidenceTable()
    {
        await using var fixture = await CreateSchema11EmptyFixtureAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var backupService = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var archiveStream = await CreateValidArchiveV2StreamAsync();
        var result = await backupService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(11, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
        var tableExists = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DerivedTermEvidenceEntries'") > 0;
        Assert.IsTrue(tableExists, "DerivedTermEvidenceEntries table must still exist after restore into empty Schema 11.");
    }

    [TestMethod]
    public async Task Schema11Database_EmptyRestore_LeavesDerivedEvidenceEmptyWhenArchiveHasNoActiveVocabularyReview()
    {
        await using var fixture = await CreateSchema11EmptyFixtureAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var backupService = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var archiveStream = await CreateValidArchiveV2StreamAsync();
        await backupService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);

        var evidenceCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM DerivedTermEvidenceEntries");
        Assert.AreEqual(0, evidenceCount, "DerivedTermEvidenceEntries must be 0 when archive has no active vocabulary review.");
    }

    [TestMethod]
    public async Task Schema11Database_PreparationWorkflow_Succeeds()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var prepService = new PreparationService(database, new NoOpLexicalEnrichmentService(), new FakeClock(DateTime.UtcNow));

        var overview = await prepService.GetOverviewAsync();
        Assert.IsNotNull(overview);
    }

    [TestMethod]
    public async Task Schema11Database_DashboardAndLearningWorkflow_Succeeds()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var dashboardService = new DashboardService(database);
        var learningService = new LearningService(
            database, new SimpleSpacedRepetitionScheduler(), new SpellingAnswerComparer(), new FakeClock(DateTime.UtcNow));

        var statistics = await dashboardService.GetStatisticsAsync();
        Assert.IsNotNull(statistics);

        var loadResult = await learningService.GetOrStartAsync();
        Assert.IsNotNull(loadResult);
    }

    [TestMethod]
    public async Task Schema11Database_GetDiagnosticsAsync_SucceedsViaCapability()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var reviewService = new TextReviewService(
            database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());

        var diagnostics = await reviewService.GetDiagnosticsAsync();
        Assert.IsNotNull(diagnostics);
        Assert.IsNotNull(diagnostics.LearningCards);
    }

    private static async Task<MemoryStream> CreateValidArchiveV2StreamAsync()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await MigrateToSchema11Async(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var backupService = new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        var stream = new MemoryStream();
        await backupService.CreatePortableArchiveAsync(stream, CancellationToken.None);
        stream.Position = 0;
        return stream;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class NoOpLexicalEnrichmentService : ILexicalEnrichmentService
    {
        public Task<LexicalResult> EnrichAsync(
            LexicalLookupRequest request,
            string originalDocumentContent,
            string? representativeContext,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by this focused test.");
    }
}
