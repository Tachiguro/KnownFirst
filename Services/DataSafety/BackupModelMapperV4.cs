using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Schema14;
using KnownFirst.Data.Targets;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Deterministic mapper from <see cref="Schema14BackupSnapshot"/> to <see cref="BackupPayloadV4"/>
/// (KF-LEARN-011 Slice 5). Reuses <see cref="BackupModelMapperV2"/> for base lexical and workflow entities,
/// and deterministically maps and orders the Schema-14 target collections (WordLearningControls, SenseLearningControls,
/// LearningTargets, TargetAnswerVariants, TargetFsrsStates, TargetFsrsReviewHistoryEntries, and TargetReviews).
/// </summary>
public static class BackupModelMapperV4
{
    public static BackupPayloadV4 MapToExternal(Schema14BackupSnapshot snapshot)
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

        // ---- LearningTargets: ordered by StableId ----
        var sortedTargets = snapshot.LearningTargets
            .OrderBy(t => t.StableId, StringComparer.Ordinal)
            .ToList();

        var targetIdMap = new Dictionary<int, string>();
        for (var i = 0; i < sortedTargets.Count; i++)
        {
            targetIdMap[sortedTargets[i].Id] = $"lt-{(i + 1):D6}";
        }

        var learningTargets = sortedTargets.Select(t =>
        {
            if (!context.SenseIdMap.TryGetValue(t.SenseId, out var senseId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var kind = t.TargetKind switch
            {
                LearningTargetKind.Definition => BackupLearningTargetKind.Definition,
                LearningTargetKind.Translation => BackupLearningTargetKind.Translation,
                _ => throw new BackupFormatException(BackupErrorCodes.InvariantViolation)
            };

            return new BackupLearningTarget(
                Id: targetIdMap[t.Id],
                StableId: t.StableId,
                SenseId: senseId,
                TargetKind: kind,
                SourceLanguage: t.SourceLanguage,
                TargetLanguage: t.TargetLanguage,
                TypingOptOut: t.TypingOptOut,
                CreatedAtUtc: t.CreatedAtUtc,
                UpdatedAtUtc: t.UpdatedAtUtc);
        }).ToList();

        // ---- TargetAnswerVariants: ordered by StableId ----
        var sortedVariants = snapshot.TargetAnswerVariants
            .OrderBy(v => v.StableId, StringComparer.Ordinal)
            .ToList();

        var targetVariantIdMap = new Dictionary<int, string>();
        for (var i = 0; i < sortedVariants.Count; i++)
        {
            targetVariantIdMap[sortedVariants[i].Id] = $"tav-{(i + 1):D6}";
        }

        var targetAnswerVariants = sortedVariants.Select(v =>
        {
            if (!targetIdMap.TryGetValue(v.TargetId, out var targetId))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            string? sourceMeaningId = null;
            if (v.SourceMeaningId.HasValue)
            {
                if (!context.MeaningIdMap.TryGetValue(v.SourceMeaningId.Value, out sourceMeaningId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }
            }

            var requirement = v.Requirement switch
            {
                AnswerVariantRequirement.Required => BackupAnswerVariantRequirement.Required,
                AnswerVariantRequirement.AcceptedOnly => BackupAnswerVariantRequirement.AcceptedOnly,
                _ => throw new BackupFormatException(BackupErrorCodes.InvariantViolation)
            };

            return new BackupTargetAnswerVariant(
                Id: targetVariantIdMap[v.Id],
                StableId: v.StableId,
                TargetId: targetId,
                AnswerLanguage: v.AnswerLanguage,
                DisplayText: v.DisplayText,
                NormalizedText: v.NormalizedText,
                Requirement: requirement,
                IsPreferred: v.IsPreferred,
                RequiredSinceUtc: v.RequiredSinceUtc,
                SourceMeaningId: sourceMeaningId,
                CreatedAtUtc: v.CreatedAtUtc,
                UpdatedAtUtc: v.UpdatedAtUtc);
        }).ToList();

        // ---- TargetFsrsStates: ordered by target archive id ----
        var targetFsrsStates = snapshot.TargetFsrsStates
            .Select(s =>
            {
                if (!targetIdMap.TryGetValue(s.TargetId, out var targetId))
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

                return new BackupTargetFsrsState(
                    TargetId: targetId,
                    State: state,
                    Stability: s.Stability,
                    Difficulty: s.Difficulty,
                    LastReviewedAtUtc: s.LastReviewedAtUtc,
                    StepIndex: s.StepIndex,
                    DueAtUtc: s.DueAtUtc);
            })
            .OrderBy(s => s.TargetId, StringComparer.Ordinal)
            .ToList();

        // ---- TargetFsrsReviewHistoryEntries: ordered by TargetId, SequenceNumber, StableId ----
        var sortedHistory = snapshot.TargetFsrsReviewHistoryEntries
            .Select(h =>
            {
                if (!targetIdMap.TryGetValue(h.TargetId, out var targetId))
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

                return (Raw: h, TargetId: targetId, Rating: rating);
            })
            .OrderBy(h => h.TargetId, StringComparer.Ordinal)
            .ThenBy(h => h.Raw.SequenceNumber)
            .ThenBy(h => h.Raw.StableId, StringComparer.Ordinal)
            .ToList();

        var targetFsrsReviewHistoryEntries = new List<BackupTargetFsrsReviewHistoryEntry>(sortedHistory.Count);
        for (var i = 0; i < sortedHistory.Count; i++)
        {
            var item = sortedHistory[i];
            targetFsrsReviewHistoryEntries.Add(new BackupTargetFsrsReviewHistoryEntry(
                Id: $"trh-{(i + 1):D6}",
                StableId: item.Raw.StableId,
                TargetId: item.TargetId,
                SequenceNumber: item.Raw.SequenceNumber,
                Rating: item.Rating,
                ReviewedAtUtc: item.Raw.ReviewedAtUtc));
        }

        // ---- TargetReviews: ordered deterministically by TargetId, ReviewedAtUtc, StableId ----
        var sortedReviews = snapshot.TargetReviews
            .Select(r =>
            {
                if (!targetIdMap.TryGetValue(r.TargetId, out var targetId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }

                if (!context.LearningSessionIdMap.TryGetValue(r.SessionId, out var sessionId))
                {
                    throw new BackupFormatException(BackupErrorCodes.MissingReference);
                }

                string? targetVariantId = null;
                if (r.TargetAnswerVariantId.HasValue)
                {
                    if (!targetVariantIdMap.TryGetValue(r.TargetAnswerVariantId.Value, out targetVariantId))
                    {
                        throw new BackupFormatException(BackupErrorCodes.MissingReference);
                    }
                }

                string? matchedVariantId = null;
                if (r.MatchedAnswerVariantId.HasValue)
                {
                    if (!targetVariantIdMap.TryGetValue(r.MatchedAnswerVariantId.Value, out matchedVariantId))
                    {
                        throw new BackupFormatException(BackupErrorCodes.MissingReference);
                    }
                }

                var rating = r.Rating switch
                {
                    ReviewRating.Again => BackupReviewRating.Again,
                    ReviewRating.Hard => BackupReviewRating.Hard,
                    ReviewRating.Good => BackupReviewRating.Good,
                    ReviewRating.Easy => BackupReviewRating.Easy,
                    _ => throw new BackupFormatException(BackupErrorCodes.InvariantViolation)
                };

                return (Raw: r, TargetId: targetId, SessionId: sessionId, Rating: rating, TargetVariantId: targetVariantId, MatchedVariantId: matchedVariantId);
            })
            .OrderBy(r => r.TargetId, StringComparer.Ordinal)
            .ThenBy(r => Schema8Utc.Normalize(r.Raw.ReviewedAtUtc).Ticks)
            .ThenBy(r => r.Raw.Id)
            .ToList();

        var targetReviews = new List<BackupTargetReview>(sortedReviews.Count);
        for (var i = 0; i < sortedReviews.Count; i++)
        {
            var item = sortedReviews[i];
            targetReviews.Add(new BackupTargetReview(
                Id: $"trv-{(i + 1):D6}",
                StableId: item.Raw.StableId,
                TargetId: item.TargetId,
                SessionId: item.SessionId,
                Rating: item.Rating,
                WasTypedAnswer: item.Raw.WasTypedAnswer,
                WasCorrect: item.Raw.WasCorrect,
                IsSessionRepeat: item.Raw.IsSessionRepeat,
                TargetAnswerVariantId: item.TargetVariantId,
                MatchedAnswerVariantId: item.MatchedVariantId,
                ReviewedAtUtc: item.Raw.ReviewedAtUtc,
                DueAtUtc: item.Raw.DueAtUtc));
        }

        var workflows = context.Payload.Workflows;
        if (workflows.LearningSessions.Count > 0)
        {
            var localSessionIdByArchiveId = context.LearningSessionIdMap
                .ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);
            var cardsBySession = snapshot.BaseSnapshot.LearningSessionCards
                .GroupBy(c => c.SessionId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(c => c.QueueOrder, c => (TargetId: c.CardId, VariantId: c.TargetAnswerVariantId)));

            var remappedSessions = new List<BackupLearningWorkflowV2>(workflows.LearningSessions.Count);
            foreach (var session in workflows.LearningSessions)
            {
                if (localSessionIdByArchiveId.TryGetValue(session.Id, out var localSessionId)
                    && cardsBySession.TryGetValue(localSessionId, out var cardsByOrder))
                {
                    var remappedItems = new List<BackupLearningQueueItemV2>(session.QueueItems.Count);
                    foreach (var item in session.QueueItems)
                    {
                        if (!cardsByOrder.TryGetValue(item.QueueOrder, out var cardInfo))
                        {
                            throw new BackupFormatException(BackupErrorCodes.MissingReference);
                        }

                        if (!targetIdMap.TryGetValue(cardInfo.TargetId, out var remappedTargetId))
                        {
                            throw new BackupFormatException(BackupErrorCodes.MissingReference);
                        }

                        string? remappedVariantId = null;
                        if (cardInfo.VariantId.HasValue)
                        {
                            if (!targetVariantIdMap.TryGetValue(cardInfo.VariantId.Value, out remappedVariantId))
                            {
                                throw new BackupFormatException(BackupErrorCodes.MissingReference);
                            }
                        }

                        remappedItems.Add(item with
                        {
                            CardId = remappedTargetId,
                            TargetAnswerVariantId = remappedVariantId
                        });
                    }

                    remappedSessions.Add(session with { QueueItems = remappedItems });
                }
                else
                {
                    remappedSessions.Add(session);
                }
            }

            workflows = workflows with { LearningSessions = remappedSessions };
        }

        return new BackupPayloadV4(
            SourceMaterials: context.Payload.SourceMaterials,
            Vocabulary: context.Payload.Vocabulary,
            Senses: context.Payload.Senses,
            PreparedLearning: context.Payload.PreparedLearning,
            Workflows: workflows,
            DerivedTermEvidence: context.Payload.DerivedTermEvidence,
            WordLearningControls: wordControls,
            SenseLearningControls: senseControls,
            LearningTargets: learningTargets,
            TargetAnswerVariants: targetAnswerVariants,
            TargetFsrsStates: targetFsrsStates,
            TargetFsrsReviewHistoryEntries: targetFsrsReviewHistoryEntries,
            TargetReviews: targetReviews,
            Extensions: context.Payload.Extensions);
    }
}
