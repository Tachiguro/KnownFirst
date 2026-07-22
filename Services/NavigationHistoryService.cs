namespace KnownFirst.Services;

public sealed class NavigationHistoryService : INavigationHistoryService
{
    private readonly Lock _syncRoot = new();
    private readonly List<string> _routes = [string.Empty];
    private readonly List<OverlayRegistration> _overlays = [];
    private string? _pendingBackTarget;
    private long _nextOverlayId;

    public bool IsHome
    {
        get
        {
            lock (_syncRoot)
            {
                return _routes.Count == 0 || string.IsNullOrEmpty(_routes[^1]);
            }
        }
    }

    public IDisposable RegisterDismissibleOverlay(Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(dismiss);
        lock (_syncRoot)
        {
            var registration = new OverlayRegistration(this, ++_nextOverlayId, dismiss);
            _overlays.Add(registration);
            return registration;
        }
    }

    public bool TryDismissOverlay()
    {
        Action? dismiss = null;
        lock (_syncRoot)
        {
            if (_overlays.Count == 0)
            {
                return false;
            }

            var registration = _overlays[^1];
            _overlays.RemoveAt(_overlays.Count - 1);
            registration.MarkRemoved();
            dismiss = registration.Dismiss;
        }

        dismiss();
        return true;
    }

    public void RecordNavigation(string relativeRoute)
    {
        var route = NormalizeRoute(relativeRoute);

        lock (_syncRoot)
        {
            if (_pendingBackTarget is not null)
            {
                if (string.Equals(route, _pendingBackTarget, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingBackTarget = null;
                    return;
                }

                _pendingBackTarget = null;
            }

            if (_routes.Count == 0)
            {
                _routes.Add(route);
                return;
            }

            if (!string.Equals(_routes[^1], route, StringComparison.OrdinalIgnoreCase))
            {
                _routes.Add(route);
            }
        }
    }

    public bool TryBeginBackNavigation(out string targetRoute, out string sourceRoute)
    {
        lock (_syncRoot)
        {
            sourceRoute = _routes.Count == 0 ? string.Empty : _routes[^1];

            if (string.IsNullOrEmpty(sourceRoute))
            {
                targetRoute = string.Empty;
                return false;
            }

            if (_routes.Count > 1)
            {
                _routes.RemoveAt(_routes.Count - 1);
                targetRoute = _routes[^1];
            }
            else
            {
                _routes.Clear();
                _routes.Add(string.Empty);
                targetRoute = string.Empty;
            }

            _pendingBackTarget = targetRoute;
            return true;
        }
    }

    public void CancelBackNavigation(string sourceRoute)
    {
        var route = NormalizeRoute(sourceRoute);

        lock (_syncRoot)
        {
            _pendingBackTarget = null;

            if (_routes.Count == 0 || !string.Equals(_routes[^1], route, StringComparison.OrdinalIgnoreCase))
            {
                _routes.Add(route);
            }
        }
    }

    private static string NormalizeRoute(string relativeRoute)
    {
        var route = relativeRoute.Trim();
        var suffixIndex = route.IndexOfAny(['?', '#']);

        if (suffixIndex >= 0)
        {
            route = route[..suffixIndex];
        }

        return route.Trim('/');
    }

    private void RemoveOverlay(long id)
    {
        lock (_syncRoot)
        {
            _overlays.RemoveAll(registration => registration.Id == id);
        }
    }

    private sealed class OverlayRegistration(
        NavigationHistoryService owner,
        long id,
        Action dismiss) : IDisposable
    {
        private bool _removed;

        public long Id { get; } = id;

        public Action Dismiss { get; } = dismiss;

        public void Dispose()
        {
            if (_removed)
            {
                return;
            }

            _removed = true;
            owner.RemoveOverlay(Id);
        }

        public void MarkRemoved() => _removed = true;
    }
}
