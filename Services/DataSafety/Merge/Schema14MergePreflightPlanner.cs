using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Pure Schema-14 target-centric merge planner (KF-LEARN-011 Slice 5).
/// Evaluates preflight merge actions and conflicts between two V4 payloads.
/// </summary>
public static class Schema14MergePreflightPlanner
{
    public static MergePreflightPlan CreateCombinedPlan(
        BackupPayloadV4 target,
        BackupPayloadV4 source,
        MergeManifestInfo manifest)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(manifest);

        var basePlan = MergePreflightPlannerV2.CreatePlan(ToV2(target), ToV2(source), manifest);
        var schema14Plan = CreatePlan(target, source);

        if (!basePlan.IsExecutable)
        {
            return basePlan with { Schema14Plan = schema14Plan };
        }

        if (!schema14Plan.IsExecutable)
        {
            return basePlan with
            {
                Status = MergePreflightStatus.NonExecutableConflict,
                IsExecutable = false,
                ErrorCode = schema14Plan.Conflicts[0].ReasonCode,
                Schema14Plan = schema14Plan
            };
        }

        var status = basePlan.Status == MergePreflightStatus.Ready || schema14Plan.RequiresMutation
            ? MergePreflightStatus.Ready
            : MergePreflightStatus.NoChanges;

