namespace KnownFirst.Models;

public enum WorkflowPrimaryAction
{
    ContinueReview = 0,
    ContinuePreparation = 1,
    ContinueLearning = 2,
    LearnDueCards = 3,
    StartLearning = 4,
    PrepareWords = 5,
    ImportText = 6
}

public sealed record WorkflowSnapshot(
    bool HasActiveReview,
    bool HasActivePreparation,
    bool HasActiveLearning,
    int DueCardCount,
    int PreparedNewItemCount,
    int UnpreparedUnknownCount,
    WorkflowPrimaryAction PrimaryAction)
{
    public bool CanLearn => !HasActiveReview
        && (HasActiveLearning || DueCardCount > 0 || PreparedNewItemCount > 0);

    public bool CanImport => !HasActiveReview;

    public bool CanPrepare => !HasActiveReview
        && (HasActivePreparation || UnpreparedUnknownCount > 0);
}
