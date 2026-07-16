namespace KnownFirst.Core.Workflow;

public enum PrimaryNavigationAction
{
    Learn = 0,
    ImportText = 1,
    Settings = 2
}

public static class PrimaryNavigationPolicy
{
    public static IReadOnlyList<PrimaryNavigationAction> Actions { get; } =
        Array.AsReadOnly([
            PrimaryNavigationAction.Learn,
            PrimaryNavigationAction.ImportText,
            PrimaryNavigationAction.Settings
        ]);
}
