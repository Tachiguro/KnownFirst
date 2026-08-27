using KnownFirst.Core.Learning;

namespace KnownFirst.Components.Pages;

public sealed class LearningSummaryDueMonitor : IAsyncDisposable, IDisposable
{
    private readonly IClock _clock;
    private readonly DateTime _dueAtUtc;
    private readonly Func<Task> _onDueAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _monitorTask;
    private readonly TimeSpan _maxRecheckInterval;
    private int _completed;

    public bool IsCompleted => Volatile.Read(ref _completed) == 1;

    public static bool IsDue(IClock clock, DateTime? nextDueAtUtc) =>
        nextDueAtUtc is not null && nextDueAtUtc.Value <= clock.UtcNow;

    public LearningSummaryDueMonitor(
        IClock clock,
        DateTime dueAtUtc,
        Func<Task> onDueAsync,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? maxRecheckInterval = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dueAtUtc = dueAtUtc;
        _onDueAsync = onDueAsync ?? throw new ArgumentNullException(nameof(onDueAsync));
        _delayAsync = delayAsync ?? ((delay, ct) => Task.Delay(delay, ct));
        _maxRecheckInterval = maxRecheckInterval ?? TimeSpan.FromSeconds(30);

        _monitorTask = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var remaining = _dueAtUtc - _clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    if (Interlocked.Exchange(ref _completed, 1) == 0)
                    {
                        await _onDueAsync();
                    }
                    return;
                }

                var nextDelay = remaining < _maxRecheckInterval ? remaining : _maxRecheckInterval;
                if (nextDelay < TimeSpan.FromMilliseconds(50) && remaining > TimeSpan.Zero)
                {
                    nextDelay = TimeSpan.FromMilliseconds(50);
                }

                await _delayAsync(nextDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled or disposed
        }
    }

    public void Cancel() => _cts.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _monitorTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
