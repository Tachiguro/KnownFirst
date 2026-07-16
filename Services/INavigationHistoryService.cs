namespace KnownFirst.Services;

public interface INavigationHistoryService
{
    bool IsHome { get; }

    void RecordNavigation(string relativeRoute);

    bool TryBeginBackNavigation(out string targetRoute, out string sourceRoute);

    void CancelBackNavigation(string sourceRoute);
}
