using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public enum Schema13MergeActionClassification
{
    AddWordLearningControl,
    ReconcileWordLearningControlTimestamp,
    AddSenseLearningControl,
    ReconcileSenseLearningControlTimestamp,
    AppendFsrsReviewHistory,
    InsertFsrsCardState,
    UpdateFsrsCardState,
    NoChange,
    PreserveTargetOnly,
    Conflict
}

public enum Schema13TargetExpectationKind
{
    WordLearningControl,
    SenseLearningControl,
    FsrsLearningCard
}

public sealed record Schema13FsrsReviewFact(
    string StableId,
    int SequenceNumber,
    BackupReviewRating Rating,
    DateTime ReviewedAtUtc);

public sealed record Schema13FsrsCardStateFact(
    BackupFsrsCardStateKind State,
    double? Stability,
    double? Difficulty,
    DateTime? LastReviewedAtUtc,
    int? StepIndex,
    DateTime? DueAtUtc);

/// <summary>
/// Complete semantic expectation for the target state observed by preview. Slice 5 can capture the same
/// semantic projection immediately before writing and compare these values without relying on a local
/// SQLite id. Controls carry their current timestamp; cards carry their complete causal prefix and exact
/// persisted state.
/// </summary>
public sealed record Schema13TargetExpectation(
    Schema13TargetExpectationKind Kind,
    string SemanticIdentity,
    bool SemanticEntityPresent,
    bool ControlPresent,
    DateTime? ControlDecidedAtUtc,
    IReadOnlyList<Schema13FsrsReviewFact> FsrsHistory,
    Schema13FsrsCardStateFact? FsrsCardState);

public sealed record Schema13MergeAction(
    string ActionKey,
    string SemanticIdentity,
    Schema13MergeActionClassification Classification,
    string ReasonCode,
    bool ExpectedTargetEntityPresent,
    DateTime? ExpectedTargetControlDecidedAtUtc = null,
    DateTime? SourceControlDecidedAtUtc = null,
    Schema13FsrsReviewFact? ReviewFact = null,
    Schema13FsrsCardStateFact? ExpectedTargetCardState = null,
    Schema13FsrsCardStateFact? SourceCardState = null);

public sealed record Schema13MergeConflict(
    string ConflictKey,
    string SemanticIdentity,
    string ReasonCode,
    string? StableId = null);

/// <summary>
/// Deterministic Schema-13 extension to the inherited V1/V2 base-graph plan. It describes only what a
/// future writer could do; creating this value never opens a write transaction on the target.
/// </summary>
public sealed record Schema13MergePreflightPlan(
    IReadOnlyList<Schema13MergeAction> Actions,
    IReadOnlyList<Schema13MergeConflict> Conflicts,
    IReadOnlyList<Schema13TargetExpectation> TargetExpectations,
    string ExpectedTargetFingerprint)
{
    public bool IsExecutable => Conflicts.Count == 0;

    public bool RequiresMutation => Actions.Any(action => action.Classification is
        Schema13MergeActionClassification.AddWordLearningControl or
        Schema13MergeActionClassification.ReconcileWordLearningControlTimestamp or
        Schema13MergeActionClassification.AddSenseLearningControl or
        Schema13MergeActionClassification.ReconcileSenseLearningControlTimestamp or
        Schema13MergeActionClassification.AppendFsrsReviewHistory or
        Schema13MergeActionClassification.InsertFsrsCardState or
        Schema13MergeActionClassification.UpdateFsrsCardState);

    public static Schema13MergePreflightPlan ForLegacyProjectionConflict(string reasonCode)
    {
        var conflictKey = new CanonicalFingerprintBuilder("KnownFirst.Merge.Schema13.Conflict.v1")
            .WriteString("legacy-source")
            .WriteString(reasonCode)
            .ComputeSha256Hex();
        return new Schema13MergePreflightPlan(
            [],
            [new Schema13MergeConflict(conflictKey, "legacy-source", reasonCode)],
            [],
            new CanonicalFingerprintBuilder("KnownFirst.Merge.Schema13.Target.v1").ComputeSha256Hex());
    }
}

public static class Schema13MergePreflightErrorCodes
{
    public const string CausalHistoryConflict = "schema13-causal-history-conflict";
    public const string StableIdCollision = "schema13-stable-id-collision";
    public const string CardStateConflict = "schema13-card-state-conflict";
    public const string LegacyHistoryInsufficient = "schema13-legacy-history-insufficient";
}
