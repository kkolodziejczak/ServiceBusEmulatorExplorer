using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// Counts every delivery of an entity bucket by peeking to its end, so the tree shows totals
/// rather than the loaded page. Targets are counted one at a time.
/// </summary>
internal sealed class ObservedCountScanner(
    IServiceBusMessageService messages,
    long connectionGeneration,
    Action<WatchTarget, ObservedDeliveryCount> publish,
    Action<WatchTarget, Exception> fail)
{
    private const int PeekSize = 100;
    private readonly List<WatchTarget> pending = [];
    private readonly CancellationTokenSource stop = new();
    private WatchTarget? scanning;

    public Task Idle { get; private set; } = Task.CompletedTask;

    /// <summary>Counts these targets before anything already queued; used when the user opens an entity.</summary>
    public void RequestFirst(IReadOnlyCollection<WatchTarget> targets)
    {
        pending.RemoveAll(targets.Contains);
        pending.InsertRange(0, targets);
        Start();
    }

    /// <summary>Queues targets behind existing work. A target already queued or being counted keeps
    /// its place, so frequent refreshes cannot starve the rest of the queue.</summary>
    public void RequestLater(IEnumerable<WatchTarget> targets)
    {
        pending.AddRange(targets.Where(target => target != scanning && !pending.Contains(target)).ToArray());
        Start();
    }

    public void Stop() => stop.Cancel();

    private void Start()
    {
        if (pending.Count > 0 && !stop.IsCancellationRequested && Idle.IsCompleted) Idle = RunAsync();
    }

    private async Task RunAsync()
    {
        while (pending.Count > 0 && !stop.IsCancellationRequested)
        {
            var target = pending[0];
            pending.RemoveAt(0);
            scanning = target;
            try
            {
                var count = await CountAsync(target);
                if (!stop.IsCancellationRequested) publish(target, count);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error) when (!stop.IsCancellationRequested) { fail(target, error); }
            catch (Exception) { return; }
            finally { scanning = null; }
        }
    }

    private async Task<ObservedDeliveryCount> CountAsync(WatchTarget target)
    {
        var deliveries = new HashSet<DeliveryIdentity>();
        long? from = null;
        while (true)
        {
            var batch = await messages.PeekMessagesAsync(target.Address, target.Bucket, PeekSize, from, stop.Token);
            if (batch.Count == 0) break;
            foreach (var message in batch)
                deliveries.Add(new(connectionGeneration, target.Address, target.Bucket, message.SequenceNumber));
            long last = batch.Max(message => message.SequenceNumber);
            if (last == long.MaxValue || (from is { } previous && last < previous)) break;
            from = last + 1;
        }
        return new(deliveries, DateTimeOffset.UtcNow);
    }
}
