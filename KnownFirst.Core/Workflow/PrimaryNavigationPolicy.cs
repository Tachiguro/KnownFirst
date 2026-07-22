namespace KnownFirst.Core.Workflow;

public enum PrimaryNavigationAction
{
    Learn = 0,
    PrepareWords = 1,
    ImportText = 2,
    Settings = 3
}

public static class PrimaryNavigationPolicy
{
    public static IReadOnlyList<PrimaryNavigationAction> Actions { get; } =
        Array.AsReadOnly([
            PrimaryNavigationAction.Learn,
            PrimaryNavigationAction.PrepareWords,
            PrimaryNavigationAction.ImportText,
            PrimaryNavigationAction.Settings
        ]);
}
