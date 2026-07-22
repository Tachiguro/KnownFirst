using KnownFirst.Core.Learning;

namespace KnownFirst.Services.Study;

public sealed class DebugLearningClock : IClock
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private TimeSpan _offset;

    public DebugLearningClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public DateTime UtcNow
    {
        get
        {
            lock (_sync)
            {
                return _timeProvider.GetUtcNow().UtcDateTime.Add(_offset);
            }
        }
    }

    public TimeSpan Offset
    {
        get
        {
            lock (_sync)
            {
                return _offset;
            }
        }
    }

    public void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Debug learning time can only advance by a positive duration.");
        }

        lock (_sync)
        {
            _ = _timeProvider.GetUtcNow().UtcDateTime.Add(_offset).Add(duration);
            _offset = _offset.Add(duration);
        }
    }

    public void AdvanceUntil(DateTime targetUtc)
    {
        var normalizedTarget = NormalizeUtc(targetUtc);
        lock (_sync)
        {
            var current = _timeProvider.GetUtcNow().UtcDateTime.Add(_offset);
            if (normalizedTarget > current)
            {
                _offset = _offset.Add(normalizedTarget - current);
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _offset = TimeSpan.Zero;
        }
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