        return basePlan with
        {
            Status = status,
            IsExecutable = true,
            ErrorCode = null,
            Schema14Plan = schema14Plan
        };
    }

    public static Schema14MergePreflightPlan CreatePlan(BackupPayloadV4 target, BackupPayloadV4 source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        var actions = new List<Schema14MergeAction>();
        var conflicts = new List<Schema14MergeConflict>();

        var targetIdentities = BuildIdentities(target);
        var sourceIdentities = BuildIdentities(source);

        // Word controls
        PlanControls(
            target.WordLearningControls.ToDictionary(c => targetIdentities.WordByLocalId[c.VocabularyId], c => c.DecidedAtUtc, StringComparer.Ordinal),
            source.WordLearningControls.ToDictionary(c => sourceIdentities.WordByLocalId[c.VocabularyId], c => c.DecidedAtUtc, StringComparer.Ordinal),
            targetIdentities.WordSet,
            Schema14MergeActionClassification.AddWordLearningControl,
            Schema14MergeActionClassification.ReconcileWordLearningControlTimestamp,
            "word-learning-control",
            actions);

        // Sense controls
        PlanControls(
            target.SenseLearningControls.ToDictionary(c => targetIdentities.SenseByLocalId[c.SenseId], c => c.DecidedAtUtc, StringComparer.Ordinal),
            source.SenseLearningControls.ToDictionary(c => sourceIdentities.SenseByLocalId[c.SenseId], c => c.DecidedAtUtc, StringComparer.Ordinal),
            targetIdentities.SenseSet,
            Schema14MergeActionClassification.AddSenseLearningControl,
            Schema14MergeActionClassification.ReconcileSenseLearningControlTimestamp,
            "sense-learning-control",
            actions);

        // LearningTargets, variants, history, and states
        PlanTargets(target, source, targetIdentities, sourceIdentities, actions, conflicts);

        var sortedActions = actions
            .OrderBy(a => a.SemanticIdentity, StringComparer.Ordinal)
            .ThenBy(a => a.Classification)
            .ThenBy(a => a.ActionKey, StringComparer.Ordinal)
            .ToList();

        var sortedConflicts = conflicts
            .DistinctBy(c => c.ConflictKey, StringComparer.Ordinal)
            .OrderBy(c => c.ReasonCode, StringComparer.Ordinal)
            .ThenBy(c => c.SemanticIdentity, StringComparer.Ordinal)
            .ToList();

        return new Schema14MergePreflightPlan(
            sortedActions,
            sortedConflicts,
            [],
            string.Empty);
    }

    internal static BackupPayloadV2 ToV2(BackupPayloadV4 payload) => new(
        payload.SourceMaterials,
        payload.Vocabulary,
        payload.Senses,
        payload.PreparedLearning,
        [],
        [],
        [],
        new BackupLearningDataV2([], []),
        payload.Workflows,
        payload.DerivedTermEvidence,
        payload.Extensions);

    private static void PlanControls(
        IReadOnlyDictionary<string, DateTime> target,
        IReadOnlyDictionary<string, DateTime> source,
        IReadOnlySet<string> targetSemanticEntities,
        Schema14MergeActionClassification addClassification,
        Schema14MergeActionClassification reconcileClassification,
        string reasonPrefix,
        ICollection<Schema14MergeAction> actions)
    {
        foreach (var identity in target.Keys.Concat(source.Keys).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal))
        {
            var targetHas = target.TryGetValue(identity, out var targetTs);
            var sourceHas = source.TryGetValue(identity, out var sourceTs);

            Schema14MergeActionClassification classification;
            string reason;
            if (sourceHas && !targetHas)
            {
                classification = addClassification;
                reason = reasonPrefix + "-add";
            }
            else if (!sourceHas)
            {
                classification = Schema14MergeActionClassification.PreserveTargetOnly;
                reason = reasonPrefix + "-target-only-preserved";
            }
            else if (sourceTs < targetTs)
            {
                classification = reconcileClassification;
                reason = reasonPrefix + "-source-earlier";
            }
            else
            {
                classification = Schema14MergeActionClassification.NoChange;
                reason = sourceTs == targetTs
                    ? reasonPrefix + "-identical"
                    : reasonPrefix + "-target-earlier-preserved";
            }

            actions.Add(new Schema14MergeAction(
                MakeActionKey(classification, identity, null),
                identity,
                classification,
                reason,
                targetSemanticEntities.Contains(identity),
                targetHas ? targetTs : null,
                sourceHas ? sourceTs : null));
        }
    }

    private static void PlanTargets(
        BackupPayloadV4 target,
        BackupPayloadV4 source,
        Schema14Identities targetIds,
        Schema14Identities sourceIds,
        ICollection<Schema14MergeAction> actions,
        ICollection<Schema14MergeConflict> conflicts)
    {
        var targetHistoryByTargetId = target.TargetFsrsReviewHistoryEntries
            .GroupBy(h => h.TargetId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.SequenceNumber).ToList(), StringComparer.Ordinal);
        var sourceHistoryByTargetId = source.TargetFsrsReviewHistoryEntries
            .GroupBy(h => h.TargetId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.SequenceNumber).ToList(), StringComparer.Ordinal);

        var targetStateByTargetId = target.TargetFsrsStates.ToDictionary(s => s.TargetId, StringComparer.Ordinal);
        var sourceStateByTargetId = source.TargetFsrsStates.ToDictionary(s => s.TargetId, StringComparer.Ordinal);

        var targetVariantsByTargetId = target.TargetAnswerVariants
            .GroupBy(v => v.TargetId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var sourceVariantsByTargetId = source.TargetAnswerVariants
            .GroupBy(v => v.TargetId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var targetBySemantic = new Dictionary<string, BackupLearningTarget>(StringComparer.Ordinal);
        foreach (var t in target.LearningTargets)
        {
            var semId = ComputeTargetSemanticIdentity(t, targetIds);
            targetBySemantic[semId] = t;
        }

        var sourceBySemantic = new Dictionary<string, BackupLearningTarget>(StringComparer.Ordinal);
        foreach (var t in source.LearningTargets)
        {
            var semId = ComputeTargetSemanticIdentity(t, sourceIds);
            sourceBySemantic[semId] = t;
        }

        // Global stable ID collision check for history entries
        var targetHistoryByStableId = target.TargetFsrsReviewHistoryEntries
            .ToDictionary(h => h.StableId, StringComparer.Ordinal);
        foreach (var sh in source.TargetFsrsReviewHistoryEntries)
        {
            if (targetHistoryByStableId.TryGetValue(sh.StableId, out var th))
            {
                if (th.Rating != sh.Rating
                    || th.ReviewedAtUtc != sh.ReviewedAtUtc
                    || th.SequenceNumber != sh.SequenceNumber)
                {
                    conflicts.Add(new Schema14MergeConflict(
                        $"conflict:stable-id:{sh.StableId}",
                        sh.TargetId,
                        Schema14MergePreflightErrorCodes.StableIdConflict,
                        sh.StableId));
                }
            }
        }

        // Plan targets
        foreach (var (semId, sourceTarget) in sourceBySemantic)
        {
            if (targetBySemantic.TryGetValue(semId, out var targetTarget))
            {
                // Target exists in both target and source
                // Compare histories
                var thList = targetHistoryByTargetId.TryGetValue(targetTarget.Id, out var th) ? th : [];
                var shList = sourceHistoryByTargetId.TryGetValue(sourceTarget.Id, out var sh) ? sh : [];

                if (IsExactMatch(thList, shList))
                {
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.NoChange, semId, "history"),
                        semId,
                        Schema14MergeActionClassification.NoChange,
                        "history-identical",
                        true));
                }
                else if (IsStrictPrefix(thList, shList))
                {
                    // Target is exact prefix of source: plan tail append and state advance
                    for (var i = thList.Count; i < shList.Count; i++)
                    {
                        var entry = shList[i];
                        actions.Add(new Schema14MergeAction(
                            MakeActionKey(Schema14MergeActionClassification.AppendTargetFsrsReviewHistory, semId, entry.StableId),
                            semId,
                            Schema14MergeActionClassification.AppendTargetFsrsReviewHistory,
                            "history-tail-extension",
                            true,
                            FsrsReviewFact: new Schema14FsrsReviewFact(entry.StableId, entry.SequenceNumber, entry.Rating, entry.ReviewedAtUtc)));
                    }

                    var sourceState = sourceStateByTargetId[sourceTarget.Id];
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.UpdateTargetFsrsState, semId, "state"),
                        semId,
                        Schema14MergeActionClassification.UpdateTargetFsrsState,
                        "state-advance-with-tail",
                        true,
                        SourceState: new Schema14FsrsStateFact(
                            sourceState.State, sourceState.Stability, sourceState.Difficulty,
                            sourceState.LastReviewedAtUtc, sourceState.StepIndex, sourceState.DueAtUtc)));
                }
                else if (IsStrictPrefix(shList, thList))
                {
                    // Source is prefix of target: target already ahead, preserve
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.NoChange, semId, "history"),
                        semId,
                        Schema14MergeActionClassification.NoChange,
                        "target-history-ahead-preserved",
                        true));
                }
                else
                {
                    // Divergent histories
                    conflicts.Add(new Schema14MergeConflict(
                        $"conflict:history:{semId}",
                        semId,
                        Schema14MergePreflightErrorCodes.CausalHistoryConflict));
                }

                // Check answer variants
                var tVariants = targetVariantsByTargetId.TryGetValue(targetTarget.Id, out var tv) ? tv : [];
                var sVariants = sourceVariantsByTargetId.TryGetValue(sourceTarget.Id, out var sv) ? sv : [];
                var tVariantByNorm = tVariants.ToDictionary(v => v.NormalizedText.ToLowerInvariant(), StringComparer.Ordinal);

                foreach (var svar in sVariants)
                {
                    var normKey = svar.NormalizedText.ToLowerInvariant();
                    if (!tVariantByNorm.ContainsKey(normKey))
                    {
                        actions.Add(new Schema14MergeAction(
                            MakeActionKey(Schema14MergeActionClassification.AddTargetAnswerVariant, semId, svar.StableId),
                            semId,
                            Schema14MergeActionClassification.AddTargetAnswerVariant,
                            "variant-add",
                            true,
                            VariantFact: new Schema14TargetAnswerVariantFact(
                                svar.StableId, semId, svar.AnswerLanguage, svar.DisplayText, svar.NormalizedText,
                                svar.Requirement, svar.IsPreferred, svar.RequiredSinceUtc,
                                svar.SourceMeaningId is not null ? sourceIds.MeaningByLocalId.GetValueOrDefault(svar.SourceMeaningId) : null,
                                svar.CreatedAtUtc, svar.UpdatedAtUtc)));
                    }
                }
            }
            else
            {
                // New target from source
                actions.Add(new Schema14MergeAction(
                    MakeActionKey(Schema14MergeActionClassification.AddLearningTarget, semId, null),
                    semId,
                    Schema14MergeActionClassification.AddLearningTarget,
                    "target-add",
                    false,
                    TargetFact: new Schema14TargetFact(
                        sourceTarget.StableId,
                        sourceIds.SenseByLocalId[sourceTarget.SenseId],
                        sourceTarget.TargetKind,
                        sourceTarget.SourceLanguage,
                        sourceTarget.TargetLanguage,
                        sourceTarget.TypingOptOut,
                        sourceTarget.CreatedAtUtc,
                        sourceTarget.UpdatedAtUtc)));

                // Add its variants
                if (sourceVariantsByTargetId.TryGetValue(sourceTarget.Id, out var sVariants))
                {
                    foreach (var svar in sVariants)
                    {
                        actions.Add(new Schema14MergeAction(
                            MakeActionKey(Schema14MergeActionClassification.AddTargetAnswerVariant, semId, svar.StableId),
                            semId,
                            Schema14MergeActionClassification.AddTargetAnswerVariant,
                            "variant-add-new-target",
                            false,
                            VariantFact: new Schema14TargetAnswerVariantFact(
                                svar.StableId, semId, svar.AnswerLanguage, svar.DisplayText, svar.NormalizedText,
                                svar.Requirement, svar.IsPreferred, svar.RequiredSinceUtc,
                                svar.SourceMeaningId is not null ? sourceIds.MeaningByLocalId.GetValueOrDefault(svar.SourceMeaningId) : null,
                                svar.CreatedAtUtc, svar.UpdatedAtUtc)));
                    }
                }

                // Add its history
                if (sourceHistoryByTargetId.TryGetValue(sourceTarget.Id, out var shList))
                {
                    foreach (var entry in shList)
                    {
                        actions.Add(new Schema14MergeAction(
                            MakeActionKey(Schema14MergeActionClassification.AppendTargetFsrsReviewHistory, semId, entry.StableId),
                            semId,
                            Schema14MergeActionClassification.AppendTargetFsrsReviewHistory,
                            "history-add-new-target",
                            false,
                            FsrsReviewFact: new Schema14FsrsReviewFact(entry.StableId, entry.SequenceNumber, entry.Rating, entry.ReviewedAtUtc)));
                    }
                }

                // Add its state
                if (sourceStateByTargetId.TryGetValue(sourceTarget.Id, out var sourceState))
                {
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.InsertTargetFsrsState, semId, "state"),
                        semId,
                        Schema14MergeActionClassification.InsertTargetFsrsState,
                        "state-insert-new-target",
                        false,
                        SourceState: new Schema14FsrsStateFact(
                            sourceState.State, sourceState.Stability, sourceState.Difficulty,
                            sourceState.LastReviewedAtUtc, sourceState.StepIndex, sourceState.DueAtUtc)));
                }
            }
        }

        // Plan TargetReviews via causal ordered-prefix interaction-stream principle
        var targetReviewsByStableId = target.TargetReviews.ToDictionary(r => r.StableId, StringComparer.Ordinal);
        var sourceVariantById = source.TargetAnswerVariants.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var targetVariantById = target.TargetAnswerVariants.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var targetTargetById = target.LearningTargets.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var sourceTargetById = source.LearningTargets.ToDictionary(t => t.Id, StringComparer.Ordinal);

        var targetVariantSemanticKeyById = target.TargetAnswerVariants.ToDictionary(
            v => v.Id,
            v => $"{v.AnswerLanguage.ToLowerInvariant()}:{v.NormalizedText.ToLowerInvariant()}",
            StringComparer.Ordinal);
        var sourceVariantSemanticKeyById = source.TargetAnswerVariants.ToDictionary(
            v => v.Id,
            v => $"{v.AnswerLanguage.ToLowerInvariant()}:{v.NormalizedText.ToLowerInvariant()}",
            StringComparer.Ordinal);

        // Global StableId collision check
        foreach (var sr in source.TargetReviews)
        {
            if (targetReviewsByStableId.TryGetValue(sr.StableId, out var tr))
            {
                var sTargetSemId = ComputeTargetSemanticIdentity(sourceTargetById[sr.TargetId], sourceIds);
                var tTargetSemId = ComputeTargetSemanticIdentity(targetTargetById[tr.TargetId], targetIds);
                var sTargetVarKey = sr.TargetAnswerVariantId is not null ? sourceVariantSemanticKeyById.GetValueOrDefault(sr.TargetAnswerVariantId) : null;
                var tTargetVarKey = tr.TargetAnswerVariantId is not null ? targetVariantSemanticKeyById.GetValueOrDefault(tr.TargetAnswerVariantId) : null;
                var sMatchedVarKey = sr.MatchedAnswerVariantId is not null ? sourceVariantSemanticKeyById.GetValueOrDefault(sr.MatchedAnswerVariantId) : null;
                var tMatchedVarKey = tr.MatchedAnswerVariantId is not null ? targetVariantSemanticKeyById.GetValueOrDefault(tr.MatchedAnswerVariantId) : null;

                if (!string.Equals(sTargetSemId, tTargetSemId, StringComparison.Ordinal)
                    || sr.Rating != tr.Rating
                    || Schema8Utc.Normalize(sr.ReviewedAtUtc).Ticks != Schema8Utc.Normalize(tr.ReviewedAtUtc).Ticks
                    || Schema8Utc.Normalize(sr.DueAtUtc).Ticks != Schema8Utc.Normalize(tr.DueAtUtc).Ticks
                    || sr.WasTypedAnswer != tr.WasTypedAnswer
                    || sr.WasCorrect != tr.WasCorrect
                    || sr.IsSessionRepeat != tr.IsSessionRepeat
                    || !string.Equals(sTargetVarKey, tTargetVarKey, StringComparison.Ordinal)
                    || !string.Equals(sMatchedVarKey, tMatchedVarKey, StringComparison.Ordinal))
                {
                    conflicts.Add(new Schema14MergeConflict(
                        $"conflict:target-review-stable-id:{sr.StableId}",
                        sTargetSemId,
                        Schema14MergePreflightErrorCodes.StableIdConflict,
                        sr.StableId));
                }
            }
        }

        // Group occurrences per TargetSemanticIdentity
        var targetOccurrencesBySemId = new Dictionary<string, List<TargetReviewOccurrence>>(StringComparer.Ordinal);
        foreach (var tr in target.TargetReviews)
        {
            var semId = ComputeTargetSemanticIdentity(targetTargetById[tr.TargetId], targetIds);
            if (!targetOccurrencesBySemId.TryGetValue(semId, out var list))
            {
                list = [];
                targetOccurrencesBySemId[semId] = list;
            }
            list.Add(new TargetReviewOccurrence(
                semId,
                Schema8Utc.Normalize(tr.ReviewedAtUtc),
                tr.Rating,
                tr.WasTypedAnswer,
                tr.WasCorrect,
                tr.IsSessionRepeat,
                Schema8Utc.Normalize(tr.DueAtUtc),
                tr.TargetAnswerVariantId is not null ? targetVariantSemanticKeyById.GetValueOrDefault(tr.TargetAnswerVariantId) : null,
                tr.MatchedAnswerVariantId is not null ? targetVariantSemanticKeyById.GetValueOrDefault(tr.MatchedAnswerVariantId) : null,
                tr));
        }

        var sourceOccurrencesBySemId = new Dictionary<string, List<TargetReviewOccurrence>>(StringComparer.Ordinal);
        foreach (var sr in source.TargetReviews)
        {
            var semId = ComputeTargetSemanticIdentity(sourceTargetById[sr.TargetId], sourceIds);
            if (!sourceOccurrencesBySemId.TryGetValue(semId, out var list))
            {
                list = [];
                sourceOccurrencesBySemId[semId] = list;
            }
            list.Add(new TargetReviewOccurrence(
                semId,
                Schema8Utc.Normalize(sr.ReviewedAtUtc),
                sr.Rating,
                sr.WasTypedAnswer,
                sr.WasCorrect,
                sr.IsSessionRepeat,
                Schema8Utc.Normalize(sr.DueAtUtc),
                sr.TargetAnswerVariantId is not null ? sourceVariantSemanticKeyById.GetValueOrDefault(sr.TargetAnswerVariantId) : null,
                sr.MatchedAnswerVariantId is not null ? sourceVariantSemanticKeyById.GetValueOrDefault(sr.MatchedAnswerVariantId) : null,
                sr));
        }

        foreach (var list in targetOccurrencesBySemId.Values)
        {
            list.Sort(CompareOccurrences);
        }
        foreach (var list in sourceOccurrencesBySemId.Values)
        {
            list.Sort(CompareOccurrences);
        }

        string? ResolveVariantStableId(string? sourceVariantId, BackupLearningTarget sourceTarget)
        {
            if (sourceVariantId is null) return null;
            var sourceVar = sourceVariantById.GetValueOrDefault(sourceVariantId);
            if (sourceVar is null) return null;
            var semId = ComputeTargetSemanticIdentity(sourceTarget, sourceIds);
            if (targetBySemantic.TryGetValue(semId, out var targetTarget))
            {
                var tVars = targetVariantsByTargetId.TryGetValue(targetTarget.Id, out var tv) ? tv : [];
                var match = tVars.FirstOrDefault(v => string.Equals(v.NormalizedText, sourceVar.NormalizedText, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match.StableId;
                }
            }
            return sourceVar.StableId;
        }

        foreach (var (targetSemId, sList) in sourceOccurrencesBySemId)
        {
            var tList = targetOccurrencesBySemId.TryGetValue(targetSemId, out var existing) ? existing : [];

            var targetIsPrefix = IsExactOccurrencePrefix(tList, sList);
            var sourceIsPrefix = IsExactOccurrencePrefix(sList, tList);

            if (targetIsPrefix && sList.Count == tList.Count)
            {
                // Case A: Semantically identical stream
                foreach (var occ in sList)
                {
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.NoChange, targetSemId, occ.Review.StableId),
                        targetSemId,
                        Schema14MergeActionClassification.NoChange,
                        "target-review-identical",
                        true));
                }
            }
            else if (targetIsPrefix)
            {
                // Case B: Destination is strict compatible prefix of imported
                for (var i = 0; i < tList.Count; i++)
                {
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.NoChange, targetSemId, sList[i].Review.StableId),
                        targetSemId,
                        Schema14MergeActionClassification.NoChange,
                        "target-review-identical",
                        true));
                }

                for (var i = tList.Count; i < sList.Count; i++)
                {
                    var sr = sList[i].Review;
                    var sTarget = sourceTargetById[sr.TargetId];
                    var targetVariantStableId = ResolveVariantStableId(sr.TargetAnswerVariantId, sTarget);
                    var matchedVariantStableId = ResolveVariantStableId(sr.MatchedAnswerVariantId, sTarget);

                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.AddTargetReview, targetSemId, sr.StableId),
                        targetSemId,
                        Schema14MergeActionClassification.AddTargetReview,
                        "target-review-add",
                        true,
                        ReviewFact: new Schema14TargetReviewFact(
                            sr.StableId,
                            targetSemId,
                            sr.SessionId,
                            sr.Rating,
                            sr.WasTypedAnswer,
                            sr.WasCorrect,
                            sr.IsSessionRepeat,
                            targetVariantStableId,
                            matchedVariantStableId,
                            sr.ReviewedAtUtc,
                            sr.DueAtUtc)));
                }
            }
            else if (sourceIsPrefix)
            {
                // Case C: Imported is older compatible prefix of destination
                foreach (var occ in sList)
                {
                    actions.Add(new Schema14MergeAction(
                        MakeActionKey(Schema14MergeActionClassification.NoChange, targetSemId, occ.Review.StableId),
                        targetSemId,
                        Schema14MergeActionClassification.NoChange,
                        "target-review-ahead-preserved",
                        true));
                }
            }
            else
            {
                // Case D: Non-prefix or contradictory overlap
                conflicts.Add(new Schema14MergeConflict(
                    $"conflict:target-review:{targetSemId}",
                    targetSemId,
                    Schema14MergePreflightErrorCodes.CausalHistoryConflict));
            }
        }
    }

    private static bool IsExactMatch(List<BackupTargetFsrsReviewHistoryEntry> a, List<BackupTargetFsrsReviewHistoryEntry> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Rating != b[i].Rating
                || a[i].SequenceNumber != b[i].SequenceNumber
                || a[i].ReviewedAtUtc != b[i].ReviewedAtUtc)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsStrictPrefix(List<BackupTargetFsrsReviewHistoryEntry> prefix, List<BackupTargetFsrsReviewHistoryEntry> full)
    {
        if (prefix.Count >= full.Count) return false;
        for (var i = 0; i < prefix.Count; i++)
        {
            if (prefix[i].Rating != full[i].Rating
                || prefix[i].SequenceNumber != full[i].SequenceNumber
                || prefix[i].ReviewedAtUtc != full[i].ReviewedAtUtc)
            {
                return false;
            }
        }
        return true;
    }

    private static string ComputeTargetSemanticIdentity(BackupLearningTarget target, Schema14Identities identities)
    {
        var senseStableId = identities.SenseByLocalId[target.SenseId];
        return $"{senseStableId}:{(int)target.TargetKind}:{target.SourceLanguage.ToLowerInvariant()}->{target.TargetLanguage.ToLowerInvariant()}";
    }

    private static string MakeActionKey(Schema14MergeActionClassification classification, string semanticIdentity, string? discriminator) =>
        $"action:{(int)classification}:{semanticIdentity}:{discriminator ?? string.Empty}";

    private static Schema14Identities BuildIdentities(BackupPayloadV4 payload)
    {
        var wordByLocalId = payload.Vocabulary.ToDictionary(
            w => w.Id,
            w => VocabularyMergeIdentityPolicy.Compute(w).Value,
            StringComparer.Ordinal);
        var senseByLocalId = payload.Senses.ToDictionary(s => s.Id, s => s.StableId, StringComparer.Ordinal);
        var meaningByLocalId = payload.PreparedLearning.ToDictionary(m => m.Id, m => m.StableId, StringComparer.Ordinal);

        return new Schema14Identities(
            wordByLocalId,
            senseByLocalId,
            meaningByLocalId,
            wordByLocalId.Values.ToHashSet(StringComparer.Ordinal),
            senseByLocalId.Values.ToHashSet(StringComparer.Ordinal));
    }

    private sealed record Schema14Identities(
        IReadOnlyDictionary<string, string> WordByLocalId,
        IReadOnlyDictionary<string, string> SenseByLocalId,
        IReadOnlyDictionary<string, string> MeaningByLocalId,
        IReadOnlySet<string> WordSet,
        IReadOnlySet<string> SenseSet);

    private sealed record TargetReviewOccurrence(
        string TargetSemanticIdentity,
        DateTime NormalizedReviewedAtUtc,
        BackupReviewRating Rating,
        bool WasTypedAnswer,
        bool WasCorrect,
        bool IsSessionRepeat,
        DateTime NormalizedDueAtUtc,
        string? TargetVariantKey,
        string? MatchedVariantKey,
        BackupTargetReview Review);

    private static int CompareOccurrences(TargetReviewOccurrence a, TargetReviewOccurrence b)
    {
        var cmp = a.NormalizedReviewedAtUtc.Ticks.CompareTo(b.NormalizedReviewedAtUtc.Ticks);
        if (cmp != 0) return cmp;

        cmp = ((int)a.Rating).CompareTo((int)b.Rating);
        if (cmp != 0) return cmp;

        cmp = a.WasTypedAnswer.CompareTo(b.WasTypedAnswer);
        if (cmp != 0) return cmp;

        cmp = a.WasCorrect.CompareTo(b.WasCorrect);
        if (cmp != 0) return cmp;

        cmp = a.IsSessionRepeat.CompareTo(b.IsSessionRepeat);
        if (cmp != 0) return cmp;

        cmp = a.NormalizedDueAtUtc.Ticks.CompareTo(b.NormalizedDueAtUtc.Ticks);
        if (cmp != 0) return cmp;

        cmp = string.CompareOrdinal(a.TargetVariantKey ?? string.Empty, b.TargetVariantKey ?? string.Empty);
        if (cmp != 0) return cmp;

        return string.CompareOrdinal(a.MatchedVariantKey ?? string.Empty, b.MatchedVariantKey ?? string.Empty);
    }

    private static bool IsExactOccurrencePrefix(
        IReadOnlyList<TargetReviewOccurrence> prefix,
        IReadOnlyList<TargetReviewOccurrence> full)
    {
        if (prefix.Count > full.Count) return false;
        for (var i = 0; i < prefix.Count; i++)
        {
            if (CompareOccurrences(prefix[i], full[i]) != 0) return false;
        }
        return true;
    }
}
