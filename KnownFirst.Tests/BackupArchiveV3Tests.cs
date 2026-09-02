using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

[TestClass]
public sealed class BackupArchiveV3Tests
{
    private const string CausalOrderFeature = "learning-review-causal-order-v1";

    [TestMethod]
    public async Task ValidateVersionedAsync_ValidFormat3Archive_IsAccepted()
    {
        using var stream = BuildArchiveV3();

        var envelope = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);

        Assert.AreEqual(3, envelope.FormatVersion);
        Assert.IsNull(envelope.V1);
        Assert.IsNull(envelope.V2);
        Assert.IsNotNull(envelope.V3);
        Assert.AreEqual(3, envelope.V3.Manifest.FormatVersion);
        Assert.AreEqual(13, envelope.V3.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(1, envelope.V3.Payload.WordLearningControls.Count);
        Assert.AreEqual(1, envelope.V3.Payload.SenseLearningControls.Count);
        Assert.AreEqual(0, envelope.V3.Payload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual(1, envelope.V3.Payload.FsrsCardStates.Count);
        Assert.AreEqual(BackupFsrsCardStateKind.New, envelope.V3.Payload.FsrsCardStates[0].State);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_WithEqualTimestampsAndDistinctStableIds_IsAccepted()
    {
        var time = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var events = new List<Fsrs6ReviewEvent>
        {
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Again),
            new(new DateTimeOffset(time, TimeSpan.Zero), ReviewRating.Hard)
        };
        var replayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), events);

        using var stream = BuildArchiveV3(
            payloadMutator: payload =>
            {
                var history = new List<BackupFsrsReviewHistoryEntry>
                {
                    new("hist_1", "card_1", 1, BackupReviewRating.Again, time),
                    new("hist_2", "card_1", 2, BackupReviewRating.Hard, time)
                };

                var stateKind = replayed.State switch
                {
                    Fsrs6CardState.New => BackupFsrsCardStateKind.New,
                    Fsrs6CardState.Learning => BackupFsrsCardStateKind.Learning,
                    Fsrs6CardState.Review => BackupFsrsCardStateKind.Review,
                    Fsrs6CardState.Relearning => BackupFsrsCardStateKind.Relearning,
                    _ => throw new InvalidOperationException()
                };

                var states = new List<BackupFsrsCardState>
                {
                    new("card_1", stateKind, replayed.Stability, replayed.Difficulty, replayed.LastReviewedAtUtc?.UtcDateTime, replayed.StepIndex, replayed.DueAtUtc?.UtcDateTime)
                };

                return payload with
                {
                    FsrsReviewHistoryEntries = history,
                    FsrsCardStates = states
                };
            });

