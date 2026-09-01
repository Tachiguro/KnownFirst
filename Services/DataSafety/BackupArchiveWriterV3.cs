using System.IO.Compression;
using System.Security.Cryptography;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Archive format v3 writer and graph validator (KF-BACKUP-006 Slice 1).
/// In-memory graph validation and archive construction contract for Schema-13 transport.
/// </summary>
public static class BackupArchiveWriterV3
{
    public static async Task WriteArchiveAsync(
        BackupPayloadV3 payload,
        IBackupPlatformInfo platformInfo,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(platformInfo);
        ArgumentNullException.ThrowIfNull(destinationStream);

        BackupModelContractV3.ValidatePayload(payload);
        ValidatePayloadGraphV3(payload);

        var dataBytes = BackupJsonCodecV3.SerializeData(payload);
        var hash = SHA256.HashData(dataBytes);
        var hashString = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

        var recordCounts = BackupModelContractV3.CountRecords(payload);

        var manifest = new BackupManifestV3(
            FormatVersion: 3,
            SourceAppVersion: platformInfo.SourceAppVersion,
            SourceDatabaseSchemaVersion: BackupModelContractV3.Schema13Version,
            SourcePlatform: platformInfo.SourcePlatform,
            CreatedAtUtc: timestampUtc,
            RecordCounts: recordCounts,
            OptionalFeatures: Array.Empty<string>(),
            RequiredFeatures: new[] { ArchiveLearningReviewCausalOrderPolicy.RequiredFeature },
            DataChecksum: hashString);

        var manifestBytes = BackupJsonCodecV3.SerializeManifest(manifest);

        using var zipArchive = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: true);

