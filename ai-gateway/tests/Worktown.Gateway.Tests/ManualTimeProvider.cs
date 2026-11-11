namespace Worktown.Gateway.Tests;

/// <summary>
/// Fake time for tests: timers fire only when <see cref="Advance"/> moves the clock past their due
/// time. One-shot timers only (the period is ignored), which is all Task.Delay and timed
/// CancellationTokenSources need.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    /// <summary>Timers that are scheduled and not yet fired or disposed.</summary>
    public int PendingTimerCount
    {
        get { lock (_gate) return _timers.Count(t => t.DueAt is not null); }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.DueAt is { } at && at <= _now).ToList();
            foreach (var timer in due) timer.DueAt = null;
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
            if (dueTime == TimeSpan.Zero) _owner.Advance(TimeSpan.Zero);
            return true;
        }

        public void Fire() => _callback(_state);

        public void Dispose()
        {
            lock (_owner._gate) _owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