        var envelope = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);

        Assert.AreEqual(3, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V3);
        Assert.AreEqual(2, envelope.V3.Payload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual(envelope.V3.Payload.FsrsReviewHistoryEntries[0].ReviewedAtUtc, envelope.V3.Payload.FsrsReviewHistoryEntries[1].ReviewedAtUtc);
        Assert.AreNotEqual(envelope.V3.Payload.FsrsReviewHistoryEntries[0].StableId, envelope.V3.Payload.FsrsReviewHistoryEntries[1].StableId);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_FeatureMarkedV3_WithEqualTimestampInteractionReviews_IsAccepted()
    {
        using var stream = BuildArchiveV3(
            payloadMutator: AddEqualTimestampInteractionReviews,
            requiredFeatures: [CausalOrderFeature]);

        var envelope = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { CausalOrderFeature }, envelope.V3!.Manifest.RequiredFeatures.ToArray());
        Assert.HasCount(4, envelope.V3.Payload.Learning.ReviewEvents);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_UnmarkedV3_WithEqualTimestampInteractionReviews_FailsClosed()
    {
        using var stream = BuildArchiveV3(payloadMutator: AddEqualTimestampInteractionReviews);

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_V3WithUnknownRequiredFeature_RemainsRejected()
    {
        using var stream = BuildArchiveV3(requiredFeatures: ["unknown-required-v3-feature"]);

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.UnsupportedRequiredFeature, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_MissingRequiredCollections_FailsValidation()
    {
        using var stream = BuildArchiveV3(
            dataMutator: data => data.Replace("\"wordLearningControls\":[", "\"missingWordLearningControls\":[", StringComparison.Ordinal));

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.IsTrue(
            exception.Code is BackupErrorCodes.InvariantViolation or BackupErrorCodes.DataJsonInvalid,
            $"Expected InvariantViolation or DataJsonInvalid but got {exception.Code}");
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_FsrsSequenceNumberGap_FailsValidation()
    {
        using var stream = BuildArchiveV3(
            payloadMutator: payload => payload with
            {
                FsrsReviewHistoryEntries =
                [
                    new BackupFsrsReviewHistoryEntry("hist_1", "card_1", 1, BackupReviewRating.Again, new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)),
                    new BackupFsrsReviewHistoryEntry("hist_2", "card_1", 3, BackupReviewRating.Good, new DateTime(2026, 8, 29, 10, 10, 0, DateTimeKind.Utc))
                ]
            });

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_DuplicateFsrsStableId_FailsValidation()
    {
        using var stream = BuildArchiveV3(
            payloadMutator: payload => payload with
            {
                FsrsReviewHistoryEntries =
                [
                    new BackupFsrsReviewHistoryEntry("hist_dup", "card_1", 1, BackupReviewRating.Again, new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)),
                    new BackupFsrsReviewHistoryEntry("hist_dup", "card_1", 2, BackupReviewRating.Good, new DateTime(2026, 8, 29, 10, 10, 0, DateTimeKind.Utc))
                ]
            });

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_UnresolvedCardReference_FailsValidation()
    {
        using var stream = BuildArchiveV3(
            payloadMutator: payload => payload with
            {
                FsrsCardStates =
                [
                    new BackupFsrsCardState("card_nonexistent", BackupFsrsCardStateKind.New, null, null, null, null, null)
                ]
            });

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.MissingReference, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_DuplicateWordControl_FailsValidation()
    {
        using var stream = BuildArchiveV3(
            payloadMutator: payload => payload with
            {
                WordLearningControls =
                [
                    new BackupWordLearningControl("vocab_1", new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)),
                    new BackupWordLearningControl("vocab_1", new DateTime(2026, 8, 29, 10, 5, 0, DateTimeKind.Utc))
                ]
            });

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_DuplicateSenseControl_FailsValidation()
    {
        using var stream = BuildArchiveV3(
            payloadMutator: payload => payload with
            {
                SenseLearningControls =
                [
                    new BackupSenseLearningControl("sense_1", new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)),
                    new BackupSenseLearningControl("sense_1", new DateTime(2026, 8, 29, 10, 5, 0, DateTimeKind.Utc))
                ]
            });

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.DuplicateId, exception.Code);
    }

    [TestMethod]
    public async Task ValidateVersionedAsync_Format3Archive_StateHistoryInconsistency_FailsValidation()
    {
        // Card has history but state claims to be New
        using var stream = BuildArchiveV3(
            payloadMutator: payload => payload with
            {
                FsrsReviewHistoryEntries =
                [
                    new BackupFsrsReviewHistoryEntry("hist_1", "card_1", 1, BackupReviewRating.Again, new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)),
                    new BackupFsrsReviewHistoryEntry("hist_2", "card_1", 2, BackupReviewRating.Good, new DateTime(2026, 8, 29, 10, 10, 0, DateTimeKind.Utc))
                ],
                FsrsCardStates =
                [
                    new BackupFsrsCardState("card_1", BackupFsrsCardStateKind.New, null, null, null, null, null)
                ]
            });

        var exception = await Assert.ThrowsExactlyAsync<BackupFormatException>(
            () => BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None));

        Assert.AreEqual(BackupErrorCodes.InvariantViolation, exception.Code);
    }

    [TestMethod]
    public void V3SourceGeneratedJson_RoundTripsAllV3OnlyTypes()
    {
        var manifest = new BackupManifestV3(
            FormatVersion: 3,
            SourceAppVersion: "1.0.0-test",
            SourceDatabaseSchemaVersion: 13,
            CreatedAtUtc: new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
            SourcePlatform: BackupSourcePlatform.Windows,
            RecordCounts: new BackupRecordCountsV3(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1),
            DataChecksum: "sha256:e8b4f174e98f06f52865961e967a544a0e4c6c22a7f502bc3becc8ef34fbe7a4",
            OptionalFeatures: Array.Empty<string>(),
            RequiredFeatures: Array.Empty<string>());

        var manifestBytes = BackupJsonCodecV3.SerializeManifest(manifest);
        var deserializedManifest = BackupJsonCodecV3.DeserializeManifest(manifestBytes);

        Assert.AreEqual(manifest.FormatVersion, deserializedManifest.FormatVersion);
        Assert.AreEqual(manifest.SourceDatabaseSchemaVersion, deserializedManifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual(manifest.RecordCounts.WordLearningControls, deserializedManifest.RecordCounts.WordLearningControls);
        Assert.AreEqual(manifest.RecordCounts.SenseLearningControls, deserializedManifest.RecordCounts.SenseLearningControls);
        Assert.AreEqual(manifest.RecordCounts.FsrsReviewHistoryEntries, deserializedManifest.RecordCounts.FsrsReviewHistoryEntries);
        Assert.AreEqual(manifest.RecordCounts.FsrsCardStates, deserializedManifest.RecordCounts.FsrsCardStates);

        var payload = new BackupPayloadV3(
            SourceMaterials: Array.Empty<BackupSourceMaterial>(),
            Vocabulary: Array.Empty<BackupVocabularyItem>(),
            Senses: Array.Empty<BackupSense>(),
            PreparedLearning: Array.Empty<BackupPreparedItemV2>(),
            AnswerVariants: Array.Empty<BackupAnswerVariant>(),
            SenseAnswerVariantAssignments: Array.Empty<BackupSenseAnswerVariantAssignment>(),
            AnswerVariantProgress: Array.Empty<BackupAnswerVariantProgress>(),
            Learning: new BackupLearningDataV2(Array.Empty<BackupLearningCardV2>(), Array.Empty<BackupLearningReviewV2>()),
            Workflows: new BackupWorkflowDataV2(Array.Empty<BackupVocabularyReviewWorkflow>(), Array.Empty<BackupPreparationWorkflow>(), Array.Empty<BackupLearningWorkflowV2>()),
            DerivedTermEvidence: Array.Empty<BackupDerivedTermEvidenceV2>(),
            WordLearningControls: new[] { new BackupWordLearningControl("vocab_1", new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)) },
            SenseLearningControls: new[] { new BackupSenseLearningControl("sense_1", new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)) },
            FsrsReviewHistoryEntries: new[] { new BackupFsrsReviewHistoryEntry("hist_1", "card_1", 1, BackupReviewRating.Good, new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc)) },
            FsrsCardStates: new[] { new BackupFsrsCardState("card_1", BackupFsrsCardStateKind.New, null, null, null, null, null) },
            Extensions: new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));

        var dataBytes = BackupJsonCodecV3.SerializeData(payload);
        var deserializedPayload = BackupJsonCodecV3.DeserializeData(dataBytes);

        Assert.AreEqual(1, deserializedPayload.WordLearningControls.Count);
        Assert.AreEqual("vocab_1", deserializedPayload.WordLearningControls[0].VocabularyId);
        Assert.AreEqual(1, deserializedPayload.SenseLearningControls.Count);
        Assert.AreEqual("sense_1", deserializedPayload.SenseLearningControls[0].SenseId);
        Assert.AreEqual(1, deserializedPayload.FsrsReviewHistoryEntries.Count);
        Assert.AreEqual("hist_1", deserializedPayload.FsrsReviewHistoryEntries[0].StableId);
        Assert.AreEqual(1, deserializedPayload.FsrsCardStates.Count);
        Assert.AreEqual(BackupFsrsCardStateKind.New, deserializedPayload.FsrsCardStates[0].State);
    }

    [TestMethod]
    public void V3SourceGeneratedJson_CoversReachableTypes()
    {
        Type[] roots = [typeof(BackupManifestV3), typeof(BackupPayloadV3)];
        foreach (var root in roots)
        {
            Assert.IsNotNull(BackupJsonCodecV3.GetGeneratedTypeInfo(root), $"Missing generated TypeInfo for {root.Name}");
        }
    }

    private static BackupPayloadV3 CreateValidPayloadV3()
    {
        var text = "network";
        var textSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var now = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        return new BackupPayloadV3(
            SourceMaterials:
            [
                new BackupSourceMaterial(
                    "doc_1",
                    "Document 1",
                    "en",
                    "de",
                    BackupLexicalLookupMode.Definition,
                    null,
                    text,
                    textSha,
                    now,
                    1,
                    [new BackupSentenceRange("sent_1", 0, 0, 7)],
                    [new BackupOccurrence("vocab_1", "sent_1", 0, 7, "network", 0, BackupTechnicalTokenFamily.None, null, null, null)])
            ],
            Vocabulary:
            [
                new BackupVocabularyItem(
                    "vocab_1",
                    "en",
                    "network",
                    "en:network",
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
                    "sense_1",
                    "st_sense_1",
                    "vocab_1",
                    "en",
                    "de",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "prep_1",
                    BackupSenseStatus.Learning,
                    now,
                    now)
            ],
            PreparedLearning:
            [
                new BackupPreparedItemV2(
                    "prep_1",
                    "sense_1",
                    "st_prep_1",
                    "vocab_1",
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
                    [new BackupContextSnapshotV2("doc_1", "Document 1", "network", 0, 7, "fp_1", now, "sense_1")])
            ],
            AnswerVariants:
            [
                new BackupAnswerVariant(
                    "ans_1",
                    "st_ans_1",
                    "sense_1",
                    "de",
                    "Netzwerk",
                    "netzwerk",
                    "prep_1",
                    now,
                    now)
            ],
            SenseAnswerVariantAssignments:
            [
                new BackupSenseAnswerVariantAssignment(
                    "asgn_1",
                    "st_asgn_1",
                    "sense_1",
                    BackupCardDirection.MeaningToTerm,
                    "ans_1",
                    BackupAnswerVariantRequirement.Required,
                    true,
                    now,
                    now,
                    now)
            ],
            AnswerVariantProgress: [],
            Learning: new BackupLearningDataV2(
                Cards:
                [
                    new BackupLearningCardV2(
                        "card_1",
                        "vocab_1",
                        "sense_1",
                        "prep_1",
                        BackupCardDirection.MeaningToTerm,
                        BackupCardState.New,
                        now,
                        0,
                        2.5,
                        0,
                        0,
                        null,
                        null,
                        now,
                        now)
                ],
                ReviewEvents: []),
            Workflows: new BackupWorkflowDataV2([], [], []),
            DerivedTermEvidence: [],
            WordLearningControls:
            [
                new BackupWordLearningControl("vocab_1", now)
            ],
            SenseLearningControls:
            [
                new BackupSenseLearningControl("sense_1", now)
            ],
            FsrsReviewHistoryEntries: [],
            FsrsCardStates:
            [
                new BackupFsrsCardState("card_1", BackupFsrsCardStateKind.New, null, null, null, null, null)
            ],
            Extensions: new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));
    }

    private static MemoryStream BuildArchiveV3(
        Func<BackupPayloadV3, BackupPayloadV3>? payloadMutator = null,
        Func<string, string>? dataMutator = null,
        Func<string, string>? manifestMutator = null,
        IReadOnlyList<string>? requiredFeatures = null)
    {
        var payload = CreateValidPayloadV3();
        if (payloadMutator is not null)
        {
            payload = payloadMutator(payload);
        }

        var counts = new BackupRecordCountsV3(
            payload.SourceMaterials.Count,
            payload.SourceMaterials.Sum(s => s.Sentences.Count),
            payload.Vocabulary.Count,
            payload.Vocabulary.Sum(v => v.EncounteredForms.Count),
            payload.SourceMaterials.Sum(s => s.Occurrences.Count),
            payload.PreparedLearning.Count,
            payload.PreparedLearning.Sum(p => p.Contexts.Count),
            payload.Vocabulary.Sum(v => v.LegacyReviewSummaries.Count),
            payload.Workflows.VocabularyReviews.Count,
            payload.Workflows.VocabularyReviews.Sum(w => w.Items.Count),
            payload.Workflows.PreparationBatches.Count,
            payload.Workflows.PreparationBatches.Sum(w => w.Items.Count),
            payload.Learning.Cards.Count,
            payload.Learning.ReviewEvents.Count,
            payload.Workflows.LearningSessions.Count,
            payload.Workflows.LearningSessions.Sum(w => w.QueueItems.Count),
            payload.Senses.Count,
            payload.AnswerVariants.Count,
            payload.SenseAnswerVariantAssignments.Count,
            payload.AnswerVariantProgress.Count,
            payload.DerivedTermEvidence.Count,
            payload.WordLearningControls.Count,
            payload.SenseLearningControls.Count,
            payload.FsrsReviewHistoryEntries.Count,
            payload.FsrsCardStates.Count);

        byte[] dataBytes;
        if (dataMutator is not null)
        {
            var dataJson = Encoding.UTF8.GetString(BackupJsonCodecV3.SerializeData(payload));
            dataJson = dataMutator(dataJson);
            dataBytes = Encoding.UTF8.GetBytes(dataJson);
        }
        else
        {
            dataBytes = BackupJsonCodecV3.SerializeData(payload);
        }

        var checksum = "sha256:" + Convert.ToHexString(SHA256.HashData(dataBytes)).ToLowerInvariant();

        var manifest = new BackupManifestV3(
            FormatVersion: 3,
            SourceAppVersion: "1.0.0-test",
            SourceDatabaseSchemaVersion: 13,
            CreatedAtUtc: new DateTime(2026, 8, 29, 18, 0, 0, DateTimeKind.Utc),
            SourcePlatform: BackupSourcePlatform.Windows,
            RecordCounts: counts,
            DataChecksum: checksum,
            OptionalFeatures: [],
            RequiredFeatures: requiredFeatures ?? []);

        byte[] manifestBytes;
        if (manifestMutator is not null)
        {
            var manifestJson = Encoding.UTF8.GetString(BackupJsonCodecV3.SerializeManifest(manifest));
            manifestJson = manifestMutator(manifestJson);
            manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        }
        else
        {
            manifestBytes = BackupJsonCodecV3.SerializeManifest(manifest);
        }

        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var stream = manifestEntry.Open())
            {
                stream.Write(manifestBytes);
            }

            var dataEntry = zip.CreateEntry("data.json", CompressionLevel.Optimal);
            using (var stream = dataEntry.Open())
            {
                stream.Write(dataBytes);
            }
        }

        output.Position = 0;
        return output;
    }

    private static BackupPayloadV3 AddEqualTimestampInteractionReviews(BackupPayloadV3 payload)
    {
        var reviewedAtUtc = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var reviews = new[]
        {
            new BackupLearningReviewV2("card_1", "learning_1", BackupReviewRating.Good, false, true, reviewedAtUtc, reviewedAtUtc.AddDays(1), 1, 2.5, "ans_1", null),
            new BackupLearningReviewV2("card_1", "learning_1", BackupReviewRating.Good, false, true, reviewedAtUtc, reviewedAtUtc.AddDays(1), 1, 2.5, "ans_1", null),
            new BackupLearningReviewV2("card_1", "learning_1", BackupReviewRating.Again, true, false, reviewedAtUtc, reviewedAtUtc.AddMinutes(10), 0, 2.5, "ans_1", null),
            new BackupLearningReviewV2("card_1", "learning_1", BackupReviewRating.Again, true, false, reviewedAtUtc, reviewedAtUtc.AddMinutes(10), 0, 2.5, "ans_1", null)
        };
        var queue = reviews.Select((review, index) => new BackupLearningQueueItemV2(
            $"queue_{index + 1}",
            "card_1",
            index,
            true,
            index >= 2,
            true,
            review.WasTypedAnswer,
            review.WasCorrect,
            true,
            review.Rating,
            reviewedAtUtc,
            "ans_1",
            $"0123456789abcdef0123456789abcd{index + 1:D2}"))
            .ToList();
        var workflow = new BackupLearningWorkflowV2(
            "learning_1",
            BackupLearningSessionStatus.Completed,
            4,
            4,
            2,
            0,
            2,
            0,
            reviewedAtUtc.AddMinutes(-10),
            reviewedAtUtc,
            reviewedAtUtc,
            queue,
            "0123456789abcdef0123456789abcdec");

        return payload with
        {
            Learning = payload.Learning with { ReviewEvents = reviews },
            Workflows = payload.Workflows with { LearningSessions = [workflow] }
        };
    }
}
