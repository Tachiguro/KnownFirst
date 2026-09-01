using System.IO.Compression;
using System.Security.Cryptography;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema13BackupRestoreTests
{
    private static readonly DateTime ReviewTime = new(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
    private const string Timestamp = "2026-08-29T10:00:00.0000000Z";

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-slice3-test";
    }

    private sealed class TemporaryDatabase(Schema7Fixture fixture) : IKnownFirstDatabase, IAsyncDisposable
    {
        public string DatabasePath => fixture.DatabasePath;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation) => operation(fixture.Connection);

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            T? result = default;
            await fixture.Connection.RunInTransactionAsync(connection => result = operation(connection));
            return result!;
        }

        public Task ResetAsync() => Task.CompletedTask;

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation) => RunInTransactionAsync(operation);

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
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
                throw new InvalidOperationException("Injected Slice-3 restore failure.");
            }
        }
    }

    private sealed class RestoredFsrsRow
    {
        public int CardId { get; set; }
        public int State { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
    }

    private sealed class RestoredHistoryRow
    {
        public string StableId { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public int Rating { get; set; }
        public string ReviewedAtUtc { get; set; } = string.Empty;
    }

    private sealed record InteractionFixture(int CardId, int SenseId, int AnswerVariantId);

    [TestMethod]
    public async Task ImportV3_EqualTimestampInteractionReviews_PreservesCausalAutomaticLearningProjection()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        var sourceFixture = await SeedEqualTimestampInteractionSourceAsync(source);
        var sourceState = await ProjectInteractionStateAsync(source, sourceFixture);

        Assert.AreEqual(LearningInteractionMode.Reading, sourceState.InteractionMode);
        Assert.AreEqual(0, sourceState.ConsecutiveRecallSuccesses);
        Assert.AreEqual(0, sourceState.ConsecutiveTypingSuccesses);
        Assert.AreEqual(0, sourceState.ConsecutiveTypingFailures);

        var archiveBytes = await ExportAsync(source);
        var repeatArchiveBytes = await ExportAsync(source);
        var archive = await ValidateV3Async(archiveBytes);
        var repeatArchive = await ValidateV3Async(repeatArchiveBytes);
        CollectionAssert.AreEqual(
            new[] { "learning-review-causal-order-v1" },
            archive.Manifest.RequiredFeatures.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                BackupReviewRating.Good,
                BackupReviewRating.Good,
                BackupReviewRating.Again,
                BackupReviewRating.Again
            },
            archive.Payload.Learning.ReviewEvents.Select(review => review.Rating).ToArray());
        CollectionAssert.AreEqual(
            BackupJsonCodecV3.SerializeData(archive.Payload),
            BackupJsonCodecV3.SerializeData(repeatArchive.Payload));

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var targetFixture = await target.RunInTransactionAsync(connection => new InteractionFixture(
            connection.ExecuteScalar<int>(
                "SELECT c.Id FROM LearningCards c JOIN Senses s ON s.Id = c.SenseId WHERE s.StableId = 'sense-causal-order'"),
            connection.ExecuteScalar<int>("SELECT Id FROM Senses WHERE StableId = 'sense-causal-order'"),
            connection.ExecuteScalar<int>("SELECT Id FROM AnswerVariants WHERE StableId = '0123456789abcdef0123456789abcdea'")));
        var restoredState = await ProjectInteractionStateAsync(target, targetFixture);

        Assert.AreEqual(sourceState, restoredState);

        await using var populatedTarget = await CreateEmptySchema13DatabaseAsync();
        await populatedTarget.RunInTransactionAsync(connection => InsertWord(connection, "target-only-causal"));
        var populatedService = new BackupService(populatedTarget, new FakePlatformInfo());
        var merged = await populatedService.ImportPortableArchiveAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, merged.Status, merged.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, merged.Summary?.Disposition);

        var mergedFixture = await ResolveInteractionFixtureAsync(populatedTarget);
        Assert.AreEqual(sourceState, await ProjectInteractionStateAsync(populatedTarget, mergedFixture));

        var repeatedMerge = await populatedService.ImportPortableArchiveAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, repeatedMerge.Status, repeatedMerge.ErrorCode);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, repeatedMerge.Summary?.Disposition);
        Assert.AreEqual(sourceState, await ProjectInteractionStateAsync(populatedTarget, mergedFixture));
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    public async Task ImportLegacy_EqualTimestampSameCardReviews_IntoSchema13_FailsBeforeMutation(
        int formatVersion,
        bool populatedTarget)
    {
        var archiveBytes = await CreateAmbiguousLegacyArchiveAsync(formatVersion);
        await using var target = await CreateEmptySchema13DatabaseAsync();
        if (populatedTarget)
        {
            await target.RunInTransactionAsync(connection => InsertWord(connection, "legacy-target-only"));
        }

        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, result.ErrorCode);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(populatedTarget ? 1 : 0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Words"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
            return true;
        });
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ImportLegacy_EqualTimestampSameCardReviews_IntoSchema12_RemainsAccepted(int formatVersion)
    {
        var archiveBytes = await CreateAmbiguousLegacyArchiveAsync(formatVersion);
        await using var target = await CreateEmptySchema12DatabaseAsync();

        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status, result.ErrorCode);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(12, connection.ExecuteScalar<int>("PRAGMA user_version"));
            Assert.AreEqual(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews"));
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV3_IntoEmptySchema13_RestoresExactNativeStateAndHistory()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);
        var archive = await ValidateV3Async(archiveBytes);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var expectedHistory = archive.Payload.FsrsReviewHistoryEntries
            .OrderBy(entry => entry.SequenceNumber)
            .ToList();
        var expectedState = archive.Payload.FsrsCardStates.Single(state => state.CardId == expectedHistory[0].CardId);

        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Words"));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses"));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards"));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
            Assert.AreEqual(
                Timestamp,
                connection.ExecuteScalar<string>(
                    """
                    SELECT c.DecidedAtUtc
                    FROM WordLearningControls c
                    JOIN Words w ON w.Id = c.WordId
                    WHERE w.CanonicalTerm = 'banana'
                    """));
            Assert.AreEqual(
                Timestamp,
                connection.ExecuteScalar<string>(
                    """
                    SELECT c.DecidedAtUtc
                    FROM SenseLearningControls c
                    JOIN Senses s ON s.Id = c.SenseId
                    WHERE s.StableId = 'sense-apple-2'
                    """));
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews"));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));

            var state = connection.Query<RestoredFsrsRow>(
                """
                SELECT f.CardId, f.State, f.Stability, f.Difficulty, f.LastReviewedAtUtc, f.StepIndex, f.DueAtUtc
                FROM FsrsCardStates f
                JOIN LearningCards c ON c.Id = f.CardId
                JOIN Senses s ON s.Id = c.SenseId
                WHERE s.StableId = 'sense-apple-1' AND c.Direction = 0
                """).Single();

            Assert.AreEqual((int)expectedState.State, state.State);
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(expectedState.Stability!.Value),
                BitConverter.DoubleToInt64Bits(state.Stability!.Value));
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(expectedState.Difficulty!.Value),
                BitConverter.DoubleToInt64Bits(state.Difficulty!.Value));
            Assert.AreEqual(Schema13TimestampCodec.FormatUtc(expectedState.LastReviewedAtUtc!.Value), state.LastReviewedAtUtc);
            Assert.AreEqual(expectedState.StepIndex, state.StepIndex);
            Assert.AreEqual(Schema13TimestampCodec.FormatUtc(expectedState.DueAtUtc!.Value), state.DueAtUtc);

            var history = connection.Query<RestoredHistoryRow>(
                "SELECT StableId, SequenceNumber, Rating, ReviewedAtUtc FROM FsrsReviewHistoryEntries WHERE CardId = ? ORDER BY SequenceNumber",
                state.CardId);
            Assert.AreEqual(2, history.Count);
            for (var index = 0; index < history.Count; index++)
            {
                Assert.AreEqual(expectedHistory[index].StableId, history[index].StableId);
                Assert.AreEqual(expectedHistory[index].SequenceNumber, history[index].SequenceNumber);
                Assert.AreEqual((int)BackupEnumMappings.ToPersistence(expectedHistory[index].Rating), history[index].Rating);
                Assert.AreEqual(Schema13TimestampCodec.FormatUtc(expectedHistory[index].ReviewedAtUtc), history[index].ReviewedAtUtc);
            }

            Assert.AreEqual(history[0].ReviewedAtUtc, history[1].ReviewedAtUtc);
            return true;
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ImportV3_IntoEmptySchema13_NearEqualReplayStateIsRejectedAndTargetRemainsEmpty(
        bool mutateStability)
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);
        var invalidArchiveBytes = await RewriteV3ArchiveAsync(
            archiveBytes,
            payload => payload with
            {
                FsrsCardStates = payload.FsrsCardStates
                    .Select(state => state.Stability.HasValue && state.Difficulty.HasValue
                        ? state with
                        {
                            Stability = mutateStability
                                ? Math.BitIncrement(state.Stability.Value)
                                : state.Stability,
                            Difficulty = mutateStability
                                ? state.Difficulty
                                : Math.BitIncrement(state.Difficulty.Value)
                        }
                        : state)
                    .ToList()
            });

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(invalidArchiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, result.ErrorCode);
        await AssertSchema13TargetEmptyAsync(target);
    }

    [TestMethod]
    public async Task ImportV2_IntoEmptySchema13_MatchesExistingBootstrapOracle()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        await sourceFixture.Connection.ExecuteAsync(
            "UPDATE Words SET Status = 1, UpdatedAt = ? WHERE CanonicalTerm = 'light'",
            ReviewTime);
        var source = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.IsTrue(Schema13MigrationIntegrityValidator.Validate(connection, out var failure), failure);
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
            Assert.AreEqual(
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV1_IntoEmptySchema13_MatchesExistingBootstrapOracleAndExactWordControl()
    {
        var sourceFixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        await using var source = new TemporaryDatabase(sourceFixture);
        await sourceFixture.Connection.ExecuteAsync(
            "UPDATE Words SET Status = 1, UpdatedAt = ? WHERE CanonicalTerm = 'light'",
            ReviewTime);
        var archiveBytes = await ExportAsync(source);
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        Assert.AreEqual(1, envelope.FormatVersion);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.IsTrue(Schema13MigrationIntegrityValidator.Validate(connection, out var failure), failure);
            Assert.AreEqual(
                Timestamp,
                connection.ExecuteScalar<string>(
                    """
                    SELECT c.DecidedAtUtc
                    FROM WordLearningControls c
                    JOIN Words w ON w.Id = c.WordId
                    WHERE w.CanonicalTerm = 'light'
                    """));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV2_PreservesSenseAndAnswerVariantDomainSeparation()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var source = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var archiveBytes = await ExportAsync(source);
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        var payload = envelope.V2!.Payload;

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(payload.Senses.Count, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses"));
            Assert.AreEqual(payload.AnswerVariants.Count, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerVariants"));
            Assert.AreEqual(payload.Learning.Cards.Count, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards"));
            Assert.AreEqual(
                payload.Senses.Count(sense => payload.Vocabulary.Single(word => word.Id == sense.VocabularyId).CanonicalTerm == "light"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Senses s JOIN Words w ON w.Id = s.WordId WHERE w.CanonicalTerm = 'light'"));
            Assert.AreEqual(
                payload.SenseAnswerVariantAssignments.Count,
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
            Assert.AreEqual(
                0,
                connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(*)
                    FROM SenseAnswerVariantAssignments a
                    JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
                    WHERE v.SenseId <> a.SenseId
                    """),
                "Every AnswerVariant remains owned by its Sense; it never becomes a learning object.");
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV2_ProgressedCardWithoutFactualHistory_FailsClosedAndRollsBack()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        await sourceFixture.Connection.ExecuteAsync("DELETE FROM LearningReviews");
        var source = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        await AssertSchema13TargetEmptyAsync(target);
    }

    [TestMethod]
    public async Task ImportV3_IntoSchema12_FailsBeforeMutationAndKeepsProductionVersion()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema12DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.Schema13ArchiveIncompatibleWithLegacyTarget, result.ErrorCode);
        Assert.AreEqual(13, DatabaseSchema.CurrentVersion);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(12, connection.ExecuteScalar<int>("PRAGMA user_version"));
            Assert.IsFalse(Schema8BackupImportRepository.HasDurableUserData(connection));
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV3_WithAmbiguousSiblingSenses_IntoPopulatedSchema13_FailsClosedWithoutMutation()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        await target.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "target-only");
            return true;
        });

        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(BackupErrorCodes.DuplicateId, result.ErrorCode);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Words"));
            Assert.AreEqual("target-only", connection.ExecuteScalar<string>("SELECT CanonicalTerm FROM Words"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV3_InvalidTargetShape_FailsBeforeMutation()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        await target.RunInTransactionAsync(connection =>
        {
            connection.Execute($"DROP INDEX {Schema13Ddl.FsrsReviewHistoryEntriesReplayIndexName}");
            return true;
        });

        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Words"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));
            return true;
        });
    }

    [TestMethod]
    public async Task ImportV3_ChecksumFailure_IsRejectedBeforeTargetMutationAndDoesNotAlterSourceBytes()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);
        var originalBytes = archiveBytes.ToArray();
        var corruptBytes = CorruptDataJsonWithoutUpdatingChecksum(archiveBytes);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var result = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(corruptBytes), CancellationToken.None);

        Assert.AreNotEqual(PortableImportStatus.Success, result.Status);
        CollectionAssert.AreEqual(originalBytes, archiveBytes);
        await AssertSchema13TargetEmptyAsync(target);
    }

    [TestMethod]
    public async Task ImportV3_WhenBaseGraphCheckpointFails_RollsBackAndIdenticalRetrySucceeds()
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var failingService = new BackupService(
            target,
            new FakePlatformInfo(),
            new ThrowAtCheckpoint(Schema8BackupImportRepository.Checkpoints.BeforeCompletion));

        var failed = await failingService.ImportPortableArchiveAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, failed.Status);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.IsFalse(Schema8BackupImportRepository.HasDurableUserData(connection));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));
            return true;
        });

        var retried = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, retried.Status);
        await target.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Words"));
            Assert.AreEqual(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));
            return true;
        });
    }

    [TestMethod]
    [DataRow(Schema13BackupImportRepository.Checkpoints.AfterBaseGraph)]
    [DataRow(Schema13BackupImportRepository.Checkpoints.DuringFsrsReviewHistoryInsertion)]
    [DataRow(Schema13BackupImportRepository.Checkpoints.DuringFsrsCardStateInsertion)]
    [DataRow(Schema13BackupImportRepository.Checkpoints.BeforeFinalIntegrityValidation)]
    public async Task ImportV3_NewWriteCheckpointFailure_RollsBackAndRetryIsDeterministic(string checkpoint)
    {
        await using var source = await CreateEmptySchema13DatabaseAsync();
        await SeedNativeV3SourceAsync(source);
        var archiveBytes = await ExportAsync(source);

        await using var target = await CreateEmptySchema13DatabaseAsync();
        var failed = await new BackupService(
                target,
                new FakePlatformInfo(),
                new ThrowAtCheckpoint(checkpoint))
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, failed.Status);
        await AssertSchema13TargetEmptyAsync(target);

        var retried = await new BackupService(target, new FakePlatformInfo())
            .ImportPortableArchiveAsync(new MemoryStream(archiveBytes), CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, retried.Status);

        var restoredArchive = await ExportAsync(target);
        var expected = await ValidateV3Async(archiveBytes);
        var actual = await ValidateV3Async(restoredArchive);
        CollectionAssert.AreEqual(
            BackupJsonCodecV3.SerializeData(expected.Payload),
            BackupJsonCodecV3.SerializeData(actual.Payload));
    }

    private static async Task<byte[]> CreateAmbiguousLegacyArchiveAsync(int formatVersion)
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        if (formatVersion == 2)
        {
            await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        }

        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningReviews
                (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc,
                 IntervalDays, EaseFactor)
            SELECT CardId, SessionId, 0, 1, 0, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor
            FROM LearningReviews
            ORDER BY Id
            LIMIT 1
            """);

        var source = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var archiveBytes = await ExportAsync(source);
        var validated = await BackupArchiveReader.ValidateVersionedAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        Assert.AreEqual(formatVersion, validated.FormatVersion);
        return archiveBytes;
    }

    private static async Task<InteractionFixture> ResolveInteractionFixtureAsync(IKnownFirstDatabase database) =>
        await database.RunInTransactionAsync(connection => new InteractionFixture(
            connection.ExecuteScalar<int>(
                "SELECT c.Id FROM LearningCards c JOIN Senses s ON s.Id = c.SenseId WHERE s.StableId = 'sense-causal-order'"),
            connection.ExecuteScalar<int>("SELECT Id FROM Senses WHERE StableId = 'sense-causal-order'"),
            connection.ExecuteScalar<int>("SELECT Id FROM AnswerVariants WHERE StableId = '0123456789abcdef0123456789abcdea'")));

    private static async Task<TemporaryDatabase> CreateEmptySchema13DatabaseAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema13Async(fixture.Connection);
        return new TemporaryDatabase(fixture);
    }

    private static async Task<TemporaryDatabase> CreateEmptySchema12DatabaseAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(fixture.Connection);
        return new TemporaryDatabase(fixture);
    }

    private static async Task AssertSchema13TargetEmptyAsync(IKnownFirstDatabase target)
    {
        await target.RunInTransactionAsync(connection =>
        {
            Assert.IsFalse(Schema13BackupImportRepository.HasDurableUserData(connection));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseLearningControls"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsReviewHistoryEntries"));
            Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FsrsCardStates"));
            return true;
        });
    }

    private static async Task<byte[]> ExportAsync(IKnownFirstDatabase database)
    {
        using var stream = new MemoryStream();
        await new BackupService(database, new FakePlatformInfo())
            .CreatePortableArchiveAsync(stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<ValidatedBackupArchiveV3> ValidateV3Async(byte[] archiveBytes)
    {
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(
            new MemoryStream(archiveBytes),
            CancellationToken.None);
        Assert.AreEqual(3, envelope.FormatVersion);
        return envelope.V3!;
    }

    private static byte[] CorruptDataJsonWithoutUpdatingChecksum(byte[] archiveBytes)
    {
        byte[] manifestBytes;
        byte[] dataBytes;
        using (var input = new MemoryStream(archiveBytes, writable: false))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
        {
            using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            using var manifestCopy = new MemoryStream();
            manifestStream.CopyTo(manifestCopy);
            manifestBytes = manifestCopy.ToArray();

            using var dataStream = archive.GetEntry("data.json")!.Open();
            using var dataCopy = new MemoryStream();
            dataStream.CopyTo(dataCopy);
            dataBytes = dataCopy.ToArray();
        }

        dataBytes[^2] ^= 0x01;
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifest = archive.CreateEntry("manifest.json").Open())
            {
                manifest.Write(manifestBytes);
            }

            using (var data = archive.CreateEntry("data.json").Open())
            {
                data.Write(dataBytes);
            }
        }

        return output.ToArray();
    }

    private static async Task<byte[]> RewriteV3ArchiveAsync(
        byte[] archiveBytes,
        Func<BackupPayloadV3, BackupPayloadV3> payloadMutator)
    {
        var validated = await ValidateV3Async(archiveBytes);
        var payload = payloadMutator(validated.Payload);
        var dataBytes = BackupJsonCodecV3.SerializeData(payload);
        var checksum = "sha256:" + Convert.ToHexString(SHA256.HashData(dataBytes)).ToLowerInvariant();
        var manifestBytes = BackupJsonCodecV3.SerializeManifest(
            validated.Manifest with { DataChecksum = checksum });

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open())
            {
                manifest.Write(manifestBytes);
            }

            using (var data = archive.CreateEntry("data.json", CompressionLevel.Optimal).Open())
            {
                data.Write(dataBytes);
            }
        }

        return output.ToArray();
    }

    private static async Task SeedNativeV3SourceAsync(IKnownFirstDatabase database)
    {
        await database.RunInTransactionAsync(connection =>
        {
            var word1Id = InsertWord(connection, "apple");
            var word2Id = InsertWord(connection, "banana");
            var sense1Id = InsertSense(connection, word1Id, "sense-apple-1");
            var sense2Id = InsertSense(connection, word1Id, "sense-apple-2");
            var meaning1Id = InsertMeaning(connection, word1Id, sense1Id, "meaning-apple-1", "fruit");
            var meaning2Id = InsertMeaning(connection, word1Id, sense2Id, "meaning-apple-2", "tree");
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning1Id, sense1Id);
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning2Id, sense2Id);

            var card1Id = InsertCard(connection, word1Id, sense1Id, meaning1Id, direction: 0);
            var card2Id = InsertCard(connection, word1Id, sense1Id, meaning1Id, direction: 1);

            connection.Execute("INSERT INTO WordLearningControls (WordId, DecidedAtUtc) VALUES (?, ?)", word2Id, Timestamp);
            connection.Execute("INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc) VALUES (?, ?)", sense2Id, Timestamp);
            connection.Execute(
                "INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('stable-review-1', ?, 1, 1, ?)",
                card1Id,
                Timestamp);
            connection.Execute(
                "INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc) VALUES ('stable-review-2', ?, 2, 2, ?)",
                card1Id,
                Timestamp);

            var replayed = new Fsrs6Replayer().Replay(
                Fsrs6Card.New(),
                [
                    new Fsrs6ReviewEvent(new DateTimeOffset(ReviewTime, TimeSpan.Zero), ReviewRating.Hard),
                    new Fsrs6ReviewEvent(new DateTimeOffset(ReviewTime, TimeSpan.Zero), ReviewRating.Good)
                ]);
            connection.Execute(
                """
                INSERT INTO FsrsCardStates
                    (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                card1Id,
                (int)replayed.State,
                replayed.Stability,
                replayed.Difficulty,
                Schema13TimestampCodec.FormatUtc(replayed.LastReviewedAtUtc!.Value),
                replayed.StepIndex,
                Schema13TimestampCodec.FormatUtc(replayed.DueAtUtc!.Value));
            connection.Execute(
                "INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc) VALUES (?, 0, NULL, NULL, NULL, NULL, NULL)",
                card2Id);

            connection.Execute(
                """
                INSERT INTO LearningSessions
                    (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                     StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('0123456789abcdef0123456789abcdef', 1, 1, 1, 0, 0, 1, 0, ?, ?, ?)
                """,
                ReviewTime.AddMinutes(-10),
                ReviewTime.AddMinutes(-5),
                ReviewTime.AddMinutes(-5));
            var sessionId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                """
                INSERT INTO LearningSessionCards
                    (StableId, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed,
                     SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
                VALUES ('fedcba9876543210fedcba9876543210', ?, ?, 0, 1, 0, 1, 0, 0, 1, 2, ?, NULL)
                """,
                sessionId,
                card1Id,
                ReviewTime.AddMinutes(-5));
            connection.Execute(
                """
                INSERT INTO LearningReviews
                    (SessionId, CardId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc,
                     IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId)
                VALUES (?, ?, 2, 0, 1, ?, ?, 1, 2.5, NULL, NULL)
                """,
                sessionId,
                card1Id,
                ReviewTime.AddMinutes(-5),
                ReviewTime.AddDays(1));
            return true;
        });
    }

    private static async Task<InteractionFixture> SeedEqualTimestampInteractionSourceAsync(
        IKnownFirstDatabase database)
    {
        return await database.RunInTransactionAsync(connection =>
        {
            var wordId = InsertWord(connection, "causal");
            var senseId = InsertSense(connection, wordId, "sense-causal-order");
            var meaningId = InsertMeaning(connection, wordId, senseId, "meaning-causal-order", "ordered");
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);
            var cardId = InsertCard(connection, wordId, senseId, meaningId, direction: 0);

            connection.Execute(
                """
                INSERT INTO AnswerVariants
                    (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES ('0123456789abcdef0123456789abcdea', ?, 'en', 'ordered', 'ordered', ?, ?, ?)
                """,
                senseId,
                meaningId,
                ReviewTime.AddDays(-1),
                ReviewTime.AddDays(-1));
            var answerVariantId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                """
                INSERT INTO SenseAnswerVariantAssignments
                    (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                     RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('0123456789abcdef0123456789abcdeb', ?, 0, ?, 0, 1, ?, ?, ?)
                """,
                senseId,
                answerVariantId,
                ReviewTime.AddDays(-1),
                ReviewTime.AddDays(-1),
                ReviewTime.AddDays(-1));

            connection.Execute(
                """
                INSERT INTO LearningSessions
                    (StableId, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount,
                     StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
                VALUES ('0123456789abcdef0123456789abcdec', 1, 4, 4, 2, 0, 2, 0, ?, ?, ?)
                """,
                ReviewTime.AddMinutes(-10),
                ReviewTime,
                ReviewTime);
            var sessionId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            var reviews = new[]
            {
                (Rating: ReviewRating.Good, WasTyped: false, WasCorrect: true),
                (Rating: ReviewRating.Good, WasTyped: false, WasCorrect: true),
                (Rating: ReviewRating.Again, WasTyped: true, WasCorrect: false),
                (Rating: ReviewRating.Again, WasTyped: true, WasCorrect: false)
            };
            for (var index = 0; index < reviews.Length; index++)
            {
                var review = reviews[index];
                connection.Execute(
                    """
                    INSERT INTO LearningSessionCards
                        (StableId, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed,
                         SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc,
                         TargetAnswerVariantId)
                    VALUES (?, ?, ?, ?, 1, ?, 1, ?, ?, 1, ?, ?, ?)
                    """,
                    $"0123456789abcdef0123456789abcd{index + 1:D2}",
                    sessionId,
                    cardId,
                    index,
                    index >= 2,
                    review.WasTyped,
                    review.WasCorrect,
                    (int)review.Rating,
                    ReviewTime,
                    answerVariantId);
                connection.Execute(
                    """
                    INSERT INTO LearningReviews
                        (SessionId, CardId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc,
                         IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId)
                    VALUES (?, ?, ?, ?, ?, ?, ?, 1, 2.5, ?, NULL)
                    """,
                    sessionId,
                    cardId,
                    (int)review.Rating,
                    review.WasTyped,
                    review.WasCorrect,
                    ReviewTime,
                    ReviewTime.AddDays(1),
                    answerVariantId);
                connection.Execute(
                    """
                    INSERT INTO FsrsReviewHistoryEntries
                        (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                    VALUES (?, ?, ?, ?, ?)
                    """,
                    $"causal-fsrs-{index + 1}",
                    cardId,
                    index + 1,
                    (int)review.Rating,
                    Timestamp);
            }

            var replayed = new Fsrs6Replayer().Replay(
                Fsrs6Card.New(),
                reviews.Select(review => new Fsrs6ReviewEvent(
                    new DateTimeOffset(ReviewTime, TimeSpan.Zero),
                    review.Rating)));
            connection.Execute(
                """
                INSERT INTO FsrsCardStates
                    (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                cardId,
                (int)replayed.State,
                replayed.Stability,
                replayed.Difficulty,
                Schema13TimestampCodec.FormatUtc(replayed.LastReviewedAtUtc!.Value),
                replayed.StepIndex,
                Schema13TimestampCodec.FormatUtc(replayed.DueAtUtc!.Value));

            return new InteractionFixture(cardId, senseId, answerVariantId);
        });
    }

    private static async Task<AutomaticLearningState> ProjectInteractionStateAsync(
        IKnownFirstDatabase database,
        InteractionFixture fixture)
    {
        return await database.RunInTransactionAsync(connection =>
        {
            var assignments = Schema8LearningRepository.LoadAssignmentsForSenseDirection(
                connection,
                fixture.SenseId,
                CardDirection.TermToMeaning);
            var reviews = Schema8LearningRepository.LoadReviewsForCard(connection, fixture.CardId);
            var progress = Schema8LearningRepository.LoadProgressForCard(connection, fixture.CardId);
            return Schema13LearningReviewPolicy.Project(fixture.CardId, assignments, reviews, progress)
                .FindOutcome(fixture.AnswerVariantId)!
                .State;
        });
    }

    private static int InsertWord(SQLiteConnection connection, string term)
    {
        connection.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES ('en', ?, ?, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ?, ?)
            """,
            term,
            term,
            ReviewTime,
            ReviewTime);
        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertSense(SQLiteConnection connection, int wordId, string stableId)
    {
        connection.Execute(
            "INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 'en', 'en', 0, ?, ?)",
            stableId,
            wordId,
            ReviewTime,
            ReviewTime);
        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertMeaning(SQLiteConnection connection, int wordId, int senseId, string stableId, string definition)
    {
        connection.Execute(
            """
            INSERT INTO Meanings
                (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                 EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                 AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                 AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                 Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
            VALUES (?, ?, ?, 'en', 'en', 'apple', 'apple', '', 0, '', '', '', ?, '', '', '[]', ?,
                    'test', '', '', '', 1, ?, ?, ?)
            """,
            wordId,
            senseId,
            stableId,
            definition,
            definition,
            ReviewTime,
            ReviewTime,
            ReviewTime);
        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertCard(SQLiteConnection connection, int wordId, int senseId, int meaningId, int direction)
    {
        connection.Execute(
            """
            INSERT INTO LearningCards
                (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, 0, ?, 0, 2.5, 0, 0, NULL, NULL, ?, ?)
            """,
            wordId,
            senseId,
            meaningId,
            direction,
            ReviewTime,
            ReviewTime,
            ReviewTime);
        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }
}