        var manifestEntry = zipArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var manifestStream = manifestEntry.Open())
        {
            await manifestStream.WriteAsync(manifestBytes, cancellationToken);
        }

        var dataEntry = zipArchive.CreateEntry("data.json", CompressionLevel.Optimal);
        using (var dataStream = dataEntry.Open())
        {
            await dataStream.WriteAsync(dataBytes, cancellationToken);
        }
    }

    internal static void ValidatePayloadGraphV3(BackupPayloadV3 payload)
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

        EnsureUniqueNonEmptyStableIds(payload.Senses.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.PreparedLearning.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.AnswerVariants.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.SenseAnswerVariantAssignments.Select(item => item.StableId));
        EnsureUniqueNonEmptyStableIds(payload.FsrsReviewHistoryEntries.Select(item => item.StableId));

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

        // SourceMaterials
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

        // Senses
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

        // Meanings
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

        // AnswerVariants
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

        // Assignments
        var assignmentTriples = new HashSet<(string SenseId, BackupCardDirection Direction, string AnswerVariantId)>();
        var preferredCountByGroup = new Dictionary<(string SenseId, BackupCardDirection Direction), int>();
        foreach (var assignment in payload.SenseAnswerVariantAssignments)
        {
            if (!variantById.TryGetValue(assignment.AnswerVariantId, out var variant) || variant.SenseId != assignment.SenseId)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!assignmentTriples.Add((assignment.SenseId, assignment.CardDirection, assignment.AnswerVariantId)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
            if (assignment.IsPreferred)
            {
                var groupKey = (assignment.SenseId, assignment.CardDirection);
                preferredCountByGroup.TryGetValue(groupKey, out var current);
                preferredCountByGroup[groupKey] = current + 1;
            }
        }

        foreach (var (groupKey, count) in preferredCountByGroup)
        {
            if (count > 1)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
        }

        // Progress
        var progressUniqueness = new HashSet<(string CardId, string AnswerVariantId)>();
        foreach (var row in payload.AnswerVariantProgress)
        {
            if (!cardById.TryGetValue(row.CardId, out var card))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!variantById.TryGetValue(row.AnswerVariantId, out var variant) || variant.SenseId != card.SenseId)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!progressUniqueness.Add((row.CardId, row.AnswerVariantId)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // LearningCards
        var cardUniqueness = new HashSet<(string SenseId, BackupCardDirection Direction)>();
        foreach (var card in payload.Learning.Cards)
        {
            if (!senseById.TryGetValue(card.SenseId, out var sense))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!vocabIds.Contains(card.VocabularyId) || sense.VocabularyId != card.VocabularyId)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
            if (!meaningById.TryGetValue(card.PreferredMeaningId, out var meaning) || meaning.SenseId != card.SenseId)
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!cardUniqueness.Add((card.SenseId, card.Direction)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // LearningReviews
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
            if (review.TargetAnswerVariantId is not null
                && (!variantById.TryGetValue(review.TargetAnswerVariantId, out var targetVariant)
                    || targetVariant.SenseId != card.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (review.MatchedAnswerVariantId is not null
                && (!variantById.TryGetValue(review.MatchedAnswerVariantId, out var matchedVariant)
                    || matchedVariant.SenseId != card.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
        }

        // Workflows
        foreach (var wf in payload.Workflows.VocabularyReviews)
        {
            if (!docIds.Contains(wf.SourceMaterialId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            var orders = new HashSet<int>();
            foreach (var item in wf.Items)
            {
                if (!vocabIds.Contains(item.VocabularyId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (!orders.Add(item.Order))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        foreach (var wf in payload.Workflows.PreparationBatches)
        {
            var orders = new HashSet<int>();
            foreach (var item in wf.Items)
            {
                if (!vocabIds.Contains(item.VocabularyId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (!orders.Add(item.Order))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        foreach (var wf in payload.Workflows.LearningSessions)
        {
            var orders = new HashSet<int>();
            foreach (var item in wf.QueueItems)
            {
                if (!cardById.TryGetValue(item.CardId, out var card))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (!orders.Add(item.QueueOrder))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
                if (item.TargetAnswerVariantId is not null
                    && (!variantById.TryGetValue(item.TargetAnswerVariantId, out var targetVariant)
                        || targetVariant.SenseId != card.SenseId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }
        }

        // DerivedTermEvidence
        var reviewItemById = payload.Workflows.VocabularyReviews
            .SelectMany(w => w.Items)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var evidenceTriples = new HashSet<(string ReviewItemId, string SourceIdentity, int StartPosition, int Length, string ComponentForm)>();
        foreach (var evidence in payload.DerivedTermEvidence)
        {
            if (!reviewItemById.TryGetValue(evidence.ReviewItemId, out var reviewItem))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!evidenceTriples.Add((evidence.ReviewItemId, evidence.SourceIdentity, evidence.SourceStartPosition, evidence.SourceLength, evidence.ComponentForm)))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // ---- V3 Collections: WordLearningControls ----
        var seenWordControlVocabIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in payload.WordLearningControls)
        {
            if (!vocabIds.Contains(control.VocabularyId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!seenWordControlVocabIds.Add(control.VocabularyId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // ---- V3 Collections: SenseLearningControls ----
        var seenSenseControlSenseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in payload.SenseLearningControls)
        {
            if (!senseById.ContainsKey(control.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!seenSenseControlSenseIds.Add(control.SenseId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // ---- V3 Collections: FsrsReviewHistoryEntries ----
        var historyByCardId = new Dictionary<string, List<BackupFsrsReviewHistoryEntry>>(StringComparer.Ordinal);
        foreach (var entry in payload.FsrsReviewHistoryEntries)
        {
            if (!cardById.ContainsKey(entry.CardId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            if (!historyByCardId.TryGetValue(entry.CardId, out var list))
            {
                list = [];
                historyByCardId[entry.CardId] = list;
            }
            list.Add(entry);
        }

        foreach (var (cardId, list) in historyByCardId)
        {
            list.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));
            DateTime? previousTime = null;

            for (var i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                var expectedSequence = i + 1;
                if (entry.SequenceNumber != expectedSequence)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (previousTime.HasValue && entry.ReviewedAtUtc < previousTime.Value)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                previousTime = entry.ReviewedAtUtc;
            }
        }

        // ---- V3 Collections: FsrsCardStates ----
        var cardStatesById = new Dictionary<string, BackupFsrsCardState>(StringComparer.Ordinal);
        foreach (var state in payload.FsrsCardStates)
        {
            if (!cardById.ContainsKey(state.CardId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!cardStatesById.TryAdd(state.CardId, state))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        // Exactly one state per represented Learning Card
        if (cardStatesById.Count != payload.Learning.Cards.Count)
        {
            throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
        }

        // State/History Consistency via Fsrs6Replayer
        var replayer = new Fsrs6Replayer();
        foreach (var card in payload.Learning.Cards)
        {
            if (!cardStatesById.TryGetValue(card.Id, out var state))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var cardHistory = historyByCardId.TryGetValue(card.Id, out var hList) ? hList : [];
            if (cardHistory.Count == 0)
            {
                if (state.State != BackupFsrsCardStateKind.New
                    || state.Stability.HasValue
                    || state.Difficulty.HasValue
                    || state.LastReviewedAtUtc.HasValue
                    || state.StepIndex.HasValue)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
            else
            {
                if (state.State == BackupFsrsCardStateKind.New)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                var events = new List<Fsrs6ReviewEvent>(cardHistory.Count);
                foreach (var h in cardHistory)
                {
                    events.Add(new Fsrs6ReviewEvent(
                        new DateTimeOffset(h.ReviewedAtUtc, TimeSpan.Zero),
                        BackupEnumMappings.ToPersistence(h.Rating)));
                }

                Fsrs6Card replayed;
                try
                {
                    replayed = replayer.Replay(Fsrs6Card.New(), events);
                }
                catch (Exception)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if ((int)replayed.State != (int)state.State)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (!AreExactDoublesEqual(replayed.Stability, state.Stability)
                    || !AreExactDoublesEqual(replayed.Difficulty, state.Difficulty))
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (replayed.LastReviewedAtUtc?.UtcDateTime != state.LastReviewedAtUtc)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (replayed.StepIndex != state.StepIndex)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                if (replayed.DueAtUtc?.UtcDateTime != state.DueAtUtc)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }
    }

    private static bool AreExactDoublesEqual(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return BitConverter.DoubleToInt64Bits(left.Value) == BitConverter.DoubleToInt64Bits(right.Value);
    }

    private static void EnsureUniqueIds(IEnumerable<string> ids)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!set.Add(id))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }
    }

    private static void EnsureUniqueNonEmptyStableIds(IEnumerable<string> stableIds)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stableId in stableIds)
        {
            if (string.IsNullOrEmpty(stableId) || !set.Add(stableId))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }
    }
}
