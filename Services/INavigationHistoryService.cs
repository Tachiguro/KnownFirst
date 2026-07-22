namespace KnownFirst.Services;

public interface INavigationHistoryService
{
    bool IsHome { get; }

    IDisposable RegisterDismissibleOverlay(Action dismiss);

    bool TryDismissOverlay();

    void RecordNavigation(string relativeRoute);

    bool TryBeginBackNavigation(out string targetRoute, out string sourceRoute);

    void CancelBackNavigation(string sourceRoute);
}
