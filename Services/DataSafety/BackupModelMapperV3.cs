using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Deterministic mapper from <see cref="Schema13BackupSnapshot"/> to <see cref="BackupPayloadV3"/>
/// (KF-BACKUP-006 Slice 2). Reuses <see cref="BackupModelMapperV2"/> to produce the base V2 payload
/// and semantic identifier mappings, then maps and deterministically orders the four Schema-13
/// collections (WordLearningControls, SenseLearningControls, FsrsReviewHistoryEntries, FsrsCardStates).
/// </summary>
public static class BackupModelMapperV3
{
    public static BackupPayloadV3 MapToExternal(Schema13BackupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var context = BackupModelMapperV2.MapToExternalWithContext(snapshot.BaseSnapshot);

        var wordControls = snapshot.WordLearningControls
            .Select(c =>
            {
                if (!context.VocabIdMap.TryGetValue(c.WordId, out var vocabId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                return new BackupWordLearningControl(vocabId, c.DecidedAtUtc);
            })
            .OrderBy(c => c.VocabularyId, StringComparer.Ordinal)
            .ToList();

        var senseControls = snapshot.SenseLearningControls
            .Select(c =>
            {
                if (!context.SenseIdMap.TryGetValue(c.SenseId, out var senseId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                return new BackupSenseLearningControl(senseId, c.DecidedAtUtc);
            })
            .OrderBy(c => c.SenseId, StringComparer.Ordinal)
            .ToList();

        var historyEntries = snapshot.FsrsReviewHistoryEntries
            .Select(h =>
            {
                if (!context.CardIdMap.TryGetValue(h.CardId, out var cardId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                var rating = h.Rating switch
                {
                    ReviewRating.Again => BackupReviewRating.Again,
                    ReviewRating.Hard => BackupReviewRating.Hard,
                    ReviewRating.Good => BackupReviewRating.Good,
                    ReviewRating.Easy => BackupReviewRating.Easy,
                    _ => throw new BackupFormatException(BackupErrorCodes.InvariantViolation)
                };
                return new BackupFsrsReviewHistoryEntry(
                    h.StableId,
                    cardId,
                    h.SequenceNumber,
                    rating,
                    h.ReviewedAtUtc);
            })
            .OrderBy(h => h.CardId, StringComparer.Ordinal)
            .ThenBy(h => h.SequenceNumber)
            .ToList();

        var cardStates = snapshot.FsrsCardStates
            .Select(s =>
            {
                if (!context.CardIdMap.TryGetValue(s.CardId, out var cardId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
                var state = s.State switch
                {
                    Fsrs6CardState.New => BackupFsrsCardStateKind.New,
                    Fsrs6CardState.Learning => BackupFsrsCardStateKind.Learning,
                    Fsrs6CardState.Review => BackupFsrsCardStateKind.Review,
                    Fsrs6CardState.Relearning => BackupFsrsCardStateKind.Relearning,
                    _ => throw new BackupFormatException(BackupErrorCodes.InvariantViolation)
                };
                return new BackupFsrsCardState(
                    cardId,
                    state,
                    s.Stability,
                    s.Difficulty,
                    s.LastReviewedAtUtc,
                    s.StepIndex,
                    s.DueAtUtc);
            })
            .OrderBy(s => s.CardId, StringComparer.Ordinal)
            .ToList();

        var causalReviews = context.LearningReviews
            .OrderBy(review => review.Review.CardId, StringComparer.Ordinal)
            .ThenBy(review => Schema8Utc.Normalize(review.Review.ReviewedAtUtc).Ticks)
            .ThenBy(review => review.SourceLocalId)
            .Select(review => review.Review)
            .ToList();

        return new BackupPayloadV3(
            context.Payload.SourceMaterials,
            context.Payload.Vocabulary,
            context.Payload.Senses,
            context.Payload.PreparedLearning,
            context.Payload.AnswerVariants,
            context.Payload.SenseAnswerVariantAssignments,
            context.Payload.AnswerVariantProgress,
            context.Payload.Learning with { ReviewEvents = causalReviews },
            context.Payload.Workflows,
            context.Payload.DerivedTermEvidence,
            wordControls,
            senseControls,
            historyEntries,
            cardStates,
            context.Payload.Extensions);
    }
}
