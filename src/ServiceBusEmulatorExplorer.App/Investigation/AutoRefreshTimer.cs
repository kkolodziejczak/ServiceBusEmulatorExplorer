namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// Raises the automatic refresh at a fixed interval through the workspace clock, delivering each tick through
/// <paramref name="deliver"/> (the UI dispatcher in the app). Starting again with the same interval keeps the
/// running countdown, so frequent reconfiguration never postpones a refresh.
/// </summary>
internal sealed class AutoRefreshTimer(TimeProvider clock, Action<Action> deliver) : IDisposable
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
            if (ReferenceEquals(timer, started)) Tick?.Invoke();
        }), null, interval, interval);
        timer = started;
    }

    public void Stop()
    {
        timer?.Dispose();
        timer = null;
    }

    public void Dispose() => Stop();
}
