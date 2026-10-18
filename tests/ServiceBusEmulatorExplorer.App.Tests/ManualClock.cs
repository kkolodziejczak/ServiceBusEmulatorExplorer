using ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// A workspace clock frozen at the baseline instant whose timers fire only when a test calls <see cref="Fire"/>,
/// so automatic refresh can be observed and driven without waiting for real time.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];

    /// <summary>How many timers have been started; a restarted countdown shows up as a new one.</summary>
    public int StartedTimers { get { lock (gate) return timers.Count; } }

    /// <summary>The interval of the running timer, or null when nothing is scheduled.</summary>
    public TimeSpan? ScheduledInterval => Running()?.Period;

    public override DateTimeOffset GetUtcNow() => BaselineClock.Instance.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, period);
        lock (gate) timers.Add(timer);
        return timer;
    }

    /// <summary>Fires the running timer once, as if its interval had elapsed.</summary>
    public void Fire() => (Running() ?? throw new InvalidOperationException("No timer is scheduled.")).Fire();

    private ManualTimer? Running()
    {
        lock (gate) return timers.SingleOrDefault(timer => !timer.IsDisposed);
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan period) : ITimer
    {
        private int disposed;
        public TimeSpan Period { get; private set; } = period;
        public bool IsDisposed => Volatile.Read(ref disposed) == 1;

        public void Fire()
        {
            if (!IsDisposed) callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Period = period;
            return !IsDisposed;
        }

        public void Dispose() => Interlocked.Exchange(ref disposed, 1);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
