namespace KnownFirst.Core.Review;

public enum ReviewAction
{
    Known = 0,
    Unknown = 1,
    UndoPreviousDecision = 2
}

public static class ReviewActionPolicy
{
    public static IReadOnlyList<ReviewAction> VisibleActions { get; } =
        Array.AsReadOnly([ReviewAction.Known, ReviewAction.Unknown, ReviewAction.UndoPreviousDecision]);
}
