using System.IO.Compression;
using System.Security.Cryptography;
using KnownFirst.Data;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public static class BackupArchiveWriter
{
    public static async Task WriteArchiveAsync(
        BackupPayload payload,
        BackupSnapshot snapshot, // For counts
        IBackupPlatformInfo platformInfo,
        DateTime timestampUtc,
        Stream destinationStream,
        CancellationToken cancellationToken)
    {
        BackupModelContract.ValidatePayload(payload);
        ValidatePayloadGraph(payload);

        // Use a temporary file for data.json to calculate hash and count bytes without keeping it in memory
        var tempDataFile = Path.GetTempFileName();
        try
        {
            byte[] hash;
            using (var fileStream = new FileStream(tempDataFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var dataBytes = BackupJsonCodec.SerializeData(payload);

                using (var sha256 = SHA256.Create())
                {
                    hash = sha256.ComputeHash(dataBytes);
                }

                await fileStream.WriteAsync(dataBytes, cancellationToken);
            }

            var hashString = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

            var manifest = new BackupManifest(
                FormatVersion: BackupFormatLimits.FormatVersion,
                SourceAppVersion: platformInfo.SourceAppVersion,
                SourceDatabaseSchemaVersion: DatabaseSchema.CurrentVersion,
                SourcePlatform: platformInfo.SourcePlatform,
                CreatedAtUtc: timestampUtc,
                RecordCounts: new BackupRecordCounts(
                    snapshot.Documents.Count,
                    snapshot.SentenceSpans.Count,
                    snapshot.Words.Count,
                    snapshot.WordForms.Count,
                    snapshot.WordOccurrences.Count,
                    snapshot.Meanings.Count,
                    snapshot.ContextSnapshots.Count,
                    snapshot.ReviewStates.Count,
                    snapshot.ReviewSessions.Count,
                    snapshot.ReviewCandidates.Count,
                    snapshot.PreparationSessions.Count,
                    snapshot.PreparationCandidates.Count,
                    snapshot.LearningCards.Count,
                    snapshot.LearningReviews.Count,
                    snapshot.LearningSessions.Count,
                    snapshot.LearningSessionCards.Count),
                OptionalFeatures: Array.Empty<string>(),
                RequiredFeatures: Array.Empty<string>(),
                DataChecksum: hashString);

            var manifestBytes = BackupJsonCodec.SerializeManifest(manifest);

            // Create ZIP archive
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

    internal static void ValidatePayloadGraph(BackupPayload payload)
    {
        EnsureUniqueIds(payload.SourceMaterials.Select(item => item.Id));
        EnsureUniqueIds(payload.Vocabulary.Select(item => item.Id));
        EnsureUniqueIds(payload.PreparedLearning.Select(item => item.Id));
        EnsureUniqueIds(payload.Learning.Cards.Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.VocabularyReviews.Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.PreparationBatches.Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.LearningSessions.Select(item => item.Id));
        EnsureUniqueIds(payload.SourceMaterials.SelectMany(item => item.Sentences).Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.VocabularyReviews.SelectMany(item => item.Items).Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.PreparationBatches.SelectMany(item => item.Items).Select(item => item.Id));
        EnsureUniqueIds(payload.Workflows.LearningSessions.SelectMany(item => item.QueueItems).Select(item => item.Id));

        var vocabKeys = new HashSet<(string Language, string IdentityKey)>();
        foreach (var item in payload.Vocabulary)
        {
            var key = (item.Language.ToLowerInvariant(), item.IdentityKey.ToLowerInvariant());
            if (!vocabKeys.Add(key))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        var vocabIds = payload.Vocabulary.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var docIds = payload.SourceMaterials.Select(sm => sm.Id).ToHashSet(StringComparer.Ordinal);
        var meaningIds = payload.PreparedLearning.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var cardIds = payload.Learning.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var sessionIds = payload.Workflows.LearningSessions.Select(ls => ls.Id).ToHashSet(StringComparer.Ordinal);
        var cardKeys = new HashSet<(string VocabularyId, BackupCardDirection Direction)>();

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
                if (!vocabIds.Contains(occ.VocabularyId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (!sentenceIds.Contains(occ.SentenceId))
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

                var sentence = doc.Sentences.FirstOrDefault(s => s.Id == occ.SentenceId);
                if (sentence is null)
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }

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

        foreach (var item in payload.PreparedLearning)
        {
            if (!vocabIds.Contains(item.VocabularyId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            var contextFingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ctx in item.Contexts)
            {
                if (!docIds.Contains(ctx.SourceMaterialId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                if (!contextFingerprints.Add(ctx.NormalizedFingerprint)
                    || ctx.TargetStart < 0
                    || ctx.TargetLength <= 0
                    || ctx.TargetStart + ctx.TargetLength > ctx.Text.Length)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }
            }
        }

        foreach (var card in payload.Learning.Cards)
        {
            if (!cardKeys.Add((card.VocabularyId, card.Direction)))
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
            if (!vocabIds.Contains(card.VocabularyId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!meaningIds.Contains(card.PreparedItemId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
        }

        foreach (var review in payload.Learning.ReviewEvents)
        {
            if (!cardIds.Contains(review.CardId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
            if (!sessionIds.Contains(review.LearningSessionId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }
        }

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

        foreach (var ls in payload.Workflows.LearningSessions)
        {
            foreach (var q in ls.QueueItems)
            {
                if (!cardIds.Contains(q.CardId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }
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
}
