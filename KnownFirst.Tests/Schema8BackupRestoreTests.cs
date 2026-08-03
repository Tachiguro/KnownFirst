using System.IO.Compression;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;
using Slice4 = KnownFirst.Tests.Schema8BackupFixtureBuilders.Slice4Boundary;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 2 — Schema-8 empty restore, deterministic v1 upgrade parity, and rollback/
/// failure-injection coverage. Every Schema-8 database is synthetic (<see cref="Schema7Fixture"/> +
/// <see cref="Data.Migrations.Schema8.Schema8DormantMigration.ApplyAsync"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema8BackupRestoreTests
{
    private sealed class CountingFailureInjector : IBackupImportFailureInjector
    {
        public int Count { get; private set; }
        public void AfterMutation(int mutationCount) => Count = mutationCount;
    }

    private sealed class ThrowAtMutationInjector(int throwAtCount) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
            if (mutationCount == throwAtCount)
            {
                throw new InvalidOperationException($"Injected failure at mutation {mutationCount}.");
            }
        }
    }

    /// <summary>Throws the first time the named checkpoint fires — proves an exact, semantically-stable
    /// restore boundary rather than an incidental raw mutation count.</summary>
    private sealed class ThrowAtCheckpointInjector(string checkpointName) : IBackupImportFailureInjector
    {
        private bool _fired;

        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName2)
        {
            if (!_fired && string.Equals(checkpointName2, checkpointName, StringComparison.Ordinal))
            {
                _fired = true;
                throw new InvalidOperationException($"Injected failure at checkpoint '{checkpointName2}'.");
            }
        }
    }

    /// <summary>Records every checkpoint name reached (in order), without throwing — used to prove a
    /// checkpoint actually fires during a normal, uninterrupted restore.</summary>
    private sealed class RecordingCheckpointInjector : IBackupImportFailureInjector
    {
        public List<string> Checkpoints { get; } = [];

        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName) => Checkpoints.Add(checkpointName);
    }

    private static async Task<byte[]> BuildV2ArchiveBytesAsync(Schema7Fixture schema8Fixture)
    {
        Schema8BackupSnapshot? snapshot = null;
        await schema8Fixture.Connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await schema8Fixture.Connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema8CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapperV2.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> BuildV1ArchiveBytesAsync(Schema7Fixture schema7Fixture)
    {
        BackupSnapshot? snapshot = null;
        await schema7Fixture.Connection.RunInTransactionAsync(conn => snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await schema7Fixture.Connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema7CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapper.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    // ---- Core 8: empty restore of v1 and v2 into an empty synthetic Schema-8 database succeeds ----

    [TestMethod]
    public async Task Core8_V2Archive_EmptyRestoreIntoEmptySchema8Database_Succeeds()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var beforeVersion = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, beforeVersion);

        var service = new BackupService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var senseCount = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses");
        Assert.IsGreaterThan(0, senseCount);

        var afterVersion = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, afterVersion);
    }

    [TestMethod]
    public async Task Core8_V1Archive_UpgradesAndEmptyRestoresIntoEmptySchema8Database_Succeeds()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var wordId = await sourceFixture.InsertWordAsync("network");
        var meaningId = await sourceFixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        await sourceFixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        var archiveBytes = await BuildV1ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var service = new BackupService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var senseCount = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses");
        Assert.AreEqual(1, senseCount);
        var cardCount = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards");
        Assert.AreEqual(1, cardCount);

        var afterVersion = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, afterVersion);
    }

    [TestMethod]
    public async Task V2NullableTargets_EmptyRestoreReopensToCurrentSchemaWithoutDataMutation()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        await sourceFixture.Connection.ExecuteAsync("UPDATE LearningReviews SET TargetAnswerVariantId = NULL");
        await sourceFixture.Connection.ExecuteAsync("UPDATE LearningSessionCards SET TargetAnswerVariantId = NULL");
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await RestoreIntoEmptyTargetAsync(targetFixture, archiveBytes);

        Assert.IsGreaterThan(0, await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.IsGreaterThan(0, await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(0, await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews WHERE TargetAnswerVariantId IS NOT NULL"));
        Assert.AreEqual(0, await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards WHERE TargetAnswerVariantId IS NOT NULL"));
        await AssertNormalReopenPreservesCompleteUserDataAsync(targetFixture);
    }

    [TestMethod]
    public async Task V1NullableHistoricalTargets_UpgradeRestoreReopensToCurrentSchemaWithoutDataMutation()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var wordId = await sourceFixture.InsertWordAsync("unattributed");
        var meaningId = await sourceFixture.InsertMeaningAsync(
            wordId,
            displayTerm: "unattributed",
            translation: string.Empty,
            definition: "historical definition without a direction-specific answer");
        var cardId = await sourceFixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning);
        var sessionId = await sourceFixture.InsertLearningSessionAsync();
        await sourceFixture.InsertReviewAsync(cardId, sessionId, wasTypedAnswer: false, wasCorrect: true);
        await sourceFixture.InsertQueueItemAsync(sessionId, cardId, 0, isCompleted: true);
        var archiveBytes = await BuildV1ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await RestoreIntoEmptyTargetAsync(targetFixture, archiveBytes);

        Assert.AreEqual(1, await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews WHERE TargetAnswerVariantId IS NULL"));
        Assert.AreEqual(1, await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards WHERE TargetAnswerVariantId IS NULL"));
        await AssertNormalReopenPreservesCompleteUserDataAsync(targetFixture);
    }

    // ---- KF-MEANING-001 Slice 4: the assignment RequiredSinceUtc boundary through a real empty Schema-8
    // restore. The source fixture (Schema8BackupFixtureBuilders.CreateSlice4BoundarySchema8FixtureAsync) is
    // fully deterministic, so every assertion below is an exact identity/tick comparison rather than a
    // tolerance window, and the graph stays mixed-role: one Required + preferred assignment, one
    // AcceptedOnly assignment in the same direction, and one AcceptedOnly + preferred assignment in the
    // other. ----

    private sealed class RestoredAssignmentRow
    {
        public string StableId { get; set; } = string.Empty;
        public AnswerVariantRequirement Requirement { get; set; }
        public bool IsPreferred { get; set; }
        public DateTime? RequiredSinceUtc { get; set; }
        public CardDirection CardDirection { get; set; }
        public string AnswerVariantStableId { get; set; } = string.Empty;
    }

    private sealed class RestoredProgressRow
    {
        public string AnswerVariantStableId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }

    private static Task<List<RestoredAssignmentRow>> ReadRestoredAssignmentsAsync(Schema7Fixture fixture) =>
        fixture.Connection.QueryAsync<RestoredAssignmentRow>(
            """
            SELECT a.StableId, a.Requirement, a.IsPreferred, a.RequiredSinceUtc, a.CardDirection,
                   v.StableId AS AnswerVariantStableId
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            ORDER BY a.StableId
            """);

    private static Task<List<RestoredProgressRow>> ReadRestoredProgressAsync(Schema7Fixture fixture) =>
        fixture.Connection.QueryAsync<RestoredProgressRow>(
            """
            SELECT v.StableId AS AnswerVariantStableId, p.CreatedAtUtc
            FROM AnswerVariantProgress p JOIN AnswerVariants v ON v.Id = p.AnswerVariantId
            ORDER BY v.StableId
            """);

    /// <summary>sqlite-net returns persisted timestamps with <see cref="DateTimeKind.Unspecified"/>; the
    /// boundary comparisons normalize to UTC first and then compare exact ticks.</summary>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static async Task<byte[]> BuildSlice4BoundaryArchiveBytesAsync()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSlice4BoundarySchema8FixtureAsync();
        return await BuildV2ArchiveBytesAsync(sourceFixture);
    }

    private static async Task RestoreIntoEmptyTargetAsync(Schema7Fixture targetFixture, byte[] archiveBytes)
    {
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
    }

    /// <summary>
    /// The restored database is a valid pinned Schema-8 database. Reopening it through
    /// <see cref="DatabaseSchema.InitializeAsync"/> is ordinary current-schema initialization and legitimately
    /// migrates it to <see cref="DatabaseSchema.CurrentVersion"/> (Schema 9) — so this proves the durable-data
    /// contract (every restored row, keyed by table, survives byte-for-byte) rather than a full schema-level
    /// snapshot, which would wrongly fail on the expected user_version bump and ReviewSessions index migration.
    /// </summary>
    private static async Task AssertNormalReopenPreservesCompleteUserDataAsync(Schema7Fixture fixture)
    {
        var versionBeforeReopen = await fixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, versionBeforeReopen);

        await fixture.Connection.CloseAsync();

        string[] before;
        var beforeConnection = new SQLiteAsyncConnection(fixture.DatabasePath);
        try
        {
            before = await PersistentDatabaseSnapshot.CaptureTableRowsAsync(beforeConnection, DurableSchema8TableNames);
        }
        finally
        {
            await beforeConnection.CloseAsync();
        }

        var reopened = new SQLiteAsyncConnection(fixture.DatabasePath);
        await DatabaseSchema.InitializeAsync(reopened);

        var versionAfterReopen = await reopened.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(DatabaseSchema.CurrentVersion, versionAfterReopen);

        var integrity = await reopened.ExecuteScalarAsync<string>("PRAGMA integrity_check");
        Assert.AreEqual("ok", integrity);
        var foreignKeyViolations = await reopened.QueryAsync<ForeignKeyViolationRow>("PRAGMA foreign_key_check");
        Assert.IsEmpty(foreignKeyViolations);

        var validShape = false;
        string? shapeFailureDetail = null;
        await reopened.RunInTransactionAsync(connection =>
            validShape = Schema9ShapeValidator.IsValidDatabase(connection, out shapeFailureDetail));
        Assert.IsTrue(validShape, shapeFailureDetail);

        var after = await PersistentDatabaseSnapshot.CaptureTableRowsAsync(reopened, DurableSchema8TableNames);
        await reopened.CloseAsync();

        CollectionAssert.AreEqual(before, after);
    }

    private static readonly string[] DurableSchema8TableNames =
    [
        "Documents", "Words", "WordForms", "SentenceSpans", "WordOccurrences", "Meanings",
        "ReviewStates", "ReviewSessions", "ReviewCandidates", "PreparationSessions", "PreparationCandidates",
        "ContextSnapshots", "LearningCards", "LearningReviews", "LearningSessions", "LearningSessionCards",
        "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress"
    ];

    private sealed class ForeignKeyViolationRow
    {
        public string Table { get; set; } = string.Empty;
        public int RowId { get; set; }
        public string Parent { get; set; } = string.Empty;
        public int FKId { get; set; }
    }

    /// <summary>Proves no unrelated row was added, dropped, or duplicated by the restore.</summary>
    private static async Task AssertSlice4RowCountsAsync(Schema7Fixture fixture)
    {
        async Task AssertCountAsync(string table, int expected) =>
            Assert.AreEqual(
                expected,
                await fixture.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}"),
                table);

        await AssertCountAsync("Words", Slice4.WordRowCount);
        await AssertCountAsync("Senses", Slice4.SenseRowCount);
        await AssertCountAsync("Meanings", Slice4.MeaningRowCount);
        await AssertCountAsync("AnswerVariants", Slice4.AnswerVariantRowCount);
        await AssertCountAsync("SenseAnswerVariantAssignments", Slice4.AssignmentRowCount);
        await AssertCountAsync("LearningCards", Slice4.CardRowCount);
        await AssertCountAsync("AnswerVariantProgress", Slice4.ProgressRowCount);
        await AssertCountAsync("Documents", 0);
        await AssertCountAsync("ContextSnapshots", 0);
        await AssertCountAsync("LearningReviews", 0);
        await AssertCountAsync("LearningSessions", 0);
        await AssertCountAsync("LearningSessionCards", 0);
    }

    [TestMethod]
    public async Task Restore_RequiredSinceUtc_SurvivesRoundTripWithTickEquality()
    {
        var archiveBytes = await BuildSlice4BoundaryArchiveBytesAsync();
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await RestoreIntoEmptyTargetAsync(targetFixture, archiveBytes);

        Assert.AreEqual(8, await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        var assignments = await ReadRestoredAssignmentsAsync(targetFixture);
        Assert.AreEqual(Slice4.AssignmentRowCount, assignments.Count);

        var required = assignments.Single(row => row.StableId == Slice4.RequiredAssignmentStableId);
        Assert.AreEqual(AnswerVariantRequirement.Required, required.Requirement);
        Assert.IsTrue(required.IsPreferred);
        Assert.AreEqual(CardDirection.TermToMeaning, required.CardDirection);
        Assert.AreEqual(Slice4.RequiredVariantStableId, required.AnswerVariantStableId);
        Assert.IsNotNull(required.RequiredSinceUtc);
        Assert.AreEqual(Slice4.RequiredSinceUtc.Ticks, AsUtc(required.RequiredSinceUtc!.Value).Ticks);

        var acceptedOnly = assignments.Single(row => row.StableId == Slice4.AcceptedOnlyAssignmentStableId);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, acceptedOnly.Requirement);
        Assert.IsFalse(acceptedOnly.IsPreferred);
        Assert.AreEqual(Slice4.AcceptedOnlyVariantStableId, acceptedOnly.AnswerVariantStableId);
        Assert.IsNull(acceptedOnly.RequiredSinceUtc);

        var term = assignments.Single(row => row.StableId == Slice4.TermAssignmentStableId);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, term.Requirement);
        Assert.IsTrue(term.IsPreferred);
        Assert.AreEqual(CardDirection.MeaningToTerm, term.CardDirection);
        Assert.IsNull(term.RequiredSinceUtc);

        var progress = await ReadRestoredProgressAsync(targetFixture);
        Assert.AreEqual(Slice4.ProgressRowCount, progress.Count);
        Assert.AreEqual(Slice4.RequiredVariantStableId, progress[0].AnswerVariantStableId);
        Assert.AreEqual(Slice4.RequiredSinceUtc.Ticks, AsUtc(progress[0].CreatedAtUtc).Ticks);

        await AssertSlice4RowCountsAsync(targetFixture);
    }

    [TestMethod]
    public async Task Restore_RequiredAssignment_PreservesBoundaryInvariant()
    {
        var archiveBytes = await BuildSlice4BoundaryArchiveBytesAsync();
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await RestoreIntoEmptyTargetAsync(targetFixture, archiveBytes);

        // Invariant I1 across every restored row, asserted in the destination database itself: Required if
        // and only if RequiredSinceUtc is non-null.
        var violations = await targetFixture.Connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments
            WHERE (Requirement = ? AND RequiredSinceUtc IS NULL)
               OR (Requirement <> ? AND RequiredSinceUtc IS NOT NULL)
            """,
            (int)AnswerVariantRequirement.Required, (int)AnswerVariantRequirement.Required);
        Assert.AreEqual(0, violations);

        var assignments = await ReadRestoredAssignmentsAsync(targetFixture);
        var required = assignments.Single(row => row.Requirement == AnswerVariantRequirement.Required);
        Assert.AreEqual(Slice4.RequiredAssignmentStableId, required.StableId);
        Assert.IsNotNull(required.RequiredSinceUtc);
        Assert.AreEqual(Slice4.RequiredSinceUtc.Ticks, AsUtc(required.RequiredSinceUtc!.Value).Ticks);
    }

    [TestMethod]
    public async Task Restore_AcceptedOnlyAssignment_PreservesNullBoundary()
    {
        var archiveBytes = await BuildSlice4BoundaryArchiveBytesAsync();
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await RestoreIntoEmptyTargetAsync(targetFixture, archiveBytes);

        var assignments = await ReadRestoredAssignmentsAsync(targetFixture);
        var rows = assignments
            .Where(row => row.Requirement == AnswerVariantRequirement.AcceptedOnly)
            .ToList();

        Assert.AreEqual(2, rows.Count);
        foreach (var row in rows)
        {
            Assert.IsNull(row.RequiredSinceUtc, row.StableId);
        }

        // One of the two is preferred: a null boundary follows from Requirement alone, never from a row
        // happening to be non-preferred.
        Assert.AreEqual(1, rows.Count(row => row.IsPreferred));
    }

    [TestMethod]
    public async Task Restore_ProgressCreatedAtUtc_RemainsEqualToRequiredSinceUtcForCurrentEpoch()
    {
        var archiveBytes = await BuildSlice4BoundaryArchiveBytesAsync();
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        await RestoreIntoEmptyTargetAsync(targetFixture, archiveBytes);

        // Required-epoch identity: a replay-owned progress row belongs to the current epoch only while its
        // CreatedAtUtc is tick-equal to its assignment's RequiredSinceUtc. Asserted first as a join over the
        // restored rows, then as an explicit tick comparison against the fixture's own fixed instant.
        var drifted = await targetFixture.Connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM AnswerVariantProgress p
            JOIN LearningCards c ON c.Id = p.CardId
            JOIN SenseAnswerVariantAssignments a
                ON a.SenseId = c.SenseId AND a.CardDirection = c.Direction AND a.AnswerVariantId = p.AnswerVariantId
            WHERE a.Requirement = ? AND (a.RequiredSinceUtc IS NULL OR p.CreatedAtUtc <> a.RequiredSinceUtc)
            """,
            (int)AnswerVariantRequirement.Required);
        Assert.AreEqual(0, drifted);

        var progress = await ReadRestoredProgressAsync(targetFixture);
        Assert.AreEqual(Slice4.ProgressRowCount, progress.Count);
        Assert.AreEqual(Slice4.RequiredVariantStableId, progress[0].AnswerVariantStableId);
        Assert.AreEqual(Slice4.RequiredSinceUtc.Ticks, AsUtc(progress[0].CreatedAtUtc).Ticks);
    }

    private static (byte[] Manifest, byte[] Data) ReadArchiveEntries(byte[] archiveBytes)
    {
        using var input = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);

        using var manifestMemory = new MemoryStream();
        using (var manifestStream = archive.GetEntry("manifest.json")!.Open())
        {
            manifestStream.CopyTo(manifestMemory);
        }

        using var dataMemory = new MemoryStream();
        using (var dataStream = archive.GetEntry("data.json")!.Open())
        {
            dataStream.CopyTo(dataMemory);
        }

        return (manifestMemory.ToArray(), dataMemory.ToArray());
    }

    private static byte[] RepackArchive(byte[] manifestBytes, byte[] dataBytes)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifestStream = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open())
            {
                manifestStream.Write(manifestBytes);
            }

            using (var dataStream = archive.CreateEntry("data.json", CompressionLevel.Optimal).Open())
            {
                dataStream.Write(dataBytes);
            }
        }

        return output.ToArray();
    }

    [TestMethod]
    public async Task Restore_InvalidRequiredBoundaryContract_FailsBeforeDatabaseMutation()
    {
        var (manifestBytes, dataBytes) = ReadArchiveEntries(await BuildSlice4BoundaryArchiveBytesAsync());
        var dataJson = Encoding.UTF8.GetString(dataBytes);

        // Strip only the Required assignment's boundary; Requirement stays Required, so the archive now
        // violates invariant I1 while remaining structurally well-formed, correctly-counted v2 JSON.
        var boundaryLiteral = "\"requiredSinceUtc\":\""
            + Slice4.RequiredSinceUtc.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture)
            + "\"";
        Assert.Contains(boundaryLiteral, dataJson, StringComparison.Ordinal);
        var mutatedDataJson = dataJson.Replace(boundaryLiteral, "\"requiredSinceUtc\":null", StringComparison.Ordinal);
        var mutatedDataBytes = Encoding.UTF8.GetBytes(mutatedDataJson);

        // Recompute the archive-level checksum so the contract invariant — never an incidental checksum or
        // record-count mismatch — is what actually rejects the archive.
        var newChecksum = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(mutatedDataBytes)).ToLowerInvariant();
        var manifestJson = Encoding.UTF8.GetString(manifestBytes);
        var checksumPattern = new System.Text.RegularExpressions.Regex("\"dataChecksum\":\"sha256:[0-9a-f]{64}\"");
        Assert.IsTrue(checksumPattern.IsMatch(manifestJson), "Expected a dataChecksum field in the manifest JSON.");
        var mutatedManifestJson = checksumPattern.Replace(manifestJson, $"\"dataChecksum\":\"{newChecksum}\"", 1);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var injector = new CountingFailureInjector();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo(),
            injector);

        using var stream = new MemoryStream(
            RepackArchive(Encoding.UTF8.GetBytes(mutatedManifestJson), mutatedDataBytes));
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, result.ErrorCode);

        // Validation ran entirely before the restore transaction: not one durable mutation was attempted,
        // so no partial assignment or progress row can exist.
        Assert.AreEqual(0, injector.Count);
        Assert.IsFalse(await HasAnyDurableSchema8RowAsync(targetFixture));
        Assert.AreEqual(
            0,
            await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
        Assert.AreEqual(
            0,
            await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariantProgress"));
        Assert.AreEqual(8, await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
    }

    // ---- PRAGMA user_version is read once (capability), never written, on success or failure ----

    [TestMethod]
    public async Task Schema8Restore_UserVersionUnchanged_BeforeAndAfterSuccess()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var before = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");

        var service = new BackupService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var after = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, before);
        Assert.AreEqual(8, after);
    }

    [TestMethod]
    public async Task Schema8Restore_UserVersionUnchanged_AfterInjectedFailureRollback()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var injector = new ThrowAtMutationInjector(throwAtCount: 1);
        var service = new BackupService(new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), new Schema8BackupFixtureBuilders.FakePlatformInfo(), injector);
        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        var version = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, version);
        var senseCount = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses");
        Assert.AreEqual(0, senseCount);
        var wordCount = await targetFixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words");
        Assert.AreEqual(0, wordCount);
    }

    // ---- Rollback proof: injected failure at the first mutation and at the very last mutation ----

    [TestMethod]
    public async Task Schema8Restore_InjectedFailureAtFirstMutation_RollsBackEveryMutation()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo(),
            new ThrowAtMutationInjector(throwAtCount: 1));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.IsFalse(await HasAnyDurableSchema8RowAsync(targetFixture));
    }

    [TestMethod]
    public async Task Schema8Restore_InjectedFailureAtLastMutation_RollsBackEveryMutation()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        // First pass: count the total number of mutations a successful restore performs.
        int totalMutations;
        {
            await using var countingTargetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
            var countingInjector = new CountingFailureInjector();
            var countingService = new BackupService(
                new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(countingTargetFixture),
                new Schema8BackupFixtureBuilders.FakePlatformInfo(),
                countingInjector);
            using var countingStream = new MemoryStream(archiveBytes);
            var countingResult = await countingService.ImportPortableArchiveAsync(countingStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Success, countingResult.Status);
            totalMutations = countingInjector.Count;
        }

        Assert.IsGreaterThan(0, totalMutations);

        // Second pass: inject a failure at the very last mutation and prove full rollback.
        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo(),
            new ThrowAtMutationInjector(throwAtCount: totalMutations));

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.IsFalse(await HasAnyDurableSchema8RowAsync(targetFixture));
        var version = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, version);
    }

    // ---- Named restore-failure checkpoints (KF-MEANING-001 Slice 2 correction): explicit, stable
    // boundaries — AfterSenseInsertion / DuringMeaningVariantAssignmentInsertion /
    // AfterDefaultMeaningBackfill / AfterCardsBeforeLearningHistory / BeforeCompletion — replacing raw
    // mutation-count proxies as the sole proof of these semantic boundaries. ----

    [TestMethod]
    public async Task Schema8Restore_NamedCheckpoints_AllFireInStableRelativeOrder_DuringNormalRestore()
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var recorder = new RecordingCheckpointInjector();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo(),
            recorder);

        using var stream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        CollectionAssert.Contains(recorder.Checkpoints, Schema8BackupImportRepository.Checkpoints.AfterSenseInsertion);
        CollectionAssert.Contains(recorder.Checkpoints, Schema8BackupImportRepository.Checkpoints.DuringMeaningVariantAssignmentInsertion);
        CollectionAssert.Contains(recorder.Checkpoints, Schema8BackupImportRepository.Checkpoints.AfterDefaultMeaningBackfill);
        CollectionAssert.Contains(recorder.Checkpoints, Schema8BackupImportRepository.Checkpoints.AfterCardsBeforeLearningHistory);
        CollectionAssert.Contains(recorder.Checkpoints, Schema8BackupImportRepository.Checkpoints.BeforeCompletion);

        var firstIndexOf = new Func<string, int>(name => recorder.Checkpoints.IndexOf(name));
        var order = new[]
        {
            firstIndexOf(Schema8BackupImportRepository.Checkpoints.AfterSenseInsertion),
            firstIndexOf(Schema8BackupImportRepository.Checkpoints.AfterDefaultMeaningBackfill),
            firstIndexOf(Schema8BackupImportRepository.Checkpoints.DuringMeaningVariantAssignmentInsertion),
            firstIndexOf(Schema8BackupImportRepository.Checkpoints.AfterCardsBeforeLearningHistory),
            firstIndexOf(Schema8BackupImportRepository.Checkpoints.BeforeCompletion)
        };
        CollectionAssert.AreEqual(order.OrderBy(i => i).ToArray(), order);
    }

    [TestMethod]
    public async Task Schema8Restore_ThrowAtAfterSenseInsertion_RollsBackCompletelyAndRetrySucceeds() =>
        await AssertCheckpointRollsBackAndRetrySucceedsAsync(Schema8BackupImportRepository.Checkpoints.AfterSenseInsertion);

    [TestMethod]
    public async Task Schema8Restore_ThrowDuringMeaningVariantAssignmentInsertion_RollsBackCompletelyAndRetrySucceeds() =>
        await AssertCheckpointRollsBackAndRetrySucceedsAsync(Schema8BackupImportRepository.Checkpoints.DuringMeaningVariantAssignmentInsertion);

    [TestMethod]
    public async Task Schema8Restore_ThrowAfterDefaultMeaningBackfill_RollsBackCompletelyAndRetrySucceeds() =>
        await AssertCheckpointRollsBackAndRetrySucceedsAsync(Schema8BackupImportRepository.Checkpoints.AfterDefaultMeaningBackfill);

    [TestMethod]
    public async Task Schema8Restore_ThrowAfterCardsBeforeLearningHistory_RollsBackCompletelyAndRetrySucceeds() =>
        await AssertCheckpointRollsBackAndRetrySucceedsAsync(Schema8BackupImportRepository.Checkpoints.AfterCardsBeforeLearningHistory);

    [TestMethod]
    public async Task Schema8Restore_ThrowBeforeCompletion_RollsBackCompletelyAndRetrySucceeds() =>
        await AssertCheckpointRollsBackAndRetrySucceedsAsync(Schema8BackupImportRepository.Checkpoints.BeforeCompletion);

    /// <summary>
    /// For the named checkpoint, proves: the injected exception occurs (the restore reports Failed); every
    /// durable Schema-8 user-data table is empty afterward (including no partially-backfilled
    /// <c>Sense.DefaultMeaningId</c>, itself one of the rolled-back columns); <c>PRAGMA user_version</c>
    /// remains exactly 8; no stray database/WAL/SHM/journal/staging artifact is left in the database's
    /// directory; and a retry without injection succeeds.
    /// </summary>
    private static async Task AssertCheckpointRollsBackAndRetrySucceedsAsync(string checkpointName)
    {
        await using var sourceFixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var archiveBytes = await BuildV2ArchiveBytesAsync(sourceFixture);

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var databaseDirectory = Path.GetDirectoryName(targetFixture.DatabasePath)!;
        var databaseFileName = Path.GetFileName(targetFixture.DatabasePath);

        var failingService = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo(),
            new ThrowAtCheckpointInjector(checkpointName));

        using (var failingStream = new MemoryStream(archiveBytes))
        {
            var failingResult = await failingService.ImportPortableArchiveAsync(failingStream, CancellationToken.None);
            Assert.AreEqual(PortableImportStatus.Failed, failingResult.Status);
        }

        Assert.IsFalse(await HasAnyDurableSchema8RowAsync(targetFixture));
        var versionAfterFailure = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, versionAfterFailure);

        // Scoped to this test's own database file only (never the shared system temp directory, which
        // legitimately holds unrelated files from other processes) — any sibling artifact whose name
        // extends the database's own file name (WAL/SHM/journal/staging) would show up here.
        var strayArtifacts = Directory.GetFileSystemEntries(databaseDirectory)
            .Where(path => Path.GetFileName(path).StartsWith(databaseFileName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), databaseFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.AreEqual(0, strayArtifacts.Count, string.Join(", ", strayArtifacts));

        // Retry without injection succeeds.
        var retryService = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture),
            new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var retryStream = new MemoryStream(archiveBytes);
        var retryResult = await retryService.ImportPortableArchiveAsync(retryStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, retryResult.Status);
        Assert.IsTrue(await HasAnyDurableSchema8RowAsync(targetFixture));

        var versionAfterRetry = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, versionAfterRetry);
    }

    private static async Task<bool> HasAnyDurableSchema8RowAsync(Schema7Fixture fixture)
    {
        foreach (var table in new[]
        {
            "Documents", "Words", "WordForms", "SentenceSpans", "WordOccurrences", "Meanings",
            "ReviewStates", "ReviewSessions", "ReviewCandidates", "PreparationSessions", "PreparationCandidates",
            "ContextSnapshots", "LearningCards", "LearningReviews", "LearningSessions", "LearningSessionCards",
            "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress"
        })
        {
            var count = await fixture.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
            if (count != 0)
            {
                return true;
            }
        }

        return false;
    }

    // ---- HasDurableUserData Schema-8 coverage ----

    [TestMethod]
    public async Task Schema8HasDurableUserData_EmptyDatabase_ReturnsFalse()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var hasData = false;
        await fixture.Connection.RunInTransactionAsync(conn => hasData = Schema8BackupImportRepository.HasDurableUserData(conn));
        Assert.IsFalse(hasData);
    }

    [TestMethod]
    public async Task Schema8HasDurableUserData_PopulatedDatabase_ReturnsTrue()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var hasData = false;
        await fixture.Connection.RunInTransactionAsync(conn => hasData = Schema8BackupImportRepository.HasDurableUserData(conn));
        Assert.IsTrue(hasData);
    }

    // ---- Core 6: v1-upgrade semantic parity with the live dormant migration ----

    [TestMethod]
    public async Task Core6_V1Upgrade_MatchesLiveMigrationSemantics()
    {
        // (a) Migrate a copy in place via the live migration.
        await using var migratedFixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(migratedFixture);
        Schema8BackupSnapshot? migratedSnapshot = null;
        await migratedFixture.Connection.RunInTransactionAsync(conn => migratedSnapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        var migratedPayload = BackupModelMapperV2.MapToExternal(migratedSnapshot!);

        // (b) Export the original as v1, then upgrade in memory.
        await using var v1Fixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        var v1Bytes = await BuildV1ArchiveBytesAsync(v1Fixture);
        using var v1Stream = new MemoryStream(v1Bytes);
        var validatedV1 = await BackupArchiveReader.ValidateAsync(v1Stream, CancellationToken.None);
        var upgradedPayload = BackupArchiveV1UpgradePolicy.Upgrade(validatedV1.Payload);

        // Semantic equivalence under canonical re-keying (StableIds legitimately differ).
        Assert.AreEqual(migratedPayload.Senses.Count, upgradedPayload.Senses.Count);
        Assert.AreEqual(migratedPayload.PreparedLearning.Count, upgradedPayload.PreparedLearning.Count);
        Assert.AreEqual(migratedPayload.Learning.Cards.Count, upgradedPayload.Learning.Cards.Count);

        var migratedVariantTexts = migratedPayload.AnswerVariants
            .Select(v => (v.AnswerLanguage, v.NormalizedText)).OrderBy(t => t.AnswerLanguage).ThenBy(t => t.NormalizedText).ToList();
        var upgradedVariantTexts = upgradedPayload.AnswerVariants
            .Select(v => (v.AnswerLanguage, v.NormalizedText)).OrderBy(t => t.AnswerLanguage).ThenBy(t => t.NormalizedText).ToList();
        CollectionAssert.AreEqual(migratedVariantTexts, upgradedVariantTexts);

        var migratedAssignmentShape = migratedPayload.SenseAnswerVariantAssignments
            .Select(a => (a.CardDirection, a.Requirement, a.IsPreferred)).OrderBy(t => t.CardDirection).ThenBy(t => t.IsPreferred).ToList();
        var upgradedAssignmentShape = upgradedPayload.SenseAnswerVariantAssignments
            .Select(a => (a.CardDirection, a.Requirement, a.IsPreferred)).OrderBy(t => t.CardDirection).ThenBy(t => t.IsPreferred).ToList();
        Assert.AreEqual(migratedAssignmentShape.Count, upgradedAssignmentShape.Count);

        // Card PreferredMeaningId correctness: every migrated card's preferred Meaning content matches
        // the corresponding upgraded card's preferred Meaning content (matched by direction, since
        // archive-local/StableIds differ between the two independently-built graphs).
        foreach (var direction in new[] { BackupCardDirection.MeaningToTerm, BackupCardDirection.TermToMeaning })
        {
            var migratedCard = migratedPayload.Learning.Cards.SingleOrDefault(c => c.Direction == direction);
            var upgradedCard = upgradedPayload.Learning.Cards.SingleOrDefault(c => c.Direction == direction);
            Assert.AreEqual(migratedCard is null, upgradedCard is null);
            if (migratedCard is null || upgradedCard is null)
            {
                continue;
            }

            var migratedMeaning = migratedPayload.PreparedLearning.Single(m => m.Id == migratedCard.PreferredMeaningId);
            var upgradedMeaning = upgradedPayload.PreparedLearning.Single(m => m.Id == upgradedCard.PreferredMeaningId);
            Assert.AreEqual(migratedMeaning.DisplayTerm, upgradedMeaning.DisplayTerm);
            Assert.AreEqual(migratedMeaning.Translation, upgradedMeaning.Translation);
        }
    }

    [TestMethod]
    public async Task V1Upgrade_IsDeterministic_AcrossTwoReadsOfTheSameArchive()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateAndMigrateRepresentativeFixtureAsync();
        var bytes = await BuildV1ArchiveBytesAsync(fixture);

        using var stream1 = new MemoryStream(bytes);
        var validated1 = await BackupArchiveReader.ValidateAsync(stream1, CancellationToken.None);
        var upgraded1 = BackupArchiveV1UpgradePolicy.Upgrade(validated1.Payload);

        using var stream2 = new MemoryStream(bytes);
        var validated2 = await BackupArchiveReader.ValidateAsync(stream2, CancellationToken.None);
        var upgraded2 = BackupArchiveV1UpgradePolicy.Upgrade(validated2.Payload);

        CollectionAssert.AreEqual(
            upgraded1.Senses.Select(s => s.StableId).OrderBy(x => x).ToList(),
            upgraded2.Senses.Select(s => s.StableId).OrderBy(x => x).ToList());
        CollectionAssert.AreEqual(
            upgraded1.PreparedLearning.Select(m => m.StableId).OrderBy(x => x).ToList(),
            upgraded2.PreparedLearning.Select(m => m.StableId).OrderBy(x => x).ToList());
        CollectionAssert.AreEqual(
            upgraded1.AnswerVariants.Select(v => v.StableId).OrderBy(x => x).ToList(),
            upgraded2.AnswerVariants.Select(v => v.StableId).OrderBy(x => x).ToList());
    }

    [TestMethod]
    public async Task V1Upgrade_TwoContentIdenticalMeanings_WithDifferentArchiveIds_RemainDistinctStableIds()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("duplicate");
        // Two content-identical Meanings (differ only by which row they are) — must not collide.
        await fixture.InsertMeaningAsync(wordId, displayTerm: "duplicate", translation: "Duplikat");
        await fixture.InsertMeaningAsync(wordId, displayTerm: "duplicate", translation: "Duplikat");

        var bytes = await BuildV1ArchiveBytesAsync(fixture);
        using var stream = new MemoryStream(bytes);
        var validated = await BackupArchiveReader.ValidateAsync(stream, CancellationToken.None);
        var upgraded = BackupArchiveV1UpgradePolicy.Upgrade(validated.Payload);

        Assert.AreEqual(2, upgraded.PreparedLearning.Count);
        Assert.AreNotEqual(upgraded.PreparedLearning[0].StableId, upgraded.PreparedLearning[1].StableId);
    }

    [TestMethod]
    public async Task V1Upgrade_SameMeaningArchiveIdText_UnderDifferentVocabulary_DoesNotCollide()
    {
        // Two different words, each with exactly one Meaning — the two Meanings' v1 archive ids are
        // independent per-collection-global positional strings ("m-000001", "m-000002", ...), so this
        // proves the Vocabulary component of the seed matters even though true id-text collision cannot
        // occur within one archive: two content-identical Meanings under two different Vocabulary items
        // must not collide either.
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId1 = await fixture.InsertWordAsync("alpha");
        var wordId2 = await fixture.InsertWordAsync("beta");
        await fixture.InsertMeaningAsync(wordId1, displayTerm: "same", translation: "gleich");
        await fixture.InsertMeaningAsync(wordId2, displayTerm: "same", translation: "gleich");

        var bytes = await BuildV1ArchiveBytesAsync(fixture);
        using var stream = new MemoryStream(bytes);
        var validated = await BackupArchiveReader.ValidateAsync(stream, CancellationToken.None);
        var upgraded = BackupArchiveV1UpgradePolicy.Upgrade(validated.Payload);

        Assert.AreEqual(2, upgraded.PreparedLearning.Count);
        Assert.AreNotEqual(upgraded.PreparedLearning[0].StableId, upgraded.PreparedLearning[1].StableId);
        Assert.AreNotEqual(upgraded.Senses[0].StableId, upgraded.Senses[1].StableId);
    }

    // ---- No-discriminator Sense StableId collision resistance (KF-MEANING-001 Slice 2 correction):
    // two independent v1 archives whose Vocabulary/Meaning archive-id text is identical, neither Sense
    // carrying a reliable discriminator, but whose exact Meaning content (Translation) differs — the
    // seed's per-member ExactMeaningVariantIdentity component must still keep the two Senses distinct. ----

    private static BackupVocabularyItem BuildNoDiscriminatorVocab(string archiveId, string canonicalTerm, DateTime now) =>
        new(
            archiveId, "en", canonicalTerm, canonicalTerm, BackupTokenKind.Word, BackupKnowledgeState.Unreviewed,
            BackupPreparationState.Prepared, 1, 1, now, now, [],
            new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false), []);

    private static BackupPreparedItem BuildNoDiscriminatorMeaning(string archiveId, string vocabularyId, string translation, DateTime now) =>
        new(
            archiveId, vocabularyId, "en", "de", "bank", null, null, BackupTokenKind.Word,
            null, null, translation, null, null, null, null, [], true,
            new BackupSourceReference("Manual", "", "", null, ""), now, now, now, []);

    /// <summary>Two Vocabulary/Meaning pairs — "v-000001"/"m-000001" (the one under test) plus an
    /// unrelated "v-000002"/"m-000002" filler pair, in either list order — so the test can prove the
    /// v-000001 Sense's StableId is unaffected by collection enumeration order.</summary>
    private static BackupPayload BuildNoDiscriminatorTwoVocabPayload(string translationForFirstVocab, bool reversedOrder, DateTime now)
    {
        var vocabUnderTest = BuildNoDiscriminatorVocab("v-000001", "bank", now);
        var vocabFiller = BuildNoDiscriminatorVocab("v-000002", "light", now);
        var meaningUnderTest = BuildNoDiscriminatorMeaning("m-000001", "v-000001", translationForFirstVocab, now);
        var meaningFiller = BuildNoDiscriminatorMeaning("m-000002", "v-000002", "Licht", now);

        IReadOnlyList<BackupVocabularyItem> vocabulary = reversedOrder
            ? [vocabFiller, vocabUnderTest]
            : [vocabUnderTest, vocabFiller];
        IReadOnlyList<BackupPreparedItem> meanings = reversedOrder
            ? [meaningFiller, meaningUnderTest]
            : [meaningUnderTest, meaningFiller];

        return new BackupPayload(
            [], vocabulary, meanings,
            new BackupLearningData([], []),
            new BackupWorkflowData([], [], []),
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));
    }

    [TestMethod]
    public void V1Upgrade_NoDiscriminator_IdenticalVocabAndMeaningArchiveIdText_DifferentExactContent_ProducesDistinctDeterministicOrderIndependentSenseStableIds()
    {
        var now = DateTime.UtcNow;

        var payloadBank = BuildNoDiscriminatorTwoVocabPayload("Bank", reversedOrder: false, now);
        var payloadBankReordered = BuildNoDiscriminatorTwoVocabPayload("Bank", reversedOrder: true, now);
        var payloadKreditinstitut = BuildNoDiscriminatorTwoVocabPayload("Kreditinstitut", reversedOrder: false, now);

        var upgradedBank = BackupArchiveV1UpgradePolicy.Upgrade(payloadBank);
        var upgradedBankReordered = BackupArchiveV1UpgradePolicy.Upgrade(payloadBankReordered);
        var upgradedBankAgain = BackupArchiveV1UpgradePolicy.Upgrade(payloadBank);
        var upgradedKreditinstitut = BackupArchiveV1UpgradePolicy.Upgrade(payloadKreditinstitut);

        // Both upgrades succeed (no exception) and each produced exactly the expected two Senses.
        Assert.AreEqual(2, upgradedBank.Senses.Count);
        Assert.AreEqual(2, upgradedKreditinstitut.Senses.Count);

        var senseBank = upgradedBank.Senses.Single(s => s.VocabularyId == "v-000001");
        var senseBankReordered = upgradedBankReordered.Senses.Single(s => s.VocabularyId == "v-000001");
        var senseBankAgain = upgradedBankAgain.Senses.Single(s => s.VocabularyId == "v-000001");
        var senseKreditinstitut = upgradedKreditinstitut.Senses.Single(s => s.VocabularyId == "v-000001");

        // Same identical Vocabulary/Meaning archive-id text and no discriminator on either side, yet the
        // differing exact Meaning content (Translation) keeps the two Senses' StableIds distinct.
        Assert.AreNotEqual(senseBank.StableId, senseKreditinstitut.StableId);

        // Deterministic across repeated execution of the same input.
        Assert.AreEqual(senseBank.StableId, senseBankAgain.StableId);

        // Changing Vocabulary/PreparedLearning collection enumeration order does not change the result.
        Assert.AreEqual(senseBank.StableId, senseBankReordered.StableId);
    }
}
