using KnownFirst.Data;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-BACKUP-005A: a database whose <c>PRAGMA user_version</c> is 10 must remain fully operational.
/// Migrating to Schema 10 and then having learning, preparation, export, safety-copy capture or any other
/// established subsystem reject the database purely because the version number changed would be a
/// regression, not an activation.
///
/// <para>Each test asserts that initialization reached version 10 first, so the pre-implementation failure
/// is behavioural rather than an unrecognized-capability exception from a database that never migrated.</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema10CapabilityActivationTests
{
    private const int Schema10 = 10;

    [TestMethod]
    public async Task Schema10Database_ResolvesAllThreeCapabilityFamilies()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var backup = BackupSchemaCapability.Resolve(connection);
            Assert.IsNotNull(backup, "The backup capability family must recognize Schema 10.");

            var learning = LearningSchemaCapability.Resolve(connection);
            Assert.IsNotNull(learning, "The learning capability family must recognize Schema 10.");

            var preparation = PreparationSchemaCapability.Resolve(connection);
            Assert.IsNotNull(preparation, "The preparation capability family must recognize Schema 10.");
        });
    }

    [TestMethod]
    public async Task Schema10Database_SupportsPortableExportAndFullBackupCapture()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var exportEnvelope = BackupSnapshotCapture.CaptureForExport(connection);
            Assert.IsNotNull(exportEnvelope, "Ordinary portable export must keep working on Schema 10.");

            var fullEnvelope = BackupSnapshotCapture.CaptureFullForBackup(connection);
            Assert.IsNotNull(fullEnvelope, "Full/internal capture must keep working on Schema 10.");
        });
    }

    [TestMethod]
    public async Task Schema10Database_WithoutActiveWorkflow_CapturesAMergeSafetyCopy()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateCompletedSessionSchema9FixtureAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var captured = BackupMergeSafetyCopySnapshotCapture.CaptureForMergeSafetyCopy(connection);
            Assert.IsNotInstanceOfType<MergeSafetyCopyCaptureBlocked>(
                captured,
                "A Schema-10 target with no active workflow must still produce a merge safety copy.");
        });
    }

    /// <summary>
    /// Characterization: the fail-closed merge-safety-copy block is unchanged by Schema-10 activation. An
    /// active learning workflow on the target still refuses capture.
    /// </summary>
    [TestMethod]
    public async Task Characterization_Schema10Database_WithActiveLearningWorkflow_StillBlocksMergeSafetyCopy()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var captured = BackupMergeSafetyCopySnapshotCapture.CaptureForMergeSafetyCopy(connection);
            Assert.IsInstanceOfType<MergeSafetyCopyCaptureBlocked>(
                captured,
                "Merge-safety-copy blocking on an active target workflow must remain unchanged by KF-BACKUP-005A.");
        });
    }

    /// <summary>
    /// Characterization: ordinary portable export still excludes an Active LearningSession. Making active
    /// sessions portable is KF-BACKUP-005B, deliberately not this package.
    /// </summary>
    [TestMethod]
    public async Task Characterization_OrdinaryPortableExport_StillExcludesActiveLearningSessions()
    {
        await using var fixture = await Schema10LegacyLearningFixtures.CreateActiveSessionSchema9FixtureAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        Assert.AreEqual(Schema10, await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var snapshot = KnownFirst.Data.Schema8.Schema8BackupSnapshotRepository.CapturePortableSnapshot(connection);
            Assert.IsEmpty(
                snapshot.LearningSessions,
                "Ordinary portable export must still exclude Active LearningSessions until KF-BACKUP-005B.");
            Assert.IsEmpty(
                snapshot.LearningSessionCards,
                "Queue rows of an excluded Active session must remain excluded.");
        });
    }
}
