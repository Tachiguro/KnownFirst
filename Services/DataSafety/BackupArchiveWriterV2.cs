using System.IO.Compression;
using System.Security.Cryptography;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>Archive format v2 writer (KF-MEANING-001 Slice 2). Structurally mirrors
/// <see cref="BackupArchiveWriter"/> exactly, against the v2 model graph. Callable only with a
/// <see cref="ValidatedSchema8Capability"/> — obtainable only from <see cref="BackupSchemaCapability.Resolve"/>
/// — so a Schema-7-derived capture can never reach this writer.</summary>
public static class BackupArchiveWriterV2
{
    public static Task WriteArchiveAsync(
        BackupPayloadV2 payload,
        IBackupPlatformInfo platformInfo,
        ValidatedSchema8Capability capability,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return WriteArchiveCoreAsync(
            payload, platformInfo, ValidatedSchema8Capability.SchemaVersion, timestampUtc, destinationStream, cancellationToken);
    }

    /// <summary>The Schema-9 counterpart. Records
    /// <see cref="ValidatedSchema9Capability.SchemaVersion"/> (9) as the manifest's
    /// <c>SourceDatabaseSchemaVersion</c> — never <see cref="ValidatedSchema8Capability.SchemaVersion"/> —
    /// even though the payload graph and validation are otherwise byte-for-byte identical to the Schema-8
    /// overload.</summary>
    public static Task WriteArchiveAsync(
        BackupPayloadV2 payload,
        IBackupPlatformInfo platformInfo,
        ValidatedSchema9Capability capability,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return WriteArchiveCoreAsync(
            payload, platformInfo, ValidatedSchema9Capability.SchemaVersion, timestampUtc, destinationStream, cancellationToken);
    }

    /// <summary>The Schema-10 counterpart. Records
    /// <see cref="ValidatedSchema10Capability.SchemaVersion"/> (10) as the manifest's
    /// <c>SourceDatabaseSchemaVersion</c>, and additionally refuses to emit a learning workflow or queue
    /// row without a valid persistent identity: a Schema-10 source always has one, so a missing value is a
    /// capture defect, and writing it would produce an archive that the reader must then reject.</summary>
    public static Task WriteArchiveAsync(
        BackupPayloadV2 payload,
        IBackupPlatformInfo platformInfo,
        ValidatedSchema10Capability capability,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(payload);
        BackupModelContractV2.ValidateLearningWorkflowStableIds(
            ValidatedSchema10Capability.SchemaVersion, payload);
        return WriteArchiveCoreAsync(
            payload, platformInfo, ValidatedSchema10Capability.SchemaVersion, timestampUtc, destinationStream, cancellationToken);
    }

    /// <summary>The Schema-11 counterpart. Records
    /// <see cref="ValidatedSchema11Capability.SchemaVersion"/> (11) as the manifest's
    /// <c>SourceDatabaseSchemaVersion</c>, and enforces persistent learning workflow identities identically to Schema 10.</summary>
    public static Task WriteArchiveAsync(
        BackupPayloadV2 payload,
        IBackupPlatformInfo platformInfo,
        ValidatedSchema11Capability capability,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(payload);
        BackupModelContractV2.ValidateLearningWorkflowStableIds(
            ValidatedSchema11Capability.SchemaVersion, payload);
        return WriteArchiveCoreAsync(
            payload, platformInfo, ValidatedSchema11Capability.SchemaVersion, timestampUtc, destinationStream, cancellationToken);
    }

    /// <summary>The Schema-12 counterpart. Records
    /// <see cref="ValidatedSchema12Capability.SchemaVersion"/> (12) as the manifest's
    /// <c>SourceDatabaseSchemaVersion</c>, and enforces persistent learning workflow identities identically to Schema 10 and 11.</summary>
    public static Task WriteArchiveAsync(
        BackupPayloadV2 payload,
        IBackupPlatformInfo platformInfo,
        ValidatedSchema12Capability capability,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(payload);
        BackupModelContractV2.ValidateLearningWorkflowStableIds(
            ValidatedSchema12Capability.SchemaVersion, payload);
        return WriteArchiveCoreAsync(
            payload, platformInfo, ValidatedSchema12Capability.SchemaVersion, timestampUtc, destinationStream, cancellationToken);
    }

