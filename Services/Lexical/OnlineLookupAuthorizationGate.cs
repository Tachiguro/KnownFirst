using KnownFirst.Services;

namespace KnownFirst.Services.Lexical;

public sealed class OnlineLookupAuthorizationGate : IOnlineLookupAuthorizationGate, IDisposable
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly object _lock = new();
    private CancellationTokenSource? _currentEpochCts;
    private bool _isAuthorized;
    private bool _disposed;

    public OnlineLookupAuthorizationGate(IAppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        _isAuthorized = _appSettingsService.HasOnlineLookupConsent;
        if (_isAuthorized)
        {
            _currentEpochCts = new CancellationTokenSource();
        }

        _appSettingsService.OnlineLookupConsentChanged += OnConsentChanged;
    }

    public bool IsAuthorized
    {
        get
        {
            lock (_lock)
            {
                return _isAuthorized;
            }
        }
    }

    public CancellationToken CurrentEpochToken
    {
        get
        {
            lock (_lock)
            {
                if (_isAuthorized && _currentEpochCts is not null)
                {
                    return _currentEpochCts.Token;
                }

                return new CancellationToken(canceled: true);
            }
        }
    }

    public CancellationTokenSource CreateLinkedCancellationTokenSource(CancellationToken callerToken = default)
    {
        lock (_lock)
        {
            if (_isAuthorized && _currentEpochCts is not null)
            {
                return callerToken.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(_currentEpochCts.Token, callerToken)
                    : CancellationTokenSource.CreateLinkedTokenSource(_currentEpochCts.Token);
            }

            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            return cancelled;
        }
    }

    public void EnsureAuthorized()
    {
        lock (_lock)
        {
            if (!_isAuthorized)
            {
                throw new InvalidOperationException("Online dictionary lookup is not authorized.");
            }
        }
    }

    private void OnConsentChanged(bool hasConsent)
    {
        CancellationTokenSource? ctsToCancel = null;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (hasConsent)
            {
                if (!_isAuthorized)
                {
                    _isAuthorized = true;
                    _currentEpochCts = new CancellationTokenSource();
                }
            }
            else
            {
                if (_isAuthorized)
                {
                    _isAuthorized = false;
                    ctsToCancel = _currentEpochCts;
                    _currentEpochCts = null;
                }
            }
        }

        if (ctsToCancel is not null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (AggregateException)
            {
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? ctsToCancel = null;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _appSettingsService.OnlineLookupConsentChanged -= OnConsentChanged;
            _isAuthorized = false;
            ctsToCancel = _currentEpochCts;
            _currentEpochCts = null;
        }

        if (ctsToCancel is not null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (AggregateException)
            {
            }
        }
    }
}
