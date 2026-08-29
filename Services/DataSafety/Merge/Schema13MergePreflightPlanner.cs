using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Pure Schema-13 learning planner. Inputs are validated portable projections; output contains only
/// semantic identities and factual values, never target-local ids.
/// </summary>
public static class Schema13MergePreflightPlanner
{
    public static MergePreflightPlan CreateCombinedPlan(
        BackupPayloadV3 target,
        BackupPayloadV3 source,
        MergeManifestInfo manifest)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(manifest);

        var basePlan = MergePreflightPlannerV2.CreatePlan(ToV2(target), ToV2(source), manifest);
        var schema13Plan = CreatePlan(target, source);

        if (!basePlan.IsExecutable)
        {
            return basePlan with { Schema13Plan = schema13Plan };
        }

        if (!schema13Plan.IsExecutable)
        {
            return basePlan with
            {
                Status = MergePreflightStatus.NonExecutableConflict,
                IsExecutable = false,
                ErrorCode = schema13Plan.Conflicts[0].ReasonCode,
                Schema13Plan = schema13Plan
            };
        }

        var status = basePlan.Status == MergePreflightStatus.Ready || schema13Plan.RequiresMutation
            ? MergePreflightStatus.Ready
            : MergePreflightStatus.NoChanges;
        return basePlan with
        {
            Status = status,
            IsExecutable = true,
            ErrorCode = null,
            Schema13Plan = schema13Plan
        };
    }

    public static Schema13MergePreflightPlan CreatePlan(BackupPayloadV3 target, BackupPayloadV3 source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        var actions = new List<Schema13MergeAction>();
        var conflicts = new List<Schema13MergeConflict>();

        var targetIdentities = BuildIdentities(target, "target");
        var sourceIdentities = BuildIdentities(source, "source");

        var targetWordControls = target.WordLearningControls.ToDictionary(
            item => Resolve(targetIdentities.WordByLocalId, item.VocabularyId, "target word control"),
            item => item.DecidedAtUtc,
            StringComparer.Ordinal);
        var sourceWordControls = source.WordLearningControls.ToDictionary(
            item => Resolve(sourceIdentities.WordByLocalId, item.VocabularyId, "source word control"),
            item => item.DecidedAtUtc,
            StringComparer.Ordinal);
        var targetSenseControls = target.SenseLearningControls.ToDictionary(
            item => Resolve(targetIdentities.SenseByLocalId, item.SenseId, "target sense control"),
            item => item.DecidedAtUtc,
            StringComparer.Ordinal);
        var sourceSenseControls = source.SenseLearningControls.ToDictionary(
            item => Resolve(sourceIdentities.SenseByLocalId, item.SenseId, "source sense control"),
            item => item.DecidedAtUtc,
            StringComparer.Ordinal);

        PlanControls(
            targetWordControls,
            sourceWordControls,
            targetIdentities.WordIdentitySet,
            Schema13MergeActionClassification.AddWordLearningControl,
            Schema13MergeActionClassification.ReconcileWordLearningControlTimestamp,
            "word-learning-control",
            actions);
        PlanControls(
            targetSenseControls,
            sourceSenseControls,
            targetIdentities.SenseIdentitySet,
            Schema13MergeActionClassification.AddSenseLearningControl,
            Schema13MergeActionClassification.ReconcileSenseLearningControlTimestamp,
            "sense-learning-control",
            actions);

        var targetHistory = BuildHistory(target, targetIdentities.CardByLocalId);
        var sourceHistory = BuildHistory(source, sourceIdentities.CardByLocalId);
        var targetStates = BuildStates(target, targetIdentities.CardByLocalId);
        var sourceStates = BuildStates(source, sourceIdentities.CardByLocalId);

        ClassifyGlobalStableIds(targetHistory, sourceHistory, actions, conflicts);
        PlanCards(targetHistory, sourceHistory, targetStates, sourceStates, actions, conflicts);

        var expectations = BuildExpectations(
            targetWordControls,
            sourceWordControls,
            targetSenseControls,
            sourceSenseControls,
            targetIdentities,
            sourceIdentities,
            targetHistory,
            targetStates);

        var sortedActions = actions
            .OrderBy(action => action.SemanticIdentity, StringComparer.Ordinal)
            .ThenBy(ActionOrder)
            .ThenBy(action => action.ReviewFact?.SequenceNumber ?? 0)
            .ThenBy(action => action.ActionKey, StringComparer.Ordinal)
            .ToList();
        var sortedConflicts = conflicts
            .DistinctBy(conflict => conflict.ConflictKey, StringComparer.Ordinal)
            .OrderBy(ConflictOrder)
            .ThenBy(conflict => conflict.ReasonCode, StringComparer.Ordinal)
            .ThenBy(conflict => conflict.SemanticIdentity, StringComparer.Ordinal)
            .ThenBy(conflict => conflict.StableId, StringComparer.Ordinal)
            .ThenBy(conflict => conflict.ConflictKey, StringComparer.Ordinal)
            .ToList();
        var sortedExpectations = expectations
            .OrderBy(expectation => expectation.Kind)
            .ThenBy(expectation => expectation.SemanticIdentity, StringComparer.Ordinal)
            .ToList();

        return new Schema13MergePreflightPlan(
            sortedActions,
            sortedConflicts,
            sortedExpectations,
            ComputeTargetFingerprint(sortedExpectations));
    }

    internal static BackupPayloadV2 ToV2(BackupPayloadV3 payload) => new(
        payload.SourceMaterials,
        payload.Vocabulary,
        payload.Senses,
        payload.PreparedLearning,
        payload.AnswerVariants,
        payload.SenseAnswerVariantAssignments,
        payload.AnswerVariantProgress,
        payload.Learning,
        payload.Workflows,
        payload.DerivedTermEvidence,
        payload.Extensions);

    private static void PlanControls(
        IReadOnlyDictionary<string, DateTime> target,
        IReadOnlyDictionary<string, DateTime> source,
        IReadOnlySet<string> targetSemanticEntities,
        Schema13MergeActionClassification addClassification,
        Schema13MergeActionClassification reconcileClassification,
        string reasonPrefix,
        ICollection<Schema13MergeAction> actions)
    {
        foreach (var identity in target.Keys.Concat(source.Keys).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            var targetHasControl = target.TryGetValue(identity, out var targetTimestamp);
            var sourceHasControl = source.TryGetValue(identity, out var sourceTimestamp);

            Schema13MergeActionClassification classification;
            string reason;
            if (sourceHasControl && !targetHasControl)
            {
                classification = addClassification;
                reason = reasonPrefix + "-add";
            }
            else if (!sourceHasControl)
            {
                classification = Schema13MergeActionClassification.PreserveTargetOnly;
                reason = reasonPrefix + "-target-only-preserved";
            }
            else if (sourceTimestamp < targetTimestamp)
            {
                classification = reconcileClassification;
                reason = reasonPrefix + "-source-earlier";
            }
            else
            {
                classification = Schema13MergeActionClassification.NoChange;
                reason = sourceTimestamp == targetTimestamp
                    ? reasonPrefix + "-identical"
                    : reasonPrefix + "-target-earlier-preserved";
            }

            actions.Add(new Schema13MergeAction(
                MakeActionKey(classification, identity, null),
                identity,
                classification,
                reason,
                targetSemanticEntities.Contains(identity),
                targetHasControl ? targetTimestamp : null,
                sourceHasControl ? sourceTimestamp : null));
        }
    }

    private static void ClassifyGlobalStableIds(
        IReadOnlyDictionary<string, IReadOnlyList<CardHistoryFact>> targetHistory,
        IReadOnlyDictionary<string, IReadOnlyList<CardHistoryFact>> sourceHistory,
        ICollection<Schema13MergeAction> actions,
        ICollection<Schema13MergeConflict> conflicts)
    {
        var targetByStableId = targetHistory.Values.SelectMany(value => value)
            .ToDictionary(value => value.Fact.StableId, StringComparer.Ordinal);
        foreach (var sourceFact in sourceHistory.Values.SelectMany(value => value))
        {
            if (!targetByStableId.TryGetValue(sourceFact.Fact.StableId, out var targetFact)
                || targetFact == sourceFact)
            {
                continue;
            }

            AddConflict(
                Schema13MergePreflightErrorCodes.StableIdCollision,
                sourceFact.CardIdentity,
                sourceFact.Fact.StableId,
                actions,
                conflicts);
        }
    }

    private static void PlanCards(
        IReadOnlyDictionary<string, IReadOnlyList<CardHistoryFact>> targetHistory,
        IReadOnlyDictionary<string, IReadOnlyList<CardHistoryFact>> sourceHistory,
        IReadOnlyDictionary<string, Schema13FsrsCardStateFact> targetStates,
        IReadOnlyDictionary<string, Schema13FsrsCardStateFact> sourceStates,
        ICollection<Schema13MergeAction> actions,
        ICollection<Schema13MergeConflict> conflicts)
    {
        foreach (var identity in targetStates.Keys.Concat(sourceStates.Keys).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            var targetHasCard = targetStates.TryGetValue(identity, out var targetState);
            var sourceHasCard = sourceStates.TryGetValue(identity, out var sourceState);
            var targetFacts = targetHistory.TryGetValue(identity, out var targetList) ? targetList : [];
            var sourceFacts = sourceHistory.TryGetValue(identity, out var sourceList) ? sourceList : [];

            if (!sourceHasCard)
            {
                actions.Add(new Schema13MergeAction(
                    MakeActionKey(Schema13MergeActionClassification.PreserveTargetOnly, identity, "card"),
                    identity,
                    Schema13MergeActionClassification.PreserveTargetOnly,
                    "fsrs-card-target-only-preserved",
                    true,
                    ExpectedTargetCardState: targetState));
                continue;
            }

            if (!targetHasCard)
            {
                foreach (var sourceFact in sourceFacts)
                {
                    actions.Add(HistoryAppendAction(identity, false, sourceFact.Fact));
                }
                actions.Add(new Schema13MergeAction(
                    MakeActionKey(Schema13MergeActionClassification.InsertFsrsCardState, identity, "state"),
                    identity,
                    Schema13MergeActionClassification.InsertFsrsCardState,
                    "fsrs-card-state-insert",
                    false,
                    SourceCardState: sourceState));
                continue;
            }

            var targetIsPrefix = IsExactPrefix(targetFacts, sourceFacts);
            var sourceIsPrefix = IsExactPrefix(sourceFacts, targetFacts);
            if (targetIsPrefix && sourceIsPrefix)
            {
                if (targetFacts.Count == 0 || StateEquals(targetState!, sourceState!))
                {
                    actions.Add(new Schema13MergeAction(
                        MakeActionKey(Schema13MergeActionClassification.NoChange, identity, "card"),
                        identity,
                        Schema13MergeActionClassification.NoChange,
                        "fsrs-history-and-state-identical",
                        true,
                        ExpectedTargetCardState: targetState,
                        SourceCardState: sourceState));
                }
                else
                {
                    AddConflict(
                        Schema13MergePreflightErrorCodes.CardStateConflict,
                        identity,
                        null,
                        actions,
                        conflicts,
                        targetState,
                        sourceState);
                }
                continue;
            }

            if (targetIsPrefix)
            {
                foreach (var sourceFact in sourceFacts.Skip(targetFacts.Count))
                {
                    actions.Add(HistoryAppendAction(identity, true, sourceFact.Fact));
                }
                actions.Add(new Schema13MergeAction(
                    MakeActionKey(Schema13MergeActionClassification.UpdateFsrsCardState, identity, "state"),
                    identity,
                    Schema13MergeActionClassification.UpdateFsrsCardState,
                    "fsrs-card-state-source-tail-result",
                    true,
                    ExpectedTargetCardState: targetState,
                    SourceCardState: sourceState));
                continue;
            }

            if (sourceIsPrefix)
            {
                actions.Add(new Schema13MergeAction(
                    MakeActionKey(Schema13MergeActionClassification.PreserveTargetOnly, identity, "card-ahead"),
                    identity,
                    Schema13MergeActionClassification.PreserveTargetOnly,
                    "fsrs-target-history-ahead-preserved",
                    true,
                    ExpectedTargetCardState: targetState,
                    SourceCardState: sourceState));
                continue;
            }

            AddConflict(
                Schema13MergePreflightErrorCodes.CausalHistoryConflict,
                identity,
                null,
                actions,
                conflicts,
                targetState,
                sourceState);
        }
    }

    private static Schema13MergeAction HistoryAppendAction(
        string identity,
        bool targetPresent,
        Schema13FsrsReviewFact fact) =>
        new(
            MakeActionKey(
                Schema13MergeActionClassification.AppendFsrsReviewHistory,
                identity,
                $"{fact.SequenceNumber}:{fact.StableId}"),
            identity,
            Schema13MergeActionClassification.AppendFsrsReviewHistory,
            "fsrs-history-append-source-tail",
            targetPresent,
            ReviewFact: fact);

    private static void AddConflict(
        string reasonCode,
        string identity,
        string? stableId,
        ICollection<Schema13MergeAction> actions,
        ICollection<Schema13MergeConflict> conflicts,
        Schema13FsrsCardStateFact? targetState = null,
        Schema13FsrsCardStateFact? sourceState = null)
    {
        var conflictKey = new CanonicalFingerprintBuilder("KnownFirst.Merge.Schema13.Conflict.v1")
            .WriteString(reasonCode)
            .WriteString(identity)
            .WriteNullableString(stableId)
            .ComputeSha256Hex();
        conflicts.Add(new Schema13MergeConflict(conflictKey, identity, reasonCode, stableId));
        actions.Add(new Schema13MergeAction(
            MakeActionKey(Schema13MergeActionClassification.Conflict, identity, conflictKey),
            identity,
            Schema13MergeActionClassification.Conflict,
            reasonCode,
            true,
            ExpectedTargetCardState: targetState,
            SourceCardState: sourceState));
    }

    private static IReadOnlyList<Schema13TargetExpectation> BuildExpectations(
        IReadOnlyDictionary<string, DateTime> targetWordControls,
        IReadOnlyDictionary<string, DateTime> sourceWordControls,
        IReadOnlyDictionary<string, DateTime> targetSenseControls,
        IReadOnlyDictionary<string, DateTime> sourceSenseControls,
        IdentityIndex targetIdentities,
        IdentityIndex sourceIdentities,
        IReadOnlyDictionary<string, IReadOnlyList<CardHistoryFact>> targetHistory,
        IReadOnlyDictionary<string, Schema13FsrsCardStateFact> targetStates)
    {
        var expectations = new List<Schema13TargetExpectation>();
        foreach (var identity in targetWordControls.Keys.Concat(sourceWordControls.Keys).Distinct(StringComparer.Ordinal))
        {
            expectations.Add(new Schema13TargetExpectation(
                Schema13TargetExpectationKind.WordLearningControl,
                identity,
                targetIdentities.WordIdentitySet.Contains(identity),
                targetWordControls.TryGetValue(identity, out var timestamp),
                targetWordControls.TryGetValue(identity, out timestamp) ? timestamp : null,
                [],
                null));
        }
        foreach (var identity in targetSenseControls.Keys.Concat(sourceSenseControls.Keys).Distinct(StringComparer.Ordinal))
        {
            expectations.Add(new Schema13TargetExpectation(
                Schema13TargetExpectationKind.SenseLearningControl,
                identity,
                targetIdentities.SenseIdentitySet.Contains(identity),
                targetSenseControls.TryGetValue(identity, out var timestamp),
                targetSenseControls.TryGetValue(identity, out timestamp) ? timestamp : null,
                [],
                null));
        }
        foreach (var identity in targetStates.Keys.Concat(sourceIdentities.CardIdentitySet).Distinct(StringComparer.Ordinal))
        {
            var facts = targetHistory.TryGetValue(identity, out var targetFacts)
                ? targetFacts.Select(item => item.Fact).ToList()
                : [];
            expectations.Add(new Schema13TargetExpectation(
                Schema13TargetExpectationKind.FsrsLearningCard,
                identity,
                targetIdentities.CardIdentitySet.Contains(identity),
                false,
                null,
                facts,
                targetStates.TryGetValue(identity, out var state) ? state : null));
        }
        return expectations;
    }

    private static string ComputeTargetFingerprint(IReadOnlyList<Schema13TargetExpectation> expectations)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.Schema13.Target.v1")
            .WriteInt32(expectations.Count);
        foreach (var expectation in expectations)
        {
            builder.WriteEnum(expectation.Kind)
                .WriteString(expectation.SemanticIdentity)
                .WriteBoolean(expectation.SemanticEntityPresent)
                .WriteBoolean(expectation.ControlPresent)
                .WriteNullableUtcTimestamp(expectation.ControlDecidedAtUtc)
                .WriteInt32(expectation.FsrsHistory.Count);
            foreach (var fact in expectation.FsrsHistory)
            {
                builder.WriteString(fact.StableId)
                    .WriteInt32(fact.SequenceNumber)
                    .WriteEnum(fact.Rating)
                    .WriteUtcTimestamp(fact.ReviewedAtUtc);
            }
            WriteState(builder, expectation.FsrsCardState);
        }
        return builder.ComputeSha256Hex();
    }

    private static void WriteState(CanonicalFingerprintBuilder builder, Schema13FsrsCardStateFact? state)
    {
        builder.WriteBoolean(state is not null);
        if (state is null)
        {
            return;
        }
        builder.WriteEnum(state.State);
        WriteNullableDouble(builder, state.Stability);
        WriteNullableDouble(builder, state.Difficulty);
        builder.WriteNullableUtcTimestamp(state.LastReviewedAtUtc)
            .WriteNullableInt32(state.StepIndex)
            .WriteNullableUtcTimestamp(state.DueAtUtc);
    }

    private static void WriteNullableDouble(CanonicalFingerprintBuilder builder, double? value)
    {
        builder.WriteBoolean(value.HasValue);
        if (value.HasValue)
        {
            builder.WriteDouble(value.Value);
        }
    }

    private static IdentityIndex BuildIdentities(BackupPayloadV3 payload, string side)
    {
        var wordByLocalId = payload.Vocabulary.ToDictionary(
            item => item.Id,
            item => VocabularyMergeIdentityPolicy.Compute(item).Value,
            StringComparer.Ordinal);
        var senseByLocalId = payload.Senses.ToDictionary(
            item => item.Id,
            item => SemanticMeaningIdentityPolicy.Compute(
                item,
                new VocabularyIdentity(Resolve(wordByLocalId, item.VocabularyId, side + " sense vocabulary"))).Value,
            StringComparer.Ordinal);
        var cardByLocalId = payload.Learning.Cards.ToDictionary(
            item => item.Id,
            item => FutureCardIdentityPolicy.Compute(
                new SemanticMeaningIdentity(Resolve(senseByLocalId, item.SenseId, side + " card sense")),
                item.Direction).Value,
            StringComparer.Ordinal);
        return new IdentityIndex(
            wordByLocalId,
            senseByLocalId,
            cardByLocalId,
            wordByLocalId.Values.ToHashSet(StringComparer.Ordinal),
            senseByLocalId.Values.ToHashSet(StringComparer.Ordinal),
            cardByLocalId.Values.ToHashSet(StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CardHistoryFact>> BuildHistory(
        BackupPayloadV3 payload,
        IReadOnlyDictionary<string, string> cardByLocalId)
    {
        var result = cardByLocalId.Values.Distinct(StringComparer.Ordinal)
            .ToDictionary(value => value, _ => new List<CardHistoryFact>(), StringComparer.Ordinal);
        foreach (var item in payload.FsrsReviewHistoryEntries)
        {
            var cardIdentity = Resolve(cardByLocalId, item.CardId, "FSRS history card");
            result[cardIdentity].Add(new CardHistoryFact(
                cardIdentity,
                new Schema13FsrsReviewFact(item.StableId, item.SequenceNumber, item.Rating, item.ReviewedAtUtc)));
        }
        return result.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<CardHistoryFact>)entry.Value.OrderBy(item => item.Fact.SequenceNumber).ToList(),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, Schema13FsrsCardStateFact> BuildStates(
        BackupPayloadV3 payload,
        IReadOnlyDictionary<string, string> cardByLocalId) =>
        payload.FsrsCardStates.ToDictionary(
            item => Resolve(cardByLocalId, item.CardId, "FSRS card state"),
            item => new Schema13FsrsCardStateFact(
                item.State,
                item.Stability,
                item.Difficulty,
                item.LastReviewedAtUtc,
                item.StepIndex,
                item.DueAtUtc),
            StringComparer.Ordinal);

    private static bool IsExactPrefix(
        IReadOnlyList<CardHistoryFact> prefix,
        IReadOnlyList<CardHistoryFact> full)
    {
        if (prefix.Count > full.Count)
        {
            return false;
        }
        for (var index = 0; index < prefix.Count; index++)
        {
            if (prefix[index] != full[index])
            {
                return false;
            }
        }
        return true;
    }

    private static bool StateEquals(Schema13FsrsCardStateFact left, Schema13FsrsCardStateFact right) =>
        left.State == right.State
        && ExactDoubleEquals(left.Stability, right.Stability)
        && ExactDoubleEquals(left.Difficulty, right.Difficulty)
        && left.LastReviewedAtUtc == right.LastReviewedAtUtc
        && left.StepIndex == right.StepIndex
        && left.DueAtUtc == right.DueAtUtc;

    private static bool ExactDoubleEquals(double? left, double? right) =>
        !left.HasValue || !right.HasValue
            ? left.HasValue == right.HasValue
            : BitConverter.DoubleToInt64Bits(left.Value) == BitConverter.DoubleToInt64Bits(right.Value);

    private static string MakeActionKey(
        Schema13MergeActionClassification classification,
        string semanticIdentity,
        string? discriminator) =>
        new CanonicalFingerprintBuilder("KnownFirst.Merge.Schema13.Action.v1")
            .WriteEnum(classification)
            .WriteString(semanticIdentity)
            .WriteNullableString(discriminator)
            .ComputeSha256Hex();

    private static int ActionOrder(Schema13MergeAction action) => action.Classification switch
    {
        Schema13MergeActionClassification.AddWordLearningControl => 10,
        Schema13MergeActionClassification.ReconcileWordLearningControlTimestamp => 11,
        Schema13MergeActionClassification.AddSenseLearningControl => 20,
        Schema13MergeActionClassification.ReconcileSenseLearningControlTimestamp => 21,
        Schema13MergeActionClassification.AppendFsrsReviewHistory => 30,
        Schema13MergeActionClassification.InsertFsrsCardState => 40,
        Schema13MergeActionClassification.UpdateFsrsCardState => 41,
        Schema13MergeActionClassification.NoChange => 50,
        Schema13MergeActionClassification.PreserveTargetOnly => 51,
        Schema13MergeActionClassification.Conflict => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static int ConflictOrder(Schema13MergeConflict conflict) => conflict.ReasonCode switch
    {
        Schema13MergePreflightErrorCodes.StableIdCollision => 10,
        Schema13MergePreflightErrorCodes.CardStateConflict => 20,
        Schema13MergePreflightErrorCodes.CausalHistoryConflict => 30,
        _ => 40
    };

    private static string Resolve(IReadOnlyDictionary<string, string> map, string localId, string reference) =>
        map.TryGetValue(localId, out var identity)
            ? identity
            : throw new MergePlanningException(BackupErrorCodes.MissingReference, $"Missing {reference} reference.");

    private sealed record IdentityIndex(
        IReadOnlyDictionary<string, string> WordByLocalId,
        IReadOnlyDictionary<string, string> SenseByLocalId,
        IReadOnlyDictionary<string, string> CardByLocalId,
        IReadOnlySet<string> WordIdentitySet,
        IReadOnlySet<string> SenseIdentitySet,
        IReadOnlySet<string> CardIdentitySet);

    private sealed record CardHistoryFact(string CardIdentity, Schema13FsrsReviewFact Fact);
}
