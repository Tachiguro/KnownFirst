using System.Globalization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public static class MergePreflightPlannerV2
{
    public static MergePreflightPlan CreatePlan(BackupPayloadV2 target, BackupPayloadV2 archive, MergeManifestInfo archiveManifest)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(archiveManifest);

        var counts = MergePreflightPlanner.CreateEmptyCounts();
        var actions = new List<MergePlanAction>();
        var warningCodes = new SortedSet<string>(StringComparer.Ordinal);
        var blockingPrerequisites = new SortedSet<string>(StringComparer.Ordinal);
        var knowledgeStateDecisions = new List<KnowledgeStateConflictDecision>();
        var workflowStatusDecisions = new List<WorkflowStatusConflictDecision>();
        var preferredVariantSelectionDecisions = new List<PreferredVariantSelectionDecision>();
        var hasCausalLearningReviewConflict = false;

        void Record(MergeEntityKind kind, string stableIdentity, string archiveLocalId, MergeEntityClassification classification, string reason, DecisionId? decisionId = null)
        {
            counts[kind] = counts[kind].Increment(classification);
            actions.Add(new MergePlanAction(kind, stableIdentity, archiveLocalId, classification, reason, decisionId));
        }

        static DecisionId MakeDecisionId(string domain, string keyValue) =>
            new(new CanonicalFingerprintBuilder(domain).WriteString(keyValue).ComputeSha256Hex());

        // Identity maps
        var targetVocabByLocalId = MergePreflightPlanner.BuildIdentityMap(target.Vocabulary, v => v.Id, VocabularyMergeIdentityPolicy.Compute, "target vocabulary");
        var archiveVocabByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.Vocabulary, v => v.Id, VocabularyMergeIdentityPolicy.Compute, "archive vocabulary");
        var targetVocabIdentitySet = new HashSet<VocabularyIdentity>(targetVocabByLocalId.Values);
        var targetVocabularyByIdentity = MergePreflightPlanner.ToUniqueDictionary(target.Vocabulary, v => targetVocabByLocalId[v.Id], "target vocabulary identity");

        var targetDocByLocalId = MergePreflightPlanner.BuildIdentityMap(target.SourceMaterials, d => d.Id, SourceMaterialIdentityPolicy.Compute, "target source material");
        var archiveDocByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.SourceMaterials, d => d.Id, SourceMaterialIdentityPolicy.Compute, "archive source material");
        var targetDocIdentitySet = new HashSet<SourceMaterialIdentity>(targetDocByLocalId.Values);

        var targetSenseByLocalId = MergePreflightPlanner.BuildIdentityMap(target.Senses, s => s.Id, s => SemanticMeaningIdentityPolicy.Compute(s, MergePreflightPlanner.Resolve(targetVocabByLocalId, s.VocabularyId, "target sense vocab")), "target sense");
        var archiveSenseByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.Senses, s => s.Id, s => SemanticMeaningIdentityPolicy.Compute(s, MergePreflightPlanner.Resolve(archiveVocabByLocalId, s.VocabularyId, "archive sense vocab")), "archive sense");
        var targetSenseIdentitySet = new HashSet<SemanticMeaningIdentity>(targetSenseByLocalId.Values);

        var targetSenseByLocalIdMap = target.Senses.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var archiveSenseByLocalIdMap = archive.Senses.ToDictionary(s => s.Id, StringComparer.Ordinal);

        var targetExactVariantByLocalId = MergePreflightPlanner.BuildIdentityMap(target.PreparedLearning, m => m.Id, m => ExactMeaningVariantIdentityPolicy.Compute(m, MergePreflightPlanner.Resolve(targetSenseByLocalId, m.SenseId, "target meaning sense")), "target prepared item exact variant");
        var archiveExactVariantByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.PreparedLearning, m => m.Id, m => ExactMeaningVariantIdentityPolicy.Compute(m, MergePreflightPlanner.Resolve(archiveSenseByLocalId, m.SenseId, "archive meaning sense")), "archive prepared item exact variant");
        var targetExactVariantIdentitySet = new HashSet<ExactMeaningVariantIdentity>(targetExactVariantByLocalId.Values);

        var targetPreparedByLocalId = MergePreflightPlanner.ToUniqueDictionary(target.PreparedLearning, m => m.Id, "target prepared item id");
        var archivePreparedByLocalId = MergePreflightPlanner.ToUniqueDictionary(archive.PreparedLearning, m => m.Id, "archive prepared item id");

        var targetAnswerVariantByLocalId = MergePreflightPlanner.BuildIdentityMap(target.AnswerVariants, a => a.Id, a => AnswerVariantIdentityPolicy.Compute(MergePreflightPlanner.Resolve(targetSenseByLocalId, a.SenseId, "target answer variant sense"), a.NormalizedText, a.AnswerLanguage), "target answer variant");
        var archiveAnswerVariantByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.AnswerVariants, a => a.Id, a => AnswerVariantIdentityPolicy.Compute(MergePreflightPlanner.Resolve(archiveSenseByLocalId, a.SenseId, "archive answer variant sense"), a.NormalizedText, a.AnswerLanguage), "archive answer variant");
        var targetAnswerVariantIdentitySet = new HashSet<AnswerVariantIdentity>(targetAnswerVariantByLocalId.Values);

        var targetAnswerVariantsById = target.AnswerVariants.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var archiveAnswerVariantsById = archive.AnswerVariants.ToDictionary(a => a.Id, StringComparer.Ordinal);

        var targetFutureCardIdByLocalId = new Dictionary<string, FutureCardIdentity>(StringComparer.Ordinal);
        foreach (var card in target.Learning.Cards)
        {
            var semanticIdentity = MergePreflightPlanner.Resolve(targetSenseByLocalId, card.SenseId, "target learning card semantic meaning");
            targetFutureCardIdByLocalId[card.Id] = FutureCardIdentityPolicy.Compute(semanticIdentity, card.Direction);
        }

        var archiveFutureCardIdByLocalId = new Dictionary<string, FutureCardIdentity>(StringComparer.Ordinal);
        foreach (var card in archive.Learning.Cards)
        {
            var semanticIdentity = MergePreflightPlanner.Resolve(archiveSenseByLocalId, card.SenseId, "archive learning card semantic meaning");
            archiveFutureCardIdByLocalId[card.Id] = FutureCardIdentityPolicy.Compute(semanticIdentity, card.Direction);
        }

        var targetCardsByLocalId = target.Learning.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var archiveCardsByLocalId = archive.Learning.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);

        var targetCardsByFutureCardIdentity = MergePreflightPlanner.ToUniqueDictionary(target.Learning.Cards, c => targetFutureCardIdByLocalId[c.Id], "target learning card future-card identity");

        string? ResolveNullableVariantIdentity(
            string? localId,
            IReadOnlyDictionary<string, AnswerVariantIdentity> variantIdentityByLocalId,
            string reference) =>
            localId is null
                ? null
                : MergePreflightPlanner.Resolve(variantIdentityByLocalId, localId, reference).Value;

        string ComputeReviewFingerprint(
            FutureCardIdentity futureCardIdentity,
            BackupLearningReviewV2 review,
            IReadOnlyDictionary<string, AnswerVariantIdentity> variantIdentityByLocalId,
            string side) =>
            Schema9LearningReviewMergeIdentity.ComputeEventFingerprint(
                futureCardIdentity.Value,
                review.ReviewedAtUtc,
                review.Rating,
                review.WasTypedAnswer,
                review.WasCorrect,
                review.DueAtUtc,
                review.IntervalDays,
                review.EaseFactor,
                ResolveNullableVariantIdentity(
                    review.TargetAnswerVariantId,
                    variantIdentityByLocalId,
                    side + " review target answer variant identity"),
                ResolveNullableVariantIdentity(
                    review.MatchedAnswerVariantId,
                    variantIdentityByLocalId,
                    side + " review matched answer variant identity"));

        bool ActiveWorkflowScalarsEqual(
            BackupLearningWorkflowV2 targetSession,
            BackupLearningWorkflowV2 archiveSession) =>
            targetSession.Status == archiveSession.Status
            && targetSession.TotalCards == archiveSession.TotalCards
            && targetSession.CompletedCards == archiveSession.CompletedCards
            && targetSession.AgainCount == archiveSession.AgainCount
            && targetSession.HardCount == archiveSession.HardCount
            && targetSession.GoodCount == archiveSession.GoodCount
            && targetSession.EasyCount == archiveSession.EasyCount
            && targetSession.StartedAtUtc == archiveSession.StartedAtUtc
            && targetSession.UpdatedAtUtc == archiveSession.UpdatedAtUtc
            && targetSession.CompletedAtUtc == archiveSession.CompletedAtUtc;

        bool ActiveQueueItemsEqual(
            BackupLearningQueueItemV2 targetItem,
            BackupLearningQueueItemV2 archiveItem) =>
            Data.Schema10.LearningWorkflowStableId.IsValid(targetItem.StableId)
            && string.Equals(targetItem.StableId, archiveItem.StableId, StringComparison.Ordinal)
            && MergePreflightPlanner.Resolve(
                targetFutureCardIdByLocalId,
                targetItem.CardId,
                "target Active learning queue card identity") ==
               MergePreflightPlanner.Resolve(
                   archiveFutureCardIdByLocalId,
                   archiveItem.CardId,
                   "archive Active learning queue card identity")
            && targetItem.QueueOrder == archiveItem.QueueOrder
            && targetItem.IsDueCard == archiveItem.IsDueCard
            && targetItem.IsAgainRepeat == archiveItem.IsAgainRepeat
            && targetItem.AnswerRevealed == archiveItem.AnswerRevealed
            && targetItem.SpellingChecked == archiveItem.SpellingChecked
            && targetItem.SpellingCorrect == archiveItem.SpellingCorrect
            && targetItem.IsCompleted == archiveItem.IsCompleted
            && targetItem.Rating == archiveItem.Rating
            && targetItem.CompletedAtUtc == archiveItem.CompletedAtUtc
            && string.Equals(
                ResolveNullableVariantIdentity(
                    targetItem.TargetAnswerVariantId,
                    targetAnswerVariantByLocalId,
                    "target Active learning queue target answer variant identity"),
                ResolveNullableVariantIdentity(
                    archiveItem.TargetAnswerVariantId,
                    archiveAnswerVariantByLocalId,
                    "archive Active learning queue target answer variant identity"),
                StringComparison.Ordinal);

        bool ActiveWorkflowQueueTopologyEqual(
            BackupLearningWorkflowV2 targetSession,
            BackupLearningWorkflowV2 archiveSession)
        {
            if (targetSession.QueueItems.Count != archiveSession.QueueItems.Count ||
                targetSession.QueueItems.Any(item => !Data.Schema10.LearningWorkflowStableId.IsValid(item.StableId)) ||
                archiveSession.QueueItems.Any(item => !Data.Schema10.LearningWorkflowStableId.IsValid(item.StableId)))
            {
                return false;
            }

            var targetItemsByStableId = new Dictionary<string, BackupLearningQueueItemV2>(StringComparer.Ordinal);
            foreach (var item in targetSession.QueueItems)
            {
                if (!targetItemsByStableId.TryAdd(item.StableId!, item))
                {
                    return false;
                }
            }

            foreach (var archiveItem in archiveSession.QueueItems)
            {
                if (!targetItemsByStableId.TryGetValue(archiveItem.StableId!, out var targetItem) ||
                    !ActiveQueueItemsEqual(targetItem, archiveItem))
                {
                    return false;
                }
            }

            return true;
        }

        Dictionary<string, int> ReviewIdentityCounts(
            BackupPayloadV2 payload,
            string sessionLocalId,
            IReadOnlyDictionary<string, FutureCardIdentity> cardIdentityByLocalId,
            IReadOnlyDictionary<string, AnswerVariantIdentity> variantIdentityByLocalId,
            string side)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var review in payload.Learning.ReviewEvents.Where(review =>
                         string.Equals(review.LearningSessionId, sessionLocalId, StringComparison.Ordinal)))
            {
                var fingerprint = ComputeReviewFingerprint(
                    MergePreflightPlanner.Resolve(
                        cardIdentityByLocalId,
                        review.CardId,
                        side + " Active learning review card identity"),
                    review,
                    variantIdentityByLocalId,
                    side);
                counts[fingerprint] = counts.GetValueOrDefault(fingerprint) + 1;
            }

            return counts;
        }

        static bool ReviewIdentityCountsEqual(
            IReadOnlyDictionary<string, int> targetCounts,
            IReadOnlyDictionary<string, int> archiveCounts) =>
            targetCounts.Count == archiveCounts.Count
            && targetCounts.All(entry =>
                archiveCounts.TryGetValue(entry.Key, out var archiveCount)
                && archiveCount == entry.Value);

        bool ActiveWorkflowDurablyEquivalent(
            BackupLearningWorkflowV2 targetSession,
            BackupLearningWorkflowV2 archiveSession) =>
            ActiveWorkflowScalarsEqual(targetSession, archiveSession)
            && ActiveWorkflowQueueTopologyEqual(targetSession, archiveSession)
            && ReviewIdentityCountsEqual(
                ReviewIdentityCounts(
                    target,
                    targetSession.Id,
                    targetFutureCardIdByLocalId,
                    targetAnswerVariantByLocalId,
                    "target"),
                ReviewIdentityCounts(
                    archive,
                    archiveSession.Id,
                    archiveFutureCardIdByLocalId,
                    archiveAnswerVariantByLocalId,
                    "archive"));

        var targetLearningSessionsByStableId = target.Workflows.LearningSessions
            .Where(session => Data.Schema10.LearningWorkflowStableId.IsValid(session.StableId))
            .ToDictionary(session => session.StableId!, StringComparer.Ordinal);
        var targetActiveLearningSessions = target.Workflows.LearningSessions
            .Where(session => session.Status == BackupLearningSessionStatus.Active)
            .ToList();
        var exactActiveWorkflowArchiveIds = new HashSet<string>(StringComparer.Ordinal);
        var activeWorkflowConflictsByArchiveId = new Dictionary<string, (DecisionId DecisionId, string Reason)>(StringComparer.Ordinal);

        foreach (var archiveSession in archive.Workflows.LearningSessions.Where(session =>
                     session.Status == BackupLearningSessionStatus.Active
                     && Data.Schema10.LearningWorkflowStableId.IsValid(session.StableId)))
        {
            string? conflictReason = null;
            if (targetLearningSessionsByStableId.TryGetValue(archiveSession.StableId!, out var targetSession))
            {
                if (targetSession.Status == BackupLearningSessionStatus.Active &&
                    ActiveWorkflowDurablyEquivalent(targetSession, archiveSession))
                {
                    exactActiveWorkflowArchiveIds.Add(archiveSession.Id);
                    continue;
                }

                conflictReason = "learning-active-workflow-durable-state-conflict";
            }
            else if (targetActiveLearningSessions.Count > 0)
            {
                conflictReason = "learning-active-target-workflow-conflict";
            }

            if (conflictReason is not null)
            {
                var decisionId = MakeDecisionId(
                    "KnownFirst.Merge.Decision.ActiveLearningWorkflow.v1",
                    archiveSession.StableId!);
                activeWorkflowConflictsByArchiveId.Add(archiveSession.Id, (decisionId, conflictReason));
                workflowStatusDecisions.Add(new WorkflowStatusConflictDecision(
                    decisionId,
                    MergeEntityKind.LearningWorkflow,
                    archiveSession.Id,
                    conflictReason));
                warningCodes.Add(conflictReason);
            }
        }

        // Vocabulary
        var targetFormOccurrenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var targetLegacySummariesByWord = new Dictionary<string, HashSet<BackupLegacyReviewSummary>>(StringComparer.Ordinal);
        foreach (var word in target.Vocabulary)
        {
            var vocabIdentity = targetVocabByLocalId[word.Id];
            foreach (var form in word.EncounteredForms)
            {
                targetFormOccurrenceCounts[MergePreflightPlanner.ComputeEncounteredFormIdentity(vocabIdentity, form.SurfaceForm)] = form.OccurrenceCount;
            }

            if (word.LegacyReviewSummaries.Count > 0)
            {
                targetLegacySummariesByWord[vocabIdentity.Value] = new HashSet<BackupLegacyReviewSummary>(word.LegacyReviewSummaries);
            }
        }

        foreach (var archiveWord in archive.Vocabulary)
        {
            var identity = archiveVocabByLocalId[archiveWord.Id];
            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (!targetVocabularyByIdentity.TryGetValue(identity, out var targetWord))
            {
                classification = MergeEntityClassification.New;
                reason = "vocabulary-new";
            }
            else
            {
                var knowledgeResult = KnowledgeStateConflictPolicy.Resolve(targetWord.KnowledgeState, archiveWord.KnowledgeState);
                var preparationResult = PreparationStateConflictPolicy.Resolve(targetWord.PreparationState, archiveWord.PreparationState);

                if (knowledgeResult.Classification == MergeConflictClassification.UnresolvedKeepTargetWithWarning)
                {
                    classification = MergeEntityClassification.UnresolvedConflict;
                    reason = knowledgeResult.ReasonCode;
                    warningCodes.Add(knowledgeResult.ReasonCode);
                    decisionId = MakeDecisionId("KnownFirst.Merge.Decision.KnowledgeState.v1", identity.Value);
                    knowledgeStateDecisions.Add(new KnowledgeStateConflictDecision(
                        decisionId.Value, identity, archiveWord.Id, targetWord.KnowledgeState, archiveWord.KnowledgeState, knowledgeResult.ReasonCode));
                }
                else
                {
                    var advanced = knowledgeResult.ResolvedState != targetWord.KnowledgeState
                        || preparationResult.ResolvedState != targetWord.PreparationState;
                    classification = advanced ? MergeEntityClassification.Enriched : MergeEntityClassification.ExactDuplicateSkipped;
                    reason = advanced ? "vocabulary-progress-advanced" : "vocabulary-progress-equal";
                }
            }

            var wordIsNewForChildren = classification == MergeEntityClassification.New;
            Record(MergeEntityKind.Vocabulary, identity.Value, archiveWord.Id, classification, reason, decisionId);

            foreach (var form in archiveWord.EncounteredForms)
            {
                var formIdentity = MergePreflightPlanner.ComputeEncounteredFormIdentity(identity, form.SurfaceForm);
                MergeEntityClassification formClassification;
                string formReason;
                if (wordIsNewForChildren || !targetFormOccurrenceCounts.TryGetValue(formIdentity, out var targetCount))
                {
                    formClassification = MergeEntityClassification.New;
                    formReason = "encountered-form-new";
                }
                else
                {
                    formClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    formReason = targetCount == form.OccurrenceCount
                        ? "encountered-form-exact-duplicate"
                        : "encountered-form-count-recompute-required";
                }

                Record(MergeEntityKind.EncounteredForm, formIdentity, archiveWord.Id + ":" + form.SurfaceForm, formClassification, formReason);
            }

            if (archiveWord.LegacyReviewSummaries.Count > 0)
            {
                var summaryIdentity = MergePreflightPlanner.ComputeLegacyReviewSummaryIdentity(identity);
                var hasTargetSummaries = targetLegacySummariesByWord.TryGetValue(identity.Value, out var targetSummarySet);
                for (var i = 0; i < archiveWord.LegacyReviewSummaries.Count; i++)
                {
                    var summary = archiveWord.LegacyReviewSummaries[i];
                    MergeEntityClassification summaryClassification;
                    string summaryReason;
                    if (wordIsNewForChildren)
                    {
                        summaryClassification = MergeEntityClassification.New;
                        summaryReason = "legacy-review-summary-new";
                    }
                    else if (hasTargetSummaries && targetSummarySet!.Contains(summary))
                    {
                        summaryClassification = MergeEntityClassification.ExactDuplicateSkipped;
                        summaryReason = "legacy-review-summary-exact-duplicate";
                    }
                    else
                    {
                        summaryClassification = MergeEntityClassification.Enriched;
                        summaryReason = "legacy-review-summary-recompute-required";
                    }

                    Record(
                        MergeEntityKind.LegacyReviewSummary,
                        summaryIdentity,
                        archiveWord.Id + ":" + i.ToString(CultureInfo.InvariantCulture),
                        summaryClassification,
                        summaryReason);
                }
            }
        }

        // SourceMaterials
        var targetSentenceIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        var targetSentenceIdentityByDocAndLocalId = new Dictionary<(string DocLocalId, string SentenceLocalId), string>();
        foreach (var doc in target.SourceMaterials)
        {
            var docIdentity = targetDocByLocalId[doc.Id];
            foreach (var sentence in doc.Sentences)
            {
                var sentenceIdentity = MergePreflightPlanner.ComputeSentenceRangeIdentity(docIdentity, sentence);
                targetSentenceIdentitySet.Add(sentenceIdentity);
                targetSentenceIdentityByDocAndLocalId[(doc.Id, sentence.Id)] = sentenceIdentity;
            }
        }

        var targetOccurrenceIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in target.SourceMaterials)
        {
            var docIdentity = targetDocByLocalId[doc.Id];
            foreach (var occurrence in doc.Occurrences)
            {
                var vocabIdentity = MergePreflightPlanner.Resolve(targetVocabByLocalId, occurrence.VocabularyId, "target occurrence vocabulary");
                var sentenceIdentity = MergePreflightPlanner.Resolve(targetSentenceIdentityByDocAndLocalId, (doc.Id, occurrence.SentenceId), "target occurrence sentence");
                targetOccurrenceIdentitySet.Add(MergePreflightPlanner.ComputeOccurrenceIdentity(docIdentity, sentenceIdentity, vocabIdentity, occurrence));
            }
        }

        // Completed review sessions are matched by their full history (design §4.4 v2), so two independently
        // completed sessions for one document are two distinct identities rather than one unrepresentable
        // conflict. Fail closed on a duplicate full-history identity; never silently keep one of them.
        static ReviewSessionIdentity ComputeReviewSessionIdentityV2(
            BackupVocabularyReviewWorkflow session,
            IReadOnlyDictionary<string, SourceMaterialIdentity> documentIdentitiesByLocalId,
            IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByLocalId,
            string context)
        {
            var result = ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
                session, documentIdentitiesByLocalId, vocabularyIdentitiesByLocalId);
            if (result.HasDuplicateCandidateVocabularyIdentity)
            {
                throw new MergePlanningException(
                    BackupErrorCodes.DuplicateId,
                    $"Ambiguous {context}: two review candidates resolve to the same stable vocabulary identity.");
            }

            return result.Identity;
        }

        var targetReviewSessionIdentityByLocalId = new Dictionary<string, ReviewSessionIdentity>(StringComparer.Ordinal);
        var targetReviewSessionIdentities = new HashSet<ReviewSessionIdentity>();
        foreach (var session in target.Workflows.VocabularyReviews)
        {
            var sessionIdentity = ComputeReviewSessionIdentityV2(
                session, targetDocByLocalId, targetVocabByLocalId, "target review session");
            targetReviewSessionIdentityByLocalId[session.Id] = sessionIdentity;
            if (!targetReviewSessionIdentities.Add(sessionIdentity))
            {
                throw new MergePlanningException(
                    BackupErrorCodes.DuplicateId,
                    "Ambiguous target review session identity: more than one completed review history resolved to the same stable identity.");
            }
        }

        foreach (var archiveDoc in archive.SourceMaterials)
        {
            var docIdentity = archiveDocByLocalId[archiveDoc.Id];
            var docClassification = targetDocIdentitySet.Contains(docIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
            var docReason = docClassification == MergeEntityClassification.New ? "source-material-new" : "source-material-exact-duplicate";
            Record(MergeEntityKind.SourceMaterial, docIdentity.Value, archiveDoc.Id, docClassification, docReason);

            var archiveDocSentenceIdentityByLocalId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var sentence in archiveDoc.Sentences)
            {
                var sentenceIdentity = MergePreflightPlanner.ComputeSentenceRangeIdentity(docIdentity, sentence);
                archiveDocSentenceIdentityByLocalId[sentence.Id] = sentenceIdentity;
                var sentenceClassification = targetSentenceIdentitySet.Contains(sentenceIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
                var sentenceReason = sentenceClassification == MergeEntityClassification.New ? "sentence-range-new" : "sentence-range-exact-duplicate";
                Record(MergeEntityKind.SentenceRange, sentenceIdentity, sentence.Id, sentenceClassification, sentenceReason);
            }

            foreach (var occurrence in archiveDoc.Occurrences)
            {
                var vocabIdentity = MergePreflightPlanner.Resolve(archiveVocabByLocalId, occurrence.VocabularyId, "archive occurrence vocabulary");
                var sentenceIdentity = MergePreflightPlanner.Resolve(archiveDocSentenceIdentityByLocalId, occurrence.SentenceId, "archive occurrence sentence");
                var occurrenceIdentity = MergePreflightPlanner.ComputeOccurrenceIdentity(docIdentity, sentenceIdentity, vocabIdentity, occurrence);
                var occurrenceClassification = targetOccurrenceIdentitySet.Contains(occurrenceIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
                var occurrenceReason = occurrenceClassification == MergeEntityClassification.New ? "occurrence-new" : "occurrence-exact-duplicate";
                Record(
                    MergeEntityKind.Occurrence,
                    occurrenceIdentity,
                    MergePreflightPlanner.ComputeOccurrenceArchiveActionKey(archiveDoc.Id, occurrence.Order),
                    occurrenceClassification,
                    occurrenceReason);
            }
        }

        // Senses
        foreach (var archiveSense in archive.Senses)
        {
            var vocabIdentity = MergePreflightPlanner.Resolve(archiveVocabByLocalId, archiveSense.VocabularyId, "archive sense vocabulary");
            var identity = archiveSenseByLocalId[archiveSense.Id];
            var wordIsNew = !targetVocabIdentitySet.Contains(vocabIdentity);

            MergeEntityClassification classification;
            string reason;
            if (wordIsNew)
            {
                classification = MergeEntityClassification.New;
                reason = "sense-new-with-new-word";
            }
            else if (targetSenseIdentitySet.Contains(identity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "sense-exact-duplicate";
            }
            else
            {
                classification = MergeEntityClassification.Enriched;
                reason = "sense-new-semantic-sense-existing-word";
            }

            Record(MergeEntityKind.Sense, identity.Value, archiveSense.Id, classification, reason);
        }

        // PreparedLearning (Meanings)
        var targetContextIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var meaning in target.PreparedLearning)
        {
            var semanticIdentity = MergePreflightPlanner.Resolve(targetSenseByLocalId, meaning.SenseId, "target meaning sense");
            foreach (var context in meaning.Contexts)
            {
                if (context.SenseId != meaning.SenseId)
                {
                    throw new MergePlanningException(
                        BackupErrorCodes.InvariantViolation,
                        $"Target context snapshot sense '{context.SenseId}' does not match parent meaning sense '{meaning.SenseId}'.");
                }
                var docIdentity = MergePreflightPlanner.Resolve(targetDocByLocalId, context.SourceMaterialId, "target context snapshot source material");
                targetContextIdentitySet.Add(MergePreflightPlanner.ComputeContextSnapshotIdentity(semanticIdentity, docIdentity, new BackupContextSnapshot(context.SourceMaterialId, context.SourceTitle, context.Text, context.TargetStart, context.TargetLength, context.NormalizedFingerprint, context.CreatedAtUtc)));
            }
        }

        foreach (var archiveMeaning in archive.PreparedLearning)
        {
            var semanticIdentity = MergePreflightPlanner.Resolve(archiveSenseByLocalId, archiveMeaning.SenseId, "archive meaning sense");
            var exactVariantIdentity = archiveExactVariantByLocalId[archiveMeaning.Id];

            MergeEntityClassification classification;
            string reason;

            if (targetExactVariantIdentitySet.Contains(exactVariantIdentity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "meaning-exact-duplicate";
            }
            else if (targetSenseIdentitySet.Contains(semanticIdentity))
            {
                classification = MergeEntityClassification.PreservedVariant;
                reason = "meaning-preserved-content-variant-same-sense";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "meaning-new-with-new-sense";
            }

            Record(MergeEntityKind.PreparedMeaning, exactVariantIdentity.Value, archiveMeaning.Id, classification, reason);

            // ContextSnapshot
            foreach (var context in archiveMeaning.Contexts)
            {
                if (context.SenseId != archiveMeaning.SenseId)
                {
                    throw new MergePlanningException(
                        BackupErrorCodes.InvariantViolation,
                        $"Archive context snapshot sense '{context.SenseId}' does not match parent meaning sense '{archiveMeaning.SenseId}'.");
                }
                var docIdentity = MergePreflightPlanner.Resolve(archiveDocByLocalId, context.SourceMaterialId, "archive context snapshot source material");
                var contextIdentity = MergePreflightPlanner.ComputeContextSnapshotIdentity(semanticIdentity, docIdentity, new BackupContextSnapshot(context.SourceMaterialId, context.SourceTitle, context.Text, context.TargetStart, context.TargetLength, context.NormalizedFingerprint, context.CreatedAtUtc));
                var contextClassification = targetContextIdentitySet.Contains(contextIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
                var contextReason = contextClassification == MergeEntityClassification.New ? "context-snapshot-new" : "context-snapshot-exact-duplicate";
                Record(MergeEntityKind.ContextSnapshot, contextIdentity, archiveMeaning.Id + ":" + context.NormalizedFingerprint, contextClassification, contextReason);
            }
        }

        // AnswerVariants
        foreach (var archiveVariant in archive.AnswerVariants)
        {
            var identity = archiveAnswerVariantByLocalId[archiveVariant.Id];
            MergeEntityClassification classification;
            string reason;
            if (targetAnswerVariantIdentitySet.Contains(identity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "answer-variant-exact-duplicate";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "answer-variant-new";
            }
            Record(MergeEntityKind.AnswerVariant, identity.Value, archiveVariant.Id, classification, reason);
        }

        // SenseAnswerVariantAssignments
        var targetAssignmentIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in target.SenseAnswerVariantAssignments)
        {
            var senseId = MergePreflightPlanner.Resolve(targetSenseByLocalId, assignment.SenseId, "target assignment sense");
            var variantIdentity = MergePreflightPlanner.Resolve(targetAnswerVariantByLocalId, assignment.AnswerVariantId, "target assignment variant");
            var variant = MergePreflightPlanner.Resolve(targetAnswerVariantsById, assignment.AnswerVariantId, "target assignment variant content");
            if (variant.SenseId != assignment.SenseId)
            {
                throw new MergePlanningException(
                    BackupErrorCodes.InvariantViolation,
                    $"Target assignment sense '{assignment.SenseId}' does not match variant sense '{variant.SenseId}'.");
            }
            var futureCardId = FutureCardIdentityPolicy.Compute(senseId, assignment.CardDirection);
            targetAssignmentIdentitySet.Add(futureCardId.Value + "|" + variantIdentity.Value);
        }

        foreach (var assignment in archive.SenseAnswerVariantAssignments)
        {
            var senseId = MergePreflightPlanner.Resolve(archiveSenseByLocalId, assignment.SenseId, "archive assignment sense");
            var variantIdentity = MergePreflightPlanner.Resolve(archiveAnswerVariantByLocalId, assignment.AnswerVariantId, "archive assignment variant");
            var variant = MergePreflightPlanner.Resolve(archiveAnswerVariantsById, assignment.AnswerVariantId, "archive assignment variant content");
            if (variant.SenseId != assignment.SenseId)
            {
                throw new MergePlanningException(
                    BackupErrorCodes.InvariantViolation,
                    $"Archive assignment sense '{assignment.SenseId}' does not match variant sense '{variant.SenseId}'.");
            }
            var futureCardId = FutureCardIdentityPolicy.Compute(senseId, assignment.CardDirection);
            var identity = futureCardId.Value + "|" + variantIdentity.Value;
            
            MergeEntityClassification classification;
            string reason;
            if (targetAssignmentIdentitySet.Contains(identity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "assignment-exact-duplicate";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "assignment-new";
            }
            Record(MergeEntityKind.SenseAnswerVariantAssignment, identity, assignment.Id, classification, reason);
        }

        // LearningReviews — meaning-aware event fingerprint (see Schema9LearningReviewMergeIdentity for the
        // exact field set, and for why LearningSessionId is deliberately not part of event identity).
        // Answer-variant references are resolved to stable AnswerVariantIdentity values through the side's
        // own map, never hashed as raw archive-local av-* ids.
        var targetReviewFingerprints = archiveManifest.LearningReviewCausalOrderRequired
            ? null
            : new HashSet<string>(
                target.Learning.ReviewEvents.Select(r => ComputeReviewFingerprint(
                    MergePreflightPlanner.Resolve(targetFutureCardIdByLocalId, r.CardId, "target learning review card"),
                    r, targetAnswerVariantByLocalId, "target")), StringComparer.Ordinal);
        var causalClassifications = archiveManifest.LearningReviewCausalOrderRequired
            ? PlanCausalLearningReviewActions(
                target.Learning.ReviewEvents,
                archive.Learning.ReviewEvents,
                review => MergePreflightPlanner.Resolve(
                    targetFutureCardIdByLocalId,
                    review.CardId,
                    "target causal learning review card"),
                review => MergePreflightPlanner.Resolve(
                    archiveFutureCardIdByLocalId,
                    review.CardId,
                    "archive causal learning review card"),
                review => ComputeReviewFingerprint(
                    MergePreflightPlanner.Resolve(
                        targetFutureCardIdByLocalId,
                        review.CardId,
                        "target causal learning review card"),
                    review,
                    targetAnswerVariantByLocalId,
                    "target"),
                review => ComputeReviewFingerprint(
                    MergePreflightPlanner.Resolve(
                        archiveFutureCardIdByLocalId,
                        review.CardId,
                        "archive causal learning review card"),
                    review,
                    archiveAnswerVariantByLocalId,
                    "archive"))
            : null;
        var cardsWithNewEvents = new HashSet<FutureCardIdentity>();

        for (var reviewIndex = 0; reviewIndex < archive.Learning.ReviewEvents.Count; reviewIndex++)
        {
            var review = archive.Learning.ReviewEvents[reviewIndex];
            var futureCardIdentity = MergePreflightPlanner.Resolve(archiveFutureCardIdByLocalId, review.CardId, "archive learning review card");
            var archiveCard = MergePreflightPlanner.Resolve(archiveCardsByLocalId, review.CardId, "archive learning review card content");

            if (review.TargetAnswerVariantId is not null)
            {
                var targetVariant = MergePreflightPlanner.Resolve(archiveAnswerVariantsById, review.TargetAnswerVariantId, "archive review target answer variant");
                if (targetVariant.SenseId != archiveCard.SenseId)
                {
                    throw new MergePlanningException(
                        BackupErrorCodes.InvariantViolation,
                        $"Archive learning review target variant sense '{targetVariant.SenseId}' does not match card sense '{archiveCard.SenseId}'.");
                }
            }

            if (review.MatchedAnswerVariantId is not null)
            {
                var matchedVariant = MergePreflightPlanner.Resolve(archiveAnswerVariantsById, review.MatchedAnswerVariantId, "archive review matched answer variant");
                if (matchedVariant.SenseId != archiveCard.SenseId)
                {
                    throw new MergePlanningException(
                        BackupErrorCodes.InvariantViolation,
                        $"Archive learning review matched variant sense '{matchedVariant.SenseId}' does not match card sense '{archiveCard.SenseId}'.");
                }
            }

            var fingerprint = ComputeReviewFingerprint(futureCardIdentity, review, archiveAnswerVariantByLocalId, "archive");

            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;
            if (causalClassifications is not null)
            {
                var causalClassification = causalClassifications[reviewIndex];
                classification = causalClassification.Classification;
                reason = causalClassification.ReasonCode;
                if (classification == MergeEntityClassification.UnresolvedConflict)
                {
                    hasCausalLearningReviewConflict = true;
                }
                else if (classification == MergeEntityClassification.New
                         && activeWorkflowConflictsByArchiveId.TryGetValue(review.LearningSessionId, out var causalActiveConflict))
                {
                    classification = MergeEntityClassification.UnresolvedConflict;
                    reason = causalActiveConflict.Reason;
                    decisionId = causalActiveConflict.DecisionId;
                }
                else if (classification == MergeEntityClassification.New)
                {
                    cardsWithNewEvents.Add(futureCardIdentity);
                }
            }
            else if (targetReviewFingerprints!.Contains(fingerprint))
            {
                classification = MergeEntityClassification.DeduplicatedEvent;
                reason = "learning-review-exact-duplicate-event";
            }
            else if (activeWorkflowConflictsByArchiveId.TryGetValue(review.LearningSessionId, out var activeConflict))
            {
                classification = MergeEntityClassification.UnresolvedConflict;
                reason = activeConflict.Reason;
                decisionId = activeConflict.DecisionId;
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "learning-review-new-distinct-event";
                cardsWithNewEvents.Add(futureCardIdentity);
            }

            // A synthesized positional label, not the CardId@ReviewedAtUtc content label: two physical
            // archive rows for one card at one instant are genuinely distinct events, and the writer keys
            // its per-row action lookup by this value.
            Record(
                MergeEntityKind.LearningReview, fingerprint, Schema9LearningReviewMergeIdentity.ArchiveActionKey(reviewIndex),
                classification, reason, decisionId);
        }

        // LearningCards
        foreach (var archiveCard in archive.Learning.Cards)
        {
            var futureCardIdentity = MergePreflightPlanner.Resolve(archiveFutureCardIdByLocalId, archiveCard.Id, "archive learning card future card identity");
            var archivePreferredItem = MergePreflightPlanner.Resolve(archivePreparedByLocalId, archiveCard.PreferredMeaningId, "archive learning card preferred meaning");
            if (archivePreferredItem.SenseId != archiveCard.SenseId)
            {
                throw new MergePlanningException(
                    BackupErrorCodes.InvariantViolation,
                    $"Archive learning card preferred meaning sense '{archivePreferredItem.SenseId}' does not match card sense '{archiveCard.SenseId}'.");
            }

            MergeEntityClassification classification;
            string reason;

            if (targetCardsByFutureCardIdentity.ContainsKey(futureCardIdentity))
            {
                classification = cardsWithNewEvents.Contains(futureCardIdentity)
                    ? MergeEntityClassification.Enriched
                    : MergeEntityClassification.ExactDuplicateSkipped;
                reason = classification == MergeEntityClassification.Enriched
                    ? "learning-card-enriched-new-review-events"
                    : "learning-card-exact-duplicate";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "learning-card-new";
            }

            Record(MergeEntityKind.LearningCard, futureCardIdentity.Value, archiveCard.Id, classification, reason);
        }

        var requiresSchedulerReplay = cardsWithNewEvents.Overlaps(targetCardsByFutureCardIdentity.Keys);

        // PreferredVariantSelectionDecision
        foreach (var archiveCard in archive.Learning.Cards.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var futureCardIdentity = MergePreflightPlanner.Resolve(archiveFutureCardIdByLocalId, archiveCard.Id, "archive learning card future card identity decision");
            if (!targetCardsByFutureCardIdentity.TryGetValue(futureCardIdentity, out var targetCard))
            {
                continue;
            }

            var targetExactVariantIdentity = MergePreflightPlanner.Resolve(targetExactVariantByLocalId, targetCard.PreferredMeaningId, "target preferred-variant card preferred meaning");
            var archiveExactVariantIdentity = MergePreflightPlanner.Resolve(archiveExactVariantByLocalId, archiveCard.PreferredMeaningId, "archive preferred-variant card preferred meaning");
            if (targetExactVariantIdentity.Equals(archiveExactVariantIdentity))
            {
                continue;
            }

            var semanticIdentity = MergePreflightPlanner.Resolve(archiveSenseByLocalId, archiveCard.SenseId, "archive preferred-variant card semantic meaning");
            var targetPreparedItem = MergePreflightPlanner.Resolve(targetPreparedByLocalId, targetCard.PreferredMeaningId, "target preferred-variant card prepared item content");
            var archivePreparedItem = MergePreflightPlanner.Resolve(archivePreparedByLocalId, archiveCard.PreferredMeaningId, "archive preferred-variant card prepared item content");

            var decisionId = MakeDecisionId(
                "KnownFirst.Merge.Decision.PreferredVariant.v2",
                futureCardIdentity.Value + "|" + targetExactVariantIdentity.Value + "|" + archiveExactVariantIdentity.Value);
            preferredVariantSelectionDecisions.Add(new PreferredVariantSelectionDecision(
                decisionId,
                futureCardIdentity,
                semanticIdentity,
                targetExactVariantIdentity,
                archiveExactVariantIdentity,
                targetPreparedItem.DisplayTerm,
                archivePreparedItem.DisplayTerm,
                PreferredVariantSelectionDecision.StandardChoices));
        }

        // AnswerVariantProgress
        var targetProgressIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var progress in target.AnswerVariantProgress)
        {
            var futureCardId = MergePreflightPlanner.Resolve(targetFutureCardIdByLocalId, progress.CardId, "target progress card");
            var variantIdentity = MergePreflightPlanner.Resolve(targetAnswerVariantByLocalId, progress.AnswerVariantId, "target progress variant");
            var targetCard = MergePreflightPlanner.Resolve(targetCardsByLocalId, progress.CardId, "target progress card content");
            var targetVariant = MergePreflightPlanner.Resolve(targetAnswerVariantsById, progress.AnswerVariantId, "target progress variant content");
            if (targetCard.SenseId != targetVariant.SenseId)
            {
                throw new MergePlanningException(
                    BackupErrorCodes.InvariantViolation,
                    $"Target progress card sense '{targetCard.SenseId}' does not match answer variant sense '{targetVariant.SenseId}'.");
            }
            targetProgressIdentitySet.Add(futureCardId.Value + "|" + variantIdentity.Value);
        }

        foreach (var progress in archive.AnswerVariantProgress)
        {
            var futureCardId = MergePreflightPlanner.Resolve(archiveFutureCardIdByLocalId, progress.CardId, "archive progress card");
            var variantIdentity = MergePreflightPlanner.Resolve(archiveAnswerVariantByLocalId, progress.AnswerVariantId, "archive progress variant");
            var archiveCard = MergePreflightPlanner.Resolve(archiveCardsByLocalId, progress.CardId, "archive progress card content");
            var archiveVariant = MergePreflightPlanner.Resolve(archiveAnswerVariantsById, progress.AnswerVariantId, "archive progress variant content");
            if (archiveCard.SenseId != archiveVariant.SenseId)
            {
                throw new MergePlanningException(
                    BackupErrorCodes.InvariantViolation,
                    $"Archive progress card sense '{archiveCard.SenseId}' does not match answer variant sense '{archiveVariant.SenseId}'.");
            }
            var identity = futureCardId.Value + "|" + variantIdentity.Value;
            
            MergeEntityClassification classification;
            string reason;
            if (targetProgressIdentitySet.Contains(identity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "progress-exact-duplicate";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "progress-new";
            }
            Record(MergeEntityKind.AnswerVariantProgress, identity, progress.CardId + ":" + progress.AnswerVariantId, classification, reason);
        }

        // VocabularyReviewWorkflow + VocabularyReviewItem
        //
        // Both the session and its candidates match by full-history v2 identity, so a divergent completed
        // history is simply New — never a WorkflowStatusConflictDecision and never a
        // WorkflowHistorySchemaMigrationRequired prerequisite (both remain valid for the Schema-7 path).
        var targetDocLanguageByLocalId = target.SourceMaterials.ToDictionary(d => d.Id, d => d.TextLanguage, StringComparer.Ordinal);
        var archiveDocLanguageByLocalId = archive.SourceMaterials.ToDictionary(d => d.Id, d => d.TextLanguage, StringComparer.Ordinal);

        var targetReviewCandidateIdentities = new HashSet<ReviewCandidateIdentity>();
        // German Enhanced Term Recognition Package 5A-2: every candidate's own resolvable identity plus its
        // owning document's language, keyed by the archive review-item id — needed below to classify
        // DerivedTermEvidence rows without recomputing session/candidate identities a second time.
        var targetReviewCandidateIdentityByItemLocalId = new Dictionary<string, ReviewCandidateIdentity>(StringComparer.Ordinal);
        var targetDocumentLanguageByItemLocalId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in target.Workflows.VocabularyReviews)
        {
            var sessionIdentity = MergePreflightPlanner.Resolve(
                targetReviewSessionIdentityByLocalId, session.Id, "target review session identity map");
            var documentLanguage = MergePreflightPlanner.Resolve(
                targetDocLanguageByLocalId, session.SourceMaterialId, "target review session document language");
            foreach (var item in session.Items)
            {
                var vocabIdentity = MergePreflightPlanner.Resolve(targetVocabByLocalId, item.VocabularyId, "target review item vocabulary");
                var candidateIdentity = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(sessionIdentity, vocabIdentity);
                targetReviewCandidateIdentities.Add(candidateIdentity);
                targetReviewCandidateIdentityByItemLocalId[item.Id] = candidateIdentity;
                targetDocumentLanguageByItemLocalId[item.Id] = documentLanguage;
            }
        }

        // Distinct archive-local workflow ids never make two identical full histories representable: the
        // writer would have to insert two rows carrying one v2 identity. Fail closed before recording any
        // action for the duplicate workflow or its candidates, exactly as the target side does.
        var archiveReviewSessionIdentities = new HashSet<ReviewSessionIdentity>();
        // German Enhanced Term Recognition Package 5A-2: the archive-side counterparts of the target maps
        // above.
        var archiveReviewCandidateIdentityByItemLocalId = new Dictionary<string, ReviewCandidateIdentity>(StringComparer.Ordinal);
        var archiveDocumentLanguageByItemLocalId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in archive.Workflows.VocabularyReviews)
        {
            var identity = ComputeReviewSessionIdentityV2(
                session, archiveDocByLocalId, archiveVocabByLocalId, "archive review session");
            if (!archiveReviewSessionIdentities.Add(identity))
            {
                throw new MergePlanningException(
                    BackupErrorCodes.DuplicateId,
                    "Ambiguous archive review session identity: more than one completed review history resolved to the same stable identity.");
            }

            var classification = targetReviewSessionIdentities.Contains(identity)
                ? MergeEntityClassification.ExactDuplicateSkipped
                : MergeEntityClassification.New;
            var reason = classification == MergeEntityClassification.New
                ? "review-workflow-new"
                : "review-workflow-exact-duplicate";

            Record(MergeEntityKind.VocabularyReviewWorkflow, identity.Value, session.Id, classification, reason);

            var documentLanguage = MergePreflightPlanner.Resolve(
                archiveDocLanguageByLocalId, session.SourceMaterialId, "archive review session document language");
            foreach (var item in session.Items)
            {
                var vocabIdentity = MergePreflightPlanner.Resolve(archiveVocabByLocalId, item.VocabularyId, "archive review item vocabulary");
                var itemIdentity = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2(identity, vocabIdentity);
                var itemClassification = targetReviewCandidateIdentities.Contains(itemIdentity)
                    ? MergeEntityClassification.ExactDuplicateSkipped
                    : MergeEntityClassification.New;
                var itemReason = itemClassification == MergeEntityClassification.New
                    ? "review-item-new"
                    : "review-item-exact-duplicate";

                Record(MergeEntityKind.VocabularyReviewItem, itemIdentity.Value, item.Id, itemClassification, itemReason);
                archiveReviewCandidateIdentityByItemLocalId[item.Id] = itemIdentity;
                archiveDocumentLanguageByItemLocalId[item.Id] = documentLanguage;
            }
        }

        // DerivedTermEvidence (German Enhanced Term Recognition Package 5A-2)
        //
        // Its own merge entity kind rather than being hidden inside VocabularyReviewItem classification,
        // since one owning candidate can carry zero, one, or several independently-classified evidence rows.
        // Identity is the owning candidate's stable ReviewCandidateIdentity plus the source-compound's own
        // VocabularyIdentity (owning-document language + SourceIdentity) plus the physical range/component
        // fields — never an archive-local id, and never re-deriving the candidate's own identity twice.
        var targetDerivedEvidenceIdentities = new HashSet<DerivedTermEvidenceIdentity>();
        foreach (var evidence in target.DerivedTermEvidence)
        {
            var candidateIdentity = MergePreflightPlanner.Resolve(
                targetReviewCandidateIdentityByItemLocalId, evidence.ReviewItemId, "target derived evidence owning review item");
            var documentLanguage = MergePreflightPlanner.Resolve(
                targetDocumentLanguageByItemLocalId, evidence.ReviewItemId, "target derived evidence owning document language");
            var sourceCompoundVocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(documentLanguage, evidence.SourceIdentity);
            targetDerivedEvidenceIdentities.Add(DerivedTermEvidenceMergeIdentity.Compute(
                candidateIdentity, sourceCompoundVocabularyIdentity, evidence.SourceStartPosition,
                evidence.SourceLength, evidence.SourceSentenceOrder, evidence.ComponentForm));
        }

        for (var derivedEvidenceIndex = 0; derivedEvidenceIndex < archive.DerivedTermEvidence.Count; derivedEvidenceIndex++)
        {
            var evidence = archive.DerivedTermEvidence[derivedEvidenceIndex];
            var candidateIdentity = MergePreflightPlanner.Resolve(
                archiveReviewCandidateIdentityByItemLocalId, evidence.ReviewItemId, "archive derived evidence owning review item");
            var documentLanguage = MergePreflightPlanner.Resolve(
                archiveDocumentLanguageByItemLocalId, evidence.ReviewItemId, "archive derived evidence owning document language");
            var sourceCompoundVocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(documentLanguage, evidence.SourceIdentity);
            var evidenceIdentity = DerivedTermEvidenceMergeIdentity.Compute(
                candidateIdentity, sourceCompoundVocabularyIdentity, evidence.SourceStartPosition,
                evidence.SourceLength, evidence.SourceSentenceOrder, evidence.ComponentForm);

            var evidenceClassification = targetDerivedEvidenceIdentities.Contains(evidenceIdentity)
                ? MergeEntityClassification.ExactDuplicateSkipped
                : MergeEntityClassification.New;
            var evidenceReason = evidenceClassification == MergeEntityClassification.New
                ? "derived-term-evidence-new"
                : "derived-term-evidence-exact-duplicate";

            Record(
                MergeEntityKind.DerivedTermEvidence,
                evidenceIdentity.Value,
                DerivedTermEvidenceMergeIdentity.ArchiveActionKey(derivedEvidenceIndex),
                evidenceClassification,
                evidenceReason);
        }

        // PreparationWorkflow + PreparationItem
        var targetPrepSessionIdByLocalId = MergePreflightPlanner.BuildIdentityMap(target.Workflows.PreparationBatches, s => s.Id, s => PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(s, targetVocabByLocalId), "target preparation session");
        var archivePrepSessionIdByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.Workflows.PreparationBatches, s => s.Id, s => PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(s, archiveVocabByLocalId), "archive preparation session");
        var targetPrepSessionsByIdentity = MergePreflightPlanner.ToUniqueDictionary(target.Workflows.PreparationBatches, s => targetPrepSessionIdByLocalId[s.Id], "target preparation session identity");

        var targetPrepItemContentByIdentity = new Dictionary<PreparationCandidateIdentity, BackupPreparationItem>();
        foreach (var session in target.Workflows.PreparationBatches)
        {
            var sessionIdentity = MergePreflightPlanner.Resolve(targetPrepSessionIdByLocalId, session.Id, "target preparation session identity map");
            foreach (var item in session.Items)
            {
                targetPrepItemContentByIdentity.TryAdd(PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(item, sessionIdentity, targetVocabByLocalId), item);
            }
        }

        foreach (var session in archive.Workflows.PreparationBatches)
        {
            var identity = MergePreflightPlanner.Resolve(archivePrepSessionIdByLocalId, session.Id, "archive preparation session identity map");
            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (!targetPrepSessionsByIdentity.TryGetValue(identity, out var targetSession))
            {
                classification = MergeEntityClassification.New;
                reason = "preparation-workflow-new";
            }
            else if (targetSession.Status == session.Status)
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "preparation-workflow-exact-duplicate";
            }
            else
            {
                var statusResult = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(targetSession.Status, session.Status);
                if (statusResult.Classification == MergeConflictClassification.UnresolvedKeepTargetWithWarning)
                {
                    classification = MergeEntityClassification.UnresolvedConflict;
                    reason = statusResult.ReasonCode;
                    warningCodes.Add(reason);
                    decisionId = MakeDecisionId("KnownFirst.Merge.Decision.WorkflowStatus.v1", identity.Value);
                    workflowStatusDecisions.Add(new WorkflowStatusConflictDecision(decisionId.Value, MergeEntityKind.PreparationWorkflow, session.Id, reason));
                }
                else
                {
                    classification = MergeEntityClassification.Enriched;
                    reason = "preparation-workflow-status-monotonic-advance";
                }
            }

            Record(MergeEntityKind.PreparationWorkflow, identity.Value, session.Id, classification, reason, decisionId);

            var sessionIdentity = identity;
            foreach (var item in session.Items)
            {
                var itemIdentity = PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(item, sessionIdentity, archiveVocabByLocalId);
                MergeEntityClassification itemClassification;
                string itemReason;
                if (!targetPrepItemContentByIdentity.TryGetValue(itemIdentity, out var targetItem))
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "preparation-item-new";
                }
                else if (MergePreflightPlanner.PreparationItemContentEquals(targetItem, item))
                {
                    itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    itemReason = "preparation-item-exact-duplicate";
                }
                else
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "preparation-item-preserved-divergent-history";
                }

                Record(MergeEntityKind.PreparationItem, itemIdentity.Value, item.Id, itemClassification, itemReason);
            }
        }

        // LearningWorkflow + LearningQueueItem
        string ComputeLearningSessionIdentity(BackupLearningWorkflowV2 session, IReadOnlyDictionary<string, FutureCardIdentity> cardIdentitiesByLocalId) =>
            LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(session, cardIdentitiesByLocalId);

        string ComputeSessionCardIdentity(BackupLearningQueueItemV2 item, string sessionIdentity, IReadOnlyDictionary<string, FutureCardIdentity> cardIdentitiesByLocalId) =>
            LearningWorkflowIdentityPolicy.ComputeSchema8QueueItemIdentity(item, sessionIdentity, cardIdentitiesByLocalId);

        bool LearningQueueItemContentEqualsV2(BackupLearningQueueItemV2 a, BackupLearningQueueItemV2 b) =>
            a.IsDueCard == b.IsDueCard
            && a.IsAgainRepeat == b.IsAgainRepeat
            && a.AnswerRevealed == b.AnswerRevealed
            && a.SpellingChecked == b.SpellingChecked
            && a.SpellingCorrect == b.SpellingCorrect
            && a.IsCompleted == b.IsCompleted
            && a.Rating == b.Rating
            && a.CompletedAtUtc == b.CompletedAtUtc;

        var targetLearningSessionIdByLocalId = MergePreflightPlanner.BuildIdentityMap(target.Workflows.LearningSessions, s => s.Id, s => ComputeLearningSessionIdentity(s, targetFutureCardIdByLocalId), "target learning session");
        var archiveLearningSessionIdByLocalId = MergePreflightPlanner.BuildIdentityMap(archive.Workflows.LearningSessions, s => s.Id, s => ComputeLearningSessionIdentity(s, archiveFutureCardIdByLocalId), "archive learning session");
        var targetLearningSessionIdentitySet = new HashSet<string>(targetLearningSessionIdByLocalId.Values, StringComparer.Ordinal);

        var targetLearningQueueItemContentByIdentity = new Dictionary<string, BackupLearningQueueItemV2>(StringComparer.Ordinal);
        foreach (var session in target.Workflows.LearningSessions)
        {
            var sessionIdentity = MergePreflightPlanner.Resolve(targetLearningSessionIdByLocalId, session.Id, "target learning session identity map");
            foreach (var item in session.QueueItems)
            {
                if (item.TargetAnswerVariantId is not null)
                {
                    var card = MergePreflightPlanner.Resolve(targetCardsByLocalId, item.CardId, "target learning queue card");
                    var variant = MergePreflightPlanner.Resolve(targetAnswerVariantsById, item.TargetAnswerVariantId, "target learning queue target answer variant");
                    if (variant.SenseId != card.SenseId)
                    {
                        throw new MergePlanningException(
                            BackupErrorCodes.InvariantViolation,
                            $"Target learning queue item target variant sense '{variant.SenseId}' does not match card sense '{card.SenseId}'.");
                    }
                }

                targetLearningQueueItemContentByIdentity.TryAdd(ComputeSessionCardIdentity(item, sessionIdentity, targetFutureCardIdByLocalId), item);
            }
        }

        foreach (var session in archive.Workflows.LearningSessions)
        {
            var identity = MergePreflightPlanner.Resolve(archiveLearningSessionIdByLocalId, session.Id, "archive learning session identity map");
            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (activeWorkflowConflictsByArchiveId.TryGetValue(session.Id, out var activeConflict))
            {
                classification = MergeEntityClassification.UnresolvedConflict;
                reason = activeConflict.Reason;
                decisionId = activeConflict.DecisionId;
            }
            else if (exactActiveWorkflowArchiveIds.Contains(session.Id) || targetLearningSessionIdentitySet.Contains(identity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "learning-workflow-exact-duplicate";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "learning-workflow-new";
            }

            Record(MergeEntityKind.LearningWorkflow, identity, session.Id, classification, reason, decisionId);

            var sessionIdentity = identity;
            foreach (var item in session.QueueItems)
            {
                if (item.TargetAnswerVariantId is not null)
                {
                    var card = MergePreflightPlanner.Resolve(archiveCardsByLocalId, item.CardId, "archive learning queue card");
                    var variant = MergePreflightPlanner.Resolve(archiveAnswerVariantsById, item.TargetAnswerVariantId, "archive learning queue target answer variant");
                    if (variant.SenseId != card.SenseId)
                    {
                        throw new MergePlanningException(
                            BackupErrorCodes.InvariantViolation,
                            $"Archive learning queue item target variant sense '{variant.SenseId}' does not match card sense '{card.SenseId}'.");
                    }
                }

                var itemIdentity = ComputeSessionCardIdentity(item, sessionIdentity, archiveFutureCardIdByLocalId);
                MergeEntityClassification itemClassification;
                string itemReason;
                DecisionId? itemDecisionId = null;
                if (activeWorkflowConflictsByArchiveId.TryGetValue(session.Id, out activeConflict))
                {
                    if (targetLearningQueueItemContentByIdentity.TryGetValue(itemIdentity, out var conflictingTargetItem) &&
                        ActiveQueueItemsEqual(conflictingTargetItem, item))
                    {
                        itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                        itemReason = "learning-queue-item-exact-duplicate";
                    }
                    else
                    {
                        itemClassification = MergeEntityClassification.UnresolvedConflict;
                        itemReason = activeConflict.Reason;
                        itemDecisionId = activeConflict.DecisionId;
                    }
                }
                else if (exactActiveWorkflowArchiveIds.Contains(session.Id))
                {
                    itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    itemReason = "learning-queue-item-exact-duplicate";
                }
                else if (!targetLearningQueueItemContentByIdentity.TryGetValue(itemIdentity, out var targetItem))
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "learning-queue-item-new";
                }
                else if (LearningQueueItemContentEqualsV2(targetItem, item))
                {
                    itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    itemReason = "learning-queue-item-exact-duplicate";
                }
                else
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "learning-queue-item-preserved-divergent-history";
                }

                Record(
                    MergeEntityKind.LearningQueueItem,
                    itemIdentity,
                    item.Id,
                    itemClassification,
                    itemReason,
                    itemDecisionId);
            }
        }

        var sortedActions = actions
            .OrderBy(a => (int)a.EntityKind)
            .ThenBy(a => a.StableIdentity, StringComparer.Ordinal)
            .ThenBy(a => a.ArchiveLocalId, StringComparer.Ordinal)
            .ToList();

        var sampleDetails = new Dictionary<MergeEntityClassification, List<string>>();
        foreach (var action in sortedActions)
        {
            if (!sampleDetails.TryGetValue(action.Classification, out var list))
            {
                list = new List<string>();
                sampleDetails[action.Classification] = list;
            }

            if (list.Count < MergePreflightPlan.MaxSampleDetailsPerCategory)
            {
                list.Add($"{action.EntityKind}:{action.ArchiveLocalId}:{action.ReasonCode}");
            }
        }

        var sampleDetailsReadOnly = sampleDetails.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value);

        var sortedKnowledgeStateDecisions = knowledgeStateDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();
        var sortedWorkflowStatusDecisions = workflowStatusDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();
        var sortedPreferredVariantSelectionDecisions = preferredVariantSelectionDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();
        var sortedBlockingPrerequisites = blockingPrerequisites
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var status = MergePreflightStatus.Ready;
        string? errorCode = null;
        if (hasCausalLearningReviewConflict)
        {
            status = MergePreflightStatus.NonExecutableConflict;
            errorCode = Schema13MergePreflightErrorCodes.CausalHistoryConflict;
        }
        else if (sortedBlockingPrerequisites.Count > 0)
        {
            status = MergePreflightStatus.BlockedByPrerequisite;
        }
        else if (sortedKnowledgeStateDecisions.Count > 0 || sortedWorkflowStatusDecisions.Count > 0 ||
                 sortedPreferredVariantSelectionDecisions.Count > 0)
        {
            status = MergePreflightStatus.RequiresUserDecision;
        }
        else if (sortedActions.All(a => a.Classification == MergeEntityClassification.ExactDuplicateSkipped || a.Classification == MergeEntityClassification.DeduplicatedEvent))
        {
            status = MergePreflightStatus.NoChanges;
        }

        var isExecutable = status is MergePreflightStatus.Ready or MergePreflightStatus.NoChanges;

        return new MergePreflightPlan(
            status,
            isExecutable,
            archiveManifest,
            true,
            counts,
            sortedActions,
            Array.Empty<DerivedAnswerVariantPlan>(),
            sortedKnowledgeStateDecisions,
            sortedWorkflowStatusDecisions,
            Array.Empty<SemanticMeaningGroupingDecision>(),
            sortedPreferredVariantSelectionDecisions,
            sortedBlockingPrerequisites,
            sampleDetailsReadOnly,
            warningCodes.ToList(),
            requiresSchedulerReplay,
            errorCode);
    }

    private static IReadOnlyDictionary<int, CausalLearningReviewAction> PlanCausalLearningReviewActions(
        IReadOnlyList<BackupLearningReviewV2> targetReviews,
        IReadOnlyList<BackupLearningReviewV2> sourceReviews,
        Func<BackupLearningReviewV2, FutureCardIdentity> targetCardIdentity,
        Func<BackupLearningReviewV2, FutureCardIdentity> sourceCardIdentity,
        Func<BackupLearningReviewV2, string> targetFingerprint,
        Func<BackupLearningReviewV2, string> sourceFingerprint)
    {
        var targetGroups = BuildCausalLearningReviewGroups(
            targetReviews,
            targetCardIdentity,
            targetFingerprint);
        var sourceGroups = BuildCausalLearningReviewGroups(
            sourceReviews,
            sourceCardIdentity,
            sourceFingerprint);
        var actions = new Dictionary<int, CausalLearningReviewAction>();

        foreach (var (key, sourceOccurrences) in sourceGroups)
        {
            var targetOccurrences = targetGroups.TryGetValue(key, out var existing)
                ? existing
                : [];
            var targetIsPrefix = IsExactFingerprintPrefix(targetOccurrences, sourceOccurrences);
            var sourceIsPrefix = IsExactFingerprintPrefix(sourceOccurrences, targetOccurrences);

            for (var occurrenceIndex = 0; occurrenceIndex < sourceOccurrences.Count; occurrenceIndex++)
            {
                var sourceOccurrence = sourceOccurrences[occurrenceIndex];
                CausalLearningReviewAction action;
                if (targetIsPrefix)
                {
                    action = occurrenceIndex < targetOccurrences.Count
                        ? new CausalLearningReviewAction(
                            MergeEntityClassification.DeduplicatedEvent,
                            "learning-review-causal-prefix-occurrence-deduplicated")
                        : new CausalLearningReviewAction(
                            MergeEntityClassification.New,
                            "learning-review-causal-source-tail-new");
                }
                else if (sourceIsPrefix)
                {
                    action = new CausalLearningReviewAction(
                        MergeEntityClassification.DeduplicatedEvent,
                        "learning-review-causal-target-ahead-occurrence-deduplicated");
                }
                else
                {
                    action = new CausalLearningReviewAction(
                        MergeEntityClassification.UnresolvedConflict,
                        Schema13MergePreflightErrorCodes.CausalHistoryConflict);
                }

                actions.Add(sourceOccurrence.SourceIndex, action);
            }
        }

        return actions;
    }

    private static Dictionary<CausalLearningReviewGroupKey, List<CausalLearningReviewOccurrence>>
        BuildCausalLearningReviewGroups(
            IReadOnlyList<BackupLearningReviewV2> reviews,
            Func<BackupLearningReviewV2, FutureCardIdentity> cardIdentity,
            Func<BackupLearningReviewV2, string> fingerprint)
    {
        var groups = new Dictionary<CausalLearningReviewGroupKey, List<CausalLearningReviewOccurrence>>();
        for (var index = 0; index < reviews.Count; index++)
        {
            var review = reviews[index];
            var key = new CausalLearningReviewGroupKey(
                cardIdentity(review).Value,
                Data.Schema8.Schema8Utc.Normalize(review.ReviewedAtUtc).Ticks);
            if (!groups.TryGetValue(key, out var occurrences))
            {
                occurrences = [];
                groups.Add(key, occurrences);
            }

            occurrences.Add(new CausalLearningReviewOccurrence(index, fingerprint(review)));
        }

        return groups;
    }

    private static bool IsExactFingerprintPrefix(
        IReadOnlyList<CausalLearningReviewOccurrence> prefix,
        IReadOnlyList<CausalLearningReviewOccurrence> full)
    {
        if (prefix.Count > full.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(prefix[index].Fingerprint, full[index].Fingerprint, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct CausalLearningReviewGroupKey(
        string FutureCardIdentity,
        long ReviewedAtUtcTicks);

    private sealed record CausalLearningReviewOccurrence(int SourceIndex, string Fingerprint);

    private sealed record CausalLearningReviewAction(
        MergeEntityClassification Classification,
        string ReasonCode);
}
