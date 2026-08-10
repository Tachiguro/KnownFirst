using System.IO.Compression;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 2 — archive format v2 and dual Schema-7/Schema-8 backup-repository support. The
/// ten tests-first core cases plus the directly-related required focused tests (format rejection, graph
/// invariants, StableId determinism/collision-resistance, local-ID independence). Every Schema-8 fixture
/// is synthetic, built via <see cref="Schema7Fixture"/> + <see cref="Schema8DormantMigration.ApplyAsync"/>
/// — never a real user database, and Schema-8 tables are never registered in ordinary initialization.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class BackupArchiveV2Tests
{
    private static async Task<Schema8BackupSnapshot> CaptureSchema8SnapshotAsync(Schema7Fixture fixture)
    {
        Schema8BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        return snapshot!;
    }

    private static async Task<ValidatedSchema7Capability> ResolveSchema7CapabilityAsync(Schema7Fixture fixture)
    {
        BackupSchemaCapabilityResult? result = null;
        await fixture.Connection.RunInTransactionAsync(conn => result = BackupSchemaCapability.Resolve(conn));
        return ((Schema7CapabilityResult)result!).Capability;
    }

    private static async Task<ValidatedSchema8Capability> ResolveSchema8CapabilityAsync(Schema7Fixture fixture)
    {
        BackupSchemaCapabilityResult? result = null;
        await fixture.Connection.RunInTransactionAsync(conn => result = BackupSchemaCapability.Resolve(conn));
        return ((Schema8CapabilityResult)result!).Capability;
    }

    // ---- Core 1: a real Schema-7 snapshot still writes and validates as archive format v1 ----

    [TestMethod]
    public async Task Core1_RealSchema7Snapshot_StillWritesAndValidatesAsV1()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("network");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        var capability = await ResolveSchema7CapabilityAsync(fixture);
        var payload = BackupModelMapper.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;

        var validated = await BackupArchiveReader.ValidateAsync(stream, CancellationToken.None);
        Assert.AreEqual(1, validated.Manifest.FormatVersion);
        Assert.AreEqual(7, validated.Manifest.SourceDatabaseSchemaVersion);

        stream.Position = 0;
        var versioned = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        Assert.AreEqual(1, versioned.FormatVersion);
        Assert.IsNotNull(versioned.V1);
        Assert.IsNull(versioned.V2);
    }

    // ---- Core 2: a synthetic Schema-8 fixture writes and validates as archive format v2 ----

    [TestMethod]
    public async Task Core2_SyntheticSchema8Fixture_WritesAndValidatesAsV2()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var snapshot = await CaptureSchema8SnapshotAsync(fixture);
        var capability = await ResolveSchema8CapabilityAsync(fixture);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;

        var versioned = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        Assert.AreEqual(2, versioned.FormatVersion);
        Assert.IsNull(versioned.V1);
        Assert.IsNotNull(versioned.V2);
        Assert.AreEqual(8, versioned.V2!.Manifest.SourceDatabaseSchemaVersion);
        Assert.IsGreaterThan(0, versioned.V2.Payload.Senses.Count);
    }

    // ---- Core 3: the reader accepts both v1 and v2, and rejects unsupported versions ----

    [TestMethod]
    public async Task Core3_Reader_RejectsFormatZero()
    {
        using var stream = await BuildArchiveWithMutatedFormatVersionAsync("0");
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.UnsupportedFormat, exception.Code);
    }

    [TestMethod]
    public async Task Core3_Reader_RejectsFormatThree()
    {
        using var stream = await BuildArchiveWithMutatedFormatVersionAsync("3");
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.UnsupportedFormat, exception.Code);
    }

    [TestMethod]
    public async Task Core3_Reader_RejectsV2ManifestWithV1ShapedData_AsImpossibleVersionMixing()
    {
        // A v1 archive whose manifest is relabeled formatVersion=2 (a v2-declared manifest) but whose
        // data.json is still v1-shaped — the strict source-generated v2 contract must reject this, not
        // silently accept it.
        using var stream = await BuildArchiveWithMutatedFormatVersionAsync("2");
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.IsTrue(exception.Code is BackupErrorCodes.ManifestInvalid or BackupErrorCodes.ChecksumMismatch or BackupErrorCodes.DataJsonInvalid);
    }

    private static async Task<MemoryStream> BuildArchiveWithMutatedFormatVersionAsync(string newFormatVersion)
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("network");
        var meaningId = await fixture.InsertMeaningAsync(wordId);
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        var capability = await ResolveSchema7CapabilityAsync(fixture);
        var payload = BackupModelMapper.MapToExternal(snapshot!);

        using var original = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, original, CancellationToken.None);
        original.Position = 0;

        byte[] manifestBytes;
        byte[] dataBytes;
        using (var readArchive = new ZipArchive(original, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var manifestStream = readArchive.GetEntry("manifest.json")!.Open();
            using var manifestMemory = new MemoryStream();
            await manifestStream.CopyToAsync(manifestMemory);
            manifestBytes = manifestMemory.ToArray();

            using var dataStream = readArchive.GetEntry("data.json")!.Open();
            using var dataMemory = new MemoryStream();
            await dataStream.CopyToAsync(dataMemory);
            dataBytes = dataMemory.ToArray();
        }

        var manifestJson = Encoding.UTF8.GetString(manifestBytes)
            .Replace("\"formatVersion\":1", $"\"formatVersion\":{newFormatVersion}", StringComparison.Ordinal);

        var output = new MemoryStream();
        using (var writeArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = writeArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var entryStream = manifestEntry.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(manifestJson);
                await entryStream.WriteAsync(bytes);
            }

            var dataEntry = writeArchive.CreateEntry("data.json", CompressionLevel.Optimal);
            using (var entryStream = dataEntry.Open())
            {
                await entryStream.WriteAsync(dataBytes);
            }
        }

        output.Position = 0;
        return output;
    }

    // ---- Core 4/5: v2 round-trip preserves the full graph and every StableId ----

    [TestMethod]
    public async Task Core4And5_V2RoundTrip_PreservesFullGraphAndStableIds()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var snapshot = await CaptureSchema8SnapshotAsync(fixture);
        var capability = await ResolveSchema8CapabilityAsync(fixture);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;
        var validated = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        var roundTripped = validated.V2!.Payload;

        // Senses, exact Meanings, AnswerVariants, direction-specific assignments.
        Assert.AreEqual(payload.Senses.Count, roundTripped.Senses.Count);
        Assert.AreEqual(payload.PreparedLearning.Count, roundTripped.PreparedLearning.Count);
        Assert.AreEqual(payload.AnswerVariants.Count, roundTripped.AnswerVariants.Count);
        Assert.AreEqual(payload.SenseAnswerVariantAssignments.Count, roundTripped.SenseAnswerVariantAssignments.Count);
        Assert.AreEqual(payload.AnswerVariantProgress.Count, roundTripped.AnswerVariantProgress.Count);

        // Card-specific PreferredMeaningId, direction.
        Assert.AreEqual(payload.Learning.Cards.Count, roundTripped.Learning.Cards.Count);
        foreach (var card in payload.Learning.Cards)
        {
            var match = roundTripped.Learning.Cards.Single(c => c.Id == card.Id);
            Assert.AreEqual(card.PreferredMeaningId, match.PreferredMeaningId);
            Assert.AreEqual(card.SenseId, match.SenseId);
            Assert.AreEqual(card.Direction, match.Direction);
        }

        // Review target/matched variants; queue target variants (including completed rows).
        Assert.AreEqual(payload.Learning.ReviewEvents.Count, roundTripped.Learning.ReviewEvents.Count);
        Assert.IsTrue(payload.Learning.ReviewEvents.Any(r => r.TargetAnswerVariantId is not null));
        foreach (var review in payload.Learning.ReviewEvents)
        {
            var match = roundTripped.Learning.ReviewEvents.First(r => r.CardId == review.CardId && r.ReviewedAtUtc == review.ReviewedAtUtc);
            Assert.AreEqual(review.TargetAnswerVariantId, match.TargetAnswerVariantId);
            Assert.AreEqual(review.MatchedAnswerVariantId, match.MatchedAnswerVariantId);
        }

        var payloadQueueItems = payload.Workflows.LearningSessions.SelectMany(s => s.QueueItems).ToList();
        var roundTrippedQueueItems = roundTripped.Workflows.LearningSessions.SelectMany(s => s.QueueItems).ToList();
        Assert.AreEqual(payloadQueueItems.Count, roundTrippedQueueItems.Count);
        Assert.IsTrue(payloadQueueItems.Any(q => q.IsCompleted && q.TargetAnswerVariantId is not null));
        foreach (var queueItem in payloadQueueItems)
        {
            var match = roundTrippedQueueItems.Single(q => q.Id == queueItem.Id);
            Assert.AreEqual(queueItem.TargetAnswerVariantId, match.TargetAnswerVariantId);
            Assert.AreEqual(queueItem.IsCompleted, match.IsCompleted);
        }

        // All v2 StableIds round-trip unchanged.
        CollectionAssert.AreEquivalent(
            payload.Senses.Select(s => s.StableId).ToList(), roundTripped.Senses.Select(s => s.StableId).ToList());
        CollectionAssert.AreEquivalent(
            payload.PreparedLearning.Select(m => m.StableId).ToList(), roundTripped.PreparedLearning.Select(m => m.StableId).ToList());
        CollectionAssert.AreEquivalent(
            payload.AnswerVariants.Select(v => v.StableId).ToList(), roundTripped.AnswerVariants.Select(v => v.StableId).ToList());
        CollectionAssert.AreEquivalent(
            payload.SenseAnswerVariantAssignments.Select(a => a.StableId).ToList(),
            roundTripped.SenseAnswerVariantAssignments.Select(a => a.StableId).ToList());
    }

    // ---- Core 9: a v2 archive restored into a Schema-7 target rejects safely with zero mutation ----

    [TestMethod]
    public async Task Core9_V2Archive_IntoSchema7Target_RejectsSafelyWithZeroMutation()
    {
        await using var schema8Fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var snapshot = await CaptureSchema8SnapshotAsync(schema8Fixture);
        var capability = await ResolveSchema8CapabilityAsync(schema8Fixture);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;

        await using var schema7Target = new TemporaryKnownFirstDatabase("archive-v2-core9-target");
        await schema7Target.InitializeAsync();
        var service = new BackupService(schema7Target, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.Schema8ArchiveIncompatibleWithSchema7Target, result.ErrorCode);

        var durable = await schema7Target.ReadAsync(conn => conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(0, durable);
    }

    // ---- Graph validation: duplicate StableIds, missing references, preferred-assignment invariants ----

    [TestMethod]
    public void ValidatePayloadGraphV2_DuplicateSenseStableIds_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        var duplicated = payload with
        {
            Senses = [payload.Senses[0], payload.Senses[0] with { Id = "se-000002" }]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(duplicated));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_MissingSenseVocabularyReference_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        var broken = payload with
        {
            Senses = [payload.Senses[0] with { VocabularyId = "v-999999" }]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(broken));
        Assert.AreEqual(BackupErrorCodes.MissingReference, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_ContextSenseDisagreesWithParentMeaning_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.BuildWithContext();
        var meaning = payload.PreparedLearning[0];
        var badContext = meaning.Contexts[0] with { SenseId = "se-999999" };
        var broken = payload with
        {
            PreparedLearning = [meaning with { Contexts = [badContext] }]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(broken));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_ZeroPreferredAssignmentsWhenAssignmentsExist_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        var downgraded = payload with
        {
            SenseAnswerVariantAssignments = payload.SenseAnswerVariantAssignments
                .Select(a => a with { IsPreferred = false }).ToList()
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(downgraded));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_MultiplePreferredAssignmentsForSameDirection_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.BuildWithTwoVariants();
        var doublePreferred = payload with
        {
            SenseAnswerVariantAssignments = payload.SenseAnswerVariantAssignments
                .Select(a => a with { IsPreferred = true }).ToList()
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(doublePreferred));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_DuplicateProgressKey_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        var progressRow = new BackupAnswerVariantProgress(
            "c-000001", payload.AnswerVariants[0].Id, BackupLearningInteractionMode.Reading, 0, 0, 0, null, false, false, 1,
            DateTime.UtcNow, DateTime.UtcNow);
        var broken = payload with { AnswerVariantProgress = [progressRow, progressRow] };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(broken));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_DuplicateAssignmentTriple_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        var duplicateAssignment = payload.SenseAnswerVariantAssignments[0] with
        {
            Id = "sa-999999", StableId = "sa-999999-stable", IsPreferred = false
        };
        var broken = payload with
        {
            SenseAnswerVariantAssignments = [.. payload.SenseAnswerVariantAssignments, duplicateAssignment]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(broken));
        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public void ValidatePayloadGraphV2_DuplicateSenseDirectionCards_Rejected()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        var card = payload.Learning.Cards[0];
        var duplicateCard = card with { Id = "c-999999" };
        var broken = payload with { Learning = payload.Learning with { Cards = [card, duplicateCard] } };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupArchiveWriterV2.ValidatePayloadGraphV2(broken));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public void BackupModelContractV2_SchemaEightWordStatus_RejectsLegacyPreparedLearningMastered()
    {
        var payload = MinimalV2PayloadBuilder.Build();
        foreach (var badState in new[] { BackupKnowledgeState.Prepared, BackupKnowledgeState.Learning, BackupKnowledgeState.Mastered })
        {
            var badVocab = payload.Vocabulary[0] with { KnowledgeState = badState };
            var broken = payload with { Vocabulary = [badVocab] };
            var exception = Assert.ThrowsExactly<BackupFormatException>(
                () => BackupModelContractV2.ValidatePayload(broken));
            Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
        }
    }

    // ---- KF-MEANING-001 Slice 4: the assignment RequiredSinceUtc boundary through the real v2 codec and
    // through the deterministic v1→v2 upgrade. No archive format-version increment is involved — the field
    // is an optional, defaulted trailing member of the existing v2 assignment contract. ----

    [TestMethod]
    public void V2Assignment_RequiredSinceUtc_RoundTrips()
    {
        // Sub-second precision proves the 7-fractional-digit UTC wire format, not merely whole seconds.
        var boundary = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567);
        var payload = Schema8BackupFixtureBuilders.BuildSingleAssignmentPayloadV2(
            BackupAnswerVariantRequirement.Required, boundary);

        var bytes = BackupJsonCodecV2.SerializeData(payload);
        var roundTripped = BackupJsonCodecV2.DeserializeData(bytes);

        var assignment = roundTripped.SenseAnswerVariantAssignments.Single();
        Assert.AreEqual(BackupAnswerVariantRequirement.Required, assignment.Requirement);
        Assert.IsNotNull(assignment.RequiredSinceUtc);
        Assert.AreEqual(boundary.Ticks, assignment.RequiredSinceUtc!.Value.Ticks);
        Assert.AreEqual(DateTimeKind.Utc, assignment.RequiredSinceUtc.Value.Kind);

        // Re-serializing the decoded payload reproduces the original bytes exactly — the boundary survives
        // encode/decode without any normalization drift.
        CollectionAssert.AreEqual(bytes, BackupJsonCodecV2.SerializeData(roundTripped));
    }

    [TestMethod]
    public void V2Assignment_MissingRequiredSinceUtcProperty_DeserializesAsNull()
    {
        // 1. Valid v2 JSON whose single assignment is AcceptedOnly (null boundary is correct for it).
        var acceptedOnly = Schema8BackupFixtureBuilders.BuildSingleAssignmentPayloadV2(
            BackupAnswerVariantRequirement.AcceptedOnly, requiredSinceUtc: null);
        var acceptedOnlyJson = Encoding.UTF8.GetString(BackupJsonCodecV2.SerializeData(acceptedOnly));
        Assert.Contains(",\"requiredSinceUtc\":null", acceptedOnlyJson, StringComparison.Ordinal);

        // 2. Remove only the RequiredSinceUtc property from that one assignment object.
        var withoutProperty = acceptedOnlyJson.Replace(
            ",\"requiredSinceUtc\":null", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("requiredSinceUtc", withoutProperty, StringComparison.Ordinal);

        // 3./4. It still decodes — the property is optional and defaulted, never a required constructor
        // parameter — and the DTO value is null.
        var decoded = BackupJsonCodecV2.DeserializeData(Encoding.UTF8.GetBytes(withoutProperty));
        Assert.IsNull(decoded.SenseAnswerVariantAssignments.Single().RequiredSinceUtc);

        // 5a. Contract validation accepts it, because the assignment is AcceptedOnly.
        BackupModelContractV2.ValidatePayload(decoded);

        // 5b. The identical omission on a Required assignment is rejected instead — an archive written
        // before the field existed can never silently import a Required row with no replay boundary.
        var required = Schema8BackupFixtureBuilders.BuildSingleAssignmentPayloadV2(
            BackupAnswerVariantRequirement.Required,
            Schema8BackupFixtureBuilders.Slice4Boundary.RequiredSinceUtc);
        var requiredJson = Encoding.UTF8.GetString(BackupJsonCodecV2.SerializeData(required));
        var requiredWithoutProperty = new System.Text.RegularExpressions.Regex(",\"requiredSinceUtc\":\"[^\"]*\"")
            .Replace(requiredJson, string.Empty, 1);
        Assert.DoesNotContain("requiredSinceUtc", requiredWithoutProperty, StringComparison.Ordinal);

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupJsonCodecV2.DeserializeData(Encoding.UTF8.GetBytes(requiredWithoutProperty)));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public void V2Assignment_RequiredMissingBoundary_FailsContractValidation()
    {
        var payload = Schema8BackupFixtureBuilders.BuildSingleAssignmentPayloadV2(
            BackupAnswerVariantRequirement.Required, requiredSinceUtc: null);

        var validation = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupModelContractV2.ValidatePayload(payload));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, validation.Code);

        // The codec validates before writing, so such an assignment can never reach an archive at all.
        var serialization = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupJsonCodecV2.SerializeData(payload));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, serialization.Code);
    }

    [TestMethod]
    public void V2Assignment_AcceptedOnlyWithBoundary_FailsContractValidation()
    {
        var payload = Schema8BackupFixtureBuilders.BuildSingleAssignmentPayloadV2(
            BackupAnswerVariantRequirement.AcceptedOnly,
            Schema8BackupFixtureBuilders.Slice4Boundary.RequiredSinceUtc);

        var validation = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupModelContractV2.ValidatePayload(payload));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, validation.Code);

        var serialization = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupJsonCodecV2.SerializeData(payload));
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, serialization.Code);
    }

    /// <summary>
    /// Minimal v1 payload for the upgrade-boundary tests: one Vocabulary, one Meaning carrying a
    /// DisplayTerm, a Translation and one accepted alias, and one card per direction whose
    /// <c>CreatedAtUtc</c> is supplied per direction — so the upgraded primary assignment's boundary can be
    /// asserted against an exact, direction-specific source value rather than a shared timestamp.
    /// </summary>
    private static BackupPayload BuildV1BoundaryUpgradePayload(
        DateTime meaningToTermCardCreatedAtUtc,
        DateTime termToMeaningCardCreatedAtUtc)
    {
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var vocabulary = new BackupVocabularyItem(
            "v-000001", "en", "boundary", "boundary", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed,
            BackupPreparationState.Prepared, 1, 1, ts, ts, [],
            new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false), []);

        var meaning = new BackupPreparedItem(
            "m-000001", "v-000001", "en", "de", "boundary", null, null, BackupTokenKind.Word,
            "slice4-1", null, "Grenze", "a dividing line", null, null, null, ["frontier"], true,
            new BackupSourceReference("Manual", "", "", null, ""), ts, ts, ts, []);

        BackupLearningCard Card(string id, BackupCardDirection direction, DateTime createdAtUtc) =>
            new(id, "v-000001", "m-000001", direction, BackupCardState.Review, ts, 3, 2.5, 1, 0, ts,
                BackupReviewRating.Good, createdAtUtc, ts);

        return new BackupPayload(
            [], [vocabulary], [meaning],
            new BackupLearningData(
                [
                    Card("c-000001", BackupCardDirection.MeaningToTerm, meaningToTermCardCreatedAtUtc),
                    Card("c-000002", BackupCardDirection.TermToMeaning, termToMeaningCardCreatedAtUtc)
                ],
                []),
            new BackupWorkflowData([], [], []),
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));
    }

    private static readonly DateTime V1UpgradeMeaningToTermCardCreatedAtUtc = new(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc);
    private static readonly DateTime V1UpgradeTermToMeaningCardCreatedAtUtc = new(2025, 9, 10, 11, 12, 13, DateTimeKind.Utc);

    [TestMethod]
    public void V1Upgrade_PrimaryRequiredAssignment_GetsCardCreatedAtUtcBoundary()
    {
        var upgraded = BackupArchiveV1UpgradePolicy.Upgrade(BuildV1BoundaryUpgradePayload(
            V1UpgradeMeaningToTermCardCreatedAtUtc, V1UpgradeTermToMeaningCardCreatedAtUtc));

        foreach (var (direction, expectedBoundary) in new[]
                 {
                     (BackupCardDirection.MeaningToTerm, V1UpgradeMeaningToTermCardCreatedAtUtc),
                     (BackupCardDirection.TermToMeaning, V1UpgradeTermToMeaningCardCreatedAtUtc)
                 })
        {
            var primary = upgraded.SenseAnswerVariantAssignments
                .Single(assignment => assignment.CardDirection == direction && assignment.IsPreferred);
            Assert.AreEqual(BackupAnswerVariantRequirement.Required, primary.Requirement);
            Assert.IsNotNull(primary.RequiredSinceUtc);
            Assert.AreEqual(expectedBoundary.Ticks, primary.RequiredSinceUtc!.Value.Ticks);
            Assert.AreEqual(DateTimeKind.Utc, primary.RequiredSinceUtc.Value.Kind);
        }

        // The upgraded graph is a writable v2 archive: the invariant holds across every assignment.
        BackupModelContractV2.ValidatePayload(upgraded);
        BackupArchiveWriterV2.ValidatePayloadGraphV2(upgraded);
    }

    [TestMethod]
    public void V1Upgrade_AcceptedOnlyAssignments_HaveNullBoundary()
    {
        var upgraded = BackupArchiveV1UpgradePolicy.Upgrade(BuildV1BoundaryUpgradePayload(
            V1UpgradeMeaningToTermCardCreatedAtUtc, V1UpgradeTermToMeaningCardCreatedAtUtc));

        var acceptedOnly = upgraded.SenseAnswerVariantAssignments
            .Where(assignment => assignment.Requirement == BackupAnswerVariantRequirement.AcceptedOnly)
            .ToList();

        // The accepted alias is a real, non-primary answer expression — the upgraded graph is genuinely
        // mixed-role, never Required everywhere merely to satisfy the invariant.
        Assert.IsGreaterThan(0, acceptedOnly.Count);
        foreach (var assignment in acceptedOnly)
        {
            Assert.IsNull(assignment.RequiredSinceUtc);
            Assert.IsFalse(assignment.IsPreferred);
        }
    }

    // ---- Per-entity v2 record-count mismatch, source-material checksum mismatch, and
    // validation-before-mutation (KF-MEANING-001 Slice 2 correction) ----

    private static async Task<(byte[] Manifest, byte[] Data)> BuildV2ManifestAndDataBytesAsync()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var snapshot = await CaptureSchema8SnapshotAsync(fixture);
        var capability = await ResolveSchema8CapabilityAsync(fixture);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        using var original = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, original, CancellationToken.None);
        original.Position = 0;

        using var readArchive = new ZipArchive(original, ZipArchiveMode.Read, leaveOpen: true);
        using var manifestStream = readArchive.GetEntry("manifest.json")!.Open();
        using var manifestMemory = new MemoryStream();
        await manifestStream.CopyToAsync(manifestMemory);

        using var dataStream = readArchive.GetEntry("data.json")!.Open();
        using var dataMemory = new MemoryStream();
        await dataStream.CopyToAsync(dataMemory);

        return (manifestMemory.ToArray(), dataMemory.ToArray());
    }

    private static async Task<MemoryStream> RepackArchiveAsync(byte[] manifestBytes, byte[] dataBytes)
    {
        var output = new MemoryStream();
        using (var writeArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = writeArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var entryStream = manifestEntry.Open())
            {
                await entryStream.WriteAsync(manifestBytes);
            }

            var dataEntry = writeArchive.CreateEntry("data.json", CompressionLevel.Optimal);
            using (var entryStream = dataEntry.Open())
            {
                await entryStream.WriteAsync(dataBytes);
            }
        }

        output.Position = 0;
        return output;
    }

    private static async Task<MemoryStream> BuildV2ArchiveWithMutatedManifestCountAsync(string jsonPropertyName)
    {
        var (manifestBytes, dataBytes) = await BuildV2ManifestAndDataBytesAsync();
        var manifestJson = Encoding.UTF8.GetString(manifestBytes);

        var pattern = new System.Text.RegularExpressions.Regex($"\"{jsonPropertyName}\":(\\d+)");
        Assert.IsTrue(pattern.IsMatch(manifestJson), $"Expected to find \"{jsonPropertyName}\" in the manifest JSON.");
        var mutatedManifestJson = pattern.Replace(
            manifestJson,
            match => $"\"{jsonPropertyName}\":{int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 1}",
            1);

        return await RepackArchiveAsync(Encoding.UTF8.GetBytes(mutatedManifestJson), dataBytes);
    }

    private static async Task AssertRecordCountMismatchRejectedAsync(string jsonPropertyName)
    {
        using var stream = await BuildV2ArchiveWithMutatedManifestCountAsync(jsonPropertyName);
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.RecordCountMismatch, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersioned_V2ManifestSensesCountMismatch_RejectedSeparately() =>
        await AssertRecordCountMismatchRejectedAsync("senses");

    [TestMethod]
    public async Task ValidateVersioned_V2ManifestAnswerVariantsCountMismatch_RejectedSeparately() =>
        await AssertRecordCountMismatchRejectedAsync("answerVariants");

    [TestMethod]
    public async Task ValidateVersioned_V2ManifestSenseAnswerVariantAssignmentsCountMismatch_RejectedSeparately() =>
        await AssertRecordCountMismatchRejectedAsync("senseAnswerVariantAssignments");

    [TestMethod]
    public async Task ValidateVersioned_V2ManifestAnswerVariantProgressCountMismatch_RejectedSeparately() =>
        await AssertRecordCountMismatchRejectedAsync("answerVariantProgress");

    [TestMethod]
    public async Task ValidateVersioned_V2SourceMaterialContentChecksumMismatch_Rejected()
    {
        var (manifestBytes, dataBytes) = await BuildV2ManifestAndDataBytesAsync();
        var dataJson = Encoding.UTF8.GetString(dataBytes);

        // Corrupt only the persisted contentSha256 field (never the original text itself) so the
        // per-source-material content-integrity check — not the outer archive-level data.json checksum —
        // is the one that actually fails.
        var contentHashPattern = new System.Text.RegularExpressions.Regex("\"contentSha256\":\"[0-9a-f]{64}\"");
        Assert.IsTrue(contentHashPattern.IsMatch(dataJson), "Expected to find a contentSha256 field in data.json.");
        var wrongHash = new string('0', 64);
        var mutatedDataJson = contentHashPattern.Replace(dataJson, $"\"contentSha256\":\"{wrongHash}\"", 1);
        var mutatedDataBytes = Encoding.UTF8.GetBytes(mutatedDataJson);

        // Recompute the outer archive-level checksum to match the mutated data.json bytes so that check
        // passes and the inner per-source-material check is the one actually exercised.
        var newDataChecksum = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(mutatedDataBytes)).ToLowerInvariant();
        var manifestJson = Encoding.UTF8.GetString(manifestBytes);
        var checksumPattern = new System.Text.RegularExpressions.Regex("\"dataChecksum\":\"sha256:[0-9a-f]{64}\"");
        Assert.IsTrue(checksumPattern.IsMatch(manifestJson), "Expected to find a dataChecksum field in the manifest JSON.");
        var mutatedManifestJson = checksumPattern.Replace(manifestJson, $"\"dataChecksum\":\"{newDataChecksum}\"", 1);

        using var stream = await RepackArchiveAsync(Encoding.UTF8.GetBytes(mutatedManifestJson), mutatedDataBytes);
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.ChecksumMismatch, exception.Code);
    }

    [TestMethod]
    public async Task ImportPortableArchive_V2ValidationFailure_LeavesTargetCompletelyUnmutated()
    {
        using var stream = await BuildV2ArchiveWithMutatedManifestCountAsync("senses");

        await using var targetFixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var service = new BackupService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture), new Schema8BackupFixtureBuilders.FakePlatformInfo());

        var result = await service.ImportPortableArchiveAsync(stream, CancellationToken.None);

        // Validation happens entirely before the restore transaction ever opens — a corrupt archive
        // reaches ValidationFailed, never Failed (the code path used for a mid-restore rollback).
        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.RecordCountMismatch, result.ErrorCode);

        var hasData = false;
        await targetFixture.Connection.RunInTransactionAsync(conn => hasData = Schema8BackupImportRepository.HasDurableUserData(conn));
        Assert.IsFalse(hasData);
        var version = await targetFixture.Connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        Assert.AreEqual(8, version);
    }

    // ---- Schema-7 export has no v2 collections; Schema-8 export has no v1 card field ----

    [TestMethod]
    public async Task Schema7Export_HasNoV2Collections()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("network");
        var meaningId = await fixture.InsertMeaningAsync(wordId);
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        var capability = await ResolveSchema7CapabilityAsync(fixture);
        var payload = BackupModelMapper.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var dataStream = archive.GetEntry("data.json")!.Open();
        using var reader = new StreamReader(dataStream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();

        Assert.DoesNotContain("\"senses\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"answerVariants\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"senseAnswerVariantAssignments\"", json, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Schema8Export_HasNoV1PreparedItemIdCardField()
    {
        await using var fixture = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var snapshot = await CaptureSchema8SnapshotAsync(fixture);
        var capability = await ResolveSchema8CapabilityAsync(fixture);
        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var dataStream = archive.GetEntry("data.json")!.Open();
        using var reader = new StreamReader(dataStream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();

        Assert.DoesNotContain("\"preparedItemId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"preferredMeaningId\"", json, StringComparison.Ordinal);
    }

    // ---- Complete local-ID independence (KF-MEANING-001 Slice 2 correction): two independently-built,
    // physically valid Schema-8 databases holding equivalent persisted content — identical StableIds and
    // field content, deliberately different local SQLite integer ids, insertion order, and displaced
    // AUTOINCREMENT sequences spanning every major collection — captured through the real Schema-8
    // snapshot and mapper path and serialized with the real source-generated v2 codec, must still produce
    // byte-for-byte identical canonical data.json. The raw serialized bytes are compared directly; the
    // result is never normalized after the fact. ----

    [TestMethod]
    public async Task LocalIdIndependence_TwoEquivalentSchema8DatabasesWithScrambledLocalIds_ProduceByteIdenticalV2DataJson()
    {
        await using var fixtureA = await BuildLocalIdScrambledSchema8FixtureAsync(scramble: false);
        await using var fixtureB = await BuildLocalIdScrambledSchema8FixtureAsync(scramble: true);

        // Prove the premise first: the equivalent logical row in every major collection actually exists
        // in both fixtures and was actually assigned a different local SQLite integer id — never assumed
        // from the scrambling code alone.
        await AssertLocalIdsDifferAcrossEveryMajorCollectionAsync(fixtureA, fixtureB);

        var snapshotA = await CaptureSchema8SnapshotAsync(fixtureA);
        var snapshotB = await CaptureSchema8SnapshotAsync(fixtureB);

        var payloadA = BackupModelMapperV2.MapToExternal(snapshotA);
        var payloadB = BackupModelMapperV2.MapToExternal(snapshotB);

        var bytesA = BackupJsonCodecV2.SerializeData(payloadA);
        var bytesB = BackupJsonCodecV2.SerializeData(payloadB);

        // Raw serialized bytes compared directly — never parsed, reordered, or normalized.
        CollectionAssert.AreEqual(bytesA, bytesB);
    }

    /// <summary>
    /// Proves the test's premise before serialization is ever attempted: every major Schema-8 collection
    /// has its own <c>Id INTEGER PRIMARY KEY AUTOINCREMENT</c> column (there is no table in this list
    /// lacking one), but each equivalent logical row is located here by a stable/semantic key — a
    /// <c>StableId</c>, fixed content (<c>ContentFingerprint</c>/<c>Title</c>/<c>NormalizedFingerprint</c>/
    /// <c>CanonicalTerm</c>/<c>SurfaceForm</c>), or a join through a parent already identified the same
    /// way — never by row order or position. For each table: the row is asserted to exist in both
    /// fixtures, and its local numeric id is asserted to differ between them.
    /// </summary>
    private static async Task AssertLocalIdsDifferAcrossEveryMajorCollectionAsync(Schema7Fixture fixtureA, Schema7Fixture fixtureB)
    {
        async Task AssertDifferentAsync(string label, string sql, params object[] args)
        {
            var idA = await fixtureA.Connection.ExecuteScalarAsync<int>(sql, args);
            var idB = await fixtureB.Connection.ExecuteScalarAsync<int>(sql, args);
            Assert.AreNotEqual(0, idA, $"{label}: equivalent row not found in fixture A.");
            Assert.AreNotEqual(0, idB, $"{label}: equivalent row not found in fixture B.");
            Assert.AreNotEqual(idA, idB, $"{label}: local numeric id was identical ({idA}) in both fixtures — scrambling did not actually displace this table's id.");
        }

        await AssertDifferentAsync("Documents", "SELECT Id FROM Documents WHERE Title = 'Bank Doc'");
        await AssertDifferentAsync("Words", "SELECT Id FROM Words WHERE CanonicalTerm = 'bank'");
        await AssertDifferentAsync(
            "WordForms",
            "SELECT wf.Id FROM WordForms wf JOIN Words w ON w.Id = wf.WordId WHERE w.CanonicalTerm = 'bank' AND wf.SurfaceForm = 'bank'");
        await AssertDifferentAsync(
            "SentenceSpans",
            "SELECT ss.Id FROM SentenceSpans ss JOIN Documents d ON d.Id = ss.DocumentId WHERE d.Title = 'Bank Doc' AND ss.\"Order\" = 0");
        await AssertDifferentAsync(
            "WordOccurrences",
            "SELECT wo.Id FROM WordOccurrences wo JOIN Words w ON w.Id = wo.WordId WHERE w.CanonicalTerm = 'bank' AND wo.SurfaceForm = 'bank'");
        await AssertDifferentAsync("Meanings (meaning1)", "SELECT Id FROM Meanings WHERE StableId = ?", LocalIdIndependenceMeaning1StableId);
        await AssertDifferentAsync("Meanings (meaning2)", "SELECT Id FROM Meanings WHERE StableId = ?", LocalIdIndependenceMeaning2StableId);
        await AssertDifferentAsync("ContextSnapshots", "SELECT Id FROM ContextSnapshots WHERE NormalizedFingerprint = 'fp-fixed-0001'");
        await AssertDifferentAsync("Senses", "SELECT Id FROM Senses WHERE StableId = 'se-fixed-0001'");
        await AssertDifferentAsync("AnswerVariants (term)", "SELECT Id FROM AnswerVariants WHERE StableId = ?", LocalIdIndependenceVariantTermStableId);
        await AssertDifferentAsync("AnswerVariants (trans1)", "SELECT Id FROM AnswerVariants WHERE StableId = ?", LocalIdIndependenceVariantTrans1StableId);
        await AssertDifferentAsync("AnswerVariants (trans2)", "SELECT Id FROM AnswerVariants WHERE StableId = ?", LocalIdIndependenceVariantTrans2StableId);
        await AssertDifferentAsync("SenseAnswerVariantAssignments (term)", "SELECT Id FROM SenseAnswerVariantAssignments WHERE StableId = ?", LocalIdIndependenceAssignmentTermStableId);
        await AssertDifferentAsync("SenseAnswerVariantAssignments (trans1)", "SELECT Id FROM SenseAnswerVariantAssignments WHERE StableId = ?", LocalIdIndependenceAssignmentTrans1StableId);
        await AssertDifferentAsync("SenseAnswerVariantAssignments (trans2)", "SELECT Id FROM SenseAnswerVariantAssignments WHERE StableId = ?", LocalIdIndependenceAssignmentTrans2StableId);
        await AssertDifferentAsync(
            "LearningCards (MeaningToTerm)",
            "SELECT lc.Id FROM LearningCards lc JOIN Senses s ON s.Id = lc.SenseId WHERE s.StableId = 'se-fixed-0001' AND lc.Direction = ?",
            (int)CardDirection.MeaningToTerm);
        await AssertDifferentAsync(
            "LearningCards (TermToMeaning)",
            "SELECT lc.Id FROM LearningCards lc JOIN Senses s ON s.Id = lc.SenseId WHERE s.StableId = 'se-fixed-0001' AND lc.Direction = ?",
            (int)CardDirection.TermToMeaning);
        await AssertDifferentAsync(
            "LearningReviews (MeaningToTerm card)",
            "SELECT lr.Id FROM LearningReviews lr JOIN LearningCards lc ON lc.Id = lr.CardId JOIN Senses s ON s.Id = lc.SenseId WHERE s.StableId = 'se-fixed-0001' AND lc.Direction = ?",
            (int)CardDirection.MeaningToTerm);
        await AssertDifferentAsync(
            "LearningReviews (TermToMeaning card)",
            "SELECT lr.Id FROM LearningReviews lr JOIN LearningCards lc ON lc.Id = lr.CardId JOIN Senses s ON s.Id = lc.SenseId WHERE s.StableId = 'se-fixed-0001' AND lc.Direction = ?",
            (int)CardDirection.TermToMeaning);
        await AssertDifferentAsync(
            "ReviewSessions",
            "SELECT rs.Id FROM ReviewSessions rs JOIN Documents d ON d.Id = rs.DocumentId WHERE d.Title = 'Bank Doc'");
        await AssertDifferentAsync(
            "ReviewCandidates",
            "SELECT rc.Id FROM ReviewCandidates rc JOIN Words w ON w.Id = rc.WordId WHERE w.CanonicalTerm = 'bank'");
        // PreparationSessions/LearningSessions have no other row in this fixture to disambiguate against —
        // the sole row's own Id is the entire collection's local-id-relationship key here.
        await AssertDifferentAsync("PreparationSessions", "SELECT Id FROM PreparationSessions");
        await AssertDifferentAsync(
            "PreparationCandidates",
            "SELECT pc.Id FROM PreparationCandidates pc JOIN Words w ON w.Id = pc.WordId WHERE w.CanonicalTerm = 'bank'");
        await AssertDifferentAsync("LearningSessions", "SELECT Id FROM LearningSessions");
        await AssertDifferentAsync("LearningSessionCards (QueueOrder=1, term card)", "SELECT Id FROM LearningSessionCards WHERE QueueOrder = 1");
        await AssertDifferentAsync("LearningSessionCards (QueueOrder=2, meaning card)", "SELECT Id FROM LearningSessionCards WHERE QueueOrder = 2");
        await AssertDifferentAsync(
            "AnswerVariantProgress (term)",
            "SELECT avp.Id FROM AnswerVariantProgress avp JOIN AnswerVariants av ON av.Id = avp.AnswerVariantId WHERE av.StableId = ?",
            LocalIdIndependenceVariantTermStableId);
        await AssertDifferentAsync(
            "AnswerVariantProgress (trans1)",
            "SELECT avp.Id FROM AnswerVariantProgress avp JOIN AnswerVariants av ON av.Id = avp.AnswerVariantId WHERE av.StableId = ?",
            LocalIdIndependenceVariantTrans1StableId);
    }

    /// <summary>
    /// Builds a physically valid Schema-8 database directly (bypassing the live dormant migration's own
    /// local-<c>Meaning.Id</c>-based tie-breaks, which are a separate, intentional concern for that
    /// single-database migration and not what this test targets) — one Document/SentenceSpan/
    /// WordOccurrence/WordForm, one Word with two grouped Meanings under one Sense, three AnswerVariants,
    /// three Assignments, two Cards (both directions), one LearningSession with two LearningReviews and
    /// two LearningSessionCards, one ReviewSession/ReviewCandidate pair, one PreparationSession/
    /// PreparationCandidate pair, and two AnswerVariantProgress rows — every StableId and content field
    /// fixed and identical between the two <paramref name="scramble"/> variants; only local integer ids,
    /// insertion order, and (when <paramref name="scramble"/> is true) every table's displaced
    /// AUTOINCREMENT sequence differ.
    /// </summary>
    private static async Task<Schema7Fixture> BuildLocalIdScrambledSchema8FixtureAsync(bool scramble)
    {
        var fixture = await Schema8BackupFixtureBuilders.CreateEmptySchema8FixtureAsync();
        var conn = fixture.Connection;
        var ts = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        const string docContent = "the bank text";
        var docFingerprint = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(docContent))).ToLowerInvariant();

        if (scramble)
        {
            // Displace every major table's AUTOINCREMENT sequence before inserting any "real" row below,
            // by pre-seeding sqlite_sequence directly — reliable even for tables that end up with only one
            // or two rows in this fixture, where reversed relative insertion order alone cannot produce a
            // different local id (a single-row table has no order to reverse). Combined with the reversed
            // insertion order used below for the multi-row collections, every one of the 16 required
            // collections ends up with a local integer id different from the non-scrambled fixture — never
            // merely assumed, this is what LocalIdIndependence_*'s own
            // AssertLocalIdsDifferAcrossEveryMajorCollectionAsync call verifies before serializing.
            foreach (var table in new[]
            {
                "Documents", "Words", "WordForms", "SentenceSpans", "WordOccurrences", "Meanings", "Senses",
                "AnswerVariants", "SenseAnswerVariantAssignments", "LearningCards", "LearningReviews",
                "ReviewSessions", "ReviewCandidates", "PreparationSessions", "PreparationCandidates",
                "LearningSessions", "LearningSessionCards", "AnswerVariantProgress", "ContextSnapshots"
            })
            {
                await conn.ExecuteAsync("INSERT INTO sqlite_sequence (name, seq) VALUES (?, 100)", table);
            }
        }

        // ---- Document + SentenceSpan + WordOccurrence + Word + WordForm ----
        await conn.ExecuteAsync(
            "INSERT INTO Documents (Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content, ContentFingerprint, ImportedAt, WordCount) VALUES (?, 'en', 'de', 0, '', ?, ?, ?, 1)",
            "Bank Doc", docContent, docFingerprint, ts);
        var docId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await conn.ExecuteAsync(
            "INSERT INTO SentenceSpans (DocumentId, StartPosition, Length, \"Order\") VALUES (?, 0, ?, 0)",
            docId, docContent.Length);
        var sentenceId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await conn.ExecuteAsync(
            "INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState, TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt) VALUES ('en','bank','bank',?,0,0,1,1,0,0,0,0,0,?,?)",
            (int)WordStatus.UnknownBacklog, ts, ts);
        var wordId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await conn.ExecuteAsync(
            "INSERT INTO WordOccurrences (WordId, DocumentId, SentenceSpanId, StartPosition, Length, SurfaceForm, TechnicalFamily, TechnicalInstanceYear, TechnicalInstanceIdentifier, TechnicalVariant, \"Order\") VALUES (?, ?, ?, 4, 4, 'bank', 0, NULL, '', '', 0)",
            wordId, docId, sentenceId);

        await conn.ExecuteAsync(
            "INSERT INTO WordForms (WordId, SurfaceForm, OccurrenceCount) VALUES (?, 'bank', 1)", wordId);

        // ---- Sense (DefaultMeaningId deferred until the Meanings below exist) ----
        await conn.ExecuteAsync(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('se-fixed-0001', ?, 'en', 'de', 'finance-1', '', '', '', '', NULL, ?, ?, ?)
            """,
            wordId, (int)SenseStatus.Learning, ts, ts);
        var senseId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        // ---- Two grouped Meanings (order reversed when scrambled) ----
        async Task<int> InsertMeaningAsync(string stableId, string translation, string definition)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO Meanings
                    (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                     EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                     AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                     AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                     SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
                VALUES (?, ?, ?, 'de', 'en', 'bank', 'bank', '', 0, 'finance-1', '', ?, ?, '', '', '[]', ?, 'Manual', '', '', NULL, '', 1, ?, ?, ?)
                """,
                wordId, senseId, stableId, translation, definition, translation, ts, ts, ts);
            return await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        }

        int meaning1Id, meaning2Id;
        if (!scramble)
        {
            meaning1Id = await InsertMeaningAsync(LocalIdIndependenceMeaning1StableId, "Bank", "financial institution");
            meaning2Id = await InsertMeaningAsync(LocalIdIndependenceMeaning2StableId, "Kreditinstitut", "money-handling institution");
        }
        else
        {
            meaning2Id = await InsertMeaningAsync(LocalIdIndependenceMeaning2StableId, "Kreditinstitut", "money-handling institution");
            meaning1Id = await InsertMeaningAsync(LocalIdIndependenceMeaning1StableId, "Bank", "financial institution");
        }

        await conn.ExecuteAsync("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning1Id, senseId);

        // ---- ContextSnapshot (attached to meaning1) ----
        await conn.ExecuteAsync(
            """
            INSERT INTO ContextSnapshots
                (MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart, TargetLength, NormalizedFingerprint, CreatedAtUtc)
            VALUES (?, ?, ?, ?, 'Bank Doc', ?, 4, 4, 'fp-fixed-0001', ?)
            """,
            meaning1Id, senseId, wordId, docId, docContent, ts);

        // ---- Three AnswerVariants (order reversed when scrambled) ----
        async Task<int> InsertVariantAsync(string stableId, string language, string displayText, string normalizedText, int sourceMeaningId)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO AnswerVariants (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                stableId, senseId, language, displayText, normalizedText, sourceMeaningId, ts, ts);
            return await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        }

        int variantTermId, variantTrans1Id, variantTrans2Id;
        if (!scramble)
        {
            variantTermId = await InsertVariantAsync(LocalIdIndependenceVariantTermStableId, "en", "bank", "bank", meaning1Id);
            variantTrans1Id = await InsertVariantAsync(LocalIdIndependenceVariantTrans1StableId, "de", "Bank", "bank", meaning1Id);
            variantTrans2Id = await InsertVariantAsync(LocalIdIndependenceVariantTrans2StableId, "de", "Kreditinstitut", "kreditinstitut", meaning2Id);
        }
        else
        {
            variantTrans2Id = await InsertVariantAsync(LocalIdIndependenceVariantTrans2StableId, "de", "Kreditinstitut", "kreditinstitut", meaning2Id);
            variantTrans1Id = await InsertVariantAsync(LocalIdIndependenceVariantTrans1StableId, "de", "Bank", "bank", meaning1Id);
            variantTermId = await InsertVariantAsync(LocalIdIndependenceVariantTermStableId, "en", "bank", "bank", meaning1Id);
        }

        // ---- Three Assignments (order reversed when scrambled) ----
        // Every assignment here is AcceptedOnly, so RequiredSinceUtc is written as an explicit NULL
        // (KF-MEANING-001 Slice 4 invariant I1) rather than left to the column default.
        async Task InsertAssignmentAsync(string stableId, CardDirection direction, int variantId, bool isPreferred)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO SenseAnswerVariantAssignments (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, NULL, ?, ?)
                """,
                stableId, senseId, (int)direction, variantId, (int)AnswerVariantRequirement.AcceptedOnly, isPreferred, ts, ts);
        }

        if (!scramble)
        {
            await InsertAssignmentAsync(LocalIdIndependenceAssignmentTermStableId, CardDirection.MeaningToTerm, variantTermId, isPreferred: true);
            await InsertAssignmentAsync(LocalIdIndependenceAssignmentTrans1StableId, CardDirection.TermToMeaning, variantTrans1Id, isPreferred: true);
            await InsertAssignmentAsync(LocalIdIndependenceAssignmentTrans2StableId, CardDirection.TermToMeaning, variantTrans2Id, isPreferred: false);
        }
        else
        {
            await InsertAssignmentAsync(LocalIdIndependenceAssignmentTrans2StableId, CardDirection.TermToMeaning, variantTrans2Id, isPreferred: false);
            await InsertAssignmentAsync(LocalIdIndependenceAssignmentTrans1StableId, CardDirection.TermToMeaning, variantTrans1Id, isPreferred: true);
            await InsertAssignmentAsync(LocalIdIndependenceAssignmentTermStableId, CardDirection.MeaningToTerm, variantTermId, isPreferred: true);
        }

        // ---- Two LearningCards (order reversed when scrambled) ----
        async Task<int> InsertCardAsync(CardDirection direction, int intervalDays)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO LearningCards
                    (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                     SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, 2.5, 1, 0, ?, ?, ?, ?)
                """,
                wordId, senseId, meaning1Id, (int)direction, (int)CardState.Review, ts, intervalDays, ts, (int)ReviewRating.Good, ts, ts);
            return await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        }

        int cardTermId, cardMeaningId;
        if (!scramble)
        {
            cardTermId = await InsertCardAsync(CardDirection.MeaningToTerm, 3);
            cardMeaningId = await InsertCardAsync(CardDirection.TermToMeaning, 5);
        }
        else
        {
            cardMeaningId = await InsertCardAsync(CardDirection.TermToMeaning, 5);
            cardTermId = await InsertCardAsync(CardDirection.MeaningToTerm, 3);
        }

        // ---- LearningSession + two LearningReviews + two LearningSessionCards (order reversed when scrambled) ----
        await conn.ExecuteAsync(
            """
            INSERT INTO LearningSessions (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, 2, 2, 0, 0, 2, 0, ?, ?, ?)
            """,
            (int)LearningSessionStatus.Completed, ts, ts, ts);
        var learningSessionId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        async Task InsertReviewAsync(int cardId, bool wasTypedAnswer, int? targetVariantId, int? matchedVariantId, int intervalDays)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO LearningReviews (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId)
                VALUES (?, ?, ?, ?, 1, ?, ?, ?, 2.5, ?, ?)
                """,
                cardId, learningSessionId, (int)ReviewRating.Good, wasTypedAnswer, ts, ts, intervalDays, targetVariantId, matchedVariantId);
        }

        async Task InsertQueueItemAsync(int cardId, int queueOrder, bool isCompleted, int? targetVariantId)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO LearningSessionCards (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
                VALUES (?, ?, ?, 1, 0, ?, ?, ?, ?, ?, ?, ?)
                """,
                learningSessionId, cardId, queueOrder, isCompleted, isCompleted, isCompleted, isCompleted,
                isCompleted ? (int)ReviewRating.Good : (int?)null, isCompleted ? ts : (DateTime?)null, targetVariantId);
        }

        if (!scramble)
        {
            await InsertReviewAsync(cardTermId, wasTypedAnswer: true, variantTermId, variantTermId, 3);
            await InsertReviewAsync(cardMeaningId, wasTypedAnswer: false, variantTrans1Id, null, 5);
            await InsertQueueItemAsync(cardTermId, 1, isCompleted: true, variantTermId);
            await InsertQueueItemAsync(cardMeaningId, 2, isCompleted: false, variantTrans1Id);
        }
        else
        {
            await InsertQueueItemAsync(cardMeaningId, 2, isCompleted: false, variantTrans1Id);
            await InsertQueueItemAsync(cardTermId, 1, isCompleted: true, variantTermId);
            await InsertReviewAsync(cardMeaningId, wasTypedAnswer: false, variantTrans1Id, null, 5);
            await InsertReviewAsync(cardTermId, wasTypedAnswer: true, variantTermId, variantTermId, 3);
        }

        // ---- Two AnswerVariantProgress rows (order reversed when scrambled) ----
        async Task InsertProgressAsync(int cardId, int variantId)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO AnswerVariantProgress
                    (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount, ConsecutiveTypingSuccessCount,
                     ConsecutiveTypingFailureCount, LastAssessedAtUtc, MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, 2, 1, 0, ?, 0, 0, 1, ?, ?)
                """,
                cardId, variantId, (int)BackupLearningInteractionMode.Reading, ts, ts, ts);
        }

        if (!scramble)
        {
            await InsertProgressAsync(cardTermId, variantTermId);
            await InsertProgressAsync(cardMeaningId, variantTrans1Id);
        }
        else
        {
            await InsertProgressAsync(cardMeaningId, variantTrans1Id);
            await InsertProgressAsync(cardTermId, variantTermId);
        }

        // ---- ReviewSession + ReviewCandidate ----
        await conn.ExecuteAsync(
            """
            INSERT INTO ReviewSessions (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount, DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, ?, 1, 1, 1, 0, 0, 1, ?, ?)
            """,
            docId, (int)ReviewSessionStatus.Completed, ts, ts);
        var reviewSessionId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await conn.ExecuteAsync(
            """
            INSERT INTO ReviewCandidates (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount, PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
            VALUES (?, ?, 0, ?, ?, 0, 0, ?, 1, 1, ?)
            """,
            reviewSessionId, wordId, (int)WordStatus.Known, (int)WordStatus.Unreviewed, ts, ts);

        // ---- PreparationSession + PreparationCandidate ----
        await conn.ExecuteAsync(
            """
            INSERT INTO PreparationSessions (Status, Method, TotalItems, CompletedItems, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, ?, 1, 1, ?, ?, ?)
            """,
            (int)PreparationSessionStatus.Completed, (int)PreparationMethod.Manual, ts, ts, ts);
        var preparationSessionId = await conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await conn.ExecuteAsync(
            """
            INSERT INTO PreparationCandidates (SessionId, WordId, "Order", Status, ResultJson, SelectedMeaningIndex, LastErrorCode, LookupAttemptCount, UpdatedAtUtc)
            VALUES (?, ?, 0, ?, '', 0, '', 1, ?)
            """,
            preparationSessionId, wordId, (int)PreparationCandidateStatus.Prepared, ts);

        return fixture;
    }

    [TestMethod]
    public async Task ValidatePortableRecoveryScopeV2_ActivePreparationBatch_IsRejected()
    {
        var payload = BackupArchiveV1UpgradePolicy.Upgrade(BackupTestData.CreateMaximumPayload());
        var activePreparation = payload.Workflows.PreparationBatches[0] with { Status = BackupPreparationSessionStatus.Active };
        var activePayload = payload with { Workflows = payload.Workflows with { PreparationBatches = [activePreparation] }, Extensions = new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()) };

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(activePayload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), new ValidatedSchema8Capability(), DateTime.UtcNow, stream, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(() => BackupArchiveReader.ValidateVersionedAsync(new MemoryStream(stream.ToArray()), CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, exception.Code);
    }

    [TestMethod]
    public async Task ValidatePortableRecoveryScopeV2_ActiveVocabularyReview_IsRejected()
    {
        var payload = BackupArchiveV1UpgradePolicy.Upgrade(BackupTestData.CreateMaximumPayload());
        var activeReview = payload.Workflows.VocabularyReviews[0] with { Status = BackupReviewSessionStatus.Active };
        var activePayload = payload with { Workflows = payload.Workflows with { VocabularyReviews = [activeReview] }, Extensions = new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()) };

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(activePayload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), new ValidatedSchema8Capability(), DateTime.UtcNow, stream, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(() => BackupArchiveReader.ValidateVersionedAsync(new MemoryStream(stream.ToArray()), CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, exception.Code);
    }

    [TestMethod]
    public async Task ValidatePortableRecoveryScopeV2_ActiveLearningSession_WithSourceSchema9_IsRejected()
    {
        var payload = BackupArchiveV1UpgradePolicy.Upgrade(BackupTestData.CreateMaximumPayload());

        var inactiveReviews = payload.Workflows.VocabularyReviews.Select(r => r with { Status = BackupReviewSessionStatus.Completed }).ToList();
        var inactiveBatches = payload.Workflows.PreparationBatches.Select(b => b with { Status = BackupPreparationSessionStatus.Completed }).ToList();

        var activeLearning = payload.Workflows.LearningSessions[0] with { Status = BackupLearningSessionStatus.Active };
        var activePayload = payload with { Workflows = payload.Workflows with { LearningSessions = [activeLearning], VocabularyReviews = inactiveReviews, PreparationBatches = inactiveBatches }, Extensions = new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()) };

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(activePayload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), new ValidatedSchema9Capability(), DateTime.UtcNow, stream, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(() => BackupArchiveReader.ValidateVersionedAsync(new MemoryStream(stream.ToArray()), CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.ActiveWorkflowUnsupported, exception.Code);
    }

    [TestMethod]
    public async Task ValidatePortableRecoveryScopeV2_ActiveLearningSession_WithSourceSchema10_IsAccepted()
    {
        var payload = BackupArchiveV1UpgradePolicy.Upgrade(BackupTestData.CreateMaximumPayload());

        // Sanitize other active workflows to not trigger rejection early
        var inactiveReviews = payload.Workflows.VocabularyReviews.Select(r => r with { Status = BackupReviewSessionStatus.Completed }).ToList();
        var inactiveBatches = payload.Workflows.PreparationBatches.Select(b => b with { Status = BackupPreparationSessionStatus.Completed }).ToList();

        var activeLearning = payload.Workflows.LearningSessions[0] with { Status = BackupLearningSessionStatus.Active, StableId = new string('a', 32) };
        var activeItem = activeLearning.QueueItems[0] with { StableId = new string('b', 32) };
        activeLearning = activeLearning with { QueueItems = [activeItem] };
        var activePayload = payload with { Workflows = payload.Workflows with { LearningSessions = [activeLearning], VocabularyReviews = inactiveReviews, PreparationBatches = inactiveBatches }, Extensions = new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()) };

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(activePayload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), new ValidatedSchema10Capability(), DateTime.UtcNow, stream, CancellationToken.None);
        var validated = await BackupArchiveReader.ValidateVersionedAsync(new MemoryStream(stream.ToArray()), CancellationToken.None);
        Assert.IsNotNull(validated.V2);
    }

    private const string LocalIdIndependenceMeaning1StableId = "me-fixed-0001";
    private const string LocalIdIndependenceMeaning2StableId = "me-fixed-0002";
    private const string LocalIdIndependenceVariantTermStableId = "av-fixed-0001";
    private const string LocalIdIndependenceVariantTrans1StableId = "av-fixed-0002";
    private const string LocalIdIndependenceVariantTrans2StableId = "av-fixed-0003";
    private const string LocalIdIndependenceAssignmentTermStableId = "sa-fixed-0001";
    private const string LocalIdIndependenceAssignmentTrans1StableId = "sa-fixed-0002";
    private const string LocalIdIndependenceAssignmentTrans2StableId = "sa-fixed-0003";

    /// <summary>Small, hand-built minimal-but-valid v2 payloads for graph-validation unit tests, avoiding
    /// the overhead of a full SQLite fixture for pure in-memory invariant checks.</summary>
    private static class MinimalV2PayloadBuilder
    {
        public static BackupPayloadV2 Build()
        {
            var now = DateTime.UtcNow;
            var vocab = new BackupVocabularyItem(
                "v-000001", "en", "bank", "bank", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed,
                BackupPreparationState.Prepared, 1, 1, now, now, [],
                new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false), []);

            var sense = new BackupSense(
                "se-000001", "se-stable-1", "v-000001", "en", "de", "", "", "", "", "", "m-000001",
                BackupSenseStatus.Learning, now, now);

            var meaning = new BackupPreparedItemV2(
                "m-000001", "se-000001", "m-stable-1", "v-000001", "en", "de", "bank", null, null,
                BackupTokenKind.Word, null, null, "Bank", null, null, null, null, [], true,
                new BackupSourceReference("Manual", "", "", null, ""), now, now, now, []);

            var variant = new BackupAnswerVariant(
                "av-000001", "av-stable-1", "se-000001", "de", "Bank", "bank", "m-000001", now, now);

            // AcceptedOnly, so the Slice-4 boundary is explicitly null rather than defaulted silently.
            var assignment = new BackupSenseAnswerVariantAssignment(
                "sa-000001", "sa-stable-1", "se-000001", BackupCardDirection.TermToMeaning, "av-000001",
                BackupAnswerVariantRequirement.AcceptedOnly, true, now, now, RequiredSinceUtc: null);

            var card = new BackupLearningCardV2(
                "c-000001", "v-000001", "se-000001", "m-000001", BackupCardDirection.TermToMeaning,
                BackupCardState.Review, now, 1, 2.5, 0, 0, null, null, now, now);

            return new BackupPayloadV2(
                [], [vocab], [sense], [meaning], [variant], [assignment], [],
                new BackupLearningDataV2([card], []),
                new BackupWorkflowDataV2([], [], []),
                new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));
        }

        public static BackupPayloadV2 BuildWithContext()
        {
            var payload = Build();
            var doc = new BackupSourceMaterial(
                "sm-000001", "Doc", "en", "de", BackupLexicalLookupMode.Translation, null, "bank text",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("bank text"))).ToLowerInvariant(),
                DateTime.UtcNow, 2, [], []);
            var context = new BackupContextSnapshotV2("sm-000001", "Doc", "bank text", 0, 4, "fp-1", DateTime.UtcNow, "se-000001");
            var meaning = payload.PreparedLearning[0] with { Contexts = [context] };
            return payload with { SourceMaterials = [doc], PreparedLearning = [meaning] };
        }

        public static BackupPayloadV2 BuildWithTwoVariants()
        {
            var payload = Build();
            var now = DateTime.UtcNow;
            var secondVariant = new BackupAnswerVariant(
                "av-000002", "av-stable-2", "se-000001", "de", "Kreditinstitut", "kreditinstitut", "m-000001", now, now);
            var secondAssignment = new BackupSenseAnswerVariantAssignment(
                "sa-000002", "sa-stable-2", "se-000001", BackupCardDirection.TermToMeaning, "av-000002",
                BackupAnswerVariantRequirement.AcceptedOnly, false, now, now, RequiredSinceUtc: null);
            return payload with
            {
                AnswerVariants = [.. payload.AnswerVariants, secondVariant],
                SenseAnswerVariantAssignments = [.. payload.SenseAnswerVariantAssignments, secondAssignment]
            };
        }
    }
}
