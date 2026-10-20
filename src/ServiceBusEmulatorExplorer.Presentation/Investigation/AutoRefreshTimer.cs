namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// Raises the automatic refresh through the workspace clock, delivering each tick through <paramref name="deliver"/>
/// (the UI dispatcher in the app). Like a dispatcher timer, the next countdown starts only after a tick has been
/// handled, so a busy UI thread gets one late refresh rather than a queue of them. Starting again with the same
/// interval keeps the running countdown, so frequent reconfiguration never postpones a refresh.
/// </summary>
public sealed class AutoRefreshTimer(TimeProvider clock, Action<Action> deliver) : IDisposable
{
    private ITimer? timer;

    public event Action? Tick;
    public bool IsRunning => timer is not null;
    public TimeSpan Interval { get; private set; }

    public void Start(TimeSpan interval)
    {
        if (timer is not null && interval == Interval) return;
        Stop();
        Interval = interval;
        ITimer? started = null;
        started = clock.CreateTimer(_ => deliver(() =>
        {
            if (!ReferenceEquals(timer, started)) return;
            Tick?.Invoke();
            if (timer is { } current && ReferenceEquals(current, started)) current.Change(interval, Timeout.InfiniteTimeSpan);
        }), null, interval, Timeout.InfiniteTimeSpan);
        timer = started;
    }

    public void Stop()
    {
        timer?.Dispose();
        timer = null;
    }

    public void Dispose() => Stop();
}