    private static async Task WriteArchiveCoreAsync(
        BackupPayloadV2 payload,
        IBackupPlatformInfo platformInfo,
        int sourceDatabaseSchemaVersion,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(platformInfo);
        ArgumentNullException.ThrowIfNull(destinationStream);

        BackupModelContractV2.ValidatePayload(payload);
        ValidatePayloadGraphV2(payload);

        var tempDataFile = Path.GetTempFileName();
        try
        {
            byte[] hash;
            using (var fileStream = new FileStream(tempDataFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var dataBytes = BackupJsonCodecV2.SerializeData(payload);

                using (var sha256 = SHA256.Create())
                {
                    hash = sha256.ComputeHash(dataBytes);
                }

                await fileStream.WriteAsync(dataBytes, cancellationToken);
            }

            var hashString = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

            // Record counts come only from the payload actually being serialized (never a second,
            // independently-suppliable source) — RecordCounts cannot disagree with the serialized
            // payload by construction.
            var recordCounts = BackupModelContractV2.CountRecords(payload);

            var manifest = new BackupManifestV2(
                FormatVersion: BackupFormatLimits.CurrentArchiveFormatVersion,
                SourceAppVersion: platformInfo.SourceAppVersion,
                SourceDatabaseSchemaVersion: sourceDatabaseSchemaVersion,
                SourcePlatform: platformInfo.SourcePlatform,
                CreatedAtUtc: timestampUtc,
                RecordCounts: recordCounts,
                OptionalFeatures: Array.Empty<string>(),
                RequiredFeatures: Array.Empty<string>(),
                DataChecksum: hashString);

            var manifestBytes = BackupJsonCodecV2.SerializeManifest(manifest);

            using (var zipArchive = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zipArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var manifestStream = manifestEntry.Open())
                {
                    await manifestStream.WriteAsync(manifestBytes, cancellationToken);
                }

                var dataEntry = zipArchive.CreateEntry("data.json", CompressionLevel.Optimal);
                using (var dataStream = dataEntry.Open())
                using (var fileStream = new FileStream(tempDataFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await fileStream.CopyToAsync(dataStream, cancellationToken);
                }
            }
        }
        finally
        {
            if (File.Exists(tempDataFile))
            {
                try { File.Delete(tempDataFile); } catch { /* Ignore */ }
            }
        }
    }

    internal static void ValidatePayloadGraphV2(BackupPayloadV2 payload)
    {
        EnsureUniqueIds(payload.SourceMaterials.Select(item => item.Id));
        EnsureUniqueIds(payload.Vocabulary.Select(item => item.Id));
        EnsureUniqueIds(payload.Senses.Select(item => item.Id));
        EnsureUniqueIds(payload.PreparedLearning.Select(item => item.Id));
        EnsureUniqueIds(payload.AnswerVariants.Select(item => item.Id));
        EnsureUniqueIds(payload.SenseAnswerVariantAssignments.Select(item => item.Id));
        EnsureUniqueIds(payload.Learning.Cards.Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.VocabularyReviews.Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.PreparationBatches.Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.LearningSessions.Select(item => item.Id));
        EnsureUniqueIds(payload.SourceMaterials.SelectMany(item => item.Sentences).Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.VocabularyReviews.SelectMany(item => item.Items).Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.PreparationBatches.SelectMany(item => item.Items).Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.LearningSessions.SelectMany(item => item.QueueItems).Select(item => item.Id));

        // StableIds: non-empty, and separately unique per collection.
        EnsureUniqueNonEmptyStableIds(payload.Senses.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.PreparedLearning.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.AnswerVariants.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.SenseAnswerVariantAssignments.Select(item => item.StableId));

        var vocabKeys = new HashSet<(string Language, string IdentityKey)>();
        foreach (var item in payload.Vocabulary)
        {
            if (!vocabKeys.Add((item.Language.ToLowerInvariant(), item.IdentityKey.ToLowerInvariant())))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        var vocabIds = payload.Vocabulary.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var docIds = payload.SourceMaterials.Select(sm => sm.Id).ToHashSet(StringComparer.Ordinal);
        var sessionIds = payload.Workflows.LearningSessions.Select(ls => ls.Id).ToHashSet(StringComparer.Ordinal);
        var senseById = payload.Senses.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var meaningById = payload.PreparedLearning.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var variantById = payload.AnswerVariants.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var cardById = payload.Learning.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);

        // ---- SourceMaterials: sentence/occurrence bounds and content-integrity (same rules as v1) ----
        foreach (var doc in payload.SourceMaterials)
        {
            var sentenceIds = new HashSet<string>(StringComparer.Ordinal);
            var sentenceOrders = new HashSet<int>();
            foreach (var sentence in doc.Sentences)
            {
                if (!sentenceIds.Add(sentence.Id))
                {
                    throw new BackupFormatException(BackupErrorCodes.DuplicateId);
                }
                if (!sentenceOrders.Add(sentence.Order))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (sentence.Start < 0 || sentence.Length <= 0 || sentence.Start + sentence.Length > doc.OriginalText.Length)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }

            var occurrenceOrders = new HashSet<int>();
            foreach (var occ in doc.Occurrences)
            {
                if (!vocabIds.Contains(occ.VocabularyId) || !sentenceIds.Contains(occ.SentenceId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (occ.Start < 0 || occ.Length <= 0 || occ.Start + occ.Length > doc.OriginalText.Length)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (!occurrenceOrders.Add(occ.Order))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                var sentence = doc.Sentences.FirstOrDefault(s => s.Id == occ.SentenceId)
                    ?? throw new BackupFormatException(BackupErrorCodes.MissingReference);
                if (occ.Start < sentence.Start || occ.Start + occ.Length > sentence.Start + sentence.Length)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                var expectedSurface = doc.OriginalText.Substring(occ.Start, occ.Length);
                if (!string.Equals(occ.SurfaceForm, expectedSurface, StringComparison.Ordinal))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        // ---- Senses ----
        foreach (var sense in payload.Senses)
        {
            if (!vocabIds.Contains(sense.VocabularyId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            if (sense.DefaultMeaningId is not null)
            {
                if (!meaningById.TryGetValue(sense.DefaultMeaningId, out var defaultMeaning)
                    || defaultMeaning.SenseId != sense.Id)
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }
        }

        // ---- Meanings ----
        foreach (var meaning in payload.PreparedLearning)
        {
            if (!senseById.TryGetValue(meaning.SenseId, out var owningSense))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!vocabIds.Contains(meaning.VocabularyId) || owningSense.VocabularyId != meaning.VocabularyId)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            var contextFingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ctx in meaning.Contexts)
            {
                if (!docIds.Contains(ctx.SourceMaterialId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (ctx.SenseId != meaning.SenseId)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (!contextFingerprints.Add(ctx.NormalizedFingerprint)
                    || ctx.TargetStart < 0 || ctx.TargetLength <= 0
                    || ctx.TargetStart + ctx.TargetLength > ctx.Text.Length)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        // ---- AnswerVariants ----
        var variantUniqueness = new HashSet<(string SenseId, string AnswerLanguage, string NormalizedText)>();
        foreach (var variant in payload.AnswerVariants)
        {
            if (!senseById.ContainsKey(variant.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (variant.SourceMeaningId is not null)
            {
                if (!meaningById.TryGetValue(variant.SourceMeaningId, out var sourceMeaning)
                    || sourceMeaning.SenseId != variant.SenseId)
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }
            if (!variantUniqueness.Add((variant.SenseId, variant.AnswerLanguage, variant.NormalizedText)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // ---- Assignments ----
        var assignmentTriples = new HashSet<(string SenseId, BackupCardDirection Direction, string AnswerVariantId)>();
        var preferredCountByGroup = new Dictionary<(string SenseId, BackupCardDirection Direction), int>();
        var assignmentLookup = new HashSet<(string SenseId, BackupCardDirection Direction, string AnswerVariantId)>();
        foreach (var assignment in payload.SenseAnswerVariantAssignments)
        {
            if (!variantById.TryGetValue(assignment.AnswerVariantId, out var variant) || variant.SenseId != assignment.SenseId)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var triple = (assignment.SenseId, assignment.CardDirection, assignment.AnswerVariantId);
            if (!assignmentTriples.Add(triple))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            assignmentLookup.Add(triple);

            var groupKey = (assignment.SenseId, assignment.CardDirection);
            preferredCountByGroup.TryGetValue(groupKey, out var currentPreferred);
            preferredCountByGroup[groupKey] = currentPreferred + (assignment.IsPreferred ? 1 : 0);
        }

        foreach (var count in preferredCountByGroup.Values)
        {
            if (count != 1)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
        }

        // ---- Cards ----
        var cardKeys = new HashSet<(string SenseId, BackupCardDirection Direction)>();
        foreach (var card in payload.Learning.Cards)
        {
            if (!cardKeys.Add((card.SenseId, card.Direction)))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
            if (!vocabIds.Contains(card.VocabularyId)
                || !senseById.TryGetValue(card.SenseId, out var cardSense)
                || cardSense.VocabularyId != card.VocabularyId)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!meaningById.TryGetValue(card.PreferredMeaningId, out var preferredMeaning)
                || preferredMeaning.SenseId != card.SenseId)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
        }

        // ---- Reviews ----
        foreach (var review in payload.Learning.ReviewEvents)
        {
            if (!cardById.TryGetValue(review.CardId, out var card))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!sessionIds.Contains(review.LearningSessionId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            ValidateVariantForCard(review.TargetAnswerVariantId, card, variantById, assignmentLookup);
            ValidateVariantForCard(review.MatchedAnswerVariantId, card, variantById, assignmentLookup);
        }

        // ---- Workflows: VocabularyReviews / PreparationBatches (reused v1 shape, same checks) ----
        foreach (var vr in payload.Workflows.VocabularyReviews)
        {
            if (!docIds.Contains(vr.SourceMaterialId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            foreach (var item in vr.Items)
            {
                if (!vocabIds.Contains(item.VocabularyId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }
        }

        foreach (var pb in payload.Workflows.PreparationBatches)
        {
            foreach (var item in pb.Items)
            {
                if (!vocabIds.Contains(item.VocabularyId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }
        }

        // ---- Queue rows ----
        foreach (var session in payload.Workflows.LearningSessions)
        {
            foreach (var queueItem in session.QueueItems)
            {
                if (!cardById.TryGetValue(queueItem.CardId, out var card))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                ValidateVariantForCard(queueItem.TargetAnswerVariantId, card, variantById, assignmentLookup);
            }
        }

        // ---- AnswerVariantProgress ----
        var progressKeys = new HashSet<(string CardId, string AnswerVariantId)>();
        foreach (var progress in payload.AnswerVariantProgress)
        {
            if (!progressKeys.Add((progress.CardId, progress.AnswerVariantId)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (!cardById.TryGetValue(progress.CardId, out var card))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            ValidateVariantForCard(progress.AnswerVariantId, card, variantById, assignmentLookup, allowNull: false);
        }

        // ---- DerivedTermEvidence (German Enhanced Term Recognition Package 5A-2) ----
        //
        // Mirrors Data/Migrations/Schema11/Schema11EvidenceValidator.cs's binding physical invariants at the
        // archive-DTO level: owning-reference resolution through the review-item chain to its document,
        // blank-field/range/sentence-containment/surface-form/vocabulary-identity checks, and duplicate
        // semantic evidence — never a weaker portable-only rule.
        var reviewItemOwnerDocumentId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var workflow in payload.Workflows.VocabularyReviews)
        {
            foreach (var item in workflow.Items)
            {
                reviewItemOwnerDocumentId[item.Id] = workflow.SourceMaterialId;
            }
        }

        var vocabularyIdentities = new HashSet<(string Language, string IdentityKey)>(
            payload.Vocabulary.Select(v => (v.Language, v.IdentityKey)));
        var derivedEvidenceKeys = new HashSet<(string ReviewItemId, string SourceIdentity, int Start, int Length, string ComponentForm)>();

        foreach (var evidence in payload.DerivedTermEvidence)
        {
            if (!derivedEvidenceKeys.Add((evidence.ReviewItemId, evidence.SourceIdentity, evidence.SourceStartPosition, evidence.SourceLength, evidence.ComponentForm)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            if (!reviewItemOwnerDocumentId.TryGetValue(evidence.ReviewItemId, out var documentId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var document = payload.SourceMaterials.FirstOrDefault(d => d.Id == documentId)
                ?? throw new BackupFormatException(BackupErrorCodes.MissingReference);

            var widenedEnd = (long)evidence.SourceStartPosition + evidence.SourceLength;
            if (widenedEnd < 0 || widenedEnd > document.OriginalText.Length)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            var actualSubstring = document.OriginalText.Substring(evidence.SourceStartPosition, evidence.SourceLength);
            if (!string.Equals(actualSubstring, evidence.SourceSurfaceForm, StringComparison.Ordinal))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            var matchingSentences = document.Sentences.Where(s => s.Order == evidence.SourceSentenceOrder).ToList();
            if (matchingSentences.Count != 1)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var sentence = matchingSentences[0];
            var widenedSentenceEnd = (long)sentence.Start + sentence.Length;
            if (evidence.SourceStartPosition < sentence.Start || widenedEnd > widenedSentenceEnd)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }

            if (!vocabularyIdentities.Contains((document.TextLanguage, evidence.SourceIdentity)))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
        }
    }

    /// <summary>A variant is "valid for a card's Sense and direction" when it belongs to the card's
    /// Sense and is actually assigned for the card's direction (i.e. a <c>SenseAnswerVariantAssignment</c>
    /// row exists for that exact (SenseId, Direction, AnswerVariantId) triple).</summary>
    private static void ValidateVariantForCard(
        string? variantId,
        BackupLearningCardV2 card,
        Dictionary<string, BackupAnswerVariant> variantById,
        HashSet<(string SenseId, BackupCardDirection Direction, string AnswerVariantId)> assignmentLookup,
        bool allowNull = true)
    {
        if (variantId is null)
        {
            if (!allowNull)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            return;
        }

        if (!variantById.TryGetValue(variantId, out var variant)
            || variant.SenseId != card.SenseId
            || !assignmentLookup.Contains((card.SenseId, card.Direction, variantId)))
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }
    }

    private static void EnsureUniqueIds(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }
    }

    private static void EnsureUniqueNonEmptyStableIds(IEnumerable<string> stableIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stableId in stableIds)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
            if (!seen.Add(stableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }
    }
}
