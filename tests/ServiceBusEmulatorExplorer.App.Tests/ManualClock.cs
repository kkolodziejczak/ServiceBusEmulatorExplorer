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

    /// <summary>The countdown of the armed timer, or null when nothing is waiting to fire.</summary>
    public TimeSpan? ScheduledInterval => Running()?.Due;

    public override DateTimeOffset GetUtcNow() => BaselineClock.Instance.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime, period);
        lock (gate) timers.Add(timer);
        return timer;
    }

    /// <summary>Fires the armed timer once, as if its countdown had elapsed.</summary>
    public void Fire() => (Running() ?? throw new InvalidOperationException("No timer is armed.")).Fire();

    private ManualTimer? Running()
    {
        lock (gate) return timers.SingleOrDefault(timer => timer.Due is not null);
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        private readonly object gate = new();
        private TimeSpan? due = Armed(dueTime);
        private TimeSpan period = period;
        private bool disposed;

        /// <summary>The countdown until the next firing, or null when disposed or waiting to be re-armed.</summary>
        public TimeSpan? Due { get { lock (gate) return disposed ? null : due; } }

        public void Fire()
        {
            lock (gate) due = Armed(period);
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                due = Armed(dueTime);
                this.period = period;
                return !disposed;
            }
        }

        public void Dispose()
        {
            lock (gate) disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static TimeSpan? Armed(TimeSpan time) => time == Timeout.InfiniteTimeSpan ? null : time;
    }
}
