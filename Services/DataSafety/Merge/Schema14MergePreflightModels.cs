using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

public enum Schema14MergeActionClassification
{
    AddWordLearningControl,
    ReconcileWordLearningControlTimestamp,
    AddSenseLearningControl,
    ReconcileSenseLearningControlTimestamp,
    AddLearningTarget,
    AddTargetAnswerVariant,
    AppendTargetFsrsReviewHistory,
    InsertTargetFsrsState,
    UpdateTargetFsrsState,
    AddTargetReview,
    NoChange,
    PreserveTargetOnly,
    Conflict
}

public enum Schema14TargetExpectationKind
{
    WordLearningControl,
    SenseLearningControl,
    LearningTarget
}

public sealed record Schema14FsrsReviewFact(
    string StableId,
    int SequenceNumber,
    BackupReviewRating Rating,
    DateTime ReviewedAtUtc);

public sealed record Schema14FsrsStateFact(
    BackupFsrsCardStateKind State,
    double? Stability,
    double? Difficulty,
    DateTime? LastReviewedAtUtc,
    int? StepIndex,
    DateTime? DueAtUtc);

public sealed record Schema14TargetFact(
    string StableId,
    string SenseSemanticIdentity,
    BackupLearningTargetKind TargetKind,
    string SourceLanguage,
    string TargetLanguage,
    bool TypingOptOut,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record Schema14TargetAnswerVariantFact(
    string StableId,
    string TargetSemanticIdentity,
    string AnswerLanguage,
    string DisplayText,
    string NormalizedText,
    BackupAnswerVariantRequirement Requirement,
    bool IsPreferred,
    DateTime? RequiredSinceUtc,
    string? SourceMeaningSemanticIdentity,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record Schema14TargetReviewFact(
    string StableId,
    string TargetSemanticIdentity,
    string SessionArchiveId,
    BackupReviewRating Rating,
    bool WasTypedAnswer,
    bool WasCorrect,
    bool IsSessionRepeat,
    string? TargetVariantStableId,
    string? MatchedVariantStableId,
    DateTime ReviewedAtUtc,
    DateTime DueAtUtc);

public sealed record Schema14TargetExpectation(
    Schema14TargetExpectationKind Kind,
    string SemanticIdentity,
    bool SemanticEntityPresent,
    bool ControlPresent,
    DateTime? ControlDecidedAtUtc,
    IReadOnlyList<Schema14FsrsReviewFact> FsrsHistory,
    Schema14FsrsStateFact? FsrsState);

public sealed record Schema14MergeAction(
    string ActionKey,
    string SemanticIdentity,
    Schema14MergeActionClassification Classification,
    string ReasonCode,
    bool ExpectedTargetEntityPresent,
    DateTime? ExpectedTargetControlDecidedAtUtc = null,
    DateTime? SourceControlDecidedAtUtc = null,
    Schema14TargetFact? TargetFact = null,
    Schema14TargetAnswerVariantFact? VariantFact = null,
    Schema14TargetReviewFact? ReviewFact = null,
    Schema14FsrsReviewFact? FsrsReviewFact = null,
    Schema14FsrsStateFact? ExpectedTargetState = null,
    Schema14FsrsStateFact? SourceState = null);

public sealed record Schema14MergeConflict(
    string ConflictKey,
    string SemanticIdentity,
    string ReasonCode,
    string? StableId = null);

public static class Schema14MergePreflightErrorCodes
{
    public const string CausalHistoryConflict = "schema14-causal-history-conflict";
    public const string StableIdConflict = "schema14-stable-id-conflict";
    public const string SemanticDuplicateConflict = "schema14-semantic-duplicate-conflict";
}

/// <summary>
/// Deterministic Schema-14 extension to the inherited V1/V2 base-graph plan (KF-LEARN-011 Slice 5).
/// </summary>
public sealed record Schema14MergePreflightPlan(
    IReadOnlyList<Schema14MergeAction> Actions,
    IReadOnlyList<Schema14MergeConflict> Conflicts,
    IReadOnlyList<Schema14TargetExpectation> TargetExpectations,
    string ExpectedTargetFingerprint)
{
    public bool IsExecutable => Conflicts.Count == 0;

    public bool RequiresMutation => Actions.Any(action => action.Classification is
        Schema14MergeActionClassification.AddWordLearningControl or
        Schema14MergeActionClassification.ReconcileWordLearningControlTimestamp or
        Schema14MergeActionClassification.AddSenseLearningControl or
        Schema14MergeActionClassification.ReconcileSenseLearningControlTimestamp or
        Schema14MergeActionClassification.AddLearningTarget or
        Schema14MergeActionClassification.AddTargetAnswerVariant or
        Schema14MergeActionClassification.AppendTargetFsrsReviewHistory or
        Schema14MergeActionClassification.InsertTargetFsrsState or
        Schema14MergeActionClassification.UpdateTargetFsrsState or
        Schema14MergeActionClassification.AddTargetReview);
}
